using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Event-loop liveness contract for parked-consumer resumes (#1211/#1212, the #320 family):
/// when a producer settles a parked stream read (async-iterator <c>next()</c>, pipeTo pump
/// read), the consumer's resume must be visible to the event loop's exit check from the moment
/// of settling. The resume rides the interpreter's SynchronizationContext, whose Post lands in
/// the callback queue synchronously inside TrySetResult — so a producer that settles the read
/// and tears down its own timer in the same tick (setInterval's last tick, a one-shot
/// setTimeout) cannot leave the loop looking quiescent while the continuation is in flight.
///
/// Before the fix these resumes hopped through the thread pool (<c>ConfigureAwait(false)</c>
/// in the stream runtime types): invisible to <c>RunEventLoopCore</c>'s
/// no-handles-and-empty-queue exit, so under CI thread-pool pressure the program exited
/// silently before the loop body ever ran — the "Expected 10,20,30 / Actual ''" signature.
///
/// SHARPTS_TEST_STREAM_PARKED_RESUME_DELAY_MS stalls the resume immediately after the parked
/// task settles, deterministically reproducing that scheduling delay: with a thread-pool hop
/// the stall sits inside the invisible window and each test fails 100% with empty output; with
/// the SynchronizationContext resume it runs inside a queued loop callback and merely delays
/// output. Assertions are output-correctness only (never a wall-clock window — #295 flake-class
/// rules) so the tests cannot themselves flake.
///
/// Interpreted mode only: compiled mode keeps continuations on the loop via the emitted
/// $EventLoopSyncContext instead.
/// </summary>
public class EventLoopParkedResumeTests
{
    private const string DelayVar = "SHARPTS_TEST_STREAM_PARKED_RESUME_DELAY_MS";

    // 600ms: decisively past the 250ms WaitForPromise quiescence window, and far past the
    // RunEventLoopCore exit check (which has no grace window at all). Env var is process-wide,
    // so concurrently running stream tests also absorb the stall during this test's window —
    // they still pass, just slower (same accepted tradeoff as SHARPTS_TEST_FS_ASYNC_DELAY_MS).
    private const string DelayMs = "600";

    /// <summary>
    /// The #1211 shape (StreamModuleTests.Readable_AsyncIterator_SlowProducer): a `for await`
    /// over a stream-module Readable races a setInterval producer whose final tick pushes EOF
    /// and clears the interval — dropping the handle count to zero in the same tick that
    /// settles the parked pull. Covers SharpTSReadableAsyncIterator.
    /// </summary>
    [Fact]
    public void ParkedReadableIteratorResume_SurvivesProducerTeardown()
    {
        Environment.SetEnvironmentVariable(DelayVar, DelayMs);
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = """
                    import { Readable } from 'stream';
                    async function main(): Promise<void> {
                        const r = new Readable({ objectMode: true });
                        let i = 0;
                        const t = setInterval(() => {
                            i++;
                            if (i <= 3) { r.push(i * 10); }
                            else { r.push(null); clearInterval(t); }
                        }, 5);
                        const out: number[] = [];
                        for await (const x of r) { out.push(x); }
                        console.log(out.join(','));
                    }
                    main();
                    """
            };

            var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
            Assert.Equal("10,20,30\n", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DelayVar, null);
        }
    }

    /// <summary>
    /// The #1212 shape (StreamsWebSemanticTests.ReadableStream_PipeTo_PushSourceViaTimer): the
    /// pipeTo pump's parked read is settled by a one-shot setTimeout producer; the timer's ref
    /// is released when its callback returns, so the settled pump resume is briefly the only
    /// remaining work. Covers the WebStreamsHelpers.PipeTo pump.
    /// </summary>
    [Fact]
    public void ParkedPipeToPumpResume_SurvivesProducerTeardown()
    {
        Environment.SetEnvironmentVariable(DelayVar, DelayMs);
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = """
                    let ctrl: any;
                    const source = new ReadableStream({ start(c) { ctrl = c; } });
                    const dest = new WritableStream({ write(chunk) { console.log("wrote " + chunk); } });
                    setTimeout(() => { ctrl.enqueue("x"); ctrl.close(); }, 10);
                    async function run() {
                        await source.pipeTo(dest);
                        console.log("done");
                    }
                    run();
                    """
            };

            var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
            Assert.Equal("wrote x\ndone\n", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DelayVar, null);
        }
    }

    /// <summary>
    /// Same race through the web-stream async iterator (`for await` over a ReadableStream
    /// rather than a stream-module Readable). Covers SharpTSReadableStreamAsyncIterator.
    /// </summary>
    [Fact]
    public void ParkedWebStreamIteratorResume_SurvivesProducerTeardown()
    {
        Environment.SetEnvironmentVariable(DelayVar, DelayMs);
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = """
                    let ctrl: any;
                    const rs = new ReadableStream({ start(c) { ctrl = c; } });
                    setTimeout(() => { ctrl.enqueue("a"); ctrl.close(); }, 10);
                    async function main(): Promise<void> {
                        const out: string[] = [];
                        for await (const x of rs) { out.push(x); }
                        console.log(out.join(','));
                    }
                    main();
                    """
            };

            var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
            Assert.Equal("a\n", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DelayVar, null);
        }
    }
}
