namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a continue statement is executed.
/// </summary>
/// <remarks>
/// Used to skip the remainder of the current loop iteration and proceed to the next.
/// Thrown by <see cref="Interpreter"/> when executing a continue statement, and caught
/// by the enclosing loop to advance iteration. This is an intentional use of exceptions
/// for control flow.
/// </remarks>
/// <seealso cref="BreakException"/>
/// <seealso cref="ReturnException"/>
public class ContinueException : Exception
{
}
