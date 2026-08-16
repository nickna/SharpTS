// =============================================================================
// IDiagnosticResult.cs - Shared diagnostics contract + projection logic
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// A phase result (e.g. <see cref="ParseDiagnosticResult"/>,
/// <see cref="TypeCheckDiagnosticResult"/>) that carries the diagnostics collected
/// during that phase, plus the common success/error projections over them.
/// </summary>
/// <remarks>
/// The projection bodies live once in <see cref="DiagnosticResults"/>; each result type
/// exposes them as instance properties that forward to it, so the logic is no longer
/// copied per result type while concrete-type access (<c>result.IsSuccess</c>) keeps
/// working without an extra <c>using</c> at every call site.
/// </remarks>
public interface IDiagnosticResult
{
    /// <summary>All diagnostics collected during the phase.</summary>
    IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Gets whether the phase succeeded without errors.</summary>
    bool IsSuccess { get; }

    /// <summary>Gets only the error diagnostics.</summary>
    IEnumerable<Diagnostic> Errors { get; }

    /// <summary>Gets the count of errors.</summary>
    int ErrorCount { get; }
}

/// <summary>
/// Single source of truth for the success/error projections shared by every
/// <see cref="IDiagnosticResult"/>.
/// </summary>
public static class DiagnosticResults
{
    /// <summary>Whether the supplied diagnostics contain no errors.</summary>
    public static bool IsSuccess(IReadOnlyList<Diagnostic> diagnostics) =>
        !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Only the error diagnostics.</summary>
    public static IEnumerable<Diagnostic> Errors(IReadOnlyList<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>The count of error diagnostics.</summary>
    public static int ErrorCount(IReadOnlyList<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
}
