using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for worker_threads-related APIs: SharedArrayBuffer, Atomics,
/// MessageChannel, and structuredClone.
/// </summary>
public class WorkerThreadsTests
{
    [Fact]
    public void AtomicsWait_TimedOutLocationIsRemovedFromRegistry()
    {
        using var buffer = new SharpTSSharedArrayBuffer(16);
        var view = new SharpTSInt32Array(buffer);

        Assert.Equal("timed-out", SharpTSAtomics.Wait(view, 2, 0, timeout: 0));
        Assert.False(SharpTSAtomics.HasWaiterLocation(buffer.BufferId, byteOffset: 8));
    }

    [Theory, ModeData]
    public void AtomicsPause_AcceptsOnlyIntegralNumbers(ExecutionMode mode)
    {
        var source = """
            console.log(Atomics.pause() === undefined);
            console.log(Atomics.pause(42) === undefined);
            for (const value of [true, 1.5, "2", null]) {
                try {
                    Atomics.pause(value as any);
                } catch (error) {
                    console.log(error instanceof TypeError);
                }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void AtomicsPause_HasFunctionMetadata(ExecutionMode mode)
    {
        var source = """
            console.log(Atomics.pause.name);
            console.log(Atomics.pause.length);
            """;
        Assert.Equal("pause\n0\n", TestHarness.Run(source, mode));
    }

    #region SharedArrayBuffer Tests

    [Theory, ModeData]
    public void SharedArrayBuffer_Constructor_CreatesBufferWithSize(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            console.log(sab.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n", output);
    }

    [Theory, ModeData]
    public void SharedArrayBuffer_Slice_CreatesNewBuffer(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let sliced = sab.slice(4, 12);
            console.log(sliced.byteLength);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region TypedArray over SharedArrayBuffer Tests

    [Theory, ModeData]
    public void Int32Array_OverSharedArrayBuffer_SharesMemory(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view1 = new Int32Array(sab);
            let view2 = new Int32Array(sab);
            view1[0] = 42;
            console.log(view2[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void TypedArray_WithByteOffset_CreatesCorrectView(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab, 4, 2);
            console.log(view.byteOffset);
            console.log(view.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n2\n", output);
    }

    [Theory, ModeData]
    public void Uint8Array_OverSharedArrayBuffer_WorksCorrectly(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(4);
            let view = new Uint8Array(sab);
            view[0] = 255;
            view[1] = 128;
            console.log(view[0]);
            console.log(view[1]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n128\n", output);
    }

    [Theory, ModeData]
    public void TypedArray_FromLength_CreatesArray(ExecutionMode mode)
    {
        var source = @"
            let arr = new Int32Array(4);
            arr[0] = 10;
            arr[1] = 20;
            arr[2] = 30;
            arr[3] = 40;
            console.log(arr[0]);
            console.log(arr[3]);
            console.log(arr.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n40\n4\n", output);
    }

    #endregion

    #region Atomics Tests

    [Theory, ModeData]
    public void Atomics_Load_ReadsValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            console.log(Atomics.load(view, 0));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Store_WritesValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            Atomics.store(view, 0, 100);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Add_AddsAndReturnsOldValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 10;
            let oldValue = Atomics.add(view, 0, 5);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Sub_SubtractsAndReturnsOldValue(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 10;
            let oldValue = Atomics.sub(view, 0, 3);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n7\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Exchange_SwapsValues(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let oldValue = Atomics.exchange(view, 0, 100);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory, ModeData]
    public void Atomics_CompareExchange_Success(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let result = Atomics.compareExchange(view, 0, 42, 100);
            console.log(result);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory, ModeData]
    public void Atomics_CompareExchange_Failure(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 42;
            let result = Atomics.compareExchange(view, 0, 99, 100);
            console.log(result);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n", output);
    }

    [Theory, ModeData]
    public void Atomics_And_PerformsBitwiseAnd(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1111;
            let oldValue = Atomics.and(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n5\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Or_PerformsBitwiseOr(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1010;
            let oldValue = Atomics.or(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n15\n", output);
    }

    [Theory, ModeData]
    public void Atomics_Xor_PerformsBitwiseXor(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view = new Int32Array(sab);
            view[0] = 0b1111;
            let oldValue = Atomics.xor(view, 0, 0b0101);
            console.log(oldValue);
            console.log(view[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n10\n", output);
    }

    [Theory, ModeData]
    public void Atomics_IsLockFree_ReturnsBooleanForSize(ExecutionMode mode)
    {
        var source = @"
            console.log(Atomics.isLockFree(4));
            console.log(Atomics.isLockFree(8));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region MessageChannel Tests

    [Theory, ModeData]
    public void MessageChannel_Constructor_CreatesTwoPorts(ExecutionMode mode)
    {
        var source = @"
            let channel = new MessageChannel();
            console.log(channel.port1 !== null);
            console.log(channel.port2 !== null);
            console.log(channel.port1 !== channel.port2);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void MessageChannel_PortOnMessage_ReceivesPostedValue(ExecutionMode mode)
    {
        // #209 (interpreter) / #222 (compiled $MessagePort): port.on() must
        // dispatch through the port's own member table (postMessage/start/
        // close reachable), a 'message' listener implicitly starts the port,
        // and the listener receives the cloned value directly per Node
        // worker_threads semantics.
        var source = @"
            let channel: any = new MessageChannel();
            channel.port2.on('message', (value: any) => {
                console.log('received: ' + value);
                channel.port1.close();
                channel.port2.close();
            });
            channel.port1.postMessage('hello');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("received: hello", output);
    }

    [Theory, ModeData]
    public void MessageChannel_MessagesPostedBeforeListener_DeliveredInOrderAfterImplicitStart(ExecutionMode mode)
    {
        // #222: messages posted before any listener exists must queue and be
        // delivered (in order) once a 'message' listener implicitly starts
        // the port.
        var source = @"
            let channel: any = new MessageChannel();
            channel.port1.postMessage('first');
            channel.port1.postMessage('second');
            channel.port2.on('message', (value: any) => {
                console.log('got: ' + value);
                if (value === 'second') {
                    channel.port1.close();
                    channel.port2.close();
                }
            });
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Contains("got: first\ngot: second", output);
    }

    [Theory, ModeData]
    public void MessageChannel_UnclosedPort_DoesNotHangProcess(ExecutionMode mode)
    {
        // #1254: a plain in-process MessageChannel (neither port ever transferred to a
        // worker) must not keep the event loop alive once its queue drains — matching
        // SharpTSMessagePort, which only Refs a started CROSS-THREAD port (#406). Before
        // the fix, the compiled $MessagePort.Start() unconditionally Ref'd the loop, so
        // this program hung forever in compiled mode despite the port never closing.
        var source = @"
            let channel: any = new MessageChannel();
            channel.port2.on('message', (value: any) => {
                console.log('got: ' + value);
            });
            channel.port1.postMessage('hi');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: hi\n", output);
    }

    #endregion

    #region StructuredClone Tests

    [Theory, ModeData]
    public void StructuredClone_ClonesFlatPrimitiveObject(ExecutionMode mode)
    {
        var source = """
            const original = { kind: 'ping', sequence: 42, ready: true, empty: null };
            const cloned = structuredClone(original);
            cloned.kind = 'pong';
            cloned.sequence = 43;
            console.log(original.kind + ':' + original.sequence);
            console.log(cloned.kind + ':' + cloned.sequence);
            console.log(cloned.ready + ':' + (cloned.empty === null));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ping:42\npong:43\ntrue:true\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesObject(ExecutionMode mode)
    {
        var source = @"
            let obj = { a: 1, b: 'hello', c: [1, 2, 3] };
            let cloned = structuredClone(obj);
            cloned.a = 999;
            console.log(obj.a);
            console.log(cloned.a);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesNestedObjects(ExecutionMode mode)
    {
        var source = @"
            let obj = { nested: { value: 42 } };
            let cloned = structuredClone(obj);
            cloned.nested.value = 100;
            console.log(obj.nested.value);
            console.log(cloned.nested.value);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesArrays(ExecutionMode mode)
    {
        var source = @"
            let arr = [1, 2, [3, 4]];
            let cloned = structuredClone(arr);
            cloned[0] = 999;
            console.log(arr[0]);
            console.log(cloned[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_SharesSharedArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            let sab = new SharedArrayBuffer(16);
            let view1 = new Int32Array(sab);
            view1[0] = 42;

            let clonedSab = structuredClone(sab);
            let view2 = new Int32Array(clonedSab);

            // SharedArrayBuffer is shared by reference, not cloned
            console.log(view2[0]);
            view2[0] = 100;
            console.log(view1[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesMap(ExecutionMode mode)
    {
        var source = @"
            let map = new Map<string, number>([['a', 1], ['b', 2]]);
            let cloned = structuredClone(map);
            cloned.set('a', 999);
            console.log(map.get('a'));
            console.log(cloned.get('a'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n999\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesSet(ExecutionMode mode)
    {
        var source = @"
            let mySet = new Set([1, 2, 3]);
            let cloned = structuredClone(mySet);
            cloned.add(4);
            console.log(mySet.size);
            console.log(cloned.size);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n", output);
    }

    /// <summary>
    /// #1255: <c>structuredClone</c> must deep-clone Date/RegExp/TypedArray/Buffer/Error —
    /// mutating (or, for RegExp, just reading the identity of) the source afterward must not
    /// affect the clone. Before the fix, compiled mode's <c>$Runtime.StructuredClone</c>
    /// aliased all of these by reference (only List/Dictionary/Set were deep-cloned).
    /// </summary>
    [Theory, ModeData]
    public void StructuredClone_ClonesDateIndependently(ExecutionMode mode)
    {
        var source = @"
            let d = new Date(1000);
            let cloned: any = structuredClone(d);
            d.setTime(2000);
            console.log(cloned.getTime());
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1000\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesRegExpSourceAndFlags(ExecutionMode mode)
    {
        var source = @"
            let r = /abc/gi;
            let cloned: any = structuredClone(r);
            console.log(cloned.source);
            console.log(cloned.flags);
            console.log(cloned !== r);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("abc\ngi\ntrue\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesTypedArrayIndependently(ExecutionMode mode)
    {
        var source = @"
            let a = new Int32Array([5, 6, 7]);
            let cloned: any = structuredClone(a);
            a[0] = 99;
            console.log(cloned[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesBufferIndependently(ExecutionMode mode)
    {
        var source = @"
            let b = Buffer.from([1, 2, 3]);
            let cloned: any = structuredClone(b);
            b[0] = 99;
            console.log(cloned[0]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ClonesErrorPreservingNameAndMessage(ExecutionMode mode)
    {
        var source = @"
            let e = new TypeError('bad');
            let cloned: any = structuredClone(e);
            console.log(cloned.name);
            console.log(cloned.message);
            console.log(cloned !== e);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TypeError\nbad\ntrue\n", output);
    }

    /// <summary>
    /// #1255: uncloneable values — functions, symbols, and class instances — must throw
    /// (spec: DataCloneError), both at the top level and nested inside a plain object.
    /// Before the fix, compiled mode's shallow fallback aliased these by reference instead
    /// of throwing.
    /// </summary>
    [Theory, ModeData]
    public void StructuredClone_ThrowsForFunction(ExecutionMode mode)
    {
        // Dual-mode parity check: a non-guest-throw exception's catch value is the raw
        // message string in both modes (interpreter: `ex.Message` for a non-ThrowException;
        // compiled: WrapException's $DataCloneError branch), not an Error instance.
        var source = @"
            try {
                structuredClone(() => {});
                console.log('no-throw');
            } catch (e) {
                console.log('threw:' + typeof e);
                console.log((e as string).includes('DataCloneError'));
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw:string\ntrue\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ThrowsForNestedFunction(ExecutionMode mode)
    {
        var source = @"
            try {
                structuredClone({ fn: () => {} });
                console.log('no-throw');
            } catch {
                console.log('threw');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ThrowsForSymbol(ExecutionMode mode)
    {
        var source = @"
            try {
                structuredClone(Symbol('x'));
                console.log('no-throw');
            } catch {
                console.log('threw');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    [Theory, ModeData]
    public void StructuredClone_ThrowsForClassInstance(ExecutionMode mode)
    {
        var source = @"
            class Foo { x = 1; }
            try {
                structuredClone(new Foo());
                console.log('no-throw');
            } catch {
                console.log('threw');
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("threw\n", output);
    }

    #endregion

    #region Worker.terminate() event-loop liveness (#324)

    /// <summary>
    /// Regression for #324: when <c>await worker.terminate()</c> is the only
    /// remaining top-level work and the worker takes longer than the event
    /// loop's 250ms quiescence window to wind down, the parent must stay alive
    /// until the terminate promise settles.
    /// </summary>
    /// <remarks>
    /// The worker keeps its own event loop alive ~500ms via a pending timer, so
    /// the parent's <c>_thread.Join</c> (inside <c>SharpTSWorker.Terminate</c>)
    /// blocks well past the 250ms give-up. That join task is invisible to
    /// <c>HasPendingEventLoopWork</c>; before the fix the parent abandoned the
    /// terminate promise and exited without printing "terminated". The fix Refs
    /// the parent loop for the join's duration — the parent interpreter loop
    /// (interpreter mode) or the emitted <c>$EventLoop</c> (compiled mode, #354).
    /// <c>__dirname</c> routes the harness through the real-disk path so the
    /// spawned worker can load its script. The assertion is positive (output
    /// present) and load-independent — under load the join simply takes longer and
    /// the Ref keeps the loop alive for it, so the test cannot flake the way a
    /// wall-clock window would.
    /// </remarks>
    [Theory, ModeData]
    public void Worker_Terminate_KeepsEventLoopAliveUntilSettled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_slow.ts"] = """
                // Hold the worker's event loop open ~500ms so the parent's
                // terminate() thread-join outlasts the 250ms quiescence window.
                setTimeout(() => {}, 500);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_slow.ts");
                async function run() {
                    await w.terminate();
                    console.log("terminated");
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("terminated", output);
    }

    /// <summary>
    /// #997: terminating a worker parked in <c>Atomics.wait</c> must actually unwind the
    /// worker thread (not just settle the promise), and the <c>'exit'</c> event must report
    /// code 1. The worker parks on a never-notified index, so before the fix the
    /// <c>Monitor.Wait(Infinite)</c> had no cancellation hook: the 5s join timed out, the
    /// thread leaked, and its <c>finally</c> — the only emitter of <c>'exit'</c> — never ran.
    /// </summary>
    /// <remarks>
    /// The arriving <c>'exit'</c> event is the correctness signal: it is enqueued solely from
    /// the worker thread's <c>finally</c>, which executes only once the thread unwinds out of
    /// the parked wait. A still-leaked thread would never produce it. <c>"survived"</c> after
    /// the wait must never be delivered — termination is not catchable by guest code.
    /// </remarks>
    [Theory, ModeData]
    public void Worker_Terminate_WakesAtomicsWaitAndEmitsExitCode1(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_wait.ts"] = """
                // Park on a worker-local SAB index that nothing ever notifies, so the wait
                // blocks forever unless terminate() unwinds the thread.
                const view = new Int32Array(new SharedArrayBuffer(16));
                postMessage("parked");
                Atomics.wait(view, 0, 0);
                postMessage("survived"); // unreachable once terminate() aborts the worker
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_wait.ts");
                w.on("exit", (code: any) => { console.log("exit:" + code); });
                w.on("message", (e: any) => {
                    if (e === "parked") {
                        // Give the worker a beat to actually enter the wait, then terminate.
                        setTimeout(async () => {
                            await w.terminate();
                            console.log("terminated");
                        }, 50);
                    } else {
                        console.log("got:" + e);
                    }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("exit:1", output);
        Assert.Contains("terminated", output);
        Assert.DoesNotContain("got:survived", output);
    }

    /// <summary>
    /// #997: terminating a cooperatively-idle worker (its event loop held open by a pending
    /// timer) stops it promptly via <c>Interpreter.Shutdown()</c> and reports <c>'exit'</c>
    /// code 1 — Node uses 1 for a terminated worker. Before the fix the exit code was
    /// hardcoded 0 and nothing stopped the loop early, so a terminated worker either reported
    /// 0 or waited out the 5s join.
    /// </summary>
    [Theory, ModeData]
    public void Worker_Terminate_StopsCooperativeEventLoopWorkerWithExitCode1(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_idle.ts"] = """
                // Hold the worker's loop open well past terminate() so the only way it exits
                // is the terminate() Shutdown(), not the timer firing.
                setTimeout(() => {}, 3000);
                postMessage("ready");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_idle.ts");
                w.on("exit", (code: any) => { console.log("exit:" + code); });
                w.on("message", (e: any) => {
                    if (e === "ready") {
                        setTimeout(async () => {
                            await w.terminate();
                            console.log("terminated");
                        }, 50);
                    }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("exit:1", output);
        Assert.Contains("terminated", output);
    }

    [Theory, ModeData]
    public void Worker_Terminate_InterruptsCpuBoundCode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_cpu.ts"] = """
                import { parentPort } from "worker_threads";
                parentPort!.postMessage("ready");
                while (true) { }
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker = new Worker(__dirname + "/worker_cpu.ts");
                worker.on("exit", (code: any) => console.log("exit:" + code));
                worker.on("message", async (value: any) => {
                    if (value === "ready") {
                        await worker.terminate();
                        console.log("terminated");
                    }
                });
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);

        Assert.Contains("exit:1", output);
        Assert.Contains("terminated", output);
    }

    #endregion

    #region 'online' event (#998)

    /// <summary>
    /// #998: Node emits <c>'online'</c> on a Worker once the worker's JS starts executing,
    /// before any <c>'message'</c> it posts. SharpTS emitted no such event. The worker posts
    /// a message at the top of its script; the parent must see <c>'online'</c> first.
    /// </summary>
    [Theory, ModeData]
    public void Worker_Online_FiresBeforeFirstMessage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_post.ts"] = """
                postMessage("hello");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_post.ts");
                w.on("online", () => { console.log("online"); });
                w.on("message", (e: any) => { console.log("message:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("online", output);
        Assert.Contains("message:hello", output);
        // 'online' must be delivered before the first 'message'.
        Assert.True(output.IndexOf("online") < output.IndexOf("message:hello"),
            $"'online' should precede the first 'message'. Output:\n{output}");
    }

    [Theory, ModeData]
    public void Worker_BurstMessages_ArriveExactlyOnce(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_burst.ts"] = """
                for (let i = 0; i < 200; i++) {
                    postMessage(i);
                }
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker = new Worker(__dirname + "/worker_burst.ts");
                let count = 0;
                let sum = 0;
                worker.on("message", (value: any) => {
                    count++;
                    sum += value;
                    if (count === 200) {
                        console.log("burst:" + count + ":" + sum);
                    }
                });
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);

        Assert.Contains("burst:200:19900", output);
    }

    #endregion

    #region ArrayBuffer transfer + detach (#999)

    /// <summary>
    /// #999: an ArrayBuffer placed in a Worker's <c>transferList</c> is detached on the
    /// sender side (Node neuters it — <c>byteLength</c> becomes 0). The Worker constructor
    /// clones <c>workerData</c> with the transfer list synchronously, so the source buffer
    /// is detached by the time <c>new Worker</c> returns. Dual-mode: the Worker uses the C#
    /// <c>StructuredClone</c> in both interpreter and compiled parents.
    /// </summary>
    [Theory, ModeData]
    public void Worker_ArrayBufferInTransferList_DetachesSource(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const buf = new ArrayBuffer(8);
                new Uint8Array(buf)[0] = 1;
                const w = new Worker(__dirname + "/worker_ok.ts", { workerData: "go", transferList: [buf] });
                // Transfer happened synchronously during construction — source is detached.
                console.log("len:" + buf.byteLength);
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("len:0", output);
        Assert.Contains("worker:ok", output);
    }

    /// <summary>
    /// #999 (gap follow-up): a non-transferred ArrayBuffer passed as <c>workerData</c> is
    /// deep-copied, not detached — its <c>byteLength</c> is preserved on the sender. Before
    /// the fix the interpreter had no ArrayBuffer clone arm and threw "Cannot clone value of
    /// type SharpTSArrayBuffer", so the worker failed to spawn. Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_NonTransferredArrayBufferWorkerData_IsCopiedNotDetached(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const buf = new ArrayBuffer(8);
                const w = new Worker(__dirname + "/worker_ok.ts", { workerData: buf });
                console.log("len:" + buf.byteLength);
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("len:8", output);
        Assert.Contains("worker:ok", output);
    }

    /// <summary>
    /// #999: full transfer round-trip — the bytes of a transferred ArrayBuffer arrive in the
    /// worker (it reads them back), and the parent's source buffer is detached. Interpreter
    /// only: a compiled parent hands the interpreting worker an emitted <c>$ArrayBuffer</c>
    /// that the interpreter's TypedArray constructor doesn't bridge yet (a separate cross-mode
    /// gap), so the byte read-back is verified in interpreter mode; detach is covered in both
    /// modes by <see cref="Worker_ArrayBufferInTransferList_DetachesSource"/>.
    /// </summary>
    [Fact]
    public void Worker_TransferredArrayBuffer_MovesBytesToWorker_Interpreted()
    {
        var files = new Dictionary<string, string>
        {
            ["worker_read.ts"] = """
                const u = new Uint8Array(workerData);
                postMessage(u[0] + "," + u[1] + ",len=" + u.length);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const buf = new ArrayBuffer(4);
                const u = new Uint8Array(buf);
                u[0] = 9; u[1] = 8;
                const w = new Worker(__dirname + "/worker_read.ts", { workerData: buf, transferList: [buf] });
                console.log("src:" + buf.byteLength);
                w.on("message", (e: any) => { console.log("got:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
        Assert.Contains("src:0", output);
        Assert.Contains("got:9,8,len=4", output);
    }

    #endregion

    #region environment data + receiveMessageOnPort (#1000)

    /// <summary>
    /// #1000: <c>setEnvironmentData</c>/<c>getEnvironmentData</c> use a real per-process data
    /// store (not <c>process.env</c>). Data set on the parent is visible in the worker via
    /// <c>getEnvironmentData</c>, and must NOT leak into <c>process.env</c>. Dual-mode: the
    /// parent's set routes to the shared C# store in both modes (compiled via a reflection
    /// helper); the worker reads it through the interpreter.
    /// </summary>
    [Theory, ModeData]
    public void Worker_EnvironmentData_VisibleInWorker_NotInProcessEnv(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_env.ts"] = """
                import { getEnvironmentData } from "worker_threads";
                // process.env must NOT carry the value — setEnvironmentData uses a separate store.
                const leaked: any = (process as any).env["ed_k1000"];
                postMessage("env:" + getEnvironmentData("ed_k1000") + ":leak=" + (leaked === undefined ? "no" : "yes"));
                """,
            ["main.ts"] = """
                import { Worker, setEnvironmentData } from "worker_threads";
                setEnvironmentData("ed_k1000", "ed_val");
                const w = new Worker(__dirname + "/worker_env.ts");
                w.on("message", (e: any) => { console.log(e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("env:ed_val:leak=no", output);
    }

    /// <summary>
    /// #1000: <c>receiveMessageOnPort</c> on an empty port returns <c>undefined</c> (was CLR
    /// null). Dual-mode on the main thread. The non-empty <c>{ message }</c> result is covered
    /// dual-mode by <see cref="Worker_ReceiveMessageOnPort_OnTransferredPort"/> (driven through
    /// the interpreter inside the worker).
    /// </summary>
    [Theory, ModeData]
    public void ReceiveMessageOnPort_EmptyPort_IsUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MessageChannel, receiveMessageOnPort } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const r: any = receiveMessageOnPort(port2);
                console.log("empty:" + (r === undefined));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("empty:true", output);
    }

    /// <summary>
    /// #1077: <c>receiveMessageOnPort</c> synchronously drains a queued message from a
    /// main-thread <c>MessageChannel</c> port, returning <c>{ message }</c>; a second call on
    /// the now-empty port returns <c>undefined</c>. Dual-mode — the compiled helper reads the
    /// emitted <c>$MessagePort</c> queue directly (it was previously a stub that always returned
    /// <c>undefined</c>).
    /// </summary>
    [Theory, ModeData]
    public void ReceiveMessageOnPort_QueuedMessage_ReturnsMessageThenUndefined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MessageChannel, receiveMessageOnPort } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                // port2 has no 'message' listener, so it stays unstarted and the posted
                // value waits in its queue for a synchronous receiveMessageOnPort drain.
                port1.postMessage("hello");
                const r: any = receiveMessageOnPort(port2);
                console.log("first:" + (r === undefined ? "undef" : r.message));
                const r2: any = receiveMessageOnPort(port2);
                console.log("second-empty:" + (r2 === undefined));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("first:hello", output);
        Assert.Contains("second-empty:true", output);
    }

    #endregion

    #region introspection (#1004)

    /// <summary>
    /// #1004: <c>worker.performance.eventLoopUtilization()</c> returns a best-effort
    /// <c>{ idle, active, utilization }</c> object (SharpTS has no precise idle/active loop
    /// accounting). Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_Performance_EventLoopUtilization_ReturnsShape(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_ok.ts", { workerData: "go" });
                const elu: any = w.performance.eventLoopUtilization();
                console.log("elu:" + typeof elu.idle + "," + typeof elu.active + "," + typeof elu.utilization);
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("elu:number,number,number", output);
        Assert.Contains("worker:ok", output);
    }

    /// <summary>
    /// #1004: <c>worker.getHeapSnapshot()</c> throws a clear "not supported" error — a
    /// V8-format heap snapshot has no .NET equivalent (epic ceiling). Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_GetHeapSnapshot_ThrowsClearError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_ok.ts", { workerData: "go" });
                try {
                    w.getHeapSnapshot();
                    console.log("no-throw");
                } catch (e: any) {
                    console.log("heap-err:" + (("" + (e && e.message ? e.message : e)).indexOf("not supported") >= 0));
                }
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("heap-err:true", output);
        Assert.DoesNotContain("no-throw", output);
    }

    /// <summary>
    /// #1004: <c>moveMessagePortToContext()</c> throws a clear "not supported" error — it needs
    /// V8 vm contexts/isolates, which SharpTS's single-process model does not provide.
    /// Interpreter only (the compiled worker_threads emitter does not expose it).
    /// </summary>
    [Fact]
    public void MoveMessagePortToContext_ThrowsClearError_Interpreted()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { moveMessagePortToContext, MessageChannel } from "worker_threads";
                const ch: any = new MessageChannel();
                try {
                    moveMessagePortToContext(ch.port1, {});
                    console.log("no-throw");
                } catch (e: any) {
                    console.log("mvp-err:" + (("" + (e && e.message ? e.message : e)).indexOf("not supported") >= 0));
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
        Assert.Contains("mvp-err:true", output);
        Assert.DoesNotContain("no-throw", output);
    }

    #endregion

    #region worker stdio + resourceLimits (#1003)

    /// <summary>
    /// #1003: the <c>resourceLimits</c> option is stored and echoed back on
    /// <c>worker.resourceLimits</c> (cosmetic — .NET cannot enforce V8 heap/stack sizing).
    /// Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_ResourceLimits_EchoedBack(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_ok.ts", {
                    workerData: "go",
                    resourceLimits: { maxOldGenerationSizeMb: 24, stackSizeMb: 4 },
                });
                console.log("rl:" + w.resourceLimits.maxOldGenerationSizeMb + "," + w.resourceLimits.stackSizeMb);
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("rl:24,4", output);
        Assert.Contains("worker:ok", output);
    }

    /// <summary>
    /// #1003: with <c>stdout: true</c>, the worker's console output is diverted off the shared
    /// Console into a per-worker Readable <c>worker.stdout</c>; the parent reads it via
    /// 'data'/'end'. Each chunk is marshalled onto the parent loop before delivery. Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_StdoutTrue_CapturesWorkerConsoleOutput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_out.ts"] = """
                console.log("hello-from-worker");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_out.ts", { stdout: true });
                // Print each chunk directly — don't depend on 'data' vs 'end' ordering.
                w.stdout.on("data", (chunk: any) => { console.log("OUT[" + ("" + chunk).trim() + "]"); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("OUT[hello-from-worker]", output);
    }

    #endregion

    #region worker.stdin (#1076)

    /// <summary>
    /// #1076: with <c>stdin: true</c>, <c>worker.stdin</c> is a Writable on the parent whose
    /// writes are bridged into the worker's <c>process.stdin</c> Readable (parent→worker, the
    /// reverse of the #1003 stdout path). The worker reads the chunk via a 'data' listener and
    /// <c>worker.stdin.end()</c> surfaces as 'end'. The parent writes only after the worker signals
    /// it has attached its listeners, so the assertion doesn't race the listener setup. Dual-mode:
    /// the worker always interprets; a compiled parent reaches <c>worker.stdin</c> via runtime
    /// dispatch on the C# SharpTSWorker (like worker.stdout).
    /// </summary>
    [Theory, ModeData]
    public void Worker_StdinTrue_ParentWriteReadableInWorker(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_in.ts"] = """
                (process as any).stdin.on("data", (chunk: any) => { postMessage("GOT[" + ("" + chunk).trim() + "]"); });
                (process as any).stdin.on("end", () => { postMessage("END"); });
                postMessage("ready");
                setTimeout(() => {}, 2000); // stay alive to receive stdin
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_in.ts", { stdin: true });
                w.on("message", (e: any) => {
                    if (e === "ready") { w.stdin.write("ping"); w.stdin.end(); }
                    else { console.log(e); }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("GOT[ping]", output);
        Assert.Contains("END", output);
    }

    /// <summary>
    /// #1076: multiple parent writes arrive at the worker's <c>process.stdin</c> in order. The
    /// worker accumulates each chunk and, on 'end', posts the concatenation — verifying both
    /// ordering and that end() flushes after the last chunk. Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_StdinTrue_MultipleWritesPreserveOrder(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_acc.ts"] = """
                let acc = "";
                (process as any).stdin.on("data", (chunk: any) => { acc += ("" + chunk); });
                (process as any).stdin.on("end", () => { postMessage("ACC[" + acc + "]"); });
                postMessage("ready");
                setTimeout(() => {}, 2000);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_acc.ts", { stdin: true });
                w.on("message", (e: any) => {
                    if (e === "ready") { w.stdin.write("a"); w.stdin.write("b"); w.stdin.write("c"); w.stdin.end(); }
                    else { console.log(e); }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("ACC[abc]", output);
    }

    /// <summary>
    /// #1076: without <c>stdin: true</c>, <c>worker.stdin</c> is not a pipe — the parent must opt
    /// in, exactly like stdout/stderr. SharpTS surfaces the absent stream as <c>undefined</c>
    /// (its GetMember convention for stdout/stderr too), which is falsy like Node's <c>null</c>,
    /// so <c>!w.stdin</c> holds. Dual-mode.
    /// </summary>
    [Theory, ModeData]
    public void Worker_StdinWithoutOption_IsAbsent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_noop.ts"] = """
                postMessage("hi");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_noop.ts");
                console.log("stdin-absent:" + (!w.stdin));
                w.on("message", () => {});
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("stdin-absent:true", output);
    }

    #endregion

    #region markAsUntransferable (#1002)

    /// <summary>
    /// #1002: an ArrayBuffer passed to <c>markAsUntransferable</c> is ignored in a transfer
    /// list — it is cloned (copied) instead of transferred, so the source is NOT detached
    /// (<c>byteLength</c> preserved). Contrast with #999 where an unmarked transferred buffer
    /// is detached to 0. Dual-mode: <c>markAsUntransferable</c> records the object in the C#
    /// <c>StructuredClone</c> registry (compiled via a reflection helper), and the Worker
    /// transferList clone honors it in both modes.
    /// </summary>
    [Theory, ModeData]
    public void Worker_MarkAsUntransferable_BufferIsClonedNotDetached(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_ok.ts"] = """
                postMessage("ok");
                """,
            ["main.ts"] = """
                import { Worker, markAsUntransferable } from "worker_threads";
                const buf = new ArrayBuffer(8);
                markAsUntransferable(buf);
                // buf is in the transfer list but marked untransferable → ignored, not detached.
                const w = new Worker(__dirname + "/worker_ok.ts", { workerData: "go", transferList: [buf] });
                console.log("len:" + buf.byteLength);
                w.on("message", (e: any) => { console.log("worker:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("len:8", output);
        Assert.Contains("worker:ok", output);
    }

    #endregion

    #region 'messageerror' event (#1001)

    /// <summary>
    /// #1001: when a worker posts a value that fails to clone, the parent <c>Worker</c> fires
    /// <c>'messageerror'</c> (Node's receiver-side model) rather than throwing in the worker's
    /// postMessage. Dual-mode: the worker always interprets and posts through the C#
    /// <c>SharpTSWorker</c>, whose clone throws DataCloneError for a function in both modes.
    /// </summary>
    [Theory, ModeData]
    public void Worker_PostUncloneableToParent_FiresMessageError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_msgerr.ts"] = """
                // A function cannot be structured-cloned → parent gets 'messageerror'.
                postMessage(() => {});
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_msgerr.ts");
                w.on("messageerror", () => { console.log("parent-messageerror"); });
                w.on("message", (e: any) => { console.log("message:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("parent-messageerror", output);
        Assert.DoesNotContain("message:", output);
    }

    /// <summary>
    /// #1001: when the parent posts a value that fails to clone, the worker's
    /// <c>parentPort</c> fires <c>'messageerror'</c>. The worker echoes which event it saw.
    /// Dual-mode (parent posts through the C# <c>SharpTSWorker</c>).
    /// </summary>
    [Theory, ModeData]
    public void Worker_ParentPostsUncloneable_WorkerParentPortFiresMessageError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_pw.ts"] = """
                import { parentPort } from "worker_threads";
                parentPort.on("messageerror", () => { postMessage("saw-err"); parentPort.close(); });
                parentPort.on("message", () => { postMessage("saw-msg"); parentPort.close(); });
                postMessage("ready");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_pw.ts");
                w.on("message", (e: any) => {
                    if (e === "ready") { w.postMessage(() => {}); }
                    else { console.log(e); }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("saw-err", output);
        Assert.DoesNotContain("saw-msg", output);
    }

    /// <summary>
    /// #1001/#1077: a <c>MessageChannel</c> port whose peer posts an uncloneable value (a
    /// function) fires <c>'messageerror'</c> — not <c>'message'</c> — on the receiver. Now
    /// dual-mode: the compiled <c>$Runtime.StructuredClone</c> throws <c>$DataCloneError</c>
    /// for the uncloneable value (#1255), which the compiled <c>$MessagePort.PostMessage</c>
    /// catches and converts to the shared clone-failure sentinel that <c>Drain</c> turns into
    /// <c>'messageerror'</c>, matching the interpreter's receiver-side model (previously
    /// compiled returned the function by reference and fired <c>'message'</c>).
    /// </summary>
    [Theory, ModeData]
    public void MessageChannelPort_PostUncloneable_FiresMessageError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                // Close both ports after delivery so the compiled $MessagePort (which Refs the
                // event loop on start) lets the process quiesce — same convention as the other
                // compiled MessageChannel tests.
                port2.on("messageerror", () => { console.log("port-err"); port1.close(); port2.close(); });
                port2.on("message", () => { console.log("port-msg"); port1.close(); port2.close(); });
                port1.postMessage(() => {});
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("port-err", output);
        Assert.DoesNotContain("port-msg", output);
    }

    /// <summary>
    /// #1255: an uncloneable value NESTED inside an object (not just at the top level)
    /// must also fire <c>'messageerror'</c>. Before the fix, the emitted <c>$Runtime.
    /// StructuredClone</c> only detected the top-level typeof and otherwise aliased
    /// unknown values by reference, so a nested function silently passed through as
    /// <c>'message'</c> in compiled mode.
    /// </summary>
    [Theory, ModeData]
    public void MessageChannelPort_PostNestedUncloneable_FiresMessageError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                port2.on("messageerror", () => { console.log("port-err"); port1.close(); port2.close(); });
                port2.on("message", () => { console.log("port-msg"); port1.close(); port2.close(); });
                port1.postMessage({ fn: () => {} });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("port-err", output);
        Assert.DoesNotContain("port-msg", output);
    }

    #endregion

    #region Running-Worker event-loop liveness (#329)

    /// <summary>
    /// Regression for #329: a running worker must keep the parent event loop alive
    /// by default (Node semantics). The worker posts a message back ~400ms after
    /// spawn — past the parent loop's 250ms quiescence window — and the parent's
    /// only pending work is the <c>'message'</c> listener. Before the fix nothing
    /// Ref'd the parent for the worker's running lifetime, so the parent abandoned
    /// the wait at 250ms and exited without ever printing the message.
    /// </summary>
    /// <remarks>
    /// The keep-alive Ref is against whichever loop owns the worker: the parent
    /// interpreter (interpreter mode) or the emitted <c>$EventLoop</c> (compiled
    /// mode, #354 — worker→parent delivery is marshalled onto the loop via the
    /// injected <c>$EventLoop.Schedule</c>). <c>__dirname</c> routes the harness
    /// through the real-disk path so the worker can load its script. The assertion
    /// is positive and load-independent — under load the worker simply posts later
    /// and the running-Ref keeps the parent alive until it does, so the test cannot
    /// flake the way a wall-clock window would.
    /// </remarks>
    [Theory, ModeData]
    public void Worker_RunningWorker_KeepsParentLoopAliveUntilMessage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_delayed.ts"] = """
                // Post back after >250ms, holding the worker's own loop open via the
                // pending timer until it fires. The worker then exits naturally.
                setTimeout(() => { postMessage("from-worker"); }, 400);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_delayed.ts");
                // The 'message' listener is the parent's ONLY pending work — the
                // running worker must keep the loop alive long enough to deliver.
                w.on("message", (e: any) => {
                    console.log("received:" + e);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:from-worker", output);
    }

    /// <summary>
    /// <c>worker.ref()</c> / <c>worker.unref()</c> are callable, chainable, and a
    /// <c>ref()</c> after an <c>unref()</c> restores the keep-alive so a later
    /// delayed message is still delivered (positive, load-independent assertion).
    /// </summary>
    [Theory, ModeData]
    public void Worker_UnrefThenRef_RestoresKeepAlive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_delayed.ts"] = """
                setTimeout(() => { postMessage("again"); }, 400);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_delayed.ts");
                w.on("message", (e: any) => {
                    console.log("received:" + e);
                });
                w.unref(); // opt out of keep-alive...
                w.ref();   // ...then opt back in — message must still arrive.
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:again", output);
    }

    #endregion

    #region Unsupported Worker options (#407)

    /// <summary>
    /// Regression for #407: the Worker <c>stdin</c>/<c>stdout</c>/<c>stderr</c> and
    /// <c>resourceLimits</c> options are intentionally unsupported, but supplying
    /// them in the options bag must not break construction — the worker still
    /// spawns, runs, and posts back. The bag also carries the honored
    /// <c>workerData</c>/<c>transferList</c> options so this exercises the
    /// unsupported keys coexisting with supported ones. Before the fix these keys
    /// were read into inert dead fields (and <c>resourceLimits</c> was mistyped as
    /// <c>SharpTSArray</c>); now they are simply ignored. <c>resourceLimits</c> is
    /// passed as an object, not an array — the shape that always yielded null under
    /// the old <c>as SharpTSArray</c> read, confirming the bag no longer trips on it.
    /// </summary>
    [Theory, ModeData]
    public void Worker_StdioAndResourceLimitsOptions_AreHonored(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_echo.ts"] = """
                setTimeout(() => { postMessage("ran"); }, 50);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w: any = new Worker(__dirname + "/worker_echo.ts", {
                    workerData: 42,
                    stdout: true,
                    stderr: true,
                    stdin: true,
                    resourceLimits: { maxOldGenerationSizeMb: 16 },
                });
                // #1003: passing all stdio + resourceLimits options no longer breaks
                // construction; resourceLimits echoes and the worker still runs.
                console.log("rl:" + w.resourceLimits.maxOldGenerationSizeMb);
                w.on("message", (e: any) => {
                    console.log("received:" + e);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:ran", output);
        Assert.Contains("rl:16", output);
    }

    #endregion

    #region Worker options bag — workerData (#380)

    /// <summary>
    /// Regression for #380: a worker spawned with a <c>workerData</c> option must see
    /// that value via <c>worker_threads.workerData</c>. In compiled mode the options
    /// bag is a <c>Dictionary&lt;string, object?&gt;</c> (a compiled object literal),
    /// not a <c>SharpTSObject</c>; before the fix the constructor's
    /// <c>options as SharpTSObject</c> cast yielded null and the entire bag was
    /// dropped, so a compiled worker saw <c>workerData === undefined</c>.
    /// </summary>
    /// <remarks>
    /// Interpreted workers bind workerData through <c>env.Define</c>; compiled workers
    /// receive it through the emitted runtime's per-realm bootstrap fields.
    /// <c>__dirname</c> routes the harness through the real-disk path so the worker
    /// can load its script.
    /// </remarks>
    [Theory, ModeData]
    public void Worker_WorkerData_PrimitiveIsVisibleInWorker(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_data.ts"] = """
                // workerData/postMessage resolve as worker-context globals (no import).
                postMessage("data:" + workerData);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_data.ts", { workerData: 123 });
                w.on("message", (e: any) => {
                    console.log("received:" + e);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:data:123", output);
    }

    /// <summary>
    /// #380: an object <c>workerData</c> is structured-cloned across the boundary and
    /// its fields are readable in the worker. Exercises the compiled
    /// <c>Dictionary&lt;string, object?&gt;</c> clone path as well as the interpreter
    /// <c>SharpTSObject</c> path.
    /// </summary>
    [Theory, ModeData]
    public void Worker_WorkerData_ObjectIsClonedAndVisibleInWorker(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_data_obj.ts"] = """
                // workerData/postMessage resolve as worker-context globals (no import).
                postMessage("got:" + workerData.name + ":" + workerData.count);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_data_obj.ts", {
                    workerData: { name: "alice", count: 7 }
                });
                w.on("message", (e: any) => {
                    console.log("received:" + e);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:got:alice:7", output);
    }

    #endregion

    #region MessagePort transfer to a worker (#406)

    /// <summary>
    /// Regression for #406: a <c>MessagePort</c> created in the parent and listed in
    /// a Worker's <c>transferList</c> must be usable inside the worker — the worker
    /// can attach a listener and post back through it, round-tripping with the
    /// partner port retained by the parent.
    /// </summary>
    /// <remarks>
    /// This exercises the full cross-runtime/cross-thread contract. In compiled mode
    /// the channel ports are the emitted <c>$MessagePort</c> type and the transferred
    /// port is adopted via <c>CompiledMessagePortBridge</c>. The bridge attaches to the
    /// isolated compiled worker's event loop, forwards posts to the compiled partner on
    /// the parent's <c>$EventLoop</c>, and dispatches incoming messages directly to the
    /// worker's emitted listeners. In interpreter mode the
    /// ports are <c>SharpTSMessagePort</c>; transfer marks the pair cross-thread so
    /// delivery marshals onto each owner's loop instead of the poster's thread, and a
    /// started port keeps its loop alive. Before the fix the compiled
    /// <c>transferList</c> (a <c>List&lt;object?&gt;</c>) was dropped and the
    /// <c>$MessagePort</c> failed to clone; the interpreter port was neutered on
    /// transfer (unusable by the receiver) and delivered on the wrong thread.
    /// <para>
    /// Load-independent: the parent's "ping" is queued on the port until the worker
    /// attaches its listener (whenever that happens), the started ports keep both
    /// loops alive until each side closes, and the assertion is a positive
    /// output-present check — so it cannot flake under load.
    /// </para>
    /// </remarks>
    [Theory, ModeData]
    public void Worker_TransferredMessagePort_RoundTripsBetweenParentAndWorker(ExecutionMode mode)
    {
        long compiledExecutionsBefore = CompiledWorkerCompilationService.ExecutionCount;
        var files = new Dictionary<string, string>
        {
            ["worker_port.ts"] = """
                // The transferred port arrives via workerData. Echo each message
                // back through it, then close so the worker's loop can quiesce.
                const port: any = workerData.port;
                port.on("message", (m: any) => {
                    port.postMessage("pong:" + m);
                    port.close();
                });
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const w = new Worker(__dirname + "/worker_port.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                port2.on("message", (m: any) => {
                    console.log("received:" + m);
                    port2.close();
                });
                port2.postMessage("ping");
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:pong:ping", output);
        if (mode == ExecutionMode.Compiled)
        {
            Assert.True(
                CompiledWorkerCompilationService.ExecutionCount > compiledExecutionsBefore,
                "A transferred MessagePort must not force the worker into interpreter fallback.");
        }
    }

    [Theory, ModeData]
    public void Worker_TransferredMessagePort_BurstArrivesExactlyOnce(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_port_burst.ts"] = """
                const port: any = workerData.port;
                port.on("message", () => {
                    for (let i: number = 0; i < 200; i++) {
                        port.postMessage(i);
                    }
                    port.close();
                });
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const w = new Worker(__dirname + "/worker_port_burst.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                let received: number = 0;
                let checksum: number = 0;
                port2.on("message", (message: number) => {
                    received++;
                    checksum += message;
                    if (received === 200) {
                        console.log("burst:" + received + ":" + checksum);
                        port2.close();
                    }
                });
                port2.postMessage("start");
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("burst:200:19900", output);
    }

    /// <summary>
    /// #406: an object posted across a transferred port is structured-cloned in both
    /// directions, so each side reads independent field values (exercises the
    /// compiled <c>Dictionary&lt;string, object?&gt;</c> clone path through the bridge
    /// as well as the interpreter <c>SharpTSObject</c> path).
    /// </summary>
    [Theory, ModeData]
    public void Worker_TransferredMessagePort_StructuredClonesObjectPayloads(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_port_obj.ts"] = """
                const port: any = workerData.port;
                port.on("message", (m: any) => {
                    port.postMessage({ tag: "reply", value: m.value + 1 });
                    port.close();
                });
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const w = new Worker(__dirname + "/worker_port_obj.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                port2.on("message", (m: any) => {
                    console.log("received:" + m.tag + ":" + m.value);
                    port2.close();
                });
                port2.postMessage({ tag: "req", value: 41 });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:reply:42", output);
    }

    /// <summary>
    /// #406: a <c>MessagePort</c> placed in <c>workerData</c> WITHOUT being listed in
    /// <c>transferList</c> must be rejected (a port can only be transferred, never
    /// cloned), in both modes — not silently shared and not an opaque crash.
    /// </summary>
    /// <remarks>
    /// Both modes now surface the reason via <c>e.message</c>: interpreter mode wraps
    /// the construction failure in a real <c>Error</c> (#464), and compiled mode yields
    /// an object carrying <c>message</c>. The guest reads <c>e.message</c> with a string
    /// fallback so the rejection text is observable either way.
    /// </remarks>
    [Theory, ModeData]
    public void Worker_MessagePortInWorkerDataWithoutTransfer_IsRejected(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_noop.ts"] = """
                console.log("worker-should-not-start");
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                try {
                    // port1 is in workerData but NOT in a transferList.
                    const w = new Worker(__dirname + "/worker_noop.ts", {
                        workerData: { port: port1 },
                    });
                    console.log("constructed-without-error");
                } catch (e: any) {
                    console.log("caught:" + (e && e.message ? e.message : e));
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("MessagePort cannot be cloned", output);
        Assert.DoesNotContain("constructed-without-error", output);
        Assert.DoesNotContain("worker-should-not-start", output);
    }

    /// <summary>
    /// #465: a transferred port must round-trip repeatedly with the worker idle between
    /// messages — exercising the event-driven receive (a parent post wakes the worker
    /// loop to drain) rather than a one-shot. In compiled mode this drives
    /// <c>CompiledMessagePortBridge</c>'s on-enqueue wake; in interpreter mode the
    /// cross-thread <c>SharpTSMessagePort</c> delivery. The parent sends the next ping
    /// only after the previous reply, so each delivery happens while the worker is
    /// otherwise quiescent.
    /// </summary>
    [Theory, ModeData]
    public void Worker_TransferredMessagePort_MultipleRoundTripsWhileIdle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_echo_port.ts"] = """
                const port: any = workerData.port;
                let n = 0;
                port.on("message", (m: any) => {
                    n++;
                    port.postMessage("pong" + n + ":" + m);
                    if (n >= 3) port.close();
                });
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const w = new Worker(__dirname + "/worker_echo_port.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                let replies = 0;
                port2.on("message", (m: any) => {
                    console.log("recv:" + m);
                    replies++;
                    if (replies < 3) port2.postMessage("ping" + (replies + 1));
                    else port2.close();
                });
                port2.postMessage("ping1");
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("recv:pong1:ping1", output);
        Assert.Contains("recv:pong2:ping2", output);
        Assert.Contains("recv:pong3:ping3", output);
    }

    /// <summary>
    /// #465: <c>worker_threads.receiveMessageOnPort(port)</c> must work on a transferred
    /// port that the worker drives with a synchronous poll (no <c>'message'</c>
    /// listener). The worker imports <c>receiveMessageOnPort</c> (module mode, #410) and
    /// polls the port until a message arrives, then echoes it back. Exercises the
    /// compiled <c>CompiledMessagePortBridge.ReceiveMessageSync</c> as well as the
    /// interpreter <c>SharpTSMessagePort</c> path.
    /// </summary>
    [Theory, ModeData]
    public void Worker_ReceiveMessageOnPort_OnTransferredPort(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_sync_port.ts"] = """
                import { workerData, receiveMessageOnPort } from "worker_threads";
                const port: any = workerData.port;
                const timer = setInterval(() => {
                    const m: any = receiveMessageOnPort(port);
                    if (m) {
                        port.postMessage("sync-got:" + m.message);
                        clearInterval(timer);
                        port.close();
                    }
                }, 10);
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const w = new Worker(__dirname + "/worker_sync_port.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                port2.on("message", (m: any) => { console.log("recv:" + m); port2.close(); });
                port2.postMessage("hello");
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("recv:sync-got:hello", output);
    }

    /// <summary>
    /// #465: parent creates a <c>MessageChannel</c> and distributes each end to a
    /// different worker; Workers A and B communicate directly through the channel —
    /// the parent is not in the message path. In compiled mode each worker adopts its
    /// port via <c>CompiledMessagePortBridge</c>, and the <c>_onEnqueue</c> hooks
    /// installed by each bridge make posting event-driven across both workers.
    /// In interpreter mode the cross-thread <c>SharpTSMessagePort</c> delivery handles
    /// both ends.
    /// </summary>
    [Theory, ModeData]
    public void Worker_SplitChannel_WorkersCanCommunicateDirectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_a.ts"] = """
                const port: any = workerData.port;
                port.on("message", (m: any) => {
                    port.postMessage("a-echo:" + m);
                    port.close();
                    postMessage("a-done");
                });
                """,
            ["worker_b.ts"] = """
                const port: any = workerData.port;
                port.on("message", (m: any) => {
                    port.close();
                    postMessage("b-got:" + m);
                });
                port.postMessage("ping");
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const wa = new Worker(__dirname + "/worker_a.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                const wb = new Worker(__dirname + "/worker_b.ts", {
                    workerData: { port: port2 },
                    transferList: [port2],
                });
                wa.on("message", (m: any) => console.log(m));
                wb.on("message", (m: any) => console.log(m));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("a-done", output);
        Assert.Contains("b-got:a-echo:ping", output);
    }

    /// <summary>
    /// #465: two workers exchange multiple messages through a split channel, verifying
    /// that each delivery is event-driven (the idle worker wakes on each post and
    /// processes in-order). Worker B pings three times; Worker A echoes each one back;
    /// messages are serialised by awaiting each reply before sending the next. Worker B
    /// accumulates the echoes and reports them to the parent in its final postMessage so
    /// the parent can log them on the main thread (worker console.log is not guaranteed
    /// to reach the captured output before the harness returns).
    /// </summary>
    [Theory, ModeData]
    public void Worker_SplitChannel_MultipleRoundTrips(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_a.ts"] = """
                const port: any = workerData.port;
                let n = 0;
                port.on("message", (m: any) => {
                    n++;
                    port.postMessage("a-echo:" + m);
                    if (n >= 3) { port.close(); postMessage("a-done"); }
                });
                """,
            ["worker_b.ts"] = """
                const port: any = workerData.port;
                let got = 0;
                let report = "";
                port.on("message", (m: any) => {
                    report += (got === 0 ? "" : ",") + m;
                    got++;
                    if (got < 3) port.postMessage("ping" + (got + 1));
                    else { port.close(); postMessage("b-recvd:" + report); }
                });
                port.postMessage("ping1");
                """,
            ["main.ts"] = """
                import { Worker, MessageChannel } from "worker_threads";
                const { port1, port2 } = new MessageChannel();
                const wa = new Worker(__dirname + "/worker_a.ts", {
                    workerData: { port: port1 },
                    transferList: [port1],
                });
                const wb = new Worker(__dirname + "/worker_b.ts", {
                    workerData: { port: port2 },
                    transferList: [port2],
                });
                wa.on("message", (m: any) => console.log(m));
                wb.on("message", (m: any) => console.log(m));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("a-done", output);
        Assert.Contains("b-recvd:a-echo:ping1,a-echo:ping2,a-echo:ping3", output);
    }

    #endregion

    #region Worker scripts in module mode (#410)

    [Fact]
    public void CompiledWorker_ExecutesCompiledArtifactWithWorkerBindings()
    {
        long executionsBefore = CompiledWorkerCompilationService.ExecutionCount;
        var files = new Dictionary<string, string>
        {
            ["worker_compiled.ts"] = """
                import { isMainThread, threadId, workerData, parentPort } from "worker_threads";
                parentPort!.postMessage(
                    "main=" + isMainThread + " id=" + (threadId > 0) + " data=" + workerData);
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker = new Worker(__dirname + "/worker_compiled.ts", { workerData: "alpha" });
                worker.on("message", (value: any) => console.log(value));
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled);

        Assert.Contains("main=false id=true data=alpha", output);
        Assert.True(CompiledWorkerCompilationService.ExecutionCount > executionsBefore,
            "The worker must execute through the compiled-worker bootstrap, not the interpreter fallback.");
    }

    [Fact]
    public void CompiledWorker_ReflectedPostMessageMethodsHaveStableIdentity()
    {
        var files = new Dictionary<string, string>
        {
            ["worker_identity.ts"] = """
                import { parentPort } from "worker_threads";
                const port: any = parentPort;
                port.postMessage("worker:" + (port.postMessage === port.postMessage));
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker: any = new Worker(__dirname + "/worker_identity.ts");
                console.log("parent:" + (worker.postMessage === worker.postMessage));
                worker.on("message", (value: any) => console.log(value));
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled);

        Assert.Contains("parent:true", output);
        Assert.Contains("worker:true", output);
    }

    [Theory, ModeData]
    public void Worker_ParentPortReceivesMessagesAndKeepsWorkerAlive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_echo.ts"] = """
                import { parentPort } from "worker_threads";
                parentPort!.on("message", (value: any) => {
                    parentPort!.postMessage("echo:" + value);
                    parentPort!.close();
                });
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker = new Worker(__dirname + "/worker_echo.ts");
                worker.on("message", (value: any) => console.log(value));
                worker.postMessage("ping");
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);

        Assert.Contains("echo:ping", output);
    }

    [Fact]
    public void CompiledParent_MessageUsesWorkerInterpreterFallbackQueue()
    {
        var files = new Dictionary<string, string>
        {
            ["worker_fallback_echo.ts"] = """
                import { parentPort } from "worker_threads";
                // The compiled-worker service conservatively falls back when a module
                // contains Atomics.wait, even when it is unreachable.
                if (false) {
                    const view = new Int32Array(new SharedArrayBuffer(4));
                    Atomics.wait(view, 0, 0);
                }
                parentPort!.on("message", (value: any) => {
                    parentPort!.postMessage("fallback:" + value);
                    parentPort!.close();
                });
                parentPort!.postMessage("ready");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const worker = new Worker(__dirname + "/worker_fallback_echo.ts");
                worker.on("message", (value: any) => {
                    if (value === "ready") worker.postMessage("ping");
                    else console.log(value);
                });
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled);

        Assert.Contains("fallback:ping", output);
    }

    [Fact]
    public void CompiledWorkers_HaveIndependentModuleState()
    {
        var files = new Dictionary<string, string>
        {
            ["counter.ts"] = """
                let count = 0;
                export function next(): number { count++; return count; }
                """,
            ["worker_isolated.ts"] = """
                import { parentPort, workerData } from "worker_threads";
                import { next } from "./counter";
                parentPort!.postMessage(workerData + ":" + next());
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                for (const name of ["first", "second"]) {
                    const worker = new Worker(__dirname + "/worker_isolated.ts", { workerData: name });
                    worker.on("message", (value: any) => console.log(value));
                }
                """,
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled);

        Assert.Contains("first:1", output);
        Assert.Contains("second:1", output);
        Assert.DoesNotContain(":2", output);
    }

    [Fact]
    public async Task CompiledWorker_TerminateDuringRealmBootstrap_DoesNotMissCancellation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_compiled_worker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string workerPath = Path.Combine(directory, "worker.ts");
        File.WriteAllText(workerPath, "while (true) { }");

        try
        {
            // Populate the artifact cache so the worker proceeds directly into its realm-load
            // window. CompiledRealmReference is published after the initial token check but
            // before the emitted cancellation bridge, which recreates the cold-start race.
            CompiledWorkerCompilationService.Compile(workerPath);

            using var worker = SharpTSWorker.CreateForCompiledLoop(
                workerPath, options: null, static () => { }, static () => { }, static action => action());
            Assert.True(
                SpinWait.SpinUntil(
                    () => worker.CompiledRealmReference is not null || !worker.IsRunning,
                    TimeSpan.FromSeconds(30)),
                "Compiled worker did not begin loading its isolated realm.");

            SharpTSPromise termination = worker.Terminate();
            object? exitCode = await termination.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(SpinWait.SpinUntil(() => !worker.IsRunning, TimeSpan.FromSeconds(1)),
                "Compiled worker remained running after terminate().");
            Assert.Equal(1d, exitCode);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CompiledWorker_PreparedCacheInvalidatesWhenSourceChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_compiled_worker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string workerPath = Path.Combine(directory, "worker.ts");

        try
        {
            File.WriteAllText(workerPath, "postMessage('first');");
            byte[] firstArtifact = CompiledWorkerCompilationService.Compile(workerPath);
            Assert.Same(firstArtifact, CompiledWorkerCompilationService.Compile(workerPath));

            File.WriteAllText(workerPath, "postMessage('second');");
            byte[] secondArtifact = CompiledWorkerCompilationService.Compile(workerPath);

            Assert.NotSame(firstArtifact, secondArtifact);
            Assert.Same(secondArtifact, CompiledWorkerCompilationService.Compile(workerPath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CompiledWorker_CollectibleRealmUnloadsAfterExit()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_compiled_worker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string workerPath = Path.Combine(directory, "worker.ts");
        File.WriteAllText(workerPath, """
            import { isMainThread, parentPort } from "worker_threads";
            if (isMainThread) throw new Error("worker context was not configured");
            parentPort!.on("message", (value: any) => {
                if (value !== "stop") throw new Error("unexpected worker message");
                parentPort!.close();
            });
            """);

        WeakReference realm;
        try
        {
            byte[] firstArtifact = CompiledWorkerCompilationService.Compile(workerPath);
            byte[] cachedArtifact = CompiledWorkerCompilationService.Compile(workerPath);
            Assert.Same(firstArtifact, cachedArtifact);

            using (var worker = SharpTSWorker.CreateForCompiledLoop(
                       workerPath, options: null, static () => { }, static () => { }, static action => action()))
            {
                // Populate RuntimeCallableDispatcher's emitted-function caches before
                // teardown; those caches must not pin the collectible worker realm.
                worker.PostMessage("stop");
                Assert.True(SpinWait.SpinUntil(() => !worker.IsRunning, TimeSpan.FromSeconds(30)),
                    "Compiled worker did not exit.");
                realm = worker.CompiledRealmReference
                    ?? throw new Xunit.Sdk.XunitException("Compiled worker did not create an isolated realm.");
            }

            for (int i = 0; i < 10 && realm.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(20);
            }

            Assert.False(realm.IsAlive, "The compiled worker AssemblyLoadContext remained rooted after exit.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void CompiledWorker_TransferredMessagePortDoesNotRootRealmAfterExit()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), $"sharpts_compiled_worker_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string workerPath = Path.Combine(directory, "worker.ts");
        File.WriteAllText(workerPath, """
            const port: any = workerData.port;
            port.on("message", () => {});
            port.close();
            """);

        WeakReference realm;
        try
        {
            object emittedPort = CreateEmittedMessagePortStub();
            var options = new Dictionary<string, object?>
            {
                ["workerData"] = new Dictionary<string, object?> { ["port"] = emittedPort },
                ["transferList"] = new List<object?> { emittedPort },
            };

            using (var worker = SharpTSWorker.CreateForCompiledLoop(
                       workerPath, options, static () => { }, static () => { },
                       static action => action()))
            {
                Assert.True(SpinWait.SpinUntil(() => !worker.IsRunning, TimeSpan.FromSeconds(30)),
                    "Compiled transferred-port worker did not exit.");
                realm = worker.CompiledRealmReference
                    ?? throw new Xunit.Sdk.XunitException(
                        "Compiled transferred-port worker did not create an isolated realm.");
            }

            for (int i = 0; i < 10 && realm.IsAlive; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(20);
            }

            Assert.False(realm.IsAlive,
                "The transferred MessagePort bridge retained the compiled worker realm after exit.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static object CreateEmittedMessagePortStub()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"SharpTS.MessagePortStub.{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType("$MessagePort", TypeAttributes.Public | TypeAttributes.Class);
        var pending = type.DefineField(
            "_pending", typeof(ConcurrentQueue<object>), FieldAttributes.Private);
        type.DefineField("_onEnqueue", typeof(Action), FieldAttributes.Private);

        var ctor = type.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Newobj,
            typeof(ConcurrentQueue<object>).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Stfld, pending);
        ctorIl.Emit(OpCodes.Ret);

        var postMessage = type.DefineMethod(
            "PostMessage", MethodAttributes.Public, typeof(void), [typeof(object)]);
        postMessage.GetILGenerator().Emit(OpCodes.Ret);
        var markTransferred = type.DefineMethod(
            "MarkTransferredAcrossThreads", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        markTransferred.GetILGenerator().Emit(OpCodes.Ret);

        return Activator.CreateInstance(type.CreateType())!;
    }

    /// <summary>
    /// Regression for #410: a worker script that uses the canonical Node import form
    /// <c>import { workerData, parentPort, ... } from "worker_threads"</c> must run —
    /// before the fix the worker ran on a bare single-file pipeline that rejected any
    /// import at type-check ("Import statements require module mode"), and the failure
    /// was swallowed by the worker's <c>error</c> event so the parent just produced no
    /// output. The imported identity bindings must carry this worker's live values
    /// (the running worker's <c>workerData</c>, a usable <c>parentPort</c>,
    /// <c>isMainThread === false</c>, a positive <c>threadId</c>) rather than the
    /// main-thread <c>null</c> placeholders.
    /// </summary>
    /// <remarks>
    /// This exercises the interpreter module pipeline in interpreted mode and the
    /// isolated compiled-worker artifact pipeline in compiled mode.
    /// <c>__dirname</c> routes the harness through the real-disk path so the worker can
    /// load its script. Load-independent positive assertion (output present).
    /// </remarks>
    [Theory, ModeData]
    public void Worker_ImportFromWorkerThreads_ResolvesInModuleMode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_import.ts"] = """
                import { workerData, parentPort, isMainThread, threadId } from "worker_threads";
                parentPort!.postMessage(
                    "wd=" + workerData + " main=" + isMainThread + " tid=" + (threadId > 0));
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_import.ts", { workerData: 123 });
                w.on("message", (e: any) => { console.log("received:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:wd=123 main=false tid=true", output);
    }

    /// <summary>
    /// #410: a module-mode worker can also import its own sibling modules — the worker
    /// runs through the full resolver/type-check/interpret pipeline, not just a special
    /// case for <c>worker_threads</c>. Here the worker imports a relative helper and a
    /// worker_threads binding together.
    /// </summary>
    [Theory, ModeData]
    public void Worker_ImportRelativeModule_WorksInModuleMode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["greet.ts"] = """
                export function greet(name: any): string { return "hello " + name; }
                """,
            ["worker_rel.ts"] = """
                import { workerData, parentPort } from "worker_threads";
                import { greet } from "./greet";
                parentPort!.postMessage(greet(workerData));
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker_rel.ts", { workerData: "alice" });
                w.on("message", (e: any) => { console.log("received:" + e); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("received:hello alice", output);
    }

    #endregion

    #region Worker construction failure surfaces as an Error (#464)

    /// <summary>
    /// Regression for #464 and #700: when the <c>Worker</c> constructor fails (here an
    /// uncloneable <c>workerData</c> containing a function), the value caught by guest
    /// <c>try/catch</c> must be a real <c>Error</c> carrying the reason in <c>.message</c>
    /// — not the bare message string the interpreter previously bound (<c>typeof e</c>
    /// was "string", <c>e.message</c> undefined), and not the plain
    /// <c>{ message, name }</c> object (with <c>name</c> = the .NET type) that compiled
    /// mode previously produced.
    /// </summary>
    /// <remarks>
    /// #700 fixed the compiled-mode <c>WrapException</c> catch-boundary path to return a
    /// real <c>$Error</c>, so <c>e instanceof Error</c> and <c>e.name === "Error"</c> now
    /// hold in BOTH modes (previously asserted only for the interpreter, which #464
    /// targeted).
    /// </remarks>
    [Theory, ModeData]
    public void Worker_UncloneableWorkerData_RejectsWithErrorObjectNotString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker_noop.ts"] = """
                console.log("worker-should-not-start");
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                try {
                    // A function is never structured-cloneable.
                    const w = new Worker(__dirname + "/worker_noop.ts", { workerData: { fn: () => 1 } });
                    console.log("constructed-without-error");
                } catch (e: any) {
                    console.log("typeof=" + typeof e);
                    console.log("hasMessage=" + (e && typeof e.message === "string" && e.message.length > 0));
                    console.log("isError=" + (e instanceof Error));
                    console.log("name=" + e.name);
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("typeof=object", output);
        Assert.Contains("hasMessage=true", output);
        Assert.DoesNotContain("constructed-without-error", output);
        Assert.DoesNotContain("worker-should-not-start", output);
        // #700: a real Error in both modes — instanceof Error holds and name is "Error"
        // (not the .NET exception type name).
        Assert.Contains("isError=true", output);
        Assert.Contains("name=Error", output);
    }

    #endregion

    #region parentPort (#1109)

    /// <summary>
    /// On the MAIN thread, <c>worker_threads.parentPort</c> is <c>null</c> in Node — and that is the
    /// default value emitted for an unconfigured main-thread realm. Worker realms are configured with
    /// a live parentPort before execution. This locks in that both modes agree and that the canonical
    /// <c>if (parentPort)</c> main-thread guard keeps working.
    /// </summary>
    [Theory, ModeData]
    public void ParentPort_OnMainThread_IsNull(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parentPort, isMainThread } from "worker_threads";
                console.log("main=" + isMainThread);
                console.log("isnull=" + (parentPort === null));
                if (parentPort) {
                    console.log("guard-entered");
                } else {
                    console.log("guard-skipped");
                }
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("main=true", output);
        Assert.Contains("isnull=true", output);
        Assert.Contains("guard-skipped", output);
        Assert.DoesNotContain("guard-entered", output);
    }

    #endregion
}
