// =============================================================================
// DiagnosticSeverity.cs - Diagnostic severity levels
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// Severity level for diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Error that prevents compilation/execution.</summary>
    Error,

    /// <summary>Warning that may indicate a problem.</summary>
    Warning,

    /// <summary>Informational message.</summary>
    Info,
}
