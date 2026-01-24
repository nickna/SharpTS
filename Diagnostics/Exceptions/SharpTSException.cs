// =============================================================================
// SharpTSException.cs - Base exception for all SharpTS errors
// =============================================================================
//
// Base exception class that carries a Diagnostic for structured error reporting.
// All SharpTS-specific exceptions should inherit from this class.
//
// =============================================================================

namespace SharpTS.Diagnostics.Exceptions;

/// <summary>
/// Base exception for all SharpTS errors.
/// Carries a structured Diagnostic for unified error reporting.
/// </summary>
public class SharpTSException : Exception
{
    /// <summary>
    /// The structured diagnostic information for this error.
    /// </summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>
    /// Optional line number where the error occurred.
    /// </summary>
    public int? Line => Diagnostic.Location?.Line;

    /// <summary>
    /// Optional column number where the error occurred.
    /// </summary>
    public int? Column => Diagnostic.Location?.Column;

    /// <summary>
    /// Creates an exception with a full diagnostic.
    /// Uses the formatted message for backward compatibility with code that reads ex.Message.
    /// </summary>
    public SharpTSException(Diagnostic diagnostic)
        : base(FormatMessage(diagnostic))
    {
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// Formats the diagnostic message with the appropriate prefix (Type Error, Runtime Error, etc.).
    /// </summary>
    private static string FormatMessage(Diagnostic diagnostic)
    {
        // Use ToHumanFormat for the full formatted message with prefix
        return diagnostic.ToHumanFormat();
    }

    /// <summary>
    /// Creates an exception with code, message, and optional location.
    /// </summary>
    public SharpTSException(DiagnosticCode code, string message, SourceLocation? location = null)
        : this(new Diagnostic(DiagnosticSeverity.Error, code, message, location))
    {
    }

    /// <summary>
    /// Creates an exception with code, message, line, and optional column.
    /// </summary>
    public SharpTSException(DiagnosticCode code, string message, int? line, int? column = null, string? filePath = null)
        : this(new Diagnostic(
            DiagnosticSeverity.Error,
            code,
            message,
            line.HasValue ? new SourceLocation(filePath, line.Value, column ?? 1) : null))
    {
    }

    /// <summary>
    /// Creates an exception with an inner exception.
    /// </summary>
    public SharpTSException(Diagnostic diagnostic, Exception innerException)
        : base(FormatMessage(diagnostic), innerException)
    {
        Diagnostic = diagnostic;
    }

    /// <summary>
    /// Returns the human-readable diagnostic message.
    /// </summary>
    public override string ToString() => Diagnostic.ToHumanFormat();
}
