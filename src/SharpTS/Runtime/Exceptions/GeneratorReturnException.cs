namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control-flow exception representing a <c>return</c> abrupt completion injected into a
/// suspended generator by <c>generator.return(v)</c> (ECMA-262 §27.5.3.4).
/// </summary>
/// <remarks>
/// Thrown from the generator worker thread at the suspended <c>yield</c> point so the
/// completion unwinds through any active <c>try</c>/<c>finally</c> blocks — running their
/// <c>finally</c> handlers — exactly as a <c>return v;</c> statement at that point would.
///
/// Unlike <see cref="ThrowException"/>, this is NOT a guest <c>throw</c>: it must bypass
/// <c>catch</c> clauses. The interpreter's <c>try</c>/<c>catch</c> and block executors
/// recognize it and convert it to an <see cref="Execution.ExecutionResult"/> of type
/// <c>Return</c>, which the existing abrupt-completion machinery routes through
/// <c>finally</c> handlers and out to the generator body.
/// </remarks>
public sealed class GeneratorReturnException(object? value) : Exception
{
    /// <summary>The value supplied to <c>generator.return(value)</c>.</summary>
    public object? Value { get; } = value;
}
