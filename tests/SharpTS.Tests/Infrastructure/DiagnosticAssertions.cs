using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>Checks the front-end contract without executing the program.</summary>
internal static class DiagnosticAssertions
{
    public static IReadOnlyList<Diagnostic> Check(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join("\n", parsed.Diagnostics));
        return new TypeChecker().CheckWithRecovery(parsed.Statements).Diagnostics;
    }

    public static Diagnostic SingleError(string source, string tsCode, int line)
    {
        var diagnostic = Assert.Single(Check(source));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(tsCode, diagnostic.TsCode);
        Assert.NotNull(diagnostic.Location);
        Assert.Equal(line, diagnostic.Line);
        return diagnostic;
    }

    public static void NoErrors(string source) => Assert.Empty(Check(source));

    public static void Errors(string source, params (string Code, int Line)[] expected)
    {
        var diagnostics = Check(source);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.NotNull(diagnostic.Location);
        });
        Assert.Equal(expected.OrderBy(d => d.Line).ThenBy(d => d.Code, StringComparer.Ordinal),
            diagnostics.Select(d => (Code: d.TsCode!, d.Line))
                .OrderBy(d => d.Line).ThenBy(d => d.Code, StringComparer.Ordinal));
    }
}
