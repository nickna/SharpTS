using System.Collections.Concurrent;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a MessagePort for bidirectional communication between threads.
/// </summary>
/// <remarks>
/// MessagePort provides asynchronous message passing between workers and the main thread,
/// or between a pair of ports created via MessageChannel. Messages are cloned using the
/// structured clone algorithm, except SharedArrayBuffer which is shared by reference.
/// </remarks>
public class SharpTSMessagePort : SharpTSEventEmitter
{
    /// <summary>
    /// Internal queue for incoming messages.
    /// </summary>
    private readonly BlockingCollection<ClonedMessage> _queue = new();

    /// <summary>
    /// The paired port (for MessageChannel-created ports).
    /// </summary>
    private SharpTSMessagePort? _partner;

    /// <summary>
    /// Whether this port has been started (messages are delivered).
    /// </summary>
    private bool _started;

    /// <summary>
    /// Whether this port has been closed.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// Whether this port has been neutered (transferred).
    /// </summary>
    private bool _neutered;

    /// <summary>
    /// Whether this port (and its partner) have been transferred to a worker on
    /// another thread. The two ports of a channel live in the same process but on
    /// different event-loop threads once one is handed to a worker, so delivery
    /// must be marshalled onto each owner's loop rather than emitted synchronously
    /// on the poster's thread, and a started port must keep its owner's loop alive
    /// (Node semantics for a listening port). See <see cref="MarkTransferredAcrossThreads"/>
    /// and #406.
    /// </summary>
    private bool _crossThread;

    /// <summary>
    /// Whether this port currently holds a keep-alive Ref against its owner loop
    /// (only ever true for a started, unclosed cross-thread port). Guards against
    /// double Ref/Unref.
    /// </summary>
    private bool _loopRefed;

    /// <summary>
    /// The interpreter to use for event dispatch (set when added to a context).
    /// </summary>
    internal Interp? OwnerInterpreter { get; set; }

    // NOTE: RuntimeCategory deliberately NOT overridden to EventEmitter.
    // The base virtual returns Unknown for subclasses, which routes property
    // access through the per-type instance registration (BuiltInRegistry) and
    // reaches this class's GetMember. Forcing the EventEmitter category here
    // dispatched through a base-typed cast, so the port-specific members
    // (postMessage/start/close) resolved as undefined (#209).

    /// <summary>
    /// Represents a cloned message ready for delivery. When <paramref name="IsError"/> is
    /// true the message failed to clone on the sender, and the receiver dispatches a
    /// <c>'messageerror'</c> event instead of <c>'message'</c> (#1001) — mirroring how the
    /// receiver, not the sender, surfaces a clone failure in Node.
    /// </summary>
    internal record ClonedMessage(object? Data, SharpTSArray? Transfer, bool IsError = false);

    /// <summary>
    /// Sets the partner port for bidirectional communication.
    /// </summary>
    internal void SetPartner(SharpTSMessagePort partner)
    {
        _partner = partner;
    }

    /// <summary>
    /// Marks this port as neutered (after transfer).
    /// </summary>
    internal void Neuter()
    {
        _neutered = true;
    }

    /// <summary>
    /// Marks this port and its partner as having been transferred across an
    /// event-loop-thread boundary (one of the pair was handed to a worker). After
    /// this, message delivery is marshalled onto each port's owner-loop thread and
    /// a started port Refs that loop so a worker waiting only on a transferred port
    /// stays alive until the port closes (#406). Idempotent; recurses once to the
    /// partner.
    /// </summary>
    internal void MarkTransferredAcrossThreads()
    {
        if (_crossThread)
            return;
        _crossThread = true;

        // If the port was already started before the transfer was recorded (the
        // listener was attached before the worker spawned), take the keep-alive Ref
        // now — Start() won't run again.
        if (_started && !_closed && !_loopRefed && OwnerInterpreter != null)
        {
            _loopRefed = true;
            OwnerInterpreter.Ref();
        }

        _partner?.MarkTransferredAcrossThreads();
    }

    /// <summary>
    /// Posts a message to the partner port or worker.
    /// </summary>
    public void PostMessage(object? message, SharpTSArray? transfer = null)
    {
        if (_neutered)
            throw new Exception("DataCloneError: Cannot post message on neutered port");

        if (_closed)
            return; // Silently ignore messages to closed ports

        if (_partner == null || _partner._closed)
            return; // No partner (or a worker port) — subclasses can override

        // Clone the message. A clone failure surfaces on the RECEIVER as 'messageerror'
        // (Node delivery model), not as a synchronous throw on the sender (#1001).
        ClonedMessage delivery;
        try
        {
            delivery = new ClonedMessage(StructuredClone.Clone(message, transfer), transfer);
        }
        catch (StructuredClone.DataCloneError)
        {
            delivery = new ClonedMessage(null, null, IsError: true);
        }
        _partner.EnqueueMessage(delivery);
    }

