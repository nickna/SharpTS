namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Exception thrown when a type is not assignable to an expected type.
/// </summary>
public class TypeMismatchException : TypeCheckException
{
    /// <summary>
    /// The expected type.
    /// </summary>
    public TypeInfo Expected { get; init; }

    /// <summary>
    /// The actual type that was provided.
    /// </summary>
    public TypeInfo Actual { get; init; }

    public TypeMismatchException(TypeInfo expected, TypeInfo actual, int? line = null, int? column = null)
        : base($"Type '{actual}' is not assignable to type '{expected}'", line, column)
    {
        Expected = expected;
        Actual = actual;
    }

    public TypeMismatchException(string customMessage, TypeInfo expected, TypeInfo actual, int? line = null, int? column = null)
        : base(customMessage, line, column)
    {
        Expected = expected;
        Actual = actual;
    }
}
