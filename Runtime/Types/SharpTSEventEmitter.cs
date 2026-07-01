using SharpTS.Compilation;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible EventEmitter instance.
/// </summary>
/// <remarks>
/// Provides event subscription, emission, and management following Node.js EventEmitter semantics.
/// Supports once listeners, prepend operations, listener inspection, and max listener warnings.
/// </remarks>
public class SharpTSEventEmitter : ITypeCategorized, IMemberProvider
{
    /// <inheritdoc />
    /// <remarks>
    /// Returns EventEmitter only for direct instances. Subclasses that have their own
    /// property dispatch (via ISharpTSPropertyAccessor) should override to return their
    /// specific category or Unknown to use the general dispatch path.
    /// </remarks>
    public virtual TypeCategory RuntimeCategory =>
        GetType() == typeof(SharpTSEventEmitter) ? TypeCategory.EventEmitter : TypeCategory.Unknown;

    /// <summary>
    /// Wraps a listener function with metadata for once tracking.
    /// </summary>
    private record ListenerWrapper(object Listener, bool Once);

    /// <summary>
    /// Default maximum listeners before emitting a warning.
    /// </summary>
    public static int DefaultMaxListeners { get; set; } = 10;

    private readonly Dictionary<string, LinkedList<ListenerWrapper>> _events = [];
    private int _maxListeners = 0; // 0 means use DefaultMaxListeners

    /// <summary>
    /// Gets the effective max listeners value.
    /// </summary>
    private int EffectiveMaxListeners => _maxListeners > 0 ? _maxListeners : DefaultMaxListeners;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// Virtual so the EventEmitter family dispatches polymorphically (#1139); subclasses
    /// override and chain to <c>base.GetMember</c>.
    /// </summary>
    public virtual object? GetMember(string name) => GetEventEmitterMember(name);

    /// <summary>
    /// Resolves the core EventEmitter members, independent of any subclass override.
    /// Call this (instead of casting to <see cref="SharpTSEventEmitter"/>) when you need
    /// the raw EventEmitter behavior — e.g. a writable-side "drain"/"response" listener
    /// that must not go through Readable's flowing-mode "on"/"once" wrappers.
    /// </summary>
    internal object? GetEventEmitterMember(string name)
    {
        return name switch
        {
            // Core event methods
            "on" => BuiltInMethod.CreateV2("on", 2, On),
            "addListener" => BuiltInMethod.CreateV2("addListener", 2, On), // Alias for on
            "once" => BuiltInMethod.CreateV2("once", 2, Once),
            "off" => BuiltInMethod.CreateV2("off", 2, Off),
            "removeListener" => BuiltInMethod.CreateV2("removeListener", 2, Off), // Alias for off
            "emit" => BuiltInMethod.CreateV2("emit", 1, int.MaxValue, Emit),
            "removeAllListeners" => BuiltInMethod.CreateV2("removeAllListeners", 0, 1, RemoveAllListeners),

            // Listener inspection
            "listeners" => BuiltInMethod.CreateV2("listeners", 1, Listeners),
            "rawListeners" => BuiltInMethod.CreateV2("rawListeners", 1, RawListeners),
            "listenerCount" => BuiltInMethod.CreateV2("listenerCount", 1, ListenerCount),
            "eventNames" => BuiltInMethod.CreateV2("eventNames", 0, EventNames),

            // Prepend methods
            "prependListener" => BuiltInMethod.CreateV2("prependListener", 2, PrependListener),
            "prependOnceListener" => BuiltInMethod.CreateV2("prependOnceListener", 2, PrependOnceListener),

            // Max listeners
            "setMaxListeners" => BuiltInMethod.CreateV2("setMaxListeners", 1, SetMaxListeners),
            "getMaxListeners" => BuiltInMethod.CreateV2("getMaxListeners", 0, GetMaxListeners),

            _ => null
        };
    }

