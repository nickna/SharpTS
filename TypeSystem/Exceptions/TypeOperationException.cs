namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Exception thrown when an invalid operation is performed (e.g., using 'super' outside a class).
/// </summary>
public class TypeOperationException : TypeCheckException
{
    public TypeOperationException(string message, int? line = null, int? column = null)
        : base(message, line, column)
    {
    }
}
