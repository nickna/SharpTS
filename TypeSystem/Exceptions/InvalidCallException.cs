namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Exception thrown when a function or method call is invalid (wrong arguments, not callable, etc.).
/// </summary>
public class InvalidCallException : TypeCheckException
{
    /// <summary>
    /// The type that was called (if available).
    /// </summary>
    public TypeInfo? CalleeType { get; init; }

    public InvalidCallException(string message, int? line = null, int? column = null)
        : base(message, line, column)
    {
    }

    public InvalidCallException(string message, TypeInfo calleeType, int? line = null, int? column = null)
        : base(message, line, column)
    {
        CalleeType = calleeType;
    }
}
