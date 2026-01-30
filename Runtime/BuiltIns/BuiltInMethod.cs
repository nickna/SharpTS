using SharpTS.Runtime.Types;
using SharpTS.Execution;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Callable wrapper for native C# implementations of built-in methods.
/// </summary>
/// <remarks>
/// Implements <see cref="ISharpTSCallable"/> to provide a uniform calling convention for
/// built-in methods (array methods like push/pop/map, string methods, Math functions).
/// Supports variable arity via min/max argument counts. The <see cref="Bind"/> method
/// associates a receiver object (e.g., the array instance for array methods).
/// Used by <see cref="Interpreter"/> when resolving method calls on built-in types.
/// </remarks>
/// <seealso cref="ISharpTSCallable"/>
/// <seealso cref="MathBuiltIns"/>
public class BuiltInMethod : ISharpTSCallable
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, object?> _implementation;
    private object? _receiver;

    // Cache for bound methods - uses weak references to avoid memory leaks
    // Key: receiver object, Value: bound method instance
    private ConditionalWeakTable<object, BuiltInMethod>? _boundMethodCache;

    /// <summary>
    /// The minimum number of arguments this method accepts.
    /// </summary>
    public int MinArity => _minArity;

    /// <summary>
    /// The maximum number of arguments this method accepts.
    /// </summary>
    public int MaxArity => _maxArity;

    public BuiltInMethod(string name, int arity, Func<Interpreter, object?, List<object?>, object?> implementation)
        : this(name, arity, arity, implementation) { }

    public BuiltInMethod(string name, int minArity, int maxArity, Func<Interpreter, object?, List<object?>, object?> implementation)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
    }

    // Private constructor for creating bound instances (no cache needed on bound instances)
    private BuiltInMethod(string name, int minArity, int maxArity,
        Func<Interpreter, object?, List<object?>, object?> implementation, object? receiver)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
        _receiver = receiver;
        // Bound instances don't have their own cache
    }

    public int Arity() => _minArity;

    public BuiltInMethod Bind(object? receiver)
    {
        // Null receivers don't need caching
        if (receiver == null)
        {
            return new BuiltInMethod(_name, _minArity, _maxArity, _implementation, null);
        }

        // Value types (like double for numbers) can't be cached in ConditionalWeakTable
        // because they're boxed each time, creating new object instances
        if (receiver.GetType().IsValueType)
        {
            return new BuiltInMethod(_name, _minArity, _maxArity, _implementation, receiver);
        }

        // Initialize cache lazily
        _boundMethodCache ??= new ConditionalWeakTable<object, BuiltInMethod>();

        // Try to get cached bound method
        if (_boundMethodCache.TryGetValue(receiver, out var cached))
        {
            return cached;
        }

        // Create new bound method and cache it
        var bound = new BuiltInMethod(_name, _minArity, _maxArity, _implementation, receiver);
        _boundMethodCache.AddOrUpdate(receiver, bound);
        return bound;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count < _minArity || arguments.Count > _maxArity)
        {
            throw new Exception($"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Count}.");
        }
        return _implementation(interpreter, _receiver, arguments);
    }

    public override string ToString() => $"<built-in {_name}>";
}
