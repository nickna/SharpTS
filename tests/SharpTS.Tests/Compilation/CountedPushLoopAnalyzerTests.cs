using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.Compilation;

public class CountedPushLoopAnalyzerTests
{
    [Fact]
    public void PureCountedRecordPush_IsRecognized()
    {
        var loop = ParseLoop("""
            for (let i: number = 0; i < n; i++) {
                items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
            }
            """);

        Assert.True(CountedPushLoopAnalyzer.TryAnalyze(loop, out var reservation));
        Assert.Equal("items", reservation.Array.Name.Lexeme);
        Assert.Equal("n", reservation.Bound.Name.Lexeme);
        Assert.IsType<Expr.ObjectLiteral>(reservation.Value);
    }

    [Theory]
    [InlineData("for (let i = 1; i < n; i++) items.push({ value: i });")]
    [InlineData("for (let i = 0; i <= n; i++) items.push({ value: i });")]
    [InlineData("for (let i = 0; i < n; i++) { items.push({ value: i }); log(i); }")]
    [InlineData("for (let i = 0; i < n; i++) items.push(makeItem(i));")]
    [InlineData("for (let i = 0; i < getCount(); i++) items.push({ value: i });")]
    public void NonProvenShapes_AreRejected(string source)
    {
        Assert.False(CountedPushLoopAnalyzer.TryAnalyze(
            ParseLoop(source),
            out _));
    }

    private static Stmt.For ParseLoop(string source) =>
        Assert.IsType<Stmt.For>(new Parser(new Lexer(source).ScanTokens())
            .ParseOrThrow()
            .Single());
}
