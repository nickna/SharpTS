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
//   SHARPTS000 - General Error (unclassified errors)
//
// =============================================================================

namespace SharpTS.Compilation;

/// <summary>
/// Error codes for SharpTS diagnostics.
/// </summary>
public enum DiagnosticCode
{
    /// <summary>General/unclassified error.</summary>
    General = 0,

    /// <summary>Type checking error (type mismatch, invalid assignment, etc.).</summary>
    TypeError = 1,

    /// <summary>Parsing error (syntax errors, unexpected tokens).</summary>
    ParseError = 2,

    /// <summary>Module resolution error (import not found, circular dependency).</summary>
    ModuleError = 3,

    /// <summary>IL compilation error (emission failure).</summary>
    CompileError = 4,

    /// <summary>Configuration error (invalid config, missing required files).</summary>
    ConfigError = 5,
}

/// <summary>
/// Severity level for diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info,
}

/// <summary>
/// A single diagnostic (error, warning, or info message).
/// </summary>
public record Diagnostic(
    DiagnosticSeverity Severity,
    DiagnosticCode Code,
    string Message,
    string? FilePath = null,
    int Line = 1,
    int Column = 1
);

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
    public void ReportError(DiagnosticCode code, string message, string? filePath = null, int line = 1, int column = 1)
    {
        Report(new Diagnostic(DiagnosticSeverity.Error, code, message, filePath, line, column));
    }

    /// <summary>
    /// Reports a warning diagnostic.
    /// </summary>
    public void ReportWarning(DiagnosticCode code, string message, string? filePath = null, int line = 1, int column = 1)
    {
        Report(new Diagnostic(DiagnosticSeverity.Warning, code, message, filePath, line, column));
    }

    /// <summary>
    /// Reports an info diagnostic (suppressed in quiet mode).
    /// </summary>
    public void ReportInfo(string message, string? filePath = null, int line = 1, int column = 1)
    {
        if (QuietMode) return;
        Report(new Diagnostic(DiagnosticSeverity.Info, DiagnosticCode.General, message, filePath, line, column));
    }

    /// <summary>
    /// Reports a diagnostic in the appropriate format.
    /// </summary>
    public void Report(Diagnostic diagnostic)
    {
        if (MsBuildFormat)
        {
            ReportMsBuildFormat(diagnostic);
        }
        else
        {
            ReportHumanFormat(diagnostic);
        }
    }

    private void ReportMsBuildFormat(Diagnostic diagnostic)
    {
        // MSBuild format: file(line,col): severity CODE: message
        var file = diagnostic.FilePath ?? "unknown";
        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "message",
            _ => "error"
        };
        var code = $"SHARPTS{(int)diagnostic.Code:D3}";

        var output = $"{file}({diagnostic.Line},{diagnostic.Column}): {severity} {code}: {diagnostic.Message}";

        if (diagnostic.Severity == DiagnosticSeverity.Error)
        {
            Console.Error.WriteLine(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }

    private void ReportHumanFormat(Diagnostic diagnostic)
    {
        var prefix = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            _ => "Error"
        };

        string output;
        if (diagnostic.FilePath != null)
        {
            output = $"{prefix} at {diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}";
        }
        else
        {
            output = $"{prefix}: {diagnostic.Message}";
        }

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
    /// Creates a type error diagnostic.
    /// </summary>
    public static Diagnostic TypeError(string message, string? filePath = null, int line = 1, int column = 1)
        => new(DiagnosticSeverity.Error, DiagnosticCode.TypeError, message, filePath, line, column);

    /// <summary>
    /// Creates a parse error diagnostic.
    /// </summary>
    public static Diagnostic ParseError(string message, string? filePath = null, int line = 1, int column = 1)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ParseError, message, filePath, line, column);

    /// <summary>
    /// Creates a module error diagnostic.
    /// </summary>
    public static Diagnostic ModuleError(string message, string? filePath = null, int line = 1, int column = 1)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ModuleError, message, filePath, line, column);

    /// <summary>
    /// Creates a compile error diagnostic.
    /// </summary>
    public static Diagnostic CompileError(string message, string? filePath = null, int line = 1, int column = 1)
        => new(DiagnosticSeverity.Error, DiagnosticCode.CompileError, message, filePath, line, column);

    /// <summary>
    /// Creates a config error diagnostic.
    /// </summary>
    public static Diagnostic ConfigError(string message, string? filePath = null, int line = 1, int column = 1)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ConfigError, message, filePath, line, column);
}
