using System.Collections.Concurrent;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the WHATWG / Node.js BroadcastChannel.
/// </summary>
/// <remarks>
/// BroadcastChannel allows simple cross-thread pub/sub messaging within a single process.
/// Channels with the same name receive each other's messages — except that the sender
/// never receives its own posts. Messages are deep-cloned with the structured clone
/// algorithm before delivery.
///
/// Delivery is dispatched onto each subscriber's owning interpreter via
/// <c>Interpreter.ScheduleTimer</c> so callbacks always run on the correct event loop.
/// </remarks>
public class SharpTSBroadcastChannel : SharpTSEventEmitter, IDisposable
{
    /// <summary>
    /// Process-wide registry of live channels keyed by channel name. The inner dictionary
    /// is keyed by per-instance id so live subscribers can be enumerated O(n) without
    /// iterating dead WeakReferences.
    /// </summary>
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<long, WeakReference<SharpTSBroadcastChannel>>> _registry =
        new(StringComparer.Ordinal);

    private static long _nextId;

    private readonly long _id;
    private readonly string _name;
    private readonly ConcurrentQueue<object?> _pendingMessages = new();
    private bool _closed;
    private bool _refed;  // event-loop keep-alive state; see Ref()/Unref()/SetOwnerInterpreter()

    // Property-style handler slots — correspond to bc.onmessage = h / bc.onmessageerror = h.
    // Stored separately from the on('message', ...) listener list so setting a new handler
    // replaces the previous one (WHATWG spec) without clobbering on()-registered listeners.
    private object? _onMessageHandler;
    private object? _onMessageErrorHandler;

    private Interp? _ownerInterpreter;

