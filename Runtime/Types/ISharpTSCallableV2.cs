using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Next-generation callable interface using RuntimeValue to eliminate boxing overhead.
/// </summary>
/// <remarks>
/// <para>
/// This interface is the migration target for <see cref="ISharpTSCallable"/>. It uses
/// <see cref="RuntimeValue"/> instead of <c>object?</c> to avoid boxing primitives.
/// </para>
/// <para>
/// Use <see cref="CallableV2Adapter"/> to wrap legacy <see cref="ISharpTSCallable"/>
/// implementations for use with RuntimeValue-based callers.
/// </para>
/// <para>
/// Use <see cref="CallableLegacyAdapter"/> to wrap new <see cref="ISharpTSCallableV2"/>
/// implementations for use with legacy object?-based callers.
/// </para>
/// </remarks>
/// <seealso cref="ISharpTSCallable"/>
/// <seealso cref="RuntimeValue"/>
public interface ISharpTSCallableV2
{
    /// <summary>
    /// Gets the minimum number of required arguments.
    /// </summary>
    int Arity { get; }

    /// <summary>
    /// Invokes the callable with RuntimeValue arguments.
    /// </summary>
    /// <param name="interpreter">The interpreter context.</param>
    /// <param name="arguments">The arguments as RuntimeValue span.</param>
    /// <returns>The result as a RuntimeValue.</returns>
    RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments);
}

/// <summary>
/// Wraps a legacy <see cref="ISharpTSCallable"/> to implement <see cref="ISharpTSCallableV2"/>.
/// </summary>
/// <remarks>
/// This adapter allows legacy callables to be used in RuntimeValue-based call sites.
/// There is conversion overhead at the boundary, but it enables incremental migration.
/// </remarks>
public sealed class CallableV2Adapter : ISharpTSCallableV2
{
    private readonly ISharpTSCallable _inner;

    /// <summary>
    /// Creates an adapter wrapping a legacy callable.
    /// </summary>
    public CallableV2Adapter(ISharpTSCallable inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public int Arity => _inner.Arity();

    /// <inheritdoc />
    public RuntimeValue CallV2(Interpreter interpreter, ReadOnlySpan<RuntimeValue> arguments)
    {
        // Convert arguments to List<object?>
        var boxedArgs = new List<object?>(arguments.Length);
        foreach (var arg in arguments)
        {
            boxedArgs.Add(arg.ToObject());
        }

        // Call legacy method
        var result = _inner.Call(interpreter, boxedArgs);

        // Convert result to RuntimeValue
        return RuntimeValue.FromBoxed(result);
    }

    /// <summary>
    /// Gets the wrapped callable.
    /// </summary>
    public ISharpTSCallable Inner => _inner;
}

/// <summary>
/// Wraps a new <see cref="ISharpTSCallableV2"/> to implement legacy <see cref="ISharpTSCallable"/>.
/// </summary>
/// <remarks>
/// This adapter allows new RuntimeValue-based callables to be used in legacy call sites.
/// There is conversion overhead at the boundary, but it enables incremental migration.
/// </remarks>
public sealed class CallableLegacyAdapter : ISharpTSCallable
{
    private readonly ISharpTSCallableV2 _inner;

    /// <summary>
    /// Creates an adapter wrapping a RuntimeValue-based callable.
    /// </summary>
    public CallableLegacyAdapter(ISharpTSCallableV2 inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public int Arity() => _inner.Arity;

    /// <inheritdoc />
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        // Convert arguments to RuntimeValue array
        var rvArgs = new RuntimeValue[arguments.Count];
        for (int i = 0; i < arguments.Count; i++)
        {
            rvArgs[i] = RuntimeValue.FromBoxed(arguments[i]);
        }

        // Call V2 method
        var result = _inner.CallV2(interpreter, rvArgs);

        // Convert result to object
        return result.ToObject();
    }

    /// <summary>
    /// Gets the wrapped V2 callable.
    /// </summary>
    public ISharpTSCallableV2 Inner => _inner;
}

/// <summary>
/// Extension methods for ISharpTSCallable interop.
/// </summary>
public static class CallableExtensions
{
    /// <summary>
    /// Wraps a legacy callable to implement ISharpTSCallableV2.
    /// Returns the existing adapter if already wrapped.
    /// </summary>
    public static ISharpTSCallableV2 AsV2(this ISharpTSCallable callable)
    {
        // If it's already a V2 callable via legacy adapter, unwrap it
        if (callable is CallableLegacyAdapter legacyAdapter)
            return legacyAdapter.Inner;

        // If it already implements V2, return as-is
        if (callable is ISharpTSCallableV2 v2)
            return v2;

        // Wrap in V2 adapter
        return new CallableV2Adapter(callable);
    }

    /// <summary>
    /// Wraps a V2 callable to implement legacy ISharpTSCallable.
    /// Returns the existing adapter if already wrapped.
    /// </summary>
    public static ISharpTSCallable AsLegacy(this ISharpTSCallableV2 callable)
    {
        // If it's already wrapped in a V2 adapter, unwrap it
        if (callable is CallableV2Adapter v2Adapter)
            return v2Adapter.Inner;

        // If it already implements legacy, return as-is
        if (callable is ISharpTSCallable legacy)
            return legacy;

        // Wrap in legacy adapter
        return new CallableLegacyAdapter(callable);
    }

    /// <summary>
    /// Calls a legacy callable with RuntimeValue arguments, handling conversions.
    /// </summary>
    public static RuntimeValue CallWithRuntimeValues(
        this ISharpTSCallable callable,
        Interpreter interpreter,
        ReadOnlySpan<RuntimeValue> arguments)
    {
        // Fast path: if it implements V2, use it directly
        if (callable is ISharpTSCallableV2 v2)
            return v2.CallV2(interpreter, arguments);

        // Slow path: convert arguments
        var boxedArgs = new List<object?>(arguments.Length);
        foreach (var arg in arguments)
        {
            boxedArgs.Add(arg.ToObject());
        }

        var result = callable.Call(interpreter, boxedArgs);
        return RuntimeValue.FromBoxed(result);
    }
}
