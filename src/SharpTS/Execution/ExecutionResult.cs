using SharpTS.Runtime;

namespace SharpTS.Execution;

/// <summary>
/// Represents the result of executing a statement, including flow control information.
/// </summary>
/// <remarks>
/// Replaces exception-based flow control (ReturnException, BreakException, etc.)
/// with a lightweight struct to avoid the high cost of stack unwinding.
/// </remarks>
public readonly struct ExecutionResult
{
    public enum ResultType
    {
        None,       // Normal completion, proceed to next statement
        Return,     // return keyword encountered
        Break,      // break keyword encountered
        Continue,   // continue keyword encountered
        Throw       // throw keyword encountered or runtime error
    }

    public readonly ResultType Type;
    public readonly RuntimeValue Value;
    public readonly string? TargetLabel;

    /// <summary>
    /// For a <see cref="ResultType.Throw"/> result, whether the thrown value originated from a
    /// genuine guest <c>throw</c> (true) rather than a translated host C# exception (false).
    /// Carried across function boundaries so <see cref="Runtime.Exceptions.ThrowException.FromResult"/>
    /// preserves guest-error identity: a guest <c>throw "TypeError: x"</c> stays the exact string
    /// instead of being flattened to a plain host <c>Exception</c> and re-typed at a downstream
    /// <c>catch</c> (the cross-boundary residual of #694). Meaningless for non-Throw results.
    /// </summary>
    public readonly bool FromGuestThrow;

    private ExecutionResult(ResultType type, RuntimeValue value = default, string? targetLabel = null, bool fromGuestThrow = false)
    {
        Type = type;
        Value = value;
        TargetLabel = targetLabel;
        FromGuestThrow = fromGuestThrow;
    }

    public static ExecutionResult Success() => new(ResultType.None);
    public static ExecutionResult Return(RuntimeValue value) => new(ResultType.Return, value);
    public static ExecutionResult Return(object? value) => new(ResultType.Return, RuntimeValue.FromBoxed(value));
    public static ExecutionResult Break(string? label = null) => new(ResultType.Break, default, label);
    public static ExecutionResult Continue(string? label = null) => new(ResultType.Continue, default, label);

    // The parameterless Throw factories default to guest origin — the overwhelmingly common case
    // (every guest `throw`). Host-translated throws (a C# exception caught and surfaced) must use
    // the explicit overloads with fromGuestThrow:false so their identity is not re-asserted as guest.
    public static ExecutionResult Throw(RuntimeValue value) => new(ResultType.Throw, value, fromGuestThrow: true);
    public static ExecutionResult Throw(object? value) => new(ResultType.Throw, RuntimeValue.FromBoxed(value), fromGuestThrow: true);
    public static ExecutionResult Throw(RuntimeValue value, bool fromGuestThrow) => new(ResultType.Throw, value, fromGuestThrow: fromGuestThrow);
    public static ExecutionResult Throw(object? value, bool fromGuestThrow) => new(ResultType.Throw, RuntimeValue.FromBoxed(value), fromGuestThrow: fromGuestThrow);

    public bool IsNormal => Type == ResultType.None;
    public bool IsAbrupt => Type != ResultType.None;
}
