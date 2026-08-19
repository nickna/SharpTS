using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class VariableDefiniteAssignmentTests
{
    private static IReadOnlyList<Diagnostic> Check(
        string source,
        bool strictNullChecks = true)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        return new TypeChecker(new TypeCheckerOptions
            {
                StrictNullChecks = strictNullChecks,
                CheckVariableUseBeforeAssignment = strictNullChecks,
                MaxErrors = 50,
            })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;
    }

    [Fact]
    public void TypedVariableReadBeforeAssignmentReportsTs2454()
    {
        var diagnostic = Assert.Single(
            Check("let value: number; console.log(value);"),
            d => d.TsCode == "TS2454");

        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public void SimpleAssignmentMakesVariableDefinitelyAssigned()
    {
        Assert.DoesNotContain(
            Check("let value: number; value = 1; console.log(value);"),
            d => d.TsCode == "TS2454");
    }

    [Fact]
    public void SelfAssignmentStillReadsTheUnassignedVariable()
    {
        Assert.Contains(
            Check("let value: number; value = value;"),
            d => d.TsCode == "TS2454");
    }

    [Fact]
    public void RepeatedUninitializedVarDeclarationStaysUnassigned()
    {
        Assert.Contains(
            Check("var value: number; var value: number; console.log(value);"),
            d => d.TsCode == "TS2454");
    }

    [Theory]
    [InlineData("let value: number | undefined; console.log(value);")]
    [InlineData("let value: unknown; console.log(value);")]
    [InlineData("let value: any; console.log(value);")]
    [InlineData("declare let value: number; console.log(value);")]
    [InlineData("let value!: number; console.log(value);")]
    public void UndefinedCapableOrExternallyAssignedBindingsDoNotReport(string source)
    {
        Assert.DoesNotContain(Check(source), d => d.TsCode == "TS2454");
    }

    [Fact]
    public void NonStrictNullChecksDoesNotReport()
    {
        Assert.DoesNotContain(
            Check("let value: number; console.log(value);", strictNullChecks: false),
            d => d.TsCode == "TS2454");
    }

    [Theory]
    [InlineData("let value: number; function read() { return value; }")]
    [InlineData("let value: number; const read = () => value;")]
    [InlineData("let value: number; class Reader { read() { return value; } }")]
    [InlineData("let value: number; const reader = { get current() { return value; } };")]
    public void DeferredClosureReadDoesNotReportOuterBinding(string source)
    {
        Assert.DoesNotContain(Check(source), d => d.TsCode == "TS2454");
    }

    [Fact]
    public void AssignmentMismatchUsesAssignmentLineInsideFunction()
    {
        var diagnostic = Assert.Single(
            Check("function update(value: number) {\n    value = 1;\n    value = 'wrong';\n}"),
            d => d.TsCode == "TS2322");

        Assert.Equal(3, diagnostic.Line);
    }

    [Fact]
    public void NamespaceVariableReadBeforeAssignmentReportsTs2454()
    {
        var diagnostic = Assert.Single(
            Check("namespace Values {\n    let value: number;\n    console.log(value);\n}"),
            d => d.TsCode == "TS2454");

        Assert.Equal(3, diagnostic.Line);
    }

    [Fact]
    public void NamespaceSymbolShadowIsNotTreatedAsInitializedGlobal()
    {
        var diagnostic = Assert.Single(
            Check("namespace Values {\n    var Symbol: number;\n    console.log(Symbol);\n}"),
            d => d.TsCode == "TS2454");

        Assert.Equal(3, diagnostic.Line);
    }

    [Fact]
    public void AssignmentInOnlyOneIfBranchIsNotDefinite()
    {
        Assert.Contains(
            Check("let value: number; if (Math.random()) value = 1; console.log(value);"),
            d => d.TsCode == "TS2454");
    }

    [Fact]
    public void AssignmentInBothIfBranchesIsDefinite()
    {
        Assert.DoesNotContain(
            Check("let value: number; if (Math.random()) value = 1; else value = 2; console.log(value);"),
            d => d.TsCode == "TS2454");
    }

    [Fact]
    public void AssignmentOnOnlyReachableContinuationIsDefinite()
    {
        Assert.DoesNotContain(
            Check("function read(flag: boolean) { let value: number; if (flag) return; else value = 1; console.log(value); }"),
            d => d.TsCode == "TS2454");
    }

    [Theory]
    [InlineData("let value: number; while (Math.random()) value = 1; console.log(value);")]
    [InlineData("let value: number; for (; Math.random();) value = 1; console.log(value);")]
    [InlineData("let value: number; for (const item of []) value = 1; console.log(value);")]
    public void AssignmentInPossiblyEmptyLoopIsNotDefinite(string source)
    {
        Assert.Contains(Check(source), d => d.TsCode == "TS2454");
    }

    [Fact]
    public void AssignmentInDoWhileBodyIsDefinite()
    {
        Assert.DoesNotContain(
            Check("let value: number; do value = 1; while (false); console.log(value);"),
            d => d.TsCode == "TS2454");
    }
}
