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
    public async Task LexerCanCancelInsideOneLongToken()
    {
        // The directive is public evidence that scanning has begun. The following
        // comment is one lexical item, so cancellation must also work within it.
        string source = "/// <reference path=\"progress.ts\" />\n/*" +
            new string('x', 8_000_000) + "*/";
        using var cancellation = new CancellationTokenSource();
        var lexer = new Lexer(source).WithCancellation(cancellation.Token);
        var work = StartDedicatedAsync(lexer.ScanTokens);

        await AssertInFlightCancellationAsync(work, () => lexer.TripleSlashDirectives.Count > 0,
            cancellation);
    }

    [Theory]
    [InlineData("function")]
    [InlineData("class")]
    [InlineData("class-expression")]
    [InlineData("jsx")]
    public async Task NestedParsingPreservesInFlightCancellation(string context)
    {
        // A completed inner declaration provides public progress before a large
        // arrow return type. Cancellation crosses nested block/class/JSX recovery
        // and speculative expression parsing without becoming a syntax result.
        string body = "let progress = 0; const fn = (value: number): [" +
            string.Join(",", Enumerable.Repeat("number", 60_000)) + "] => value;";
        string source = context switch
        {
            "function" => "function outer() {" + body + "}",
            "class" => "class Container { method() {" + body + "} }",
            "class-expression" => "const Container = class { method() {" + body + "} };",
            "jsx" => "const view = <div>{(() => {" + body + "})()}</div>;",
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        var tokens = new Lexer(source) { JsxTolerant = context == "jsx" }.ScanTokens();
        using var cancellation = new CancellationTokenSource();
        var parser = new Parser(tokens).WithCancellation(cancellation.Token);
        if (context == "jsx")
            parser.WithJsx(source, JsxParseOptions.Default);
        var work = StartDedicatedAsync(parser.Parse);

        await AssertInFlightCancellationAsync(work, () => parser.Spans.Count > 0, cancellation);
    }

    private static Task<T> StartDedicatedAsync<T>(Func<T> action) =>
        Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static async Task AssertInFlightCancellationAsync<T>(Task<T> work, Func<bool> hasProgress,
        CancellationTokenSource cancellation)
    {
        bool observed = SpinWait.SpinUntil(() => hasProgress() || work.IsCompleted,
            TimeSpan.FromSeconds(5));
        bool progressed = hasProgress();
        bool wasRunning = !work.IsCompleted;
        // Always cancel before asserting, so a failed progress assertion cannot
        // leave background parsing alive for the rest of the test process.
        await cancellation.CancelAsync();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await work.WaitAsync(TimeSpan.FromSeconds(5));
        });

        Assert.True(observed && progressed, "The operation did not report parsing progress.");
        Assert.True(wasRunning, "The source finished before in-flight cancellation was exercised.");
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
