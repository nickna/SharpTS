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
public class SharpTSMessagePort : SharpTSEventEmitter, ITypeCategorized
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
    /// The interpreter to use for event dispatch (set when added to a context).
    /// </summary>
    internal Interp? OwnerInterpreter { get; set; }

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.EventEmitter;

    /// <summary>
    /// Represents a cloned message ready for delivery.
    /// </summary>
    internal record ClonedMessage(object? Data, SharpTSArray? Transfer);

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
    /// Posts a message to the partner port or worker.
    /// </summary>
    public void PostMessage(object? message, SharpTSArray? transfer = null)
    {
        if (_neutered)
            throw new Exception("DataCloneError: Cannot post message on neutered port");

        if (_closed)
            return; // Silently ignore messages to closed ports

        // Clone the message
        var clonedMessage = StructuredClone.Clone(message, transfer);

        if (_partner != null && !_partner._closed)
        {
            // Direct delivery to partner port
            _partner.EnqueueMessage(new ClonedMessage(clonedMessage, transfer));
        }
        // If no partner, this might be a worker port - subclasses can override
    }

    /// <summary>
    /// Enqueues a message for delivery.
    /// </summary>
    internal void EnqueueMessage(ClonedMessage message)
    {
        if (_closed || _neutered)
            return;

        _queue.Add(message);

        // If started, trigger message delivery
        if (_started && OwnerInterpreter != null)
        {
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

        // Deliver any queued messages
        if (OwnerInterpreter != null)
        {
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
            // Create MessageEvent-like object
            var eventData = new SharpTSObject(new Dictionary<string, object?>
            {
                ["data"] = message.Data,
                ["ports"] = message.Transfer ?? new SharpTSArray()
            });

            EmitEvent("message", [eventData]);
        }
    }

    /// <summary>
    /// Emits an event to listeners.
    /// </summary>
    private void EmitEvent(string eventName, List<object?> args)
    {
        if (OwnerInterpreter == null)
            return;

        // Call emit through the base EventEmitter
        var emitMethod = GetMember("emit") as BuiltInMethod;
        emitMethod?.Call(OwnerInterpreter, [eventName, ..args]);
    }

    /// <summary>
    /// Gets a member (method or property) by name.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            "postMessage" => new BuiltInMethod("postMessage", 1, 2, (interp, recv, args) =>
            {
                if (args.Count == 0)
                    throw new Exception("postMessage requires at least one argument");
                var transfer = args.Count > 1 ? args[1] as SharpTSArray : null;
                PostMessage(args[0], transfer);
                return null;
            }),

            "start" => new BuiltInMethod("start", 0, (interp, recv, args) =>
            {
                Start();
                return null;
            }),

            "close" => new BuiltInMethod("close", 0, (interp, recv, args) =>
            {
                Close();
                return null;
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
