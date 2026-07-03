using System.Diagnostics;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Gap-surfacing tests for the Web Streams API — exercises semantic corners
/// that the base <see cref="StreamsWebBasicTests"/> suite doesn't cover.
/// </summary>
/// <remarks>
/// Purpose: expose differences between the interpreter-side runtime
/// (<c>SharpTS.Runtime.Types.SharpTS*Stream</c>) and the pure-IL emitted
/// compiled-mode types (<c>$ReadableStream</c>, <c>$WritableStream</c>,
/// <c>$TransformStream</c>). Tests that pass in interpreter mode but fail
/// in compiled mode document a real pure-IL gap. Tests marked
/// <see cref="ExecutionModes.InterpretedOnly"/> are known-failing gaps with a
/// comment explaining the compiled-mode limitation.
/// </remarks>
public class StreamsWebSemanticTests
{
    #region Pending reads — queue empty, chunk arrives later

    /// <summary>
    /// Classic push-style ReadableStream: <c>start</c> captures the
    /// controller; chunks are enqueued later from a timer callback. The
    /// reader's <c>read()</c> must park and resolve when a chunk arrives.
    /// </summary>
    /// <remarks>
    /// <para><b>INTERPRETER:</b> the runtime <c>SharpTSReadableStream</c> DOES
    /// have pending-reads parking via <c>TaskCompletionSource&lt;object?&gt;</c>,
    /// but exercising it via a <c>setTimeout</c>-driven enqueue exposes an
    /// event-loop interaction bug: the test hangs for the full harness timeout
    /// (30s) even though the timer fires and <c>EnqueueInternal</c> resolves
    /// the TCS. Root cause likely involves the TCS continuation
    /// (<c>RunContinuationsAsynchronously</c>) being posted to the
    /// <c>InterpreterSynchronizationContext</c> but not actually being
    /// drained back to the awaiting task. Debugging requires instrumenting
    /// the event loop and is deferred.</para>
    ///
    /// <para><b>COMPILED:</b> the pure-IL <c>$ReadableStream.Read()</c> does
    /// not implement pending-reads parking at all. Queue empty → returns
    /// <c>done:true</c> immediately. Fixing requires async state machine
    /// emission.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PendingReadResolvedByLaterEnqueue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let ctrl: any;
                const rs = new ReadableStream({
                    start(c) { ctrl = c; }
                });
                const reader = rs.getReader();
                setTimeout(() => {
                    ctrl.enqueue("delayed");
                    ctrl.close();
                }, 10);
                async function run() {
                    const r = await reader.read();
                    console.log(r.value);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("delayed\n", output);
    }

    #endregion

    #region Async pull callback

