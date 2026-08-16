// =============================================================================
// TypeCheckDiagnosticResult.cs - Type checker result with unified diagnostics
// =============================================================================

using SharpTS.TypeSystem;

namespace SharpTS.Diagnostics;

/// <summary>
/// Result of type checking with collected diagnostics.
/// </summary>
/// <param name="TypeMap">The type information map for all expressions.</param>
/// <param name="Diagnostics">All diagnostics collected during type checking.</param>
/// <param name="HitErrorLimit">Whether the error limit was reached.</param>
public record TypeCheckDiagnosticResult(
    TypeMap TypeMap,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool HitErrorLimit = false
) : IDiagnosticResult
{
    /// <summary>Gets whether type checking succeeded without errors.</summary>
    public bool IsSuccess => DiagnosticResults.IsSuccess(Diagnostics);

    /// <summary>Gets only the error diagnostics.</summary>
    public IEnumerable<Diagnostic> Errors => DiagnosticResults.Errors(Diagnostics);

    /// <summary>Gets the count of errors.</summary>
    public int ErrorCount => DiagnosticResults.ErrorCount(Diagnostics);
}
