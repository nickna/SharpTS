using SharpTS.Diagnostics;

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

    public TypeMismatchException(TypeInfo expected, TypeInfo actual, int? line = null, int? column = null, string? tsCode = null)
        : base(DiagnosticCode.TypeMismatch, $"Type '{actual}' is not assignable to type '{expected}'", line, column, tsCode: tsCode)
    {
        Expected = expected;
        Actual = actual;
    }

    public TypeMismatchException(string customMessage, TypeInfo expected, TypeInfo actual, int? line = null, int? column = null, string? tsCode = null)
        : base(DiagnosticCode.TypeMismatch, customMessage, line, column, tsCode: tsCode)
    {
        Expected = expected;
        Actual = actual;
    }
}
