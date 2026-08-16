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
    /// <param name="tsCode">
    /// Optional <c>TSnnnn</c> code matching the closest TypeScript diagnostic
    /// (e.g. <c>"TS2339"</c>). Used by the TS conformance runner to diff
    /// against <c>*.errors.txt</c> baselines. Leave null for SharpTS-only
    /// diagnostics with no canonical TS analogue.
    /// </param>
    public TypeCheckException(string message, int? line = null, int? column = null, string? tsCode = null)
        : base(DiagnosticCode.TypeError, message, line, column, tsCode: tsCode)
    {
    }

    /// <summary>
    /// Creates a type check exception with a specific diagnostic code.
    /// </summary>
    public TypeCheckException(DiagnosticCode code, string message, int? line = null, int? column = null, string? tsCode = null)
        : base(code, message, line, column, tsCode: tsCode)
    {
    }

    /// <summary>
    /// Creates a type check exception with an inner exception.
    /// </summary>
    public TypeCheckException(string message, Exception innerException, int? line = null, int? column = null, string? tsCode = null)
        : base(
            new Diagnostic(
                DiagnosticSeverity.Error,
                DiagnosticCode.TypeError,
                message,
                line.HasValue ? new SourceLocation(null, line.Value, column ?? 1) : null,
                TsCode: tsCode),
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
