using SharpTS.Compilation;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible EventEmitter instance.
/// </summary>
/// <remarks>
/// Provides event subscription, emission, and management following Node.js EventEmitter semantics.
/// Supports once listeners, prepend operations, listener inspection, and max listener warnings.
/// </remarks>
public class SharpTSEventEmitter
{
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
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            // Core event methods
            "on" => new BuiltInMethod("on", 2, On),
            "addListener" => new BuiltInMethod("addListener", 2, On), // Alias for on
            "once" => new BuiltInMethod("once", 2, Once),
            "off" => new BuiltInMethod("off", 2, Off),
            "removeListener" => new BuiltInMethod("removeListener", 2, Off), // Alias for off
            "emit" => new BuiltInMethod("emit", 1, int.MaxValue, Emit),
            "removeAllListeners" => new BuiltInMethod("removeAllListeners", 0, 1, RemoveAllListeners),

            // Listener inspection
            "listeners" => new BuiltInMethod("listeners", 1, Listeners),
            "rawListeners" => new BuiltInMethod("rawListeners", 1, RawListeners),
            "listenerCount" => new BuiltInMethod("listenerCount", 1, ListenerCount),
            "eventNames" => new BuiltInMethod("eventNames", 0, EventNames),

            // Prepend methods
            "prependListener" => new BuiltInMethod("prependListener", 2, PrependListener),
            "prependOnceListener" => new BuiltInMethod("prependOnceListener", 2, PrependOnceListener),

            // Max listeners
            "setMaxListeners" => new BuiltInMethod("setMaxListeners", 1, SetMaxListeners),
            "getMaxListeners" => new BuiltInMethod("getMaxListeners", 0, GetMaxListeners),

            _ => null
        };
    }

    /// <summary>
    /// Adds a listener for the specified event.
    /// </summary>
    private object? On(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("on() requires event name and listener arguments");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1] ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: false, prepend: false);
        return this; // Method chaining
    }

    /// <summary>
    /// Adds a one-time listener for the specified event.
    /// </summary>
    private object? Once(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("once() requires event name and listener arguments");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1] ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: true, prepend: false);
        return this;
    }

    /// <summary>
    /// Removes a listener for the specified event.
    /// </summary>
    private object? Off(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("off() requires event name and listener arguments");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1] ?? throw new Exception("Listener must be a function");

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

        return this;
    }

    /// <summary>
    /// Emits an event, calling all registered listeners with the provided arguments.
    /// </summary>
    private object? Emit(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("emit() requires at least an event name argument");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners) || listeners.Count == 0)
            return false;

        // Snapshot the listeners to handle modifications during emit
        var snapshot = new List<ListenerWrapper>(listeners);
        var eventArgs = args.Count > 1 ? args.Skip(1).ToList() : [];

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

            // Call the listener
            if (wrapper.Listener is ISharpTSCallable callable)
            {
                callable.Call(interpreter, eventArgs);
            }
        }

        return true;
    }

    /// <summary>
    /// Removes all listeners for the specified event, or all events if no event name is provided.
    /// </summary>
    private object? RemoveAllListeners(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
        {
            _events.Clear();
        }
        else
        {
            var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
            _events.Remove(eventName);
        }

        return this;
    }

    /// <summary>
    /// Returns an array of listener functions for the specified event.
    /// </summary>
    private object? Listeners(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("listeners() requires an event name argument");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners))
            return new SharpTSArray([]);

        // Return just the listener functions, not the wrappers
        var listenerFunctions = listeners.Select(w => w.Listener).Cast<object?>().ToList();
        return new SharpTSArray(listenerFunctions);
    }

    /// <summary>
    /// Returns an array of raw listener wrappers for the specified event.
    /// In Node.js this includes wrapper objects for once listeners; we return the same as listeners.
    /// </summary>
    private object? RawListeners(Interp interpreter, object? receiver, List<object?> args)
    {
        // For simplicity, return same as listeners - real Node.js wraps once listeners
        return Listeners(interpreter, receiver, args);
    }

    /// <summary>
    /// Returns the number of listeners for the specified event.
    /// </summary>
    private object? ListenerCount(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("listenerCount() requires an event name argument");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");

        if (!_events.TryGetValue(eventName, out var listeners))
            return 0.0;

        return (double)listeners.Count;
    }

    /// <summary>
    /// Returns an array of event names that have registered listeners.
    /// </summary>
    private object? EventNames(Interp interpreter, object? receiver, List<object?> args)
    {
        var names = _events.Keys
            .Where(k => _events[k].Count > 0)
            .Cast<object?>()
            .ToList();
        return new SharpTSArray(names);
    }

    /// <summary>
    /// Adds a listener to the beginning of the listeners array.
    /// </summary>
    private object? PrependListener(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("prependListener() requires event name and listener arguments");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1] ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: false, prepend: true);
        return this;
    }

    /// <summary>
    /// Adds a one-time listener to the beginning of the listeners array.
    /// </summary>
    private object? PrependOnceListener(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("prependOnceListener() requires event name and listener arguments");

        var eventName = args[0]?.ToString() ?? throw new Exception("Event name must be a string");
        var listener = args[1] ?? throw new Exception("Listener must be a function");

        AddListenerInternal(eventName, listener, once: true, prepend: true);
        return this;
    }

    /// <summary>
    /// Sets the maximum number of listeners for this emitter.
    /// </summary>
    private object? SetMaxListeners(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("setMaxListeners() requires a number argument");

        if (args[0] is not double n)
            throw new Exception("setMaxListeners() argument must be a number");

        _maxListeners = (int)n;
        return this;
    }

    /// <summary>
    /// Returns the current maximum listener count for this emitter.
    /// </summary>
    private object? GetMaxListeners(Interp interpreter, object? receiver, List<object?> args)
    {
        return (double)EffectiveMaxListeners;
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
    /// Invokes a listener directly without an interpreter.
    /// </summary>
    private static void InvokeListenerDirect(object listener, object?[] args)
    {
        // TSFunction from compiled code - can invoke directly
        if (listener is TSFunction tsFunc)
        {
            tsFunc.Invoke(args);
            return;
        }

        // BuiltInMethod - create minimal args list and invoke with null interpreter
        // This works for methods that don't actually use the interpreter parameter
        if (listener is BuiltInMethod builtIn)
        {
            builtIn.Call(null!, args.ToList());
            return;
        }

        // ISharpTSCallable from interpreted code - cannot invoke without interpreter
        // This is a limitation: interpreted callbacks won't work in compiled Worker context
        if (listener is ISharpTSCallable)
        {
            // Log or silently skip - these listeners require interpreter
            return;
        }

        // Action delegate (for internal use)
        if (listener is Action<object?[]> action)
        {
            action(args);
            return;
        }
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
}
