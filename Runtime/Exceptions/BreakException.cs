namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a break statement is executed.
/// </summary>
/// <remarks>
/// Used to exit loops (while, for, for...of) and switch statements early.
/// Thrown by <see cref="Interpreter"/> when executing a break statement, and caught
/// by the enclosing loop or switch to terminate iteration. This is an intentional
/// use of exceptions for control flow. When TargetLabel is set, the break targets
/// a specific labeled statement.
/// </remarks>
/// <seealso cref="ContinueException"/>
/// <seealso cref="ReturnException"/>
public class BreakException : Exception
{
    /// <summary>
    /// The target label for labeled break statements, or null for unlabeled break.
    /// </summary>
    public string? TargetLabel { get; }

    public BreakException(string? targetLabel = null) => TargetLabel = targetLabel;
}
