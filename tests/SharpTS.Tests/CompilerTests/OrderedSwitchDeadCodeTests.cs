using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.ControlFlow;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class OrderedSwitchDeadCodeTests
{
    [Fact]
    public void ExhaustiveSelectionStillKeepsDefaultReachedByFallthrough()
    {
        var (function, info) = Analyze("function read(value:0|1){switch(value){case 0:console.log('fall');default:return 1;case 1:return 2;}}");
        var switched = Assert.IsType<Stmt.Switch>(function.Body![0]);
        var summary = Assert.IsType<SwitchAnalysis>(info.GetSwitchResult(switched));
        Assert.True(summary.IsExhaustive);
        Assert.False(summary.DefaultIsUnreachable);
        Assert.False(info.IsDead(switched.Clauses.Single(clause => clause.IsDefault).Body[0]));
    }

    [Fact]
    public void ExhaustiveSelectionCanDiscardDefaultWhenEveryPredecessorStops()
    {
        var (function, info) = Analyze("function read(value:0|1){switch(value){case 0:break;case 1:break;default:console.log('dead');}}");
        var switched = Assert.IsType<Stmt.Switch>(function.Body![0]);
        Assert.True(Assert.IsType<SwitchAnalysis>(info.GetSwitchResult(switched)).DefaultIsUnreachable);
        Assert.True(info.IsDead(switched.Clauses.Single(clause => clause.IsDefault).Body[0]));
    }

    [Fact]
    public void ConditionalBreakDoesNotEraseTheFollowingReturn()
    {
        var (function, info) = Analyze("function read(value:0|1,choose:boolean):number{switch(value){case 0:if(choose)break;return 1;default:return 2;}return 3;}");
        Assert.False(info.IsDead(function.Body![1]));
    }

    [Fact]
    public void ConditionalContinueCannotProveThatTheFunctionReturns()
    {
        var statements = TestHarness.ParseOrThrow("function read(value:number,choose:boolean):number{outer:for(let i=0;i<2;i++){switch(value){case 0:if(choose)continue outer;return 1;default:return 2;}}return 3;}");
        var function = Assert.IsType<Stmt.Function>(Assert.Single(statements));
        var labeled = Assert.IsType<Stmt.LabeledStatement>(function.Body![0]);
        var loop = Assert.IsType<Stmt.For>(labeled.Statement);
        var block = Assert.IsType<Stmt.Block>(loop.Body);
        Assert.False(StatementCompletionAnalysis.SwitchDefinitelyReturns(Assert.IsType<Stmt.Switch>(block.Statements[0]), allowEmptyReturn: true));
    }

    [Theory]
    [InlineData("1|'1'", "1")]
    [InlineData("true|'True'", "true")]
    [InlineData("1n|'1n'", "1n")]
    public void LiteralCoverageUsesStrictTypeIdentity(string type, string label)
    {
        var (function, info) = Analyze($"function read(value:{type}){{switch(value){{case {label}:return 1;default:return 2;}}}}");
        var switched = Assert.IsType<Stmt.Switch>(function.Body![0]);
        Assert.False(info.GetSwitchResult(switched)?.IsExhaustive ?? false);
        Assert.False(info.IsDead(switched.Clauses.Single(clause => clause.IsDefault).Body[0]));
    }

    public static IEnumerable<object[]> Programs() => SwitchCompletionPreservationTests.Programs().Select(row => new[] { row[1] });

    [Theory, MemberData(nameof(Programs))]
    public void CompletionPathsHaveVerifiedIl(string source)
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

    private static (Stmt.Function Function, DeadCodeInfo Info) Analyze(string source)
    {
        var statements = TestHarness.ParseOrThrow(source);
        var types = new TypeChecker(TypeCheckerOptions.Strict).Check(statements);
        return (Assert.IsType<Stmt.Function>(Assert.Single(statements)), new DeadCodeAnalyzer(types).Analyze(statements));
    }
}
