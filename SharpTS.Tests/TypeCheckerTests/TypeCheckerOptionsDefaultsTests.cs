using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Pins the product's default strictness so the CLI/tsconfig layer can never change behavior
/// for a user who passes no flags and has no tsconfig.json.
/// </summary>
/// <remarks>
/// This is the zero-regression guarantee for the strictness-options work. Both conformance
/// suites (Test262 and microsoft/TypeScript) build their checkers directly rather than through
/// the CLI, so their committed baselines are pinned to exactly these values. If a test here
/// fails, a baseline is about to move.
/// </remarks>
public class TypeCheckerOptionsDefaultsTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source, TypeChecker checker)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parsed = new Parser(tokens).Parse();
        return checker.CheckWithRecovery(parsed.Statements).Diagnostics;
    }

    [Fact]
    public void Default_MatchesHistoricalProductDefaults()
    {
        var d = TypeCheckerOptions.Default;

        // SharpTS's historical mix: neither tsc's `strict: true` nor `strict: false`.
        Assert.True(d.StrictNullChecks);
        Assert.False(d.StrictFunctionTypes);
        Assert.False(d.NoImplicitAny);
        Assert.Equal(10, d.MaxErrors);
    }

    [Fact]
    public void ZeroArgCtor_IsEquivalentToDefaultOptions()
    {
        Assert.Equal(TypeCheckerOptions.Default, new TypeChecker().Options);
    }

    [Fact]
    public void LegacyCtor_ForwardsAllThreeFlags_AndLeavesNoImplicitAnyOff()
    {
        // The exact shape SharpTS.TypeScriptConformance/TypeScriptConformanceRunner.cs uses.
        var checker = new TypeChecker(strictNullChecks: false, maxErrors: 1000, strictFunctionTypes: true);

        Assert.False(checker.Options.StrictNullChecks);
        Assert.True(checker.Options.StrictFunctionTypes);
        Assert.Equal(1000, checker.Options.MaxErrors);
        Assert.False(checker.Options.NoImplicitAny);
    }

    [Fact]
    public void StrictUmbrella_TurnsOnAllThree()
    {
        var s = TypeCheckerOptions.Strict;

        Assert.True(s.StrictNullChecks);
        Assert.True(s.StrictFunctionTypes);
        Assert.True(s.NoImplicitAny);
        Assert.Equal(10, s.MaxErrors); // the umbrella must not touch the error cap
    }

    [Fact]
    public void StrictUmbrella_IndividualFlagOverridesIt()
    {
        // How the CLI expresses `--strict --noImplicitAny=false`.
        var o = TypeCheckerOptions.Strict with { NoImplicitAny = false };

        Assert.True(o.StrictNullChecks);
        Assert.True(o.StrictFunctionTypes);
        Assert.False(o.NoImplicitAny);
    }

    [Fact]
    public void DefaultOptions_ProduceIdenticalDiagnosticsToZeroArgCtor()
    {
        const string source = """
            let a: number = "nope";
            let b: string = null;
            function f(): number { return "also nope"; }
            """;

        var viaZeroArg = Diagnose(source, new TypeChecker());
        var viaOptions = Diagnose(source, new TypeChecker(TypeCheckerOptions.Default));

        Assert.Equal(
            viaZeroArg.Select(d => (d.TsCode, d.Severity, d.Line, d.Message)),
            viaOptions.Select(d => (d.TsCode, d.Severity, d.Line, d.Message)));
        Assert.NotEmpty(viaZeroArg); // guard against both paths silently reporting nothing
    }

    [Fact]
    public void NullOptions_FallBackToDefaults()
    {
        Assert.Equal(TypeCheckerOptions.Default, new TypeChecker(null!).Options);
    }
}
