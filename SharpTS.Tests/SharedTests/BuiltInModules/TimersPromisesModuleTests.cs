using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'timers/promises' module: promise-based setTimeout, setImmediate, setInterval.
/// </summary>
[Collection("TimerTests")]
public class TimersPromisesModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void TimersPromises_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as timers from 'timers/promises';
                console.log(typeof timers === 'object');
                console.log(typeof timers.setTimeout === 'function');
                console.log(typeof timers.setImmediate === 'function');
                console.log(typeof timers.setInterval === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout, setImmediate } from 'timers/promises';
                console.log(typeof setTimeout === 'function');
                console.log(typeof setImmediate === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_Import_NodePrefix(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'node:timers/promises';
                console.log(typeof setTimeout === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region setTimeout Tests

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_ReturnsPromise(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                const p = setTimeout(10);
                console.log(typeof p.then === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_ResolvesWithValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const result = await setTimeout(10, 'hello');
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_ResolvesWithNumericValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const result = await setTimeout(10, 42);
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_DefaultValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const result = await setTimeout(10);
                    console.log(result === undefined);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_ZeroDelay(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const result = await setTimeout(0, 'immediate');
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("immediate\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_ThenChaining(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                setTimeout(10, 'world').then((val: string) => {
                    console.log('hello ' + val);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    #endregion

    #region setImmediate Tests

    [Theory, ModeData]
    public void TimersPromises_SetImmediate_ReturnsPromise(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setImmediate } from 'timers/promises';
                const p = setImmediate();
                console.log(typeof p.then === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetImmediate_ResolvesWithValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setImmediate } from 'timers/promises';
                async function main() {
                    const result = await setImmediate('quick');
                    console.log(result);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("quick\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetImmediate_DefaultValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setImmediate } from 'timers/promises';
                async function main() {
                    const result = await setImmediate();
                    console.log(result === undefined);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Multiple Timers

    [Theory, ModeData]
    public void TimersPromises_MultipleSetTimeout_Sequential(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const a = await setTimeout(10, 'first');
                    const b = await setTimeout(10, 'second');
                    console.log(a);
                    console.log(b);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("first\nsecond\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_MixedWithSetImmediate(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout, setImmediate } from 'timers/promises';
                async function main() {
                    const a = await setImmediate('fast');
                    const b = await setTimeout(10, 'slow');
                    console.log(a);
                    console.log(b);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("fast\nslow\n", output);
    }

    #endregion

    #region setInterval AsyncIterable Tests

    [Theory, ModeData]
    public void TimersPromises_SetInterval_ForAwaitOf_Basic(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    let count = 0;
                    for await (const val of setInterval(10, 'tick')) {
                        console.log(val);
                        count++;
                        if (count >= 3) break;
                    }
                    console.log('done');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("tick\ntick\ntick\ndone\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetInterval_ForAwaitOf_NumericValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    let sum = 0;
                    for await (const val of setInterval(10, 5)) {
                        sum += val;
                        if (sum >= 15) break;
                    }
                    console.log(sum);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetInterval_ForAwaitOf_BreakCleanup(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    for await (const val of setInterval(10, 'x')) {
                        break;
                    }
                    console.log('after break');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("after break\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetInterval_ForAwaitOf_DefaultValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    for await (const val of setInterval(10)) {
                        console.log(val === undefined);
                        break;
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region AbortSignal Tests

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_AbortSignal_PreAborted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const ac = new AbortController();
                    ac.abort();
                    try {
                        await setTimeout(1000, 'val', { signal: ac.signal });
                        console.log('should not reach');
                    } catch (e) {
                        const msg = (e as any).message ?? e;
                        console.log(msg);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("AbortError: The operation was aborted\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetImmediate_AbortSignal_PreAborted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setImmediate } from 'timers/promises';
                async function main() {
                    const ac = new AbortController();
                    ac.abort();
                    try {
                        await setImmediate('val', { signal: ac.signal });
                        console.log('should not reach');
                    } catch (e) {
                        const msg = (e as any).message ?? e;
                        console.log(msg);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("AbortError: The operation was aborted\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetInterval_AbortSignal_PreAborted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    const ac = new AbortController();
                    ac.abort();
                    try {
                        for await (const val of setInterval(10, 'tick', { signal: ac.signal })) {
                            console.log('should not reach:', val);
                        }
                        console.log('should not reach end');
                    } catch (e) {
                        const msg = (e as any).message ?? e;
                        console.log(msg);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("AbortError: The operation was aborted\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetTimeout_AbortSignal_MidDelay(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';
                async function main() {
                    const ac = new AbortController();
                    // Schedule abort after 20ms in a fire-and-forget async helper
                    const scheduleAbort = async () => {
                        await setTimeout(20);
                        ac.abort();
                    };
                    scheduleAbort();
                    try {
                        await setTimeout(5000, 'val', { signal: ac.signal });
                        console.log('should not reach');
                    } catch (e) {
                        console.log('caught');
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught\n", output);
    }

    [Theory, ModeData]
    public void TimersPromises_SetInterval_AbortSignal_MidIteration(ExecutionMode mode)
    {
        // Aborts the signal from within the loop after 3 iterations.
        // This is deterministic (no time-based race) and verifies that:
        //   1. The loop yields values even with a signal attached
        //   2. Calling ac.abort() ends the iterator cleanly (no throw)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setInterval } from 'timers/promises';
                async function main() {
                    const ac = new AbortController();
                    let count = 0;
                    for await (const val of setInterval(10, 'tick', { signal: ac.signal })) {
                        count++;
                        if (count >= 3) {
                            ac.abort();
                        }
                        if (count > 100) break; // safety
                    }
                    console.log('count:', count);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("count: 3\n", output);
    }

    #endregion
}