    /// <summary>
    /// The interpreter that owns this channel — used to schedule message delivery on the
    /// correct event loop. Set when the channel is constructed via the interpreter factory.
    /// </summary>
    internal Interp? OwnerInterpreter
    {
        get => _ownerInterpreter;
        set
        {
            _ownerInterpreter = value;
            // Register with the event loop keep-alive counter now that we have an owner.
            if (value != null && !_refed && !_closed)
            {
                _refed = true;
                value.Ref();
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns Unknown so dispatch falls through to the per-runtime-type lookup
    /// (SharpTSBroadcastChannel registered as its own instance type). The
    /// EventEmitter category dispatch in BuiltInRegistry hard-casts to
    /// SharpTSEventEmitter and would otherwise hide our GetMember override.
    /// </remarks>
    public override TypeCategory RuntimeCategory => TypeCategory.Unknown;

    /// <summary>
    /// Creates a new BroadcastChannel bound to <paramref name="name"/>.
    /// </summary>
    public SharpTSBroadcastChannel(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _id = Interlocked.Increment(ref _nextId);

        var bucket = _registry.GetOrAdd(_name, _ => new ConcurrentDictionary<long, WeakReference<SharpTSBroadcastChannel>>());
        bucket[_id] = new WeakReference<SharpTSBroadcastChannel>(this);
    }

    /// <summary>
    /// The channel name passed to the constructor.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Posts a message to all other live BroadcastChannels with the same name.
    /// </summary>
    /// <param name="message">The message to broadcast. Cloned via structured clone.</param>
    /// <exception cref="Exception">Thrown if the channel has been closed.</exception>
    public void PostMessage(object? message)
    {
        if (_closed)
            throw new Exception("InvalidStateError: BroadcastChannel is closed");

        if (!_registry.TryGetValue(_name, out var bucket))
            return;

        // Snapshot to avoid holding the bucket while we invoke external code.
        var subscribers = new List<SharpTSBroadcastChannel>(bucket.Count);
        foreach (var entry in bucket)
        {
            // Skip self — sender never receives its own posts.
            if (entry.Key == _id)
                continue;

            if (entry.Value.TryGetTarget(out var target) && !target._closed)
            {
                subscribers.Add(target);
            }
            else
            {
                // Opportunistically clean up dead refs.
                bucket.TryRemove(entry.Key, out _);
            }
        }

        if (subscribers.Count == 0)
            return;

        foreach (var subscriber in subscribers)
        {
            object? cloned;
            try
            {
                cloned = StructuredClone.Clone(message, null);
            }
            catch (StructuredClone.DataCloneError)
            {
                // Spec: dispatch a messageerror event on the receiver instead of throwing on the sender.
                subscriber.EnqueueMessageError();
                continue;
            }

            subscriber.EnqueueAndSchedule(cloned);
        }
    }

    /// <summary>
    /// Closes this channel — removes it from the registry, fires the close event, and
    /// causes future <see cref="PostMessage"/> calls to throw.
    /// </summary>
    public void Close()
    {
        if (_closed)
            return;

        _closed = true;

        if (_registry.TryGetValue(_name, out var bucket))
        {
            bucket.TryRemove(_id, out _);
            if (bucket.IsEmpty)
                _registry.TryRemove(_name, out _);
        }

        if (_refed && _ownerInterpreter != null)
        {
            _ownerInterpreter.Unref();
            _refed = false;
        }

        if (_ownerInterpreter != null)
        {
            EmitEvent(_ownerInterpreter, "close", []);
        }
    }

    /// <summary>
    /// Marks this channel as keeping the event loop alive (the default once constructed).
    /// </summary>
    public void Ref()
    {
        if (_closed || _refed)
            return;
        _refed = true;
        _ownerInterpreter?.Ref();
    }

    /// <summary>
    /// Marks this channel as not keeping the event loop alive.
    /// </summary>
    public void Unref()
    {
        if (!_refed)
            return;
        _refed = false;
        _ownerInterpreter?.Unref();
    }

    /// <summary>
    /// Enqueues a cloned message and schedules a drain on the owning interpreter's loop.
    /// </summary>
    private void EnqueueAndSchedule(object? cloned)
    {
        if (_closed)
            return;

        _pendingMessages.Enqueue(cloned);

        if (OwnerInterpreter != null)
        {
            OwnerInterpreter.ScheduleTimer(0, 0, DrainPendingMessages, false);
        }
    }

    /// <summary>
    /// Enqueues a messageerror notification and schedules a drain on the owning loop.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="DrainPendingMessages"/>: the scheduled closure does NOT re-check
    /// <c>_closed</c> — an already-enqueued notification still fires even if the receiver
    /// closes before the timer runs, matching Node's "in-flight deliveries complete after
    /// Close()" semantics (#1255). Only the initial (enqueue-time) guard checks <c>_closed</c>.
    /// </remarks>
    private void EnqueueMessageError()
    {
        if (_closed || OwnerInterpreter == null)
            return;

        OwnerInterpreter.ScheduleTimer(0, 0, () =>
        {
            if (OwnerInterpreter == null)
                return;
            EmitEvent(OwnerInterpreter, "messageerror", []);

            // Also invoke the property-style onmessageerror handler if set (WHATWG spec),
            // mirroring DrainPendingMessages' handling of onmessage (#1255).
            if (_onMessageErrorHandler is ISharpTSCallable callable)
            {
                try { callable.Call(OwnerInterpreter, []); }
                catch { /* listeners may not throw out of delivery */ }
            }
        }, false);
    }

    /// <summary>
    /// Delivers any pending messages to listeners as MessageEvent-shaped objects.
    /// </summary>
    /// <remarks>
    /// Pending messages are drained even after <see cref="Close"/> — Close removes us from
    /// the registry and prevents future PostMessage, but in-flight deliveries that were
    /// already enqueued still complete to match Node semantics.
    /// </remarks>
    private void DrainPendingMessages()
    {
        if (_ownerInterpreter == null)
            return;

        while (_pendingMessages.TryDequeue(out var msg))
        {
            var eventData = new SharpTSObject(new Dictionary<string, object?>
            {
                ["data"] = msg,
                ["type"] = "message",
                ["target"] = this,
            });

            // Invoke on('message', ...) listeners via EventEmitter.
            EmitEvent(_ownerInterpreter, "message", [eventData]);

            // Also invoke the property-style onmessage handler if set (WHATWG spec).
            if (_onMessageHandler is ISharpTSCallable callable)
            {
                try { callable.Call(_ownerInterpreter, [eventData]); }
                catch { /* listeners may not throw out of delivery */ }
            }
        }
    }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// Falls through to <see cref="SharpTSEventEmitter.GetMember"/> for inherited methods.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "name" => _name,
            "onmessage" => _onMessageHandler,
            "onmessageerror" => _onMessageErrorHandler,

            "postMessage" => BuiltInMethod.CreateV2("postMessage", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("postMessage requires a message argument");
                PostMessage(args[0].ToObject());
                return RuntimeValue.Null;
            }),

            "close" => BuiltInMethod.CreateV2("close", 0, (_, _, _) =>
            {
                Close();
                return RuntimeValue.Null;
            }),

            "ref" => BuiltInMethod.CreateV2("ref", 0, (_, _, _) =>
            {
                Ref();
                return RuntimeValue.FromObject(this);
            }),

            "unref" => BuiltInMethod.CreateV2("unref", 0, (_, _, _) =>
            {
                Unref();
                return RuntimeValue.FromObject(this);
            }),

            // Map addEventListener / removeEventListener / dispatchEvent onto
            // EventEmitter's on / off / emit so the WHATWG and Node-style APIs both work.
            "addEventListener" => BuiltInMethod.CreateV2("addEventListener", 2, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new Exception("addEventListener requires event name and listener arguments");
                var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
                var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");
                AddListenerDirect(eventName, listener, once: false);
                return RuntimeValue.Null;
            }),

            "removeEventListener" => BuiltInMethod.CreateV2("removeEventListener", 2, (_, _, args) =>
            {
                if (args.Length < 2)
                    throw new Exception("removeEventListener requires event name and listener arguments");
                var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
                var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");
                RemoveListenerDirect(eventName, listener);
                return RuntimeValue.Null;
            }),

            // Inherit on/once/off/emit/etc from EventEmitter
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Sets a property by name. Supports the <c>onmessage</c> and <c>onmessageerror</c>
    /// property-style event handlers defined by the WHATWG BroadcastChannel spec.
    /// </summary>
    /// <returns>True if the property was recognized; false to fall through.</returns>
    public bool SetMember(string name, object? value)
    {
        switch (name)
        {
            case "onmessage":
                _onMessageHandler = value;
                return true;
            case "onmessageerror":
                _onMessageErrorHandler = value;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    public override string ToString() =>
        _closed ? $"BroadcastChannel {{ name: {_name}, closed }}" : $"BroadcastChannel {{ name: {_name} }}";
}

/// <summary>
/// Constructor for the BroadcastChannel class — used as the worker_threads module export.
/// </summary>
internal class BroadcastChannelConstructor : ISharpTSCallable
{
    public int Arity() => 1;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        if (arguments.Count < 1)
            throw new Exception("BroadcastChannel constructor requires a name argument");

        var name = arguments[0]?.ToString() ?? throw new Exception("BroadcastChannel name must be a string");
        var channel = new SharpTSBroadcastChannel(name)
        {
            OwnerInterpreter = interpreter
        };
        return channel;
    }
}