    /// <summary>
    /// Pull callback returns a Promise that resolves after a tick. Reader
    /// should wait for the pull promise before dequeueing.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_AsyncPullCallback(ExecutionMode mode)
    {
        // Compiled mode: pure-IL $ReadableStream.Read() now sync-awaits a
        // Task<object> / $Promise returned from the pull callback via
        // GetAwaiter().GetResult() before retrying the queue drain. Blocks
        // the calling thread but matches PipeTo's sync-pump strategy.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let i = 0;
                const rs = new ReadableStream({
                    pull: async (c: any) => {
                        await Promise.resolve();
                        c.enqueue(i++);
                        if (i >= 2) c.close();
                    }
                });
                const reader = rs.getReader();
                async function run() {
                    const r1 = await reader.read();
                    const r2 = await reader.read();
                    const r3 = await reader.read();
                    console.log(r1.value, r2.value, r3.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("0 1 true\n", output);
    }

    #endregion

    #region Async write callback

    /// <summary>
    /// Write callback that does a simple <c>await Promise.resolve()</c> before
    /// recording the chunk. This tests whether the emitted <c>$WritableStream</c>
    /// (via <c>EmitUnwrapResultToTask</c>) correctly unwraps a compiled async
    /// function's returned task.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void WritableStream_AsyncWriteCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const chunks: string[] = [];
                const ws = new WritableStream({
                    write: async (chunk: any) => {
                        await Promise.resolve();
                        chunks.push(chunk);
                    }
                });
                const writer = ws.getWriter();
                async function run() {
                    await writer.write("a");
                    await writer.write("b");
                    await writer.write("c");
                    console.log(chunks.join(","));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a,b,c\n", output);
    }

    #endregion

    #region WritableStream controller access from inside write callback

    /// <summary>
    /// User <c>write(chunk, controller)</c> accesses the second argument.
    /// Tests whether the stream actually passes a controller object.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void WritableStream_WriteCallbackReceivesController(ExecutionMode mode)
    {
        // Compiled mode: $WritableStream now instantiates a real
        // $WritableStreamDefaultController in its constructor and passes it
        // as the second arg to user write(chunk, controller) callbacks.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let observedControllerType = "not-called";
                const ws = new WritableStream({
                    write(chunk, controller) {
                        observedControllerType = typeof controller;
                    }
                });
                const writer = ws.getWriter();
                async function run() {
                    await writer.write("data");
                    console.log(observedControllerType);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\n", output);
    }

    #endregion

    #region WritableStream concurrent writes serialization

    /// <summary>
    /// Concurrent writes (not awaited individually) should be serialized
    /// internally — only one user write callback runs at a time.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void WritableStream_ConcurrentWritesAreSerialized(ExecutionMode mode)
    {
        // KNOWN LIMITATION in compiled mode: the emitted $WritableStream has
        // no internal write queue. Write() calls the user callback directly
        // and returns immediately. In interpreter mode SharpTSWritableStream
        // uses AdvanceQueue to ensure at most one in-flight write; the ordering
        // and "ready" promise semantics follow from that.
        //
        // This test schedules three concurrent writes where the user callback
        // records the entry order. With serialization, all three complete
        // before the test's run() moves on. Without it, interleaving is
        // technically allowed but we rely on observable ordering.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const order: number[] = [];
                let inflight = 0;
                let maxInflight = 0;
                const ws = new WritableStream({
                    write: async (chunk: any) => {
                        inflight++;
                        if (inflight > maxInflight) maxInflight = inflight;
                        await Promise.resolve();
                        order.push(chunk);
                        inflight--;
                    }
                });
                const writer = ws.getWriter();
                async function run() {
                    const p1 = writer.write(1);
                    const p2 = writer.write(2);
                    const p3 = writer.write(3);
                    await p1; await p2; await p3;
                    console.log(order.join(","), "max=" + maxInflight);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1,2,3 max=1\n", output);
    }

    #endregion

    #region ByteLengthQueuingStrategy size weighting

    /// <summary>
    /// <c>ByteLengthQueuingStrategy</c> should weight desiredSize by
    /// <c>chunk.byteLength</c> (or the strategy's <c>size(chunk)</c>), not by
    /// 1 per chunk.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_ByteLengthStrategyWeighting(ExecutionMode mode)
    {
        // KNOWN LIMITATION in compiled mode: $ReadableStreamDefaultController.DesiredSize
        // computes `_highWaterMark - _queue.Count` (count-based) instead of
        // accumulating chunk sizes via the strategy's size() function.
        // Fixing this requires calling strategy.size(chunk) on each enqueue
        // and maintaining a running total — straightforward but not done yet.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const sizes: number[] = [];
                const rs = new ReadableStream({
                    start(c) {
                        sizes.push(c.desiredSize);        // 10
                        c.enqueue(new Uint8Array(3));     // 3 bytes
                        sizes.push(c.desiredSize);        // 7
                        c.enqueue(new Uint8Array(4));     // 4 bytes
                        sizes.push(c.desiredSize);        // 3
                    }
                }, new ByteLengthQueuingStrategy({ highWaterMark: 10 }));
                console.log(sizes.join(","));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10,7,3\n", output);
    }

    #endregion

    #region tee() — fork into two branches

    /// <summary>
    /// Both branches of a teed ReadableStream should receive every chunk.
    /// </summary>
    /// <remarks>
    /// This originally exposed a race in the interpreter-side tee() — branch1's
    /// constructor triggered a pull whose callback closed over branch2, a
    /// still-null local during construction, causing the first chunk to be
    /// lost. Fixed by switching to the eager-drain approach that the pure-IL
    /// emitted <c>$ReadableStream.Tee</c> already used.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_Tee_BothBranchesReceiveAllChunks(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) {
                        c.enqueue("a");
                        c.enqueue("b");
                        c.enqueue("c");
                        c.close();
                    }
                });
                const branches = rs.tee();
                const r1 = branches[0].getReader();
                const r2 = branches[1].getReader();
                async function run() {
                    const a1 = await r1.read();
                    const a2 = await r1.read();
                    const a3 = await r1.read();
                    const a4 = await r1.read();
                    const b1 = await r2.read();
                    const b2 = await r2.read();
                    const b3 = await r2.read();
                    const b4 = await r2.read();
                    console.log(a1.value, a2.value, a3.value, a4.done);
                    console.log(b1.value, b2.value, b3.value, b4.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a b c true\na b c true\n", output);
    }

    #endregion

    #region pipeTo with AbortSignal

    /// <remarks>
    /// Mid-pipe abort: a <c>setTimeout</c>-driven <c>ac.abort()</c> fires while
    /// the pump is parked, the pump's per-iteration <c>Task.Yield</c> lets it
    /// observe the signal, and it runs the abort/cancel/reject teardown.
    /// Previously flaky under CI load: the teardown awaits ran as un-Ref'd
    /// thread-pool continuations after the abort timer had Unref'd the loop, so
    /// the 250ms quiescence give-up could exit before <c>source.cancel()</c>
    /// flushed "source-canceled". Fixed in #325 — the pump now Refs the event
    /// loop for the duration of the teardown sequence (see
    /// <c>WebStreamsHelpers.PipeTo</c>), then Unrefs. Compiled-mode
    /// <c>$ReadableStream.PipeTo</c> extracts the signal and checks
    /// <c>aborted</c> per iteration, and as of #355 also drives the event-loop
    /// timer processor once per iteration when a signal is present (see
    /// <c>RuntimeEmitter.EmitPumpEventLoopForSignal</c>), so the
    /// <c>setTimeout(() => ac.abort())</c> here actually fires mid-pipe rather
    /// than being starved by the synchronous pump. The deterministic
    /// compiled-mode coverage for that fix is
    /// <see cref="ReadableStream_PipeTo_MidPipeAbortSignal_Compiled"/>; this
    /// guest test stays InterpretedOnly because compiled mode has that separate
    /// synchronous-pump variant.
    ///
    /// The residual "real thread-pool continuation timing" flake this remark
    /// used to document (#1213) is gone: since #1215 the pump's awaits ride the
    /// interpreter's SynchronizationContext instead of ConfigureAwait(false)
    /// thread-pool hops, so the whole abort/cancel/reject teardown runs on the
    /// event-loop thread and its output ordering is deterministic. The
    /// white-box guards for the two product fixes are
    /// <see cref="PipeTo_MidPipeAbort_RefsEventLoopAcrossTeardown"/> (teardown
    /// Ref, #325) and <c>EventLoopParkedResumeTests</c> (visible parked-resume
    /// handoff, #1211/#1212).
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_AbortSignalCancelsSourceAndAbortsDest(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let i = 0;
                const source = new ReadableStream({
                    pull(c) { c.enqueue(i++); },
                    cancel(reason) { console.log("source-canceled"); }
                });
                const dest = new WritableStream({
                    write(chunk) { /* accept everything */ },
                    abort(reason) { console.log("dest-aborted"); }
                });
                const ac = new AbortController();
                setTimeout(() => ac.abort(), 0);
                async function run() {
                    try {
                        await source.pipeTo(dest, { signal: ac.signal });
                        console.log("pipe-completed-normally");
                    } catch (e) {
                        console.log("pipe-rejected");
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        // The signal handler fires, pipeTo's pump sees the abort, calls
        // dest.abort and source.cancel, then rejects. The catch block runs.
        Assert.Contains("dest-aborted", output);
        Assert.Contains("source-canceled", output);
        Assert.Contains("pipe-rejected", output);
        Assert.DoesNotContain("pipe-completed-normally", output);
    }

    /// <remarks>
    /// White-box regression for #325, the product fix behind the (load-flaky)
    /// guest-level test above. The pipeTo pump runs its abort/cancel/reject
    /// teardown as un-Ref'd thread-pool continuations; before the fix, a program
    /// whose only remaining work was that teardown could hit the interpreter's
    /// 250ms quiescence give-up after <c>dest.abort()</c> but before the source
    /// <c>cancel()</c> callback flushed "source-canceled". The fix
    /// (<c>WebStreamsHelpers.PipeTo</c>) scopes an event-loop Ref to the whole
    /// teardown sequence.
    ///
    /// A guest-level test can't pin this deterministically: every guest async
    /// delay is either a scheduled virtual timer (which
    /// <c>HasPendingEventLoopWork</c> already counts as pending work) or an
    /// active handle, so none reproduces the invisible thread-pool continuation
    /// window the fix closes. So this drives <c>PipeTo</c> directly and gates the
    /// dest <c>abort()</c> on a TaskCompletionSource, freezing the pump in the
    /// middle of the teardown. That lets us assert the event loop stays Ref'd for
    /// the entire abort→cancel→reject sequence with no wall-clock window — the
    /// assertion fails deterministically if the teardown Ref is removed, and is
    /// load-independent (the gate, not a timer, drives ordering).
    /// </remarks>
    [Fact]
    public void PipeTo_MidPipeAbort_RefsEventLoopAcrossTeardown()
    {
        var interp = new Interpreter(TextWriter.Null, TextWriter.Null);

        // dest.abort() returns a promise we hold open, freezing the pump inside
        // the teardown right after it takes the Ref and before it reaches
        // source.cancel().
        var abortGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var abortInvoked = new ManualResetEventSlim(false);
        using var cancelInvoked = new ManualResetEventSlim(false);

        var destSink = new Dictionary<string, object?>
        {
            ["write"] = BuiltInMethod.CreateV2("write", 1, static (_, _, _) => RuntimeValue.Undefined),
            ["abort"] = BuiltInMethod.CreateV2("abort", 1, (_, _, _) =>
            {
                abortInvoked.Set();
                return RuntimeValue.FromObject(new SharpTSPromise(abortGate.Task));
            }),
        };
        var dest = new SharpTSWritableStream(interp, destSink, strategy: null);

        var sourceSink = new Dictionary<string, object?>
        {
            ["cancel"] = BuiltInMethod.CreateV2("cancel", 1, (_, _, _) =>
            {
                cancelInvoked.Set();
                return RuntimeValue.Undefined;
            }),
        };
        var source = new SharpTSReadableStream(interp, sourceSink, strategy: null);

        // Already-aborted signal → the pump goes straight to the teardown branch
        // on its first iteration without needing any real reads.
        var signal = new SharpTSAbortSignal(new CancellationToken(canceled: true));
        var opts = new Dictionary<string, object?> { ["signal"] = signal };

        Assert.False(interp.HasActiveHandles, "no active handles before piping starts");

        // The pump's first `await Task.Yield()` captures the ambient
        // SynchronizationContext; null it so the pump runs free on the thread
        // pool rather than on any xUnit-installed context that this synchronous
        // test thread would never pump.
        SharpTSPromise pipe;
        var prevContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            pipe = WebStreamsHelpers.PipeTo(interp, source, dest, opts);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prevContext);
        }

        // The pump reached dest.abort() and parked on our gate. The teardown Ref
        // is taken before the abort await, so the handle is already active here.
        // Without the #325 fix this stays false and the next assert fails.
        Assert.True(abortInvoked.Wait(TimeSpan.FromSeconds(5)), "pump never reached the abort teardown");
        Assert.True(interp.HasActiveHandles, "teardown must keep the event loop Ref'd (#325)");
        Assert.False(cancelInvoked.IsSet, "source cancel() must not run until dest.abort() resolves");

        // Release the gate: the pump runs source.cancel(), then rejects.
        abortGate.SetResult(null);

        Assert.True(cancelInvoked.Wait(TimeSpan.FromSeconds(5)), "source cancel() was dropped");

        // The pipe promise rejects with the abort reason once the pump unwinds.
        var fault = Assert.Throws<AggregateException>(() => pipe.Task.Wait(TimeSpan.FromSeconds(5)));
        Assert.IsType<SharpTSPromiseRejectedException>(fault.InnerException);

        // The teardown Ref is released exactly once, so the loop can quiesce.
        var sw = Stopwatch.StartNew();
        while (interp.HasActiveHandles && sw.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(5);
        Assert.False(interp.HasActiveHandles, "teardown Ref must be released after the pump settles");
    }

    #endregion

    #region TransformStream controller.terminate

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TransformStream_ControllerTerminateInsideTransform(ExecutionMode mode)
    {
        // Interpreter-side: FIXED. terminate() now closes the readable (per
        // WHATWG spec) rather than erroring it, matching what `reader.read()`
        // expects after the stream stops.
        //
        // Compiled-mode: the emitted $TransformSinkHolder passes the
        // underlying $ReadableStream directly as the "controller" arg to the
        // user's transform(chunk, controller) callback. The readable exposes
        // Enqueue/CloseStream/ErrorStream which JS-side dispatch finds as
        // enqueue/close/error. "terminate" maps to CloseStream via the
        // PascalCase reflection fallback? No — "terminate" doesn't PascalCase
        // to "CloseStream". It would need a dedicated $TransformStreamDefaultController
        // class to expose terminate. Compiled mode expected to fail.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const ts = new TransformStream({
                    transform(chunk, c) {
                        if (chunk === "stop") {
                            c.terminate();
                            return;
                        }
                        c.enqueue(chunk.toUpperCase());
                    }
                });
                const writer = ts.writable.getWriter();
                const reader = ts.readable.getReader();
                async function run() {
                    writer.write("a");
                    writer.write("b");
                    writer.write("stop");
                    const r1 = await reader.read();
                    const r2 = await reader.read();
                    const r3 = await reader.read();
                    console.log(r1.value, r2.value, r3.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("A B true\n", output);
    }

    #endregion

    #region ReadableStream Symbol.asyncIterator

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_AsyncIteratorForAwaitOf(ExecutionMode mode)
    {
        // KNOWN LIMITATION in both modes (gap from original review): neither
        // the interpreter-side SharpTSReadableStream nor the emitted
        // $ReadableStream implements [Symbol.asyncIterator]. Node 18+ makes
        // `for await (const chunk of stream)` work on ReadableStream directly;
        // we don't. This test is marked interpreter-only and is expected to
        // fail even there — it's a "surface the gap" marker.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) {
                        c.enqueue(1);
                        c.enqueue(2);
                        c.enqueue(3);
                        c.close();
                    }
                });
                async function run() {
                    const collected: number[] = [];
                    for await (const chunk of rs) {
                        collected.push(chunk);
                    }
                    console.log(collected.join(","));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1,2,3\n", output);
    }

    #endregion

    #region Compiled-mode-only: pure-IL pending-reads parking

    /// <summary>
    /// Compiled-mode variant of <see cref="ReadableStream_PendingReadResolvedByLaterEnqueue"/>.
    /// Exercises the pure-IL <c>$ReadableStream</c> pending-reads parking path
    /// where <c>reader.read()</c> is called before any chunks are available,
    /// then a microtask-scheduled <c>enqueue</c> resolves the parked
    /// <see cref="TaskCompletionSource{T}"/>.
    /// </summary>
    /// <remarks>
    /// Uses <c>Promise.resolve().then(...)</c> rather than <c>setTimeout</c>
    /// because compiled-mode <c>await new Promise(r => setTimeout(r, 10))</c>
    /// is a separate known-broken path (timer-driven async resumption) and
    /// orthogonal to the parking feature being validated here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PendingReadResolvedByLaterEnqueue_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let ctrl: any;
                const rs = new ReadableStream({
                    start(c) { ctrl = c; }
                });
                const reader = rs.getReader();
                Promise.resolve().then(() => {
                    ctrl.enqueue("delayed");
                    ctrl.close();
                });
                async function run() {
                    const r = await reader.read();
                    console.log(r.value);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("delayed\n", output);
    }

    /// <summary>
    /// Verifies that closing a <c>$ReadableStream</c> that has a parked
    /// reader drains the pending-reads queue with <c>{value:undefined, done:true}</c>,
    /// not leaving the reader hanging forever.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PendingReadResolvedByLaterClose_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let ctrl: any;
                const rs = new ReadableStream({
                    start(c) { ctrl = c; }
                });
                const reader = rs.getReader();
                Promise.resolve().then(() => { ctrl.close(); });
                async function run() {
                    const r = await reader.read();
                    console.log(r.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    /// <summary>
    /// Pre-aborted <see cref="AbortController"/> signal passed in <c>pipeTo</c>
    /// options. Pure-IL <c>$ReadableStream.PipeTo</c> should check the signal
    /// before each read, call <c>writer.abort(reason)</c> +
    /// <c>source.cancel(reason)</c>, and reject the returned task.
    /// </summary>
    /// <remarks>
    /// Compiled-mode only: the interpreter-side variant is blocked by the
    /// top-level GetResult event-loop issue (see
    /// <see cref="ReadableStream_PipeTo_AbortSignalCancelsSourceAndAbortsDest"/>).
    /// This variant uses a pre-aborted signal, so no mid-pipe timer-driven
    /// abort is needed, and issue #22 (timer-callback dispatch of
    /// $PromiseResolveCallback) does not apply.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_PreAbortedSignal_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const source = new ReadableStream({
                    start(c) { c.enqueue("a"); c.enqueue("b"); c.close(); },
                    cancel(reason) { console.log("source-canceled"); }
                });
                const dest = new WritableStream({
                    write(chunk) { /* accept */ },
                    abort(reason) { console.log("dest-aborted"); }
                });
                const ac = new AbortController();
                ac.abort();
                async function run() {
                    try {
                        await source.pipeTo(dest, { signal: ac.signal });
                        console.log("pipe-completed-normally");
                    } catch {
                        console.log("pipe-rejected");
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("dest-aborted", output);
        Assert.Contains("source-canceled", output);
        Assert.Contains("pipe-rejected", output);
        Assert.DoesNotContain("pipe-completed-normally", output);
    }

    /// <summary>
    /// #355: a mid-pipe abort delivered through the event loop
    /// (<c>setTimeout(() =&gt; ac.abort(), 0)</c>) must abort the destination,
    /// cancel the source, and reject the pipe — even though the compiled pump
    /// is a synchronous blocking loop.
    /// </summary>
    /// <remarks>
    /// Compiled-mode only, and deterministic here. The source is an infinite
    /// synchronous-pull stream, so before the fix the sync pump spun forever
    /// reading chunks and the abort timer never got a chance to run (the whole
    /// test would hang to the harness timeout). The fix drives the event-loop
    /// timer processor once per pump iteration when a signal is present (see
    /// <c>RuntimeEmitter.EmitPumpEventLoopForSignal</c>), so the
    /// <c>setTimeout(0)</c> callback fires on the first iteration, mutates the
    /// signal, and the existing per-iteration <c>aborted</c> check runs the
    /// abort/cancel/reject teardown. Unlike the interpreter variant
    /// (<see cref="ReadableStream_PipeTo_AbortSignalCancelsSourceAndAbortsDest"/>),
    /// the entire sequence runs synchronously on the main thread, so there is
    /// no thread-pool continuation race and the assertion is load-independent.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_MidPipeAbortSignal_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let i = 0;
                const source = new ReadableStream({
                    pull(c) { c.enqueue(i++); },
                    cancel(reason) { console.log("source-canceled"); }
                });
                const dest = new WritableStream({
                    write(chunk) { /* accept everything */ },
                    abort(reason) { console.log("dest-aborted"); }
                });
                const ac = new AbortController();
                setTimeout(() => ac.abort(), 0);
                async function run() {
                    try {
                        await source.pipeTo(dest, { signal: ac.signal });
                        console.log("pipe-completed-normally");
                    } catch (e) {
                        console.log("pipe-rejected");
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("dest-aborted", output);
        Assert.Contains("source-canceled", output);
        Assert.Contains("pipe-rejected", output);
        Assert.DoesNotContain("pipe-completed-normally", output);
    }

    /// <remarks>
    /// #448 — push-style source piped with no signal. <c>start</c> captures the
    /// controller; the only chunk is <c>enqueue</c>d (and the stream
    /// <c>close</c>d) later from a <c>setTimeout</c> callback. The first
    /// <c>this.Read()</c> inside the pump parks on a pending
    /// <c>TaskCompletionSource</c> that only the timer can settle.
    ///
    /// <para>Before the fix the compiled pump sync-awaited that read with
    /// <c>GetAwaiter().GetResult()</c> on the main thread, starving the loop: the
    /// enqueue timer could never run, so the process hung. The fix makes the
    /// read/write awaits cooperative — the pump drives <c>$EventLoop.PumpOnce()</c>
    /// (drain queue + fire timers) while the task is pending (see
    /// <c>RuntimeEmitter.EmitCooperativeAwaitTaskOrSignal</c> /
    /// <c>EmitEventLoopPumpOnce</c>), so the timer fires, the read resolves, and
    /// the chunk flows. The interpreter already handled this via its real async
    /// pump (<c>WebStreamsHelpers.PipeTo</c>).</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_PushSourceViaTimer(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("wrote x\ndone\n", output);
    }

    /// <remarks>
    /// #448 residual gap — a signal-bearing pipe over a push source that never
    /// produces, with a <b>delayed</b> abort (<c>setTimeout(() => ac.abort(),
    /// 50)</c>). The #355 loop-top pump fires once before the abort is due, then
    /// the parking read used to block the main thread forever. With the
    /// cooperative wait the pump keeps driving the loop, the abort timer fires
    /// mid-wait, the in-loop signal check observes it, and the pump branches back
    /// to its loop top to run the abort/cancel/reject teardown. CompiledOnly: the
    /// interpreter abort-pipe end-to-end assertion is load-flaky (see
    /// <see cref="ReadableStream_PipeTo_AbortSignalCancelsSourceAndAbortsDest"/>),
    /// whereas the compiled pump runs synchronously and is deterministic.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_PushSourceDelayedAbort_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const ac = new AbortController();
                const source = new ReadableStream({ start(c) { /* never enqueues */ } });
                const dest = new WritableStream({
                    write(chunk) { /* accept everything */ },
                    abort(reason) { console.log("dest-aborted"); }
                });
                setTimeout(() => ac.abort(), 50);
                async function run() {
                    try {
                        await source.pipeTo(dest, { signal: ac.signal });
                        console.log("pipe-completed-normally");
                    } catch (e) {
                        console.log("pipe-rejected");
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("dest-aborted", output);
        Assert.Contains("pipe-rejected", output);
        Assert.DoesNotContain("pipe-completed-normally", output);
    }

    /// <remarks>
    /// #448 write-side — a push-style destination whose <c>write()</c> settles
    /// later through the event loop (here a microtask via
    /// <c>Promise.resolve().then</c>). The pump's main-loop write await is
    /// cooperative too (see <c>EmitCooperativeUnwrapResultToTask</c>), so a write
    /// that can only resolve once the loop runs doesn't deadlock the synchronous
    /// pump the way a push source read does. CompiledOnly: this is a compiled-pump
    /// gap; the interpreter's async pump already awaits writes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PipeTo_PushStyleWrite_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const source = new ReadableStream({ start(c) { c.enqueue("a"); c.close(); } });
                const dest = new WritableStream({
                    write(chunk) { return Promise.resolve().then(() => { console.log("wrote " + chunk); }); }
                });
                async function run() {
                    await source.pipeTo(dest);
                    console.log("done");
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("wrote a\ndone\n", output);
    }

    /// <summary>
    /// Verifies that erroring a <c>$ReadableStream</c> that has a parked
    /// reader rejects the pending-reads queue via <c>TrySetException</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStream_PendingReadRejectedByLaterError_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let ctrl: any;
                const rs = new ReadableStream({
                    start(c) { ctrl = c; }
                });
                const reader = rs.getReader();
                Promise.resolve().then(() => { ctrl.error("boom"); });
                async function run() {
                    try {
                        await reader.read();
                        console.log("no-throw");
                    } catch (e) {
                        console.log("threw");
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw\n", output);
    }

    #endregion

    #region Promise-object surfaces (#223: reader/writer promises must support .then)

    /// <summary>
    /// #223: <c>reader.read()</c>/<c>reader.closed</c>/<c>reader.cancel()</c>
    /// must return Promise objects usable with <c>.then()</c>/<c>.catch()</c>,
    /// not raw Tasks whose property access throws.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ReadableStreamReader_ReadAndClosed_AreThenable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ReadableStream as RS } from "stream/web";
                const rs = new (RS as any)({ start(c: any) { c.enqueue("z"); c.close(); } });
                const reader = rs.getReader();
                reader.closed.then(() => console.log("closed-resolved"));
                reader.read().then((r: any) => console.log("read:", r.value, r.done));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("read: z false", output);
        Assert.Contains("closed-resolved", output);
    }

    /// <summary>
    /// #223 audit follow-through: writer <c>write()</c>/<c>close()</c>/<c>ready</c>/
    /// <c>closed</c> must be thenable Promise objects as well.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void WritableStreamWriter_WriteReadyClosed_AreThenable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const chunks: string[] = [];
                const ws = new WritableStream({
                    write(chunk) { chunks.push(chunk as string); }
                });
                const writer = (ws as any).getWriter();
                writer.ready.then(() => console.log("ready"));
                writer.write("a").then(() => {
                    console.log("wrote:", chunks.join(","));
                    writer.close().then(() => console.log("close-resolved"));
                    writer.closed.then(() => console.log("closed-resolved"));
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("ready", output);
        Assert.Contains("wrote: a", output);
        Assert.Contains("close-resolved", output);
        Assert.Contains("closed-resolved", output);
    }

    #endregion
}
