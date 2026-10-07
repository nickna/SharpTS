using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class EditorExpressionRecoveryTests
{
    private static EditorParseArtifact Parse(string source, EditorRecoveryPolicy? policy = null,
        EditorQueryKind query = EditorQueryKind.Completion)
    {
        int cursor = source.IndexOf('|');
        Assert.True(cursor >= 0);
        return Parser.ParseForEditor(new SourceDocument("recovery.ts", source.Remove(cursor, 1)), cursor,
            query, policy ?? EditorRecoveryPolicy.Default);
    }

    [Theory]
    [InlineData("receiver.|")]
    [InlineData("receiver?.   |")]
    [InlineData("receiver. /* closed */ |")]
    [InlineData("receiver. // completed comment\n|")]
    public void MissingMemberRetainsRealReceiverAndHiddenNonAuthoritativeName(string source)
    {
        var parsed = Parse(source);
        int cursor = source.IndexOf('|');
        Assert.True(parsed.IsRecovered);
        Assert.False(parsed.ParseResult.IsSuccess);
        var member = parsed.Syntax.FindMember(cursor);
        Assert.NotNull(member);
        Assert.True(member!.IsRecovered);
        Assert.Equal(-1, member.Name.Start);
        Assert.IsType<Expr.Variable>(member.Receiver);
        Assert.DoesNotContain(parsed.Syntax.GetRecords(member.Owner), record => record.IsAuthoritative);
        Assert.Contains(parsed.Syntax.GetRecords(member.Receiver), record => record.IsAuthoritative && record.Role == EditorSyntaxRole.Name);
        Assert.Equal(source.Remove(cursor, 1), parsed.Document.Text);
    }

    [Theory]
    [InlineData("f(|", 0, false)]
    [InlineData("f(a,   |", 1, false)]
    [InlineData("f(a, , |", 2, false)]
    [InlineData("new C(  |", 0, true)]
    [InlineData("outer(inner(1,  |", 1, false)]
    [InlineData("f?.(a, |", 1, false)]
    public void UnfinishedArgumentListsExposeBoundedLocalContext(string source, int argument, bool isNew)
    {
        var parsed = Parse(source, query: EditorQueryKind.SignatureHelp);
        int cursor = source.IndexOf('|');
        Assert.True(parsed.IsRecovered);
        Assert.False(parsed.ParseResult.IsSuccess);
        var invocation = parsed.Syntax.FindInvocation(cursor);
        Assert.NotNull(invocation);
        Assert.True(invocation!.IsRecovered);
        Assert.Null(invocation.CloseParen);
        Assert.Equal(argument, invocation.GetActiveArgumentIndex(cursor));
        Assert.Equal(isNew, invocation.IsNew);
        Assert.DoesNotContain(parsed.Syntax.GetRecords(invocation.Owner), record => record.IsAuthoritative);
        Assert.NotEmpty(parsed.ParseResult.Statements);
        Assert.InRange(parsed.RecoveryCount, 1, EditorRecoveryPolicy.Default.MaxRepairs);
    }

    [Theory]
    [InlineData("receiver. // comment|\n")]
    [InlineData("receiver. /* comment| */")]
    [InlineData("f('str|ing')")]
    [InlineData("f(`te|xt`)")]
    [InlineData("f(/pa|ttern/)")]
    public void CaretsInsideCommentsAndLiteralTokensNeverEnableRepairs(string source)
    {
        Assert.False(Parse(source).IsRecovered);
    }

    [Theory]
    [InlineData("receiver.|")]
    [InlineData("f(a, |")]
    [InlineData("new C(|")]
    public void OrdinaryParserStillRejectsTheUnfinishedSyntax(string source)
    {
        string text = source.Replace("|", "", StringComparison.Ordinal);
        Assert.False(new Parser(new Lexer(text).ScanTokens()).Parse().IsSuccess);
        Assert.False(Parse(source, query: EditorQueryKind.Hover).IsRecovered);
        Assert.False(Parse(source, query: EditorQueryKind.Syntax).IsRecovered);
    }

    [Fact]
    public void RecoveryNeverMutatesPublishedSourceDocument()
    {
        var original = new SourceDocument("test.ts", "receiver.");
        var sentinel = new object();
        original.Spans.Record(sentinel, new SourceSpan(0, 1));
        var parsed = Parser.ParseForEditor(original, original.Text.Length, EditorQueryKind.Completion,
            EditorRecoveryPolicy.Default);
        Assert.NotSame(original, parsed.Document);
        Assert.Equal(1, original.Spans.Count);
        Assert.Null(original.EditorSyntax);
        Assert.True(parsed.IsRecovered);
    }

    [Fact]
    public void RepairAndNestingBudgetsRefuseExcessRecovery()
    {
        Assert.False(Parse("f(|", new(MaxRepairs: 0)).IsRecovered);
        var nested = Parse("outer(inner(|", new(MaxRepairs: 1, MaxNesting: 1));
        Assert.InRange(nested.RecoveryCount, 0, 1);
        Assert.Null(nested.Syntax.FindInvocation("outer(inner(".Length));
    }

    [Theory]
    [InlineData("const value = #|")]
    [InlineData("/* unfinished|")]
    public void LexicalFailureReturnsUnavailableSyntaxInsteadOfThrowing(string source)
    {
        var parsed = Parse(source);
        Assert.False(parsed.ParseResult.IsSuccess);
        Assert.False(parsed.IsRecovered);
        Assert.Equal(0, parsed.Syntax.Count);
    }

    [Fact]
    public void PreCancelledEditorParsePropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => Parser.ParseForEditor(new SourceDocument("test.ts", "f("),
            2, EditorQueryKind.SignatureHelp, EditorRecoveryPolicy.Default, cancellationToken: cancellation.Token));
    }
}
