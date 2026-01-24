// =============================================================================
// SourceLocation.cs - Source code location tracking
// =============================================================================
//
// Represents a location in source code with optional span (start to end).
// Used by all diagnostics to provide precise error locations.
//
// =============================================================================

namespace SharpTS.Diagnostics;

/// <summary>
/// Represents a location (or span) in source code.
/// </summary>
/// <param name="FilePath">Path to the source file (null for REPL/stdin).</param>
/// <param name="Line">Starting line number (1-based).</param>
/// <param name="Column">Starting column number (1-based).</param>
/// <param name="EndLine">Ending line number for spans (null for point locations).</param>
/// <param name="EndColumn">Ending column number for spans (null for point locations).</param>
public record SourceLocation(
    string? FilePath,
    int Line,
    int Column = 1,
    int? EndLine = null,
    int? EndColumn = null
)
{
    /// <summary>
    /// Creates a location from just a line number.
    /// </summary>
    public static SourceLocation FromLine(int line, string? filePath = null)
        => new(filePath, line);

    /// <summary>
    /// Creates a location from line and column.
    /// </summary>
    public static SourceLocation FromPosition(int line, int column, string? filePath = null)
        => new(filePath, line, column);

    /// <summary>
    /// Formats as "file:line:column" or "line:column" if no file.
    /// </summary>
    public override string ToString()
    {
        if (FilePath != null)
            return $"{FilePath}:{Line}:{Column}";
        return $"{Line}:{Column}";
    }
}
