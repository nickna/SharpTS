using System.Reflection;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class ParsingCancellationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("const value = 1;")]
    public void LexerObservesCancellationBeforeScanning(string source)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = Assert.Throws<OperationCanceledException>(() =>
            new Lexer(source).WithCancellation(cancellation.Token).ScanTokens());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParserEntryPointsPreserveCancellation(bool throwOnSyntaxErrors)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var parser = new Parser(new Lexer("").ScanTokens()).WithCancellation(cancellation.Token);

        var exception = Assert.Throws<OperationCanceledException>(() =>
        {
            if (throwOnSyntaxErrors)
                parser.ParseOrThrow();
            else
                parser.Parse();
        });

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void TypeFragmentAndJsxSuffixEntryPointsPreserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var fragment = Assert.Throws<OperationCanceledException>(() =>
            Parser.TryParseTypeFragment("number", cancellation.Token));
        var suffix = Assert.Throws<OperationCanceledException>(() =>
            Lexer.Relex("value;", 0, 1, cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, fragment.CancellationToken);
        Assert.Equal(cancellation.Token, suffix.CancellationToken);
    }

    [Fact]
    public void CancellationNoneRestoresOrdinaryParsing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lexer = new Lexer("const value = 1;")
            .WithCancellation(cancellation.Token)
            .WithCancellation(CancellationToken.None);
        var parser = new Parser(lexer.ScanTokens())
            .WithCancellation(cancellation.Token)
            .WithCancellation(CancellationToken.None);

        Assert.True(parser.Parse().IsSuccess);
    }

    [Fact]
    public void LexerSampledCancellationEscapesFromInsideOneToken()
    {
        // Call the actual token scanner directly to exclude public entry/exit checks.
        // Skipping its first sampled poll makes the canceled-token exception occur
        // strictly inside this one comment, without depending on thread scheduling.
        string source = "/*" + new string('x', 1024) + "*/";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lexer = new Lexer(source).WithCancellation(cancellation.Token);
        SetCheckpointCounter(lexer, 1);
        var scanToken = typeof(Lexer).GetMethod("ScanToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(lexer);

        var exception = Assert.Throws<OperationCanceledException>(scanToken);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        int offset = GetCursor(lexer);
        Assert.InRange(offset, 3, source.Length - 3);
    }

    [Theory]
    [InlineData("function")]
    [InlineData("class")]
    [InlineData("class-expression")]
    public void NestedParsingPreservesSampledInternalCancellation(string context)
    {
        // Enter the real declaration parser directly, bypassing public pre-cancel
        // checks. Its first sampled poll is skipped; a completed inner declaration
        // and a cursor inside the tuple prove cancellation crosses nested parsing
        // and speculative expression recovery instead of becoming a syntax result.
        string body = NestedBody();
        string source = context switch
        {
            "function" => "function outer() {" + body + "}",
            "class" => "class Container { method() {" + body + "} }",
            "class-expression" => "const Container = class { method() {" + body + "} };",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        var tokens = new Lexer(source).ScanTokens();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var parser = new Parser(tokens).WithCancellation(cancellation.Token);
        SetCheckpointCounter(parser, 1);
        var declaration = typeof(Parser).GetMethod("Declaration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Stmt>>(parser);

        var exception = Assert.Throws<OperationCanceledException>(() => declaration());

        AssertNestedCancellation(exception, cancellation.Token, parser, tokens, source);
    }

    [Fact]
    public void JsxAttributeExpressionPreservesSampledInternalCancellation()
    {
        string source = "const view = <div value={function () {" + NestedBody() + "}} />;";
        var tokens = new Lexer(source) { JsxTolerant = true }.ScanTokens();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var parser = new Parser(tokens).WithCancellation(cancellation.Token)
            .WithJsx(source, JsxParseOptions.Default);

        // Enter the actual attribute-value parser after the exact written '='.
        // This bypasses opening-tag diagnostic preflight, which independently polls
        // across the entire attribute before nested expressions are parsed. The
        // proof here covers attribute/function/arrow/block propagation, not JSX text.
        int equalsOffset = source.IndexOf("value={", StringComparison.Ordinal) + "value".Length;
        int equalsIndex = tokens.FindIndex(token => token.Type == TokenType.EQUAL && token.Start == equalsOffset);
        Assert.True(equalsIndex >= 0, "The written attribute '=' must exist in the token stream.");
        typeof(Parser).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(parser, equalsIndex + 1);
        SetCheckpointCounter(parser, 1);
        var parseAttribute = typeof(Parser).GetMethod("ParseJsxAttributeValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<ParseJsxAttribute>(parser);

        var exception = Assert.Throws<OperationCanceledException>(() => parseAttribute(out _));

        AssertNestedCancellation(exception, cancellation.Token, parser, tokens, source);
    }

    private delegate Expr ParseJsxAttribute(out int? recoveryEndOffset);

    private static string NestedBody() => "let progress = 0; const fn = (value: number): [" +
        string.Join(",", Enumerable.Repeat("number", 1024)) + "] => value;";

    private static void AssertNestedCancellation(OperationCanceledException exception,
        CancellationToken cancellationToken, Parser parser, IReadOnlyList<Token> tokens, string source)
    {
        Assert.Equal(cancellationToken, exception.CancellationToken);
        int offset = tokens[GetCursor(parser)].Start;
        Assert.InRange(offset, source.IndexOf('[', StringComparison.Ordinal) + 1,
            source.IndexOf(']', StringComparison.Ordinal) - 1);
        Assert.True(parser.Spans.Count > 0, "An inner declaration must finish before the sampled poll.");
    }

    private static void SetCheckpointCounter(object scanner, int value) =>
        scanner.GetType().GetField("_cancellationCheckpoints", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scanner, value);

    private static int GetCursor(object scanner) =>
        (int)scanner.GetType().GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(scanner)!;
}
