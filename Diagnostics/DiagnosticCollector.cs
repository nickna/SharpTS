// =============================================================================
// DiagnosticCollector.cs - Collects diagnostics with error limiting
// =============================================================================
//
// Replaces the separate error lists in Parser and TypeChecker.
// Provides error recovery by collecting multiple errors before stopping.
//
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// Collects diagnostics during parsing/type checking with error limiting.
/// </summary>
public class DiagnosticCollector
{
    private readonly List<Diagnostic> _diagnostics = [];

    /// <summary>
    /// Maximum number of errors before stopping collection. Default is 10.
    /// </summary>
    public int MaxErrors { get; set; } = 10;

    /// <summary>
    /// Gets whether the error limit has been reached.
    /// </summary>
    public bool HitErrorLimit => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) >= MaxErrors;

    /// <summary>
    /// Gets all collected diagnostics.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Gets whether any errors have been recorded.
    /// </summary>
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets whether any warnings have been recorded.
    /// </summary>
    public bool HasWarnings => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

    /// <summary>
    /// Gets the count of error diagnostics.
    /// </summary>
    public int ErrorCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Adds a diagnostic to the collection.
    /// </summary>
    /// <returns>True if the diagnostic was added, false if error limit reached.</returns>
    public bool Add(Diagnostic diagnostic)
    {
        if (diagnostic.Severity == DiagnosticSeverity.Error && HitErrorLimit)
            return false;

        _diagnostics.Add(diagnostic);
        return true;
    }

    /// <summary>
    /// Adds an error diagnostic.
    /// </summary>
    public bool AddError(DiagnosticCode code, string message, SourceLocation? location = null)
    {
        return Add(new Diagnostic(DiagnosticSeverity.Error, code, message, location));
    }

    /// <summary>
    /// Adds a warning diagnostic.
    /// </summary>
    public bool AddWarning(DiagnosticCode code, string message, SourceLocation? location = null)
    {
        return Add(new Diagnostic(DiagnosticSeverity.Warning, code, message, location));
    }

    /// <summary>
    /// Adds an info diagnostic.
    /// </summary>
    public bool AddInfo(string message, SourceLocation? location = null)
    {
        return Add(new Diagnostic(DiagnosticSeverity.Info, DiagnosticCode.General, message, location));
    }

    /// <summary>
    /// Clears all collected diagnostics.
    /// </summary>
    public void Clear() => _diagnostics.Clear();

    /// <summary>
    /// Gets all error diagnostics.
    /// </summary>
    public IEnumerable<Diagnostic> Errors => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>
    /// Gets all warning diagnostics.
    /// </summary>
    public IEnumerable<Diagnostic> Warnings => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning);
}
