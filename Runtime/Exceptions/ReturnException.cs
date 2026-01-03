namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a return statement is executed.
/// </summary>
/// <remarks>
/// Used to unwind the call stack and propagate return values from functions.
/// Thrown by <see cref="Interpreter"/> when executing a return statement, and caught
/// by <see cref="SharpTSFunction"/> or <see cref="SharpTSArrowFunction"/> to extract
/// the returned value. This is an intentional use of exceptions for control flow.
/// </remarks>
/// <seealso cref="BreakException"/>
/// <seealso cref="ContinueException"/>
public class ReturnException(object? value) : Exception
{
    public object? Value { get; } = value;
}