    /// <summary>
    /// Enqueues a message for delivery.
    /// </summary>
    internal void EnqueueMessage(ClonedMessage message)
    {
        if (_closed || _neutered)
            return;

        _queue.Add(message);

        // If started, trigger message delivery.
        if (_started && OwnerInterpreter != null)
        {
            if (_crossThread)
                // The poster runs on the partner's thread (e.g. a worker), not on
                // this port's owner loop. Marshal delivery onto the owner loop so
                // guest 'message' listeners run on the correct, single thread.
                OwnerInterpreter.EnqueueCallback(DeliverPendingMessages);
            else
                DeliverPendingMessages();
        }
    }

    /// <summary>
    /// Starts receiving messages (explicit start required for ports from MessageChannel).
    /// </summary>
    public void Start()
    {
        if (_started || _closed || _neutered)
            return;

        _started = true;

        // A started cross-thread port keeps its owner loop alive (Node: a port with
        // a 'message' listener is ref'd). Without this a worker whose only pending
        // work is a transferred port would quiesce and exit before any message
        // arrives (#406 — same liveness class as #329).
        if (_crossThread && !_loopRefed && OwnerInterpreter != null)
        {
            _loopRefed = true;
            OwnerInterpreter.Ref();
        }

        // Deliver any queued messages.
        if (OwnerInterpreter != null)
        {
            if (_crossThread)
                OwnerInterpreter.EnqueueCallback(DeliverPendingMessages);
            else
                DeliverPendingMessages();
        }
    }

    /// <summary>
    /// Closes the port, preventing further message sending/receiving.
    /// </summary>
    public void Close()
    {
        if (_closed)
            return;

        _closed = true;
        _queue.CompleteAdding();

        // Release the keep-alive Ref so the owner loop can quiesce and exit.
        if (_loopRefed && OwnerInterpreter != null)
        {
            _loopRefed = false;
            OwnerInterpreter.Unref();
        }

        // Emit close event
        if (OwnerInterpreter != null)
        {
            EmitEvent("close", []);
        }
    }

    /// <summary>
    /// Delivers pending messages to event listeners.
    /// </summary>
    internal void DeliverPendingMessages()
    {
        if (!_started || _closed || OwnerInterpreter == null)
            return;

        while (_queue.TryTake(out var message))
        {
            if (message.IsError)
            {
                // The sender's clone failed — Node fires 'messageerror' on the receiver.
                EmitEvent("messageerror", []);
                continue;
            }
            // Node worker_threads semantics: 'message' listeners receive the
            // cloned input value of postMessage() directly (not a browser-style
            // MessageEvent wrapper).
            EmitEvent("message", [message.Data]);
        }
    }

    /// <summary>
    /// Emits an event to listeners.
    /// </summary>
    private void EmitEvent(string eventName, List<object?> args)
    {
        if (OwnerInterpreter == null)
            return;
        base.EmitEvent(OwnerInterpreter, eventName, args);
    }

    /// <summary>
    /// Gets a member (method or property) by name.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "postMessage" => BuiltInMethod.CreateV2("postMessage", 1, 2, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("postMessage requires at least one argument");
                var transfer = args.Length > 1 ? args[1].ToObject() as SharpTSArray : null;
                PostMessage(args[0].ToObject(), transfer);
                return RuntimeValue.Null;
            }),

            "start" => BuiltInMethod.CreateV2("start", 0, (_, _, _) =>
            {
                Start();
                return RuntimeValue.Null;
            }),

            "close" => BuiltInMethod.CreateV2("close", 0, (_, _, _) =>
            {
                Close();
                return RuntimeValue.Null;
            }),

            // Node semantics: attaching a 'message' listener implicitly starts
            // the port (https://nodejs.org/api/worker_threads.html#event-message).
            // MessageChannel-created ports also have no owner interpreter until
            // someone interacts with them, so capture it here — without it,
            // queued messages are never delivered.
            "on" or "addListener" or "once" => BuiltInMethod.CreateV2(name, 2, (interp, _, args) =>
            {
                var eventName = args[0].ToObject()?.ToString()
                    ?? throw new Exception("Event name must be a string");
                var listener = args[1].ToObject()
                    ?? throw new Exception("Listener must be a function");
                AddListenerDirect(eventName, listener, once: name == "once");
                // Attaching a 'message' (or 'messageerror', #1001) listener implicitly
                // starts the port so queued deliveries flow.
                if (eventName == "message" || eventName == "messageerror")
                {
                    OwnerInterpreter ??= interp;
                    Start();
                }
                return RuntimeValue.FromObject(this);
            }),

            // Inherit EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Receives a message synchronously (blocking). Used for receiveMessageOnPort().
    /// </summary>
    internal object? ReceiveMessageSync(int timeoutMs = 0)
    {
        if (_neutered || _closed)
            return null;

        ClonedMessage? message;
        if (timeoutMs <= 0)
        {
            if (!_queue.TryTake(out message))
                return null;
        }
        else
        {
            if (!_queue.TryTake(out message, timeoutMs))
                return null;
        }

        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["message"] = message.Data
        });
    }

    public override string ToString() => _neutered ? "MessagePort { neutered }" :
                                         _closed ? "MessagePort { closed }" :
                                         "MessagePort {}";
}
