// =============================================================================
// InterpreterException.cs - Exception for interpreter runtime errors
// =============================================================================

using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;

namespace SharpTS.Execution;

/// <summary>
/// Exception thrown during tree-walking interpretation.
/// </summary>
public class InterpreterException : RuntimeException
{
    /// <summary>
    /// Creates an interpreter exception with a message and optional line/file.
    /// </summary>
    public InterpreterException(string message, int? line = null, string? filePath = null)
        : base(message, line.HasValue ? new SourceLocation(filePath, line.Value) : null)
    {
    }

    /// <summary>
    /// Creates an interpreter exception with a specific code.
    /// </summary>
    public InterpreterException(DiagnosticCode code, string message, int? line = null, string? filePath = null)
        : base(code, message, line.HasValue ? new SourceLocation(filePath, line.Value) : null)
    {
    }

    /// <summary>
    /// Creates an interpreter exception from a diagnostic.
    /// </summary>
    public InterpreterException(Diagnostic diagnostic)
        : base(diagnostic)
    {
    }
}
