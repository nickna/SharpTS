using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class ExponentiationAssignmentSyntaxTests
{
    [Fact]
    public void LexerRecognizesExponentiationAssignmentWithoutSplittingItsEquals()
    {
        var tokens = new Lexer("**= ** *= *").ScanTokens();
        // String names let this regression compile against the pre-fix enum.
        Assert.Equal(new[] { "STAR_STAR_EQUAL", "STAR_STAR", "STAR_EQUAL", "STAR", "EOF" },
            tokens.Select(token => token.Type.ToString()));
        Assert.Equal("**=", tokens[0].Lexeme);
    }

    [Fact]
    public void ParserBuildsRightAssociativeExponentiationCompoundAssignments()
    {
        var statement = Assert.IsType<Stmt.Expression>(Assert.Single(
            TestHarness.ParseOrThrow("left **= right **= 2;")));
        var outer = Assert.IsType<Expr.CompoundAssign>(statement.Expr);
        Assert.Equal("left", outer.Name.Lexeme);
        Assert.Equal("STAR_STAR_EQUAL", outer.Operator.Type.ToString());
        Assert.Equal("**=", outer.Operator.Lexeme);
        var inner = Assert.IsType<Expr.CompoundAssign>(outer.Value);
        Assert.Equal("right", inner.Name.Lexeme);
        Assert.Equal("STAR_STAR_EQUAL", inner.Operator.Type.ToString());
        Assert.Equal(2d, Assert.IsType<Expr.Literal>(inner.Value).Value);
    }
}
