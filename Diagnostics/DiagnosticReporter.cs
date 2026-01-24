// =============================================================================
// DiagnosticReporter.cs - Structured error reporting for MSBuild integration
// =============================================================================
//
// Provides error and warning reporting in both human-readable and MSBuild formats.
// MSBuild format enables IDE integration (clickable error locations, Error List).
//
// Error codes:
//   SHARPTS001 - Type Error (type mismatch, invalid assignment, etc.)
//   SHARPTS002 - Parse Error (syntax errors, unexpected tokens)
//   SHARPTS003 - Module Error (import resolution, circular dependencies)
//   SHARPTS004 - Compile Error (IL emission failures)
//   SHARPTS005 - Config Error (invalid configuration, missing files)
//   SHARPTS006 - Runtime Error (interpreter errors)
//   SHARPTS000 - General Error (unclassified errors)
//
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// Reports diagnostics in either human-readable or MSBuild format.
/// </summary>
public class DiagnosticReporter
{
    /// <summary>
    /// When true, output diagnostics in MSBuild-compatible format.
    /// Format: file(line,col): error|warning CODE: message
    /// </summary>
    public bool MsBuildFormat { get; set; }

    /// <summary>
    /// When true, suppress informational messages.
    /// </summary>
    public bool QuietMode { get; set; }

    /// <summary>
    /// Reports an error diagnostic.
    /// </summary>
    public void ReportError(DiagnosticCode code, string message, SourceLocation? location = null)
    {
        Report(new Diagnostic(DiagnosticSeverity.Error, code, message, location));
    }

    /// <summary>
    /// Reports a warning diagnostic.
    /// </summary>
    public void ReportWarning(DiagnosticCode code, string message, SourceLocation? location = null)
    {
        Report(new Diagnostic(DiagnosticSeverity.Warning, code, message, location));
    }

    /// <summary>
    /// Reports an info diagnostic (suppressed in quiet mode).
    /// </summary>
    public void ReportInfo(string message, SourceLocation? location = null)
    {
        if (QuietMode) return;
        Report(new Diagnostic(DiagnosticSeverity.Info, DiagnosticCode.General, message, location));
    }

    /// <summary>
    /// Reports a diagnostic in the appropriate format.
    /// </summary>
    public void Report(Diagnostic diagnostic)
    {
        if (diagnostic.Severity == DiagnosticSeverity.Info && QuietMode)
            return;

        var output = MsBuildFormat
            ? diagnostic.ToMsBuildFormat()
            : diagnostic.ToHumanFormat();

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            Console.Error.WriteLine(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }

    /// <summary>
    /// Reports all diagnostics from a collector.
    /// </summary>
    public void ReportAll(DiagnosticCollector collector)
    {
        foreach (var diagnostic in collector.Diagnostics)
        {
            Report(diagnostic);
        }
    }

    /// <summary>
    /// Reports all diagnostics from a collection.
    /// </summary>
    public void ReportAll(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Report(diagnostic);
        }
    }
}
