namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control flow exception thrown when a TypeScript throw statement is executed.
/// </summary>
/// <remarks>
/// Wraps user-thrown values from TypeScript code (e.g., <c>throw new Error("msg")</c>
/// or <c>throw "error"</c>). Thrown by <see cref="Interpreter"/> when executing a throw
/// statement, and caught by try/catch blocks to handle errors. The <see cref="Value"/>
/// property holds the thrown object, which can be any TypeScript value.
/// </remarks>
/// <seealso cref="ReturnException"/>
public class ThrowException(object? value) : Exception
{
    public object? Value { get; } = value;
}
