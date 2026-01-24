using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Base exception for all type checking errors.
/// Provides structured error information including optional line/column numbers.
/// </summary>
public class TypeCheckException : SharpTSException
{
    /// <summary>
    /// Creates a type check exception with message and optional location.
    /// </summary>
    public TypeCheckException(string message, int? line = null, int? column = null)
        : base(DiagnosticCode.TypeError, message, line, column)
    {
    }

    /// <summary>
    /// Creates a type check exception with a specific diagnostic code.
    /// </summary>
    public TypeCheckException(DiagnosticCode code, string message, int? line = null, int? column = null)
        : base(code, message, line, column)
    {
    }

    /// <summary>
    /// Creates a type check exception with an inner exception.
    /// </summary>
    public TypeCheckException(string message, Exception innerException, int? line = null, int? column = null)
        : base(
            new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCode.TypeError,
                message,
                line.HasValue ? new SourceLocation(null, line.Value, column ?? 1) : null),
            innerException)
    {
    }

    /// <summary>
    /// Creates a type check exception from a diagnostic.
    /// </summary>
    public TypeCheckException(Diagnostic diagnostic)
        : base(diagnostic)
    {
    }
}
