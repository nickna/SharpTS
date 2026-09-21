using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class TimerMicrotaskCheckpointTests
{
    [Theory, ModeData]
    public void OverdueTimersRunPromiseAndMicrotaskCheckpointBetweenCallbacks(ExecutionMode mode)
    {
        const string source = """
            setTimeout(() => {
                console.log('timer1');
                Promise.resolve(0).then(() => console.log('promise'));
                queueMicrotask(() => console.log('microtask'));
            }, 0);
            setTimeout(() => console.log('timer2'), 0);
            // Block the event-loop thread so both timers are due on its first tick.
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 30);
            """;
        Assert.Equal("timer1\npromise\nmicrotask\ntimer2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MicrotaskCanCancelTheNextOverdueTimer(ExecutionMode mode)
    {
        const string source = """
            setTimeout(() => {
                queueMicrotask(() => { clearTimeout(later); console.log('cancelled'); });
            }, 0);
            const later = setTimeout(() => console.log('must not run'), 0);
            setTimeout(() => console.log('last'), 0);
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 30);
            """;
        Assert.Equal("cancelled\nlast\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PendingRejectionIsCapturedBeforeAnotherOverdueTimer(ExecutionMode mode)
    {
        const string source = """
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
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 100);
            """;
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
