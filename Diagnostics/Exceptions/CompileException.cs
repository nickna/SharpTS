// =============================================================================
// CompileException.cs - Exception for IL compilation errors
// =============================================================================

namespace SharpTS.Diagnostics.Exceptions;

/// <summary>
/// Exception thrown during IL compilation.
/// </summary>
public class CompileException : SharpTSException
{
    /// <summary>
    /// Creates a compile exception with a message and optional location.
    /// </summary>
    public CompileException(string message, SourceLocation? location = null)
        : base(DiagnosticCode.CompileError, message, location)
    {
    }

    /// <summary>
    /// Creates a compile exception with a specific code and message.
    /// </summary>
    public CompileException(DiagnosticCode code, string message, SourceLocation? location = null)
        : base(code, message, location)
    {
    }

    /// <summary>
    /// Creates a compile exception with line number.
    /// </summary>
    public CompileException(string message, int? line, string? filePath = null)
        : base(DiagnosticCode.CompileError, message, line, null, filePath)
    {
    }

    /// <summary>
    /// Creates a compile exception from a diagnostic.
    /// </summary>
    public CompileException(Diagnostic diagnostic)
        : base(diagnostic)
    {
    }
}