    /// <summary>
    /// Adds a listener for the specified event.
    /// </summary>
    private RuntimeValue On(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("on() requires event name and listener arguments");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: false, prepend: false);
        return RuntimeValue.FromObject(this); // Method chaining
    }

    /// <summary>
    /// Adds a one-time listener for the specified event.
    /// </summary>
    private RuntimeValue Once(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("once() requires event name and listener arguments");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: true, prepend: false);
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Removes a listener for the specified event.
    /// </summary>
    private RuntimeValue Off(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("off() requires event name and listener arguments");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");

        if (_events.TryGetValue(eventName, out var listeners))
        {
            // Remove first matching listener (by reference equality)
            for (var node = listeners.First; node != null; node = node.Next)
            {
                if (ReferenceEquals(node.Value.Listener, listener))
                {
                    listeners.Remove(node);
                    if (listeners.Count == 0)
                        _events.Remove(eventName);
                    break;
                }
            }
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Emits an event, calling all registered listeners with the provided arguments.
    /// </summary>
    private RuntimeValue Emit(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("emit() requires at least an event name argument");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
            return RuntimeValue.False;

        // Snapshot the listeners to handle modifications during emit
        var snapshot = new List<ListenerWrapper>(listeners);
        var eventArgs = new List<object?>(Math.Max(0, args.Length - 1));
        for (int i = 1; i < args.Length; i++)
            eventArgs.Add(args[i].ToObject());

        foreach (var wrapper in snapshot)
        {
            // Remove once listeners before calling
            if (wrapper.Once)
            {
                for (var node = listeners.First; node != null; node = node.Next)
                {
                    if (ReferenceEquals(node.Value, wrapper))
                    {
                        listeners.Remove(node);
                        if (listeners.Count == 0)
                            _events.Remove(eventName);
                        break;
                    }
                }
            }

            // Call the listener - support multiple listener types
            InvokeListener(wrapper.Listener, interpreter, eventArgs);
        }

        return RuntimeValue.True;
    }

    /// <summary>
    /// Emits an event from subclass code using the interpreter.
    /// Shared helper to avoid duplicating event dispatch logic in every EventEmitter subclass.
    /// </summary>
    protected internal void EmitEvent(Interp interpreter, string eventName, List<object?> args)
    {
        var emit = GetMember("emit") as BuiltInMethod;
        if (emit != null)
        {
            var fullArgs = new List<object?> { eventName };
            fullArgs.AddRange(args);
            emit.Bind(this).Call(interpreter, fullArgs);
        }
    }

    /// <summary>
    /// Invokes a listener supporting multiple listener types (ISharpTSCallable, TSFunction, Action, BuiltInMethod).
    /// </summary>
    private static void InvokeListener(object listener, Interp? interpreter, List<object?> eventArgs)
    {
        if (listener is ISharpTSCallable callable)
        {
            var result = callable.Call(interpreter!, eventArgs);
            // An async listener's promise is unreachable from guest code —
            // report its rejection instead of swallowing it (#228).
            interpreter?.ObserveDiscardedCallbackResult(result);
        }
        else
        {
            // Try direct invocation for compiled code (TSFunction, Action, etc.)
            InvokeListenerDirect(listener, eventArgs.ToArray());
        }
    }

    /// <summary>
    /// Removes all listeners for the specified event, or all events if no event name is provided.
    /// </summary>
    private RuntimeValue RemoveAllListeners(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].IsNull)
        {
            _events.Clear();
        }
        else
        {
            var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
            _events.Remove(eventName);
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Clears all event listeners. Used internally to reset singleton stream state.
    /// </summary>
    internal void ClearAllListenersInternal() => _events.Clear();

    /// <summary>
    /// Returns true if at least one listener is registered for the event (host-side check,
    /// used to skip building event payloads when nobody is listening).
    /// </summary>
    internal bool HasListenersInternal(string eventName)
        => _events.TryGetValue(eventName, out var l) && l.Count > 0;

    /// <summary>
    /// Returns an array of listener functions for the specified event.
    /// </summary>
    private RuntimeValue Listeners(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("listeners() requires an event name argument");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners))
            return RuntimeValue.FromObject(new SharpTSArray([]));

        // Return just the listener functions, not the wrappers
        var listenerFunctions = listeners.Select(w => w.Listener).Cast<object?>().ToList();
        return RuntimeValue.FromObject(new SharpTSArray(listenerFunctions));
    }

    /// <summary>
    /// Returns an array of raw listener wrappers for the specified event.
    /// In Node.js this includes wrapper objects for once listeners; we return the same as listeners.
    /// </summary>
    private RuntimeValue RawListeners(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // For simplicity, return same as listeners - real Node.js wraps once listeners
        return Listeners(interpreter, receiver, args);
    }

    /// <summary>
    /// Returns the number of listeners for the specified event.
    /// </summary>
    private RuntimeValue ListenerCount(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("listenerCount() requires an event name argument");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners))
            return RuntimeValue.Zero;

        return RuntimeValue.FromNumber(listeners.Count);
    }

    /// <summary>
    /// Returns an array of event names that have registered listeners.
    /// </summary>
    private RuntimeValue EventNames(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var names = _events.Keys
            .Where(k => _events[k].Count > 0)
            .Cast<object?>()
            .ToList();
        return RuntimeValue.FromObject(new SharpTSArray(names));
    }

    /// <summary>
    /// Adds a listener to the beginning of the listeners array.
    /// </summary>
    private RuntimeValue PrependListener(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("prependListener() requires event name and listener arguments");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: false, prepend: true);
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Adds a one-time listener to the beginning of the listeners array.
    /// </summary>
    private RuntimeValue PrependOnceListener(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("prependOnceListener() requires event name and listener arguments");

        var eventName = args[0].ToObject()?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1].ToObject() ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: true, prepend: true);
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Sets the maximum number of listeners for this emitter.
    /// </summary>
    private RuntimeValue SetMaxListeners(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
            throw new Exception("setMaxListeners() requires a number argument");

        if (!args[0].IsNumber)
            throw new Exception("setMaxListeners() argument must be a number");

        _maxListeners = (int)args[0].AsNumberUnsafe();
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Returns the current maximum listener count for this emitter.
    /// </summary>
    private RuntimeValue GetMaxListeners(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromNumber(EffectiveMaxListeners);
    }

    /// <summary>
    /// Internal method to add a listener with various options.
    /// </summary>
    private void AddListenerInternal(string eventName, object listener, bool once, bool prepend)
    {
        if (!_events.TryGetValue(eventName, out var listeners))
        {
            listeners = new LinkedList<ListenerWrapper>();
            _events[eventName] = listeners;
        }

        var wrapper = new ListenerWrapper(listener, once);

        if (prepend)
            listeners.AddFirst(wrapper);  // O(1) with LinkedList
        else
            listeners.AddLast(wrapper);   // O(1) with LinkedList

        // Check max listeners warning (only when adding, not prepending a second time)
        if (listeners.Count > EffectiveMaxListeners && EffectiveMaxListeners > 0)
        {
            // In Node.js this emits a warning. For now we just continue silently.
            // A full implementation could emit a 'warning' event or write to stderr.
        }

        // Notify subclasses (e.g., Readable enters flowing mode on 'data' listener)
        OnListenerAdded(eventName);
    }

    /// <summary>
    /// Called after a listener is added. Override in subclasses to react to specific events.
    /// </summary>
    protected virtual void OnListenerAdded(string eventName)
    {
        // Default: no-op. Readable overrides this to enter flowing mode on 'data'.
    }

    public override string ToString() => "EventEmitter {}";

    /// <summary>
    /// Emits an event directly without requiring an interpreter.
    /// Used by compiled code where TSFunction listeners can be invoked directly.
    /// </summary>
    /// <param name="eventName">The name of the event to emit.</param>
    /// <param name="args">Arguments to pass to the event listeners.</param>
    /// <returns>True if the event had listeners, false otherwise.</returns>
    /// <remarks>
    /// This method enables Worker communication in compiled code by directly invoking
    /// TSFunction listeners instead of going through the interpreter. For interpreted
    /// code, use the regular emit() method through the interpreter.
    /// </remarks>
    public bool EmitDirect(string eventName, params object?[] args)
    {
        if (!_events.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
            return false;

        // Snapshot the listeners to handle modifications during emit
        var snapshot = new List<ListenerWrapper>(listeners);

        foreach (var wrapper in snapshot)
        {
            // Remove once listeners before calling
            if (wrapper.Once)
            {
                for (var node = listeners.First; node != null; node = node.Next)
                {
                    if (ReferenceEquals(node.Value, wrapper))
                    {
                        listeners.Remove(node);
                        if (listeners.Count == 0)
                            _events.Remove(eventName);
                        break;
                    }
                }
            }

            // Invoke the listener directly
            InvokeListenerDirect(wrapper.Listener, args);
        }

        return true;
    }

    /// <summary>
    /// Like <see cref="EmitDirect"/> but invokes listeners with a real interpreter, so
    /// interpreter-function listeners that use their interpreter argument (e.g. ones that
    /// call <c>console.log</c>) work. Used by async built-ins (child_process, etc.) that
    /// emit lifecycle events from the event-loop thread.
    /// </summary>
    public bool EmitWith(Interp interpreter, string eventName, params object?[] args)
    {
        if (!_events.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
            return false;

        var snapshot = new List<ListenerWrapper>(listeners);

        foreach (var wrapper in snapshot)
        {
            if (wrapper.Once)
            {
                for (var node = listeners.First; node != null; node = node.Next)
                {
                    if (ReferenceEquals(node.Value, wrapper))
                    {
                        listeners.Remove(node);
                        if (listeners.Count == 0)
                            _events.Remove(eventName);
                        break;
                    }
                }
            }

            SharpTS.Runtime.RuntimeCallableDispatcher.Invoke(interpreter, wrapper.Listener, args);
        }

        return true;
    }

    /// <summary>
    /// Invokes a listener directly without an interpreter.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="SharpTS.Runtime.RuntimeCallableDispatcher"/>,
    /// which handles every callable shape SharpTS produces (including
    /// <see cref="ISharpTSCallable"/> implementations such as
    /// <see cref="BuiltInMethod"/>, interpreter <see cref="SharpTSFunction"/>
    /// instances, the runtime <see cref="TSFunction"/> from compiled code, and
    /// emitted per-DLL <c>$TSFunction</c> / <c>$BoundTSFunction</c>).
    ///
    /// Prior to this delegation, the body silently skipped
    /// <see cref="ISharpTSCallable"/> listeners (e.g., a
    /// <see cref="BuiltInMethod"/> registered as an event listener never
    /// fired). The dispatcher accepts a <c>null</c> interpreter the same way
    /// the previous <see cref="BuiltInMethod"/> branch did, so all interpreter
    /// listeners that don't actually use their interpreter argument now work.
    /// </remarks>
    private static void InvokeListenerDirect(object listener, object?[] args)
    {
        SharpTS.Runtime.RuntimeCallableDispatcher.Invoke(null, listener, args);
    }

    /// <summary>
    /// Adds a listener programmatically (for internal use in compiled code).
    /// </summary>
    /// <param name="eventName">The event name.</param>
    /// <param name="listener">The listener function.</param>
    /// <param name="once">Whether this is a one-time listener.</param>
    public void AddListenerDirect(string eventName, object listener, bool once = false)
    {
        AddListenerInternal(eventName, listener, once, prepend: false);
    }

    /// <summary>
    /// Removes a listener programmatically (for internal use).
    /// </summary>
    public void RemoveListenerDirect(string eventName, object listener)
    {
        if (_events.TryGetValue(eventName, out var listeners))
        {
            for (var node = listeners.First; node != null; node = node.Next)
            {
                if (ReferenceEquals(node.Value.Listener, listener))
                {
                    listeners.Remove(node);
                    if (listeners.Count == 0)
                        _events.Remove(eventName);
                    break;
                }
            }
        }
    }
}
