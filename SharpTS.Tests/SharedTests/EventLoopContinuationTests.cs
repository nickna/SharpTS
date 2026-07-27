using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for the compiled-mode event-loop continuation handling.
///
/// A Task-backed promise (e.g. <c>fetch</c>) settles on a thread-pool thread, so
/// its <c>await</c> continuation is scheduled via <c>TaskAwaiter.OnCompleted</c>,
/// which captures the ambient <see cref="System.Threading.SynchronizationContext"/>.
/// Compiled mode now installs an event-loop sync context at the entry point, so
/// those continuations resume on the event-loop thread (Node semantics) instead
/// of escaping to the pool. Before that, a saturated/loaded pool could delay the
/// escaped continuation past the entry point's 250ms quiescence window, causing
/// the still-settling top-level promise to be abandoned and the program to print
/// nothing — the macOS-CI flake behind the FetchCookie failures.
///
/// These exercise the post-await path deterministically: code after two
/// sequential awaited fetches must run, see its closure binding, and print.
/// </summary>
public class EventLoopContinuationTests : IDisposable
{
    private readonly MockHttpServer _server;

    public EventLoopContinuationTests()
    {
        _server = new MockHttpServer();
        _server.AddTextRoute("/a", "AA");
        _server.AddTextRoute("/b", "BB");
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    [Theory, ModeData]
    public void TopLevelAsync_SequentialFetches_RunsContinuationAfterAwait(ExecutionMode mode)
    {
        // The continuation after each await (and the closure capture of `tag`)
        // must run for this to print. Mirrors the FetchCookie pattern that
        // flaked on macOS CI.
        var source = $$"""
            const tag: string = "OK";
            async function main(): Promise<void> {
                const a: any = await fetch('{{_server.BaseUrl}}a');
                const ta: string = await a.text();
                const b: any = await fetch('{{_server.BaseUrl}}b');
                const tb: string = await b.text();
                console.log(tag + ":" + ta + tb);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("OK:AABB\n", output);
    }
}
