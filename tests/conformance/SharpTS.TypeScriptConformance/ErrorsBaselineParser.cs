using System.Text.RegularExpressions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Parses TypeScript's <c>*.errors.txt</c> baseline format. Each diagnostic
/// occupies one header line followed by zero or more indented continuation
/// lines that explain the error in detail. Example:
///
/// <code>
/// conditionalTypes1.ts(12,5): error TS2322: Type 'T' is not assignable to type 'NonNullable&lt;T&gt;'.
///   Type 'T' is not assignable to type '{}'.
/// </code>
///
/// We extract <see cref="BaselineDiagnostic"/> tuples (line, code) for the
/// header lines only — continuation lines are explanatory text, not new
/// diagnostics. Column is intentionally dropped (the runner matches on line
/// only; see <see cref="TypeScriptConformanceOutcome"/>).
/// </summary>
public static class ErrorsBaselineParser
{
    // file(line,col): error TSnnnn:
    // Filename can contain dots and slashes (multi-file tests use names like ./a.ts).
    private static readonly Regex HeaderRegex = new(
        @"^(?<file>[^(]+)\((?<line>\d+),(?<col>\d+)\):\s+error\s+(?<code>TS\d+):",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a baseline file content into a list of (line, code) tuples.
    /// Returns an empty list if no header lines match — that's a valid
    /// baseline shape (an .errors.txt with only continuation noise) and not
    /// a parse failure.
    /// </summary>
    public static IReadOnlyList<BaselineDiagnostic> Parse(string content)
    {
        var result = new List<BaselineDiagnostic>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = HeaderRegex.Match(line);
            if (!m.Success) continue;
            var lineNum = int.Parse(m.Groups["line"].Value);
            var code = m.Groups["code"].Value;
            result.Add(new BaselineDiagnostic(lineNum, code));
        }
        return result;
    }
}
