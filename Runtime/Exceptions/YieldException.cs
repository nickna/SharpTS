namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a yield expression is executed in a generator.
/// </summary>
/// <remarks>
/// Used to suspend generator execution and propagate yielded values.
/// Thrown by <see cref="Interpreter"/> when executing a yield expression, and caught
/// by <see cref="SharpTSGenerator"/> to extract the yielded value. This is an
/// intentional use of exceptions for control flow (similar to ReturnException).
/// </remarks>
/// <seealso cref="ReturnException"/>
public class YieldException(object? value, bool isDelegating = false) : Exception
{
    /// <summary>
    /// The value being yielded.
    /// </summary>
    public object? Value { get; } = value;

    /// <summary>
    /// True if this is a yield* delegation to another iterable.
    /// </summary>
    public bool IsDelegating { get; } = isDelegating;
}
