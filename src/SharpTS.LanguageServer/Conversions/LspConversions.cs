using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Newtonsoft.Json.Linq;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using SharpDiagnostic = SharpTS.Diagnostics.Diagnostic;
using SharpSeverity = SharpTS.Diagnostics.DiagnosticSeverity;

namespace SharpTS.LanguageServer.Conversions;

/// <summary>
/// Coexistence with the built-in TypeScript server (see docs/plans/lsp-server.md §4):
/// <list type="bullet">
/// <item><c>SharpTsOnly</c> (default) — publish only SharpTS-specific diagnostics
/// (no <c>TsCode</c>); tsc-equivalent ones (with a <c>TSnnnn</c> code) are suppressed
/// because tsserver already shows them.</item>
/// <item><c>All</c> — publish everything (SharpTS as source of truth).</item>
/// <item><c>Off</c> — publish nothing.</item>
/// </list>
/// </summary>
public enum DiagnosticPublishMode { SharpTsOnly, All, Off }

/// <summary>Converts SharpTS diagnostics to LSP diagnostics, applying the publish-mode filter.</summary>
public static class LspConversions
{
    public static List<Diagnostic> ToLsp(IEnumerable<SharpDiagnostic> diags, string text, DiagnosticPublishMode mode)
    {
        var result = new List<Diagnostic>();
        if (mode == DiagnosticPublishMode.Off) return result;

        string[] lines = text.Split('\n');
        foreach (var d in diags)
        {
            if (mode == DiagnosticPublishMode.SharpTsOnly && d.TsCode != null) continue; // tsserver covers it
            result.Add(new Diagnostic
            {
                Range = ToRange(d, lines),
                Severity = ToSeverity(d.Severity),
                Message = d.Message,
                Source = "sharpts",
                Data = d.Properties is null
                    ? null
                    : JObject.FromObject(d.Properties),
            });
        }
        return result;
    }

    // SharpTS SourceLocation is 1-based with optional end; LSP is 0-based. When no end span
    // is present (most diagnostics are line-only today, pending AST spans / Phase 4), extend
    // to end-of-line so the squiggle is visible rather than a zero-width caret.
    private static Range ToRange(SharpDiagnostic d, string[] lines)
    {
        int line = Math.Max(0, (d.Location?.Line ?? 1) - 1);
        int col = Math.Max(0, (d.Location?.Column ?? 1) - 1);
        int endLine = Math.Max(0, (d.Location?.EndLine ?? d.Location?.Line ?? 1) - 1);

        int endCol;
        if (d.Location?.EndColumn is int ec)
            endCol = Math.Max(0, ec - 1);
        else
            endCol = line < lines.Length ? Math.Max(col + 1, lines[line].TrimEnd('\r').Length) : col + 1;

        return new Range(new Position(line, col), new Position(endLine, Math.Max(endCol, col)));
    }

    private static DiagnosticSeverity ToSeverity(SharpSeverity s) => s switch
    {
        SharpSeverity.Error => DiagnosticSeverity.Error,
        SharpSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Information
    };
}
