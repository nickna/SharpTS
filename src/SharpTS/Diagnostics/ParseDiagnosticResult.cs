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
) : IDiagnosticResult
{
    /// <summary>Gets whether parsing succeeded without errors.</summary>
    public bool IsSuccess => DiagnosticResults.IsSuccess(Diagnostics);

    /// <summary>Gets only the error diagnostics.</summary>
    public IEnumerable<Diagnostic> Errors => DiagnosticResults.Errors(Diagnostics);

    /// <summary>Gets the count of errors.</summary>
    public int ErrorCount => DiagnosticResults.ErrorCount(Diagnostics);
}
