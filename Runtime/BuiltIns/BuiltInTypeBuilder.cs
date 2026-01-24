using SharpTS.Execution;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Fluent builder for defining built-in type members (methods and properties).
/// Eliminates name duplication and provides type-safe registration.
/// </summary>
/// <typeparam name="TReceiver">The receiver type (e.g., SharpTSMap, SharpTSSet, string)</typeparam>
/// <example>
/// <code>
/// private static readonly BuiltInTypeMemberLookup&lt;SharpTSMap&gt; _lookup =
///     BuiltInTypeBuilder&lt;SharpTSMap&gt;.ForInstanceType()
///         .Property("size", map => (double)map.Size)
///         .Method("get", 1, Get)
///         .Method("set", 2, Set)
///         .Build();
/// </code>
/// </example>
public sealed class BuiltInTypeBuilder<TReceiver>
{
    private readonly Dictionary<string, BuiltInMethod> _methods = new();
    private readonly Dictionary<string, Func<TReceiver, object?>> _properties = new();
    private readonly bool _requiresBind;

    private BuiltInTypeBuilder(bool requiresBind)
    {
        _requiresBind = requiresBind;
    }

    /// <summary>
    /// Creates a builder for an instance type where methods need to be bound to a receiver.
    /// Use this for types like SharpTSArray, SharpTSMap, string, etc.
    /// </summary>
    public static BuiltInTypeBuilder<TReceiver> ForInstanceType() => new(requiresBind: true);

    /// <summary>
    /// Creates a builder for a static type where methods don't need binding.
    /// Use this for types like Math, JSON, Number (static members).
    /// </summary>
    public static BuiltInTypeBuilder<TReceiver> ForStaticType() => new(requiresBind: false);

    /// <summary>
    /// Registers a read-only property that computes a value from the receiver.
    /// </summary>
    /// <param name="name">The property name (e.g., "size", "length")</param>
    /// <param name="getter">Function to compute the property value from the receiver</param>
    public BuiltInTypeBuilder<TReceiver> Property(string name, Func<TReceiver, object?> getter)
    {
        _properties[name] = getter;
        return this;
    }

    /// <summary>
    /// Registers a method with fixed arity.
    /// </summary>
    /// <param name="name">The method name (e.g., "get", "set")</param>
    /// <param name="arity">The number of required arguments</param>
    /// <param name="implementation">The typed implementation delegate</param>
    public BuiltInTypeBuilder<TReceiver> Method(
        string name,
        int arity,
        Func<Interpreter, TReceiver, List<object?>, object?> implementation)
    {
        return Method(name, arity, arity, implementation);
    }

    /// <summary>
    /// Registers a method with variable arity (min to max arguments).
    /// </summary>
    /// <param name="name">The method name</param>
    /// <param name="minArity">Minimum number of arguments</param>
    /// <param name="maxArity">Maximum number of arguments (use int.MaxValue for variadic)</param>
    /// <param name="implementation">The typed implementation delegate</param>
    public BuiltInTypeBuilder<TReceiver> Method(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, TReceiver, List<object?>, object?> implementation)
    {
        // Wrap the typed implementation in the untyped BuiltInMethod signature
        _methods[name] = new BuiltInMethod(
            name,
            minArity,
            maxArity,
            (interp, recv, args) => implementation(interp, (TReceiver)recv!, args));
        return this;
    }

    /// <summary>
    /// Builds the member lookup for fast O(1) access.
    /// </summary>
    public BuiltInTypeMemberLookup<TReceiver> Build()
    {
        return new BuiltInTypeMemberLookup<TReceiver>(
            new Dictionary<string, BuiltInMethod>(_methods),
            new Dictionary<string, Func<TReceiver, object?>>(_properties),
            _requiresBind);
    }
}

/// <summary>
/// Builder for static-only types that have no receiver (Math, JSON, etc.).
/// </summary>
public sealed class BuiltInStaticBuilder
{
    private readonly Dictionary<string, BuiltInMethod> _methods = new();
    private readonly Dictionary<string, object?> _rawConstants = new();

    private BuiltInStaticBuilder() { }

    /// <summary>
    /// Creates a new builder for a static type.
    /// </summary>
    public static BuiltInStaticBuilder Create() => new();

    /// <summary>
    /// Registers a raw constant value (e.g., Math.PI, Math.E).
    /// Raw constants are returned as-is when accessed.
    /// Use this for values that should be returned directly without calling.
    /// </summary>
    public BuiltInStaticBuilder Constant(string name, object? value)
    {
        _rawConstants[name] = value;
        return this;
    }

    /// <summary>
    /// Registers a callable constant (e.g., Number.MAX_VALUE).
    /// Callable constants are wrapped as zero-arity methods for registry compatibility.
    /// Use this when the constant is accessed through GetStaticMethod which expects ISharpTSCallable.
    /// </summary>
    public BuiltInStaticBuilder CallableConstant(string name, object? value)
    {
        _methods[name] = new BuiltInMethod(name, 0, 0, (_, _, _) => value);
        return this;
    }

    /// <summary>
    /// Registers a static method with fixed arity.
    /// </summary>
    public BuiltInStaticBuilder Method(
        string name,
        int arity,
        Func<Interpreter, List<object?>, object?> implementation)
    {
        return Method(name, arity, arity, implementation);
    }

    /// <summary>
    /// Registers a static method with variable arity.
    /// </summary>
    public BuiltInStaticBuilder Method(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, List<object?>, object?> implementation)
    {
        _methods[name] = new BuiltInMethod(
            name,
            minArity,
            maxArity,
            (interp, _, args) => implementation(interp, args));
        return this;
    }

    /// <summary>
    /// Builds the static member lookup.
    /// </summary>
    public BuiltInStaticMemberLookup Build()
    {
        return new BuiltInStaticMemberLookup(
            new Dictionary<string, BuiltInMethod>(_methods),
            new Dictionary<string, object?>(_rawConstants));
    }
}
