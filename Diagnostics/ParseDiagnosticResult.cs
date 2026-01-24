// =============================================================================
// ParseDiagnosticResult.cs - Parser result with unified diagnostics
// =============================================================================

using SharpTS.Parsing;

namespace SharpTS.Diagnostics;

/// <summary>
/// Result of parsing with collected diagnostics.
/// </summary>
/// <param name="Statements">The parsed statements (may be partial if errors occurred).</param>
/// <param name="Diagnostics">All diagnostics collected during parsing.</param>
/// <param name="HitErrorLimit">Whether the error limit was reached.</param>
public record ParseDiagnosticResult(
    List<Stmt> Statements,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool HitErrorLimit = false
)
{
    /// <summary>
    /// Gets whether parsing succeeded without errors.
    /// </summary>
    public bool IsSuccess => !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets only the error diagnostics.
    /// </summary>
    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets the count of errors.
    /// </summary>
    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
}
