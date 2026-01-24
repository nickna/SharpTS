// =============================================================================
// Diagnostic.cs - Unified diagnostic record
// =============================================================================
//
// Represents a single diagnostic (error, warning, or info) with location,
// code, and optional metadata. Provides formatting for both human-readable
// and MSBuild-compatible output.
//
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// A single diagnostic (error, warning, or info message).
/// </summary>
/// <param name="Severity">The severity level of the diagnostic.</param>
/// <param name="Code">The diagnostic code for categorization.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Location">Optional source location for the diagnostic.</param>
/// <param name="Properties">Optional additional properties for context.</param>
public record Diagnostic(
    DiagnosticSeverity Severity,
    DiagnosticCode Code,
    string Message,
    SourceLocation? Location = null,
    IReadOnlyDictionary<string, object>? Properties = null
)
{
    /// <summary>
    /// Gets the file path from location, or null.
    /// </summary>
    public string? FilePath => Location?.FilePath;

    /// <summary>
    /// Gets the line number from location, or 1.
    /// </summary>
    public int Line => Location?.Line ?? 1;

    /// <summary>
    /// Gets the column number from location, or 1.
    /// </summary>
    public int Column => Location?.Column ?? 1;

    /// <summary>
    /// Formats the diagnostic in MSBuild format for IDE integration.
    /// Format: file(line,col): severity CODE: message
    /// </summary>
    public string ToMsBuildFormat()
    {
        var file = Location?.FilePath ?? "unknown";
        var severity = Severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "message",
            _ => "error"
        };
        var code = $"SHARPTS{(int)Code:D3}";

        return $"{file}({Line},{Column}): {severity} {code}: {Message}";
    }

    /// <summary>
    /// Formats the diagnostic in human-readable format.
    /// Format: Severity at file:line:column: message
    /// </summary>
    public string ToHumanFormat()
    {
        var prefix = Severity switch
        {
            DiagnosticSeverity.Error => GetErrorPrefix(),
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            _ => "Error"
        };

        if (Location?.FilePath != null)
        {
            return $"{prefix} at {Location.FilePath}:{Line}:{Column}: {Message}";
        }
        else if (Location != null)
        {
            return $"{prefix} at line {Line}: {Message}";
        }
        return $"{prefix}: {Message}";
    }

    /// <summary>
    /// Gets the appropriate error prefix based on code category.
    /// Preserves legacy "Type Error:", "Parse Error:", "Runtime Error:" prefixes.
    /// </summary>
    private string GetErrorPrefix()
    {
        return Code switch
        {
            DiagnosticCode.TypeError or
            DiagnosticCode.TypeMismatch or
            DiagnosticCode.UndefinedMember or
            DiagnosticCode.InvalidCall or
            DiagnosticCode.TypeOperation => "Type Error",

            DiagnosticCode.ParseError or
            DiagnosticCode.UnexpectedToken or
            DiagnosticCode.SyntaxError => "Parse Error",

            DiagnosticCode.RuntimeError or
            DiagnosticCode.DivisionByZero or
            DiagnosticCode.NullReference or
            DiagnosticCode.IndexOutOfRange or
            DiagnosticCode.InvalidOperation => "Runtime Error",

            DiagnosticCode.ModuleError or
            DiagnosticCode.ModuleNotFound or
            DiagnosticCode.CircularDependency => "Module Error",

            DiagnosticCode.CompileError or
            DiagnosticCode.ILValidation => "Compile Error",

            DiagnosticCode.ConfigError => "Config Error",

            _ => "Error"
        };
    }

    /// <summary>
    /// Default string representation uses human format.
    /// </summary>
    public override string ToString() => ToHumanFormat();

    // Factory methods for common diagnostics

    /// <summary>Creates a type error diagnostic.</summary>
    public static Diagnostic TypeError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.TypeError, message, location);

    /// <summary>Creates a type mismatch diagnostic.</summary>
    public static Diagnostic TypeMismatch(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.TypeMismatch, message, location);

    /// <summary>Creates an undefined member diagnostic.</summary>
    public static Diagnostic UndefinedMember(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.UndefinedMember, message, location);

    /// <summary>Creates an invalid call diagnostic.</summary>
    public static Diagnostic InvalidCall(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.InvalidCall, message, location);

    /// <summary>Creates a parse error diagnostic.</summary>
    public static Diagnostic ParseError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ParseError, message, location);

    /// <summary>Creates a module error diagnostic.</summary>
    public static Diagnostic ModuleError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ModuleError, message, location);

    /// <summary>Creates a compile error diagnostic.</summary>
    public static Diagnostic CompileError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.CompileError, message, location);

    /// <summary>Creates a runtime error diagnostic.</summary>
    public static Diagnostic RuntimeError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.RuntimeError, message, location);

    /// <summary>Creates a config error diagnostic.</summary>
    public static Diagnostic ConfigError(string message, SourceLocation? location = null)
        => new(DiagnosticSeverity.Error, DiagnosticCode.ConfigError, message, location);
}
