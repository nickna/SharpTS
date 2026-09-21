using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the events module captureRejections / errorMonitor /
/// EventEmitterAsyncResource surface (issue #1099). The instance behaviors are
/// implemented in the runtime EventEmitter type (SharpTSEventEmitter /
/// $EventEmitter) so they hold for a direct <c>new EventEmitter()</c> in both
/// interpreter and compiled modes.
/// </summary>
public class EventsRejectionMonitorTests
{
    [Theory, ModeData]
    public void RejectionDispatchPrecedesTimers(ExecutionMode mode)
    {
        var source = """
            import { EventEmitter } from 'events';
            const events = new EventEmitter({ captureRejections: true });
            events.on('error', error => console.log(error));
            events.on('task', async () => { throw 'captured'; });
            events.emit('task');
            setTimeout(() => console.log('timer'), 0);
            Promise.resolve().then(() => console.log('reaction'));
            console.log('scheduled');
            """;
        Assert.Equal("scheduled\nreaction\ncaptured\ntimer\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void RejectionDispatchFollowsPromiseCheckpoint(ExecutionMode mode)
    {
        var source = """
            import { EventEmitter } from 'events';
            const events = new EventEmitter({ captureRejections: true });
            events.on('error', error => console.log(error));
            events.on('task', async () => { throw 'captured'; });
            events.emit('task');
            Promise.resolve().then(() => console.log('reaction')).then(() => console.log('nested'));
            console.log('scheduled');
            """;
        Assert.Equal("scheduled\nreaction\nnested\ncaptured\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CompletedRejectionRunsAfterEmitReturns(ExecutionMode mode)
    {
        var source = """
            import { EventEmitter, errorMonitor } from 'events';
            const events = new EventEmitter({ captureRejections: true });
            events.on(errorMonitor, (error) => { console.log('monitor:' + error); });
            events.on('error', (error) => { console.log('handled:' + error); });
            async function fail() { throw 'rejected'; }
            events.on('task', fail);
            events.emit('task');
            console.log('scheduled');
            """;
        Assert.Equal("scheduled\nmonitor:rejected\nhandled:rejected\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void PendingListenerRejectionIsCaptured(ExecutionMode mode)
    {
        var source = """
            import { EventEmitter } from 'events';
            const events = new EventEmitter({ captureRejections: true });
            let captured = false;
            let observed = false;
            async function failLater() {
                await new Promise((resolve) => setTimeout(resolve, 10));
                throw 'late';
            }
            const pending = failLater();
            pending.catch(() => { observed = true; });
            events.on('error', () => { captured = true; });
            events.on('task', () => pending);
            events.emit('task');
            setTimeout(() => { console.log(observed); console.log(captured); }, 50);
            """;
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CaptureRejections_RoutesAsyncListenerRejectionToError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter({ captureRejections: true });
                    let got = '';
                    ee.on('error', (err: any) => { got = 'routed:' + err.message; });
                    ee.on('go', async () => { throw new Error('boom'); });
                    ee.emit('go');
                    await new Promise((r: any) => setTimeout(r, 10));
                    console.log(got);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("routed:boom\n", output);
    }

    [Theory, ModeData]
    public void CaptureRejections_PreservesRawGuestReason(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                async function main(): Promise<void> {
                    const marker: any = {};
                    const ee = new EventEmitter({ captureRejections: true });
                    let same = false;
                    ee.on('error', (reason: any) => { same = reason === marker; });
                    ee.on('go', async () => { throw marker; });
                    ee.emit('go');
                    await new Promise((resolve: any) => setTimeout(resolve, 10));
                    console.log(same);
                }
                main();
                """
        };

        Assert.Equal("true\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CaptureRejections_Off_DoesNotRouteToError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    let got = '';
                    ee.on('error', (err: any) => { got = 'routed'; });
                    ee.on('go', async () => { throw new Error('boom'); });
                    ee.emit('go');
                    await new Promise((r: any) => setTimeout(r, 10));
                    console.log(got === '' ? 'not-routed' : got);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("not-routed\n", output);
    }

    [Theory, ModeData]
    public void ErrorMonitor_ObservesAndStillThrowsWhenUnhandled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, errorMonitor } from 'events';
                const ee = new EventEmitter();
                let saw = '';
                let threw = false;
                ee.on(errorMonitor, (e: any) => { saw = e.message; });
                try {
                    ee.emit('error', new Error('boom'));
                } catch (e: any) {
                    threw = true;
                }
                console.log(saw + '|' + threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("boom|true\n", output);
    }

    [Theory, ModeData]
    public void ErrorMonitor_WithRegularListener_DoesNotThrow(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, errorMonitor } from 'events';
                const ee = new EventEmitter();
                let mon = '';
                let reg = '';
                let threw = false;
                ee.on(errorMonitor, () => { mon = 'm'; });
                ee.on('error', () => { reg = 'r'; });
                try {
                    ee.emit('error', new Error('x'));
                } catch (e: any) {
                    threw = true;
                }
                console.log(mon + reg + '|' + threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("mr|false\n", output);
    }

    [Theory, ModeData]
    public void UnhandledError_OnDirectEmitter_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const ee = new EventEmitter();
                let threw = false;
                let msg = '';
                try {
                    ee.emit('error', new Error('unhandled'));
                } catch (e: any) {
                    threw = true;
                    msg = e.message;
                }
                console.log(threw + ':' + msg);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true:unhandled\n", output);
    }

    [Theory, ModeData]
    public void UnhandledNonErrorEvent_DoesNotThrow(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const ee = new EventEmitter();
                console.log(ee.emit('whatever'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void ErrorMonitor_And_CaptureRejectionSymbol_AreSymbols(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { errorMonitor, captureRejectionSymbol } from 'events';
                console.log(typeof errorMonitor);
                console.log(typeof captureRejectionSymbol);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("symbol\nsymbol\n", output);
    }

    [Theory, ModeData]
    public void EventEmitterAsyncResource_Surface(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitterAsyncResource } from 'events';
                const eear = new EventEmitterAsyncResource({ name: 'MyResource' });
                let fired = false;
                eear.on('ping', () => { fired = true; });
                eear.emit('ping');
                console.log(fired);
                console.log(eear.asyncId > 0);
                console.log(eear.triggerAsyncId);
                console.log(eear.asyncResource.runInAsyncScope(() => 5));
                console.log(eear.emitDestroy() === eear);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n0\n5\ntrue\n", output);
    }
}
