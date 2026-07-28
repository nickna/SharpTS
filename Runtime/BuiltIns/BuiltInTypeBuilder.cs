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
///         .MethodV2("get", 1, Get)
///         .MethodV2("set", 2, Set)
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
    /// Registers a V2 method with fixed arity using RuntimeValue (no boxing).
    /// The receiver is extracted from the RuntimeValue at call time.
    /// </summary>
    public BuiltInTypeBuilder<TReceiver> MethodV2(
        string name,
        int arity,
        Func<Interpreter, TReceiver, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        return MethodV2(name, arity, arity, implementation);
    }

    /// <summary>
    /// Registers a V2 method with variable arity using RuntimeValue (no boxing).
    /// </summary>
    public BuiltInTypeBuilder<TReceiver> MethodV2(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, TReceiver, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        _methods[name] = BuiltInMethod.CreateV2(name, minArity, maxArity,
            (interp, receiver, args) => implementation(interp, (TReceiver)receiver.ToObject()!, args));
        return this;
    }

    /// <summary>
    /// Registers a V2 variadic method with an explicit ECMA-262 spec length
    /// (the value visible as <c>fn.length</c>). Use when minArity differs
    /// from the spec length — e.g. <c>Array.prototype.slice</c> registered
    /// with <c>(0, 2, 2, …)</c>: minArity 0 (slice() is legal), maxArity 2,
    /// spec length 2.
    /// </summary>
    public BuiltInTypeBuilder<TReceiver> MethodV2(
        string name,
        int minArity,
        int maxArity,
        int specLength,
        Func<Interpreter, TReceiver, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        _methods[name] = BuiltInMethod.CreateV2(name, minArity, maxArity,
            (interp, receiver, args) => implementation(interp, (TReceiver)receiver.ToObject()!, args))
            .WithSpecLength(specLength);
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
        // Use CreateConstant so the built method carries IsConstant=true; the interpreter
        // property-access shortcut uses that flag to decide whether to auto-invoke (constants
        // materialize on read) vs. return the method reference (real zero-arity methods like
        // Date.now must be aliasable: `const n = Date.now; n()`).
        _methods[name] = BuiltInMethod.CreateConstant(name, value);
        return this;
    }

    /// <summary>
    /// Registers a V2 static method with fixed arity using RuntimeValue (no boxing).
    /// </summary>
    public BuiltInStaticBuilder MethodV2(
        string name,
        int arity,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        _methods[name] = BuiltInMethod.CreateV2(name, arity, implementation).AsNonConstructor();
        return this;
    }

    /// <summary>
    /// Registers a V2 static method with variable arity using RuntimeValue (no boxing).
    /// </summary>
    public BuiltInStaticBuilder MethodV2(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        _methods[name] = BuiltInMethod.CreateV2(name, minArity, maxArity, implementation)
            .AsNonConstructor();
        return this;
    }

    /// <summary>
    /// Registers a V2 variadic static method with an explicit ECMA-262 spec length
    /// (the value visible as <c>fn.length</c>). Use when minArity differs from the spec
    /// length — e.g. <c>Math.max</c> registered with <c>(0, int.MaxValue, 2, …)</c>:
    /// minArity 0 (Math.max() is legal), spec length 2.
    /// </summary>
    public BuiltInStaticBuilder MethodV2(
        string name,
        int minArity,
        int maxArity,
        int specLength,
        Func<Interpreter, RuntimeValue, ReadOnlySpan<RuntimeValue>, RuntimeValue> implementation)
    {
        _methods[name] = BuiltInMethod.CreateV2(name, minArity, maxArity, implementation)
            .WithSpecLength(specLength)
            .AsNonConstructor();
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
