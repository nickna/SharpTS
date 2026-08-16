using SharpTS.Diagnostics;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Decides whether a given source file should be type-checked.
/// Mirrors tsc's <c>checkJs</c> behavior: <c>.ts</c> files are checked by default,
/// <c>.js</c> files are not unless opted in. File-level <c>// @ts-check</c> /
/// <c>// @ts-nocheck</c> pragmas override the extension default.
/// </summary>
public static class TypeCheckPolicy
{
    /// <summary>
    /// Returns true when the type checker should run for this source.
    /// </summary>
    /// <param name="filePath">
    /// Source file path (extension matters). Pass null/empty for in-memory sources
    /// (REPL, eval) — those follow <paramref name="checkJsDefault"/> and pragmas.
    /// </param>
    /// <param name="pragmas">Pragma directives recovered by the lexer.</param>
    /// <param name="checkJsDefault">
    /// Global default for <c>.js</c> files. Mirrors tsc's <c>checkJs</c> tsconfig
    /// option / <c>--check-js</c> CLI flag. Default false (matches tsc).
    /// </param>
    public static bool ShouldTypeCheck(string? filePath, TypeScriptPragmas pragmas, bool checkJsDefault)
    {
        // File-level pragmas always win (tsc semantics).
        if (pragmas.HasTsNoCheck) return false;
        if (pragmas.HasTsCheck) return true;

        // No pragma — fall back to extension-based decision.
        bool isJs = IsJavaScriptFile(filePath);
        if (isJs) return checkJsDefault;

        // .ts / .tsx / unknown extension → check.
        return true;
    }

    private static bool IsJavaScriptFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".js" or ".cjs" or ".mjs" or ".jsx";
    }

    /// <summary>
    /// Applies <c>// @ts-ignore</c> and <c>// @ts-expect-error</c> directives to a diagnostic
    /// stream. Ignored diagnostics are dropped silently. <c>@ts-expect-error</c> directives
    /// that did NOT match a diagnostic on the next line produce a synthetic "unused suppression"
    /// diagnostic, matching tsc's behavior.
    /// </summary>
    public static IReadOnlyList<Diagnostic> ApplyLineDirectives(
        IEnumerable<Diagnostic> diagnostics,
        TypeScriptPragmas pragmas)
    {
        if (pragmas.IgnoreLines.Count == 0 && pragmas.ExpectErrorLines.Count == 0)
            return diagnostics as IReadOnlyList<Diagnostic> ?? diagnostics.ToList();

        var matchedExpectErrorLines = new HashSet<int>();
        var kept = new List<Diagnostic>();

        foreach (var d in diagnostics)
        {
            if (d.Severity != DiagnosticSeverity.Error)
            {
                kept.Add(d);
                continue;
            }

            // A pragma on line N suppresses errors on line N+1.
            if (pragmas.IgnoreLines.Contains(d.Line - 1))
                continue;

            if (pragmas.ExpectErrorLines.Contains(d.Line - 1))
            {
                matchedExpectErrorLines.Add(d.Line - 1);
                continue;
            }

            kept.Add(d);
        }

        // Unused @ts-expect-error → synthetic diagnostic. tsc emits TS2578.
        foreach (var line in pragmas.ExpectErrorLines)
        {
            if (!matchedExpectErrorLines.Contains(line))
            {
                kept.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    DiagnosticCode.TypeError,
                    "Unused '@ts-expect-error' directive.",
                    new SourceLocation(null, line, 1)));
            }
        }

        return kept;
    }
}
