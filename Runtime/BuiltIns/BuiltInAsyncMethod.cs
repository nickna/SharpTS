using SharpTS.Execution;
using SharpTS.Runtime.Types;
using SharpTS.Runtime.Exceptions;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Callable wrapper for native C# async implementations of built-in methods.
/// Used by Promise methods that must return Promises.
/// </summary>
/// <remarks>
/// PROMISE WRAPPING CONTRACT:
///
/// This wrapper automatically wraps the implementation's result in a SharpTSPromise.
/// Therefore, implementation functions should return RAW VALUES, not Promises.
///
/// ✓ CORRECT: Implementation returns Task&lt;object?&gt; containing the raw value
///   Example: Task.FromResult(42) → Call() returns SharpTSPromise(Task&lt;42&gt;)
///
/// ✗ WRONG: Implementation returns Task&lt;SharpTSPromise&gt;
///   This causes double-wrapping: SharpTSPromise(Task&lt;SharpTSPromise(Task&lt;value&gt;)&gt;)
///   which leads to infinite loops when awaited in async iterators.
///
/// For Promise.reject(), throw SharpTSPromiseRejectedException instead of
/// returning a rejected Promise - the catch block will create the rejected Promise.
/// </remarks>
public class BuiltInAsyncMethod : ISharpTSCallable, ISharpTSAsyncCallable
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, Task<object?>> _implementation;
    private object? _receiver;

    // Cache for bound methods - uses weak references to avoid memory leaks
    private ConditionalWeakTable<object, BuiltInAsyncMethod>? _boundMethodCache;

    public BuiltInAsyncMethod(
        string name,
        int arity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation)
        : this(name, arity, arity, implementation) { }

    public BuiltInAsyncMethod(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
    }

    // Private constructor for creating bound instances
    private BuiltInAsyncMethod(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation,
        object? receiver)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
        _receiver = receiver;
    }

    public int Arity() => _minArity;

    public BuiltInAsyncMethod Bind(object? receiver)
    {
        // Null receivers don't need caching
        if (receiver == null)
        {
            return new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, null);
        }

        // Value types can't be cached efficiently
        if (receiver.GetType().IsValueType)
        {
            return new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, receiver);
        }

        // Initialize cache lazily
        _boundMethodCache ??= new ConditionalWeakTable<object, BuiltInAsyncMethod>();

        // Try to get cached bound method
        if (_boundMethodCache.TryGetValue(receiver, out var cached))
        {
            return cached;
        }

        // Create new bound method and cache it
        var bound = new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation, receiver);
        _boundMethodCache.AddOrUpdate(receiver, bound);
        return bound;
    }

    /// <summary>
    /// Synchronous call - returns a Promise that wraps the async execution.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);
        try
        {
            var task = _implementation(interpreter, _receiver, arguments);
            return new SharpTSPromise(task);
        }
        catch (Exception ex)
        {
            // If the implementation throws synchronously, return a rejected Promise
            object? errorValue = ex switch
            {
                ThrowException tex => tex.Value,
                SharpTSPromiseRejectedException rex => rex.Reason,
                _ => ex.Message
            };
            return SharpTSPromise.Reject(errorValue);
        }
    }

    /// <summary>
    /// Async call - awaits the implementation directly.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);
        return await _implementation(interpreter, _receiver, arguments);
    }

    private void ValidateArguments(List<object?> arguments)
    {
        if (arguments.Count < _minArity || arguments.Count > _maxArity)
        {
            throw new Exception(
                $"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Count}.");
        }
    }

    public override string ToString() => $"<built-in async {_name}>";
}
