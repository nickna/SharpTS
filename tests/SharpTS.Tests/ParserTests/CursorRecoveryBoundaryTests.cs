using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class CursorRecoveryBoundaryTests
{
    private static EditorParseArtifact Parse(string marked, EditorRecoveryPolicy? policy = null,
        EditorQueryKind kind = EditorQueryKind.Completion)
    {
        int caret = marked.IndexOf('|');
        Assert.True(caret >= 0);
        return Parser.ParseForEditor(new SourceDocument("cursor.ts", marked.Remove(caret, 1)),
            caret, kind, policy ?? EditorRecoveryPolicy.Default);
    }

    [Theory]
    [InlineData("|receiver.;")]
    [InlineData("const here = 1; | receiver.;")]
    [InlineData("receiver.; const here = 1;|")]
    [InlineData("f(|); receiver.;")]
    public void MissingNamesOutsideTheCaretGapAreNeverRepaired(string source)
    {
        var parsed = Parse(source);
        Assert.Equal(0, parsed.RecoveryCount);
        Assert.DoesNotContain(parsed.Syntax.Members, member => member.IsRecovered);
    }

    [Theory]
    [InlineData("receiver.|;")]
    [InlineData("receiver.  |;")]
    [InlineData("receiver?.  |;")]
    public void CaretContainingMemberGapRetainsOnlyTheWrittenReceiver(string source)
    {
        var parsed = Parse(source);
        var member = Assert.Single(parsed.Syntax.Members);
        Assert.Equal(1, parsed.RecoveryCount);
        Assert.True(member.IsRecovered);
        Assert.True(member.Name.Span.IsHidden);
        Assert.IsType<Expr.Variable>(member.Receiver);
        Assert.Contains(parsed.Syntax.GetRecords(member.Receiver), record => record.IsAuthoritative);
        Assert.DoesNotContain(parsed.Syntax.GetRecords(member.Owner), record =>
            record.Kind == EditorSyntaxKind.Expression && record.IsAuthoritative);
    }

    [Fact]
    public void CommaRunBeyondRemainingRepairBudgetIsRefusedBeforeAnyHole()
    {
        var parsed = Parse("f(" + new string(',', 20_000) + "|);", new(MaxRepairs: 4));
        Assert.Equal(0, parsed.RecoveryCount);
        Assert.False(parsed.ParseResult.IsSuccess);
        Assert.DoesNotContain(parsed.Syntax.Invocations, invocation => invocation.IsRecovered);
    }

    [Fact]
    public void BoundedCommaHolesPreserveTopLevelArgumentOwnership()
    {
        var parsed = Parse("f(,,,|);", new(MaxRepairs: 3), EditorQueryKind.SignatureHelp);
        var invocation = Assert.Single(parsed.Syntax.Invocations);
        Assert.Equal(3, parsed.RecoveryCount);
        Assert.True(invocation.IsRecovered);
        Assert.Equal(3, invocation.Commas.Count);
        Assert.Equal(3, invocation.GetActiveArgumentIndex("f(,,,".Length));
        var call = Assert.IsType<Expr.Call>(invocation.Owner);
        Assert.Equal(3, call.Arguments.Count);
        Assert.All(call.Arguments, argument => Assert.True(parsed.Document.Spans.GetSpan(argument)!.Value.IsHidden));
    }

    [Fact]
    public void NestedMissingClosersBelongToTheirActualInvocation()
    {
        const string source = "outer(inner(1, |";
        var parsed = Parse(source, kind: EditorQueryKind.SignatureHelp);
        Assert.Equal(2, parsed.RecoveryCount);
        Assert.Equal(2, parsed.Syntax.Invocations.Count);
        int caret = source.IndexOf('|');
        var inner = parsed.Syntax.FindInvocation(caret);
        Assert.NotNull(inner);
        Assert.Equal("inner", Assert.IsType<Expr.Variable>(inner!.Callee).Name.Lexeme);
        Assert.Equal(1, inner.GetActiveArgumentIndex(caret));
        Assert.All(parsed.Syntax.Invocations, invocation =>
        {
            Assert.True(invocation.IsRecovered);
            Assert.True(invocation.ContainsCursor(caret));
            Assert.Equal(caret, invocation.Span.End);
        });
    }

    [Fact]
    public void SuperMemberGapKeepsKeywordProofWithoutInventingAName()
    {
        var parsed = Parse("class Derived extends Base { method() { super.  |; } }");
        var member = Assert.Single(parsed.Syntax.Members);
        var super = Assert.IsType<Expr.Super>(member.Owner);
        Assert.Same(super, member.Receiver);
        Assert.Equal(1, parsed.RecoveryCount);
        Assert.True(member.IsRecovered);
        Assert.True(member.Name.Span.IsHidden);
        Assert.Contains(parsed.Syntax.GetRecords(super), record =>
            record.IsAuthoritative && record.Token?.Type == TokenType.SUPER);
        Assert.DoesNotContain(parsed.Syntax.GetRecords(super), record =>
            record.IsAuthoritative && record.Role == EditorSyntaxRole.MemberName);
        Assert.False(new Parser(new Lexer("class Derived extends Base { method() { super.; } }").ScanTokens()).Parse().IsSuccess);
    }

    [Theory]
    [InlineData(EditorQueryKind.Syntax)]
    [InlineData(EditorQueryKind.Hover)]
    public void NonRepairQueriesDoNotEnableSuperMemberRecovery(EditorQueryKind kind)
    {
        var parsed = Parse("class Derived extends Base { method() { super.|; } }", kind: kind);
        Assert.Equal(0, parsed.RecoveryCount);
        Assert.DoesNotContain(parsed.Syntax.Members, member => member.IsRecovered);
    }

    [Fact]
    public void LongCommaInputPreservesPublicCancellation()
    {
        string source = "f(" + new string(',', 20_000);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() => Parser.ParseForEditor(
            new SourceDocument("cursor.ts", source), source.Length, EditorQueryKind.SignatureHelp,
            new(MaxRepairs: 4), cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }
}
