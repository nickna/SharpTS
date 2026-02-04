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
public class BuiltInMethod : ISharpTSCallable, ISharpTSCallableV2
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, object?> _implementation;
    private readonly Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue>? _implementationV2;
    private object? _receiver;
    private RuntimeValue _receiverV2;
    private readonly bool _hasV2Receiver;

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

    /// <summary>
    /// Gets the method name.
    /// </summary>
    public string Name => _name;

    /// <inheritdoc />
    int ISharpTSCallableV2.Arity => _minArity;

    public BuiltInMethod(string name, int arity, Func<Interpreter, object?, List<object?>, object?> implementation)
        : this(name, arity, arity, implementation) { }

    public BuiltInMethod(string name, int minArity, int maxArity, Func<Interpreter, object?, List<object?>, object?> implementation)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
    }

    /// <summary>
    /// Creates a BuiltInMethod with a RuntimeValue-based implementation.
    /// Use <see cref="CreateV2"/> factory method instead for clearer intent.
    /// </summary>
    private BuiltInMethod(int arity,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation,
        string name)
    {
        _name = name;
        _minArity = arity;
        _maxArity = arity;
        _implementationV2 = implementation;
        // Create a legacy wrapper
        _implementation = WrapV2Implementation(implementation);
    }

    /// <summary>
    /// Creates a BuiltInMethod with a RuntimeValue-based implementation.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="arity">The number of required arguments.</param>
    /// <param name="implementation">The V2 implementation using RuntimeValue.</param>
    /// <returns>A new BuiltInMethod instance.</returns>
    public static BuiltInMethod CreateV2(string name, int arity,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        return new BuiltInMethod(arity, implementation, name);
    }

    /// <summary>
    /// Creates a BuiltInMethod with a RuntimeValue-based implementation and variable arity.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="minArity">The minimum number of arguments.</param>
    /// <param name="maxArity">The maximum number of arguments.</param>
    /// <param name="implementation">The V2 implementation using RuntimeValue.</param>
    /// <returns>A new BuiltInMethod instance.</returns>
    public static BuiltInMethod CreateV2(string name, int minArity, int maxArity,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        return new BuiltInMethod(name, minArity, maxArity, null!, implementation, (object?)null)
        {
            // Override the null implementation with the wrapper
        };
    }

    // Private constructor for creating bound instances (no cache needed on bound instances)
    private BuiltInMethod(string name, int minArity, int maxArity,
        Func<Interpreter, object?, List<object?>, object?>? implementation,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue>? implementationV2,
        object? receiver)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        // If only V2 is provided, create the legacy wrapper
        if (implementation == null && implementationV2 != null)
        {
            _implementationV2 = implementationV2;
            _implementation = WrapV2Implementation(implementationV2);
        }
        else
        {
            _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));
            _implementationV2 = implementationV2;
        }
        _receiver = receiver;
        // Bound instances don't have their own cache
    }

    // Private constructor for RuntimeValue-bound instances
    private BuiltInMethod(string name, int minArity, int maxArity,
        Func<Interpreter, object?, List<object?>, object?> implementation,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue>? implementationV2,
        RuntimeValue receiverV2)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
        _implementationV2 = implementationV2;
        _receiverV2 = receiverV2;
        _hasV2Receiver = true;
        _receiver = receiverV2.ToObject();
    }

    public int Arity() => _minArity;

    /// <summary>
    /// Binds the method to a receiver using RuntimeValue.
    /// </summary>
    public BuiltInMethod BindV2(RuntimeValue receiver)
    {
        return new BuiltInMethod(_name, _minArity, _maxArity, _implementation, _implementationV2, receiver);
    }

    public BuiltInMethod Bind(object? receiver)
    {
        // Null receivers don't need caching
        if (receiver == null)
        {
            return new BuiltInMethod(_name, _minArity, _maxArity, _implementation, _implementationV2, (object?)null);
        }

        // Value types (like double for numbers) can't be cached in ConditionalWeakTable
        // because they're boxed each time, creating new object instances
        if (receiver.GetType().IsValueType)
        {
            return new BuiltInMethod(_name, _minArity, _maxArity, _implementation, _implementationV2, receiver);
        }

        // Initialize cache lazily
        _boundMethodCache ??= new ConditionalWeakTable<object, BuiltInMethod>();

        // Try to get cached bound method
        if (_boundMethodCache.TryGetValue(receiver, out var cached))
        {
            return cached;
        }

        // Create new bound method and cache it
        var bound = new BuiltInMethod(_name, _minArity, _maxArity, _implementation, _implementationV2, receiver);
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

    /// <summary>
    /// Calls the method with RuntimeValue arguments.
    /// </summary>
    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        if (arguments.Length < _minArity || arguments.Length > _maxArity)
        {
            throw new Exception($"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Length}.");
        }

        // Fast path: if we have a V2 implementation, use it directly
        if (_implementationV2 != null)
        {
            return _hasV2Receiver
                ? _implementationV2(interpreter, _receiverV2, arguments)
                : _implementationV2(interpreter, RuntimeValue.FromBoxed(_receiver), arguments);
        }

        // Slow path: convert to legacy call
        var boxedArgs = new List<object?>(arguments.Length);
        foreach (var arg in arguments)
        {
            boxedArgs.Add(arg.ToObject());
        }

        var result = _implementation(interpreter, _receiver, boxedArgs);
        return RuntimeValue.FromBoxed(result);
    }

    /// <summary>
    /// Creates a legacy wrapper for a V2 implementation.
    /// </summary>
    private static Func<Interpreter, object?, List<object?>, object?> WrapV2Implementation(
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> v2Impl)
    {
        return (interpreter, receiver, arguments) =>
        {
            var rvArgs = new RuntimeValue[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
            {
                rvArgs[i] = RuntimeValue.FromBoxed(arguments[i]);
            }

            var result = v2Impl(interpreter, RuntimeValue.FromBoxed(receiver), rvArgs);
            return result.ToObject();
        };
    }

    public override string ToString() => $"<built-in {_name}>";
}
