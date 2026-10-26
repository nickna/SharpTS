using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class SwitchClauseOrderingTests
{
    [Theory]
    [InlineData(0, "default:10;case 1:20;case 2:30;")]
    [InlineData(1, "case 1:10;default:20;case 2:30;")]
    [InlineData(2, "case 1:10;case 2:20;default:30;")]
    public void ParserPreservesDefaultPositionAndOriginalSubjectToken(int defaultIndex, string clauses)
    {
        var tokens = new Lexer("let subject=1;switch(subject){" + clauses + "}").ScanTokens();
        var statements = new Parser(tokens).ParseOrThrow();
        var statement = Assert.IsType<Stmt.Switch>(statements[1]);
        Assert.Same(tokens.Last(token => token.Lexeme == "subject"), Assert.IsType<Expr.Variable>(statement.Subject).Name);
        Assert.Equal(3, statement.Clauses.Count);
        Assert.Equal(defaultIndex, statement.Clauses.FindIndex(clause => clause.IsDefault));
        Assert.Single(statement.Clauses, clause => clause.IsDefault);
        Assert.Equal(new[] { 10d, 20d, 30d }, statement.Clauses.Select(clause =>
            Assert.IsType<double>(Assert.IsType<Expr.Literal>(Assert.IsType<Stmt.Expression>(Assert.Single(clause.Body)).Expr).Value)));
    }

    [Fact]
    public void NullCaseRemainsAnExpressionDistinctFromDefault()
    {
        var statement = Assert.IsType<Stmt.Switch>(Assert.Single(TestHarness.ParseOrThrow("switch(null){case null:1;default:2;}")));
        Assert.False(statement.Clauses[0].IsDefault);
        Assert.Null(Assert.IsType<Expr.Literal>(statement.Clauses[0].Value).Value);
        Assert.True(statement.Clauses[1].IsDefault);
        Assert.Null(statement.Clauses[1].Value);
    }

    [Fact]
    public void DuplicateDefaultIsAParseError()
        => Assert.ThrowsAny<Exception>(() => TestHarness.ParseOrThrow("switch(0){default:1;default:2;}"));

    [Fact]
    public void VisitorVisitsEachTestAndBodyOnceInWrittenOrder()
    {
        var statement = Assert.Single(TestHarness.ParseOrThrow("switch(0){case 1:10;default:20;case 2:30;}"));
        var visitor = new LiteralVisitor();
        visitor.Visit(statement);
        Assert.Equal(new[] { 0d, 1d, 10d, 20d, 2d, 30d }, visitor.Values);
    }

    [Fact]
    public void VarHoistingPreservesUnchangedClauseAndExpressionIdentities()
    {
        var statements = TestHarness.ParseOrThrow("switch(0){case 1:10;default:var moved=2;case 2:30;}");
        var parsed = Assert.Single(statements.OfType<Stmt.Switch>());
        var declaration = Assert.Single(statements.OfType<Stmt.Var>());
        var assignment = Assert.IsType<Expr.Assign>(Assert.IsType<Stmt.Expression>(Assert.Single(parsed.Clauses[1].Body)).Expr);
        // Parsing already hoists. Restore this one declaration using its original
        // source token and initializer to exercise one rewrite in isolation.
        var original = parsed with
        {
            Clauses = [parsed.Clauses[0], parsed.Clauses[1] with
            {
                Body = [declaration with { Initializer = assignment.Value, HoistTypeInferenceInitializer = null }]
            }, parsed.Clauses[2]]
        };
        var rewritten = Assert.Single(VarHoister.Hoist([original]).OfType<Stmt.Switch>());
        Assert.NotSame(original, rewritten);
        Assert.Same(original.Subject, rewritten.Subject);
        Assert.Same(original.Clauses[0], rewritten.Clauses[0]);
        Assert.Same(original.Clauses[2], rewritten.Clauses[2]);
        Assert.True(rewritten.Clauses[1].IsDefault);
        Assert.IsType<Stmt.Expression>(Assert.Single(rewritten.Clauses[1].Body));
        Assert.IsType<Stmt.Var>(Assert.Single(original.Clauses[1].Body));
    }

    private sealed class LiteralVisitor : AstVisitorBase
    {
        public List<double> Values { get; } = [];
        protected override void VisitLiteral(Expr.Literal expression)
        {
            if (expression.Value is double value) Values.Add(value);
        }
    }
}
