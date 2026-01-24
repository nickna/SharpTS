// =============================================================================
// RuntimeException.cs - Exception for runtime/interpreter errors
// =============================================================================

namespace SharpTS.Diagnostics.Exceptions;

/// <summary>
/// Exception thrown during runtime interpretation.
/// </summary>
public class RuntimeException : SharpTSException
{
    /// <summary>
    /// Creates a runtime exception with a message and optional location.
    /// </summary>
    public RuntimeException(string message, SourceLocation? location = null)
        : base(DiagnosticCode.RuntimeError, message, location)
    {
    }

    /// <summary>
    /// Creates a runtime exception with a specific code and message.
    /// </summary>
    public RuntimeException(DiagnosticCode code, string message, SourceLocation? location = null)
        : base(code, message, location)
    {
    }

    /// <summary>
    /// Creates a runtime exception with line number.
    /// </summary>
    public RuntimeException(string message, int? line, string? filePath = null)
        : base(DiagnosticCode.RuntimeError, message, line, null, filePath)
    {
    }

    /// <summary>
    /// Creates a runtime exception from a diagnostic.
    /// </summary>
    public RuntimeException(Diagnostic diagnostic)
        : base(diagnostic)
    {
    }
}
