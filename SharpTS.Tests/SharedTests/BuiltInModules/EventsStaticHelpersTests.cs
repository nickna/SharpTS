using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the module-level / static helpers on the events module:
/// once, on, getEventListeners, setMaxListeners, getMaxListeners,
/// addAbortListener, static listenerCount (issue #1098).
/// </summary>
public class EventsStaticHelpersTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Once_ResolvesWithEventArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, once } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    setTimeout(() => ee.emit('ready', 1, 2), 0);
                    const args = await once(ee, 'ready');
                    console.log(args[0] + ',' + args[1]);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1,2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Once_StaticForm_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    setTimeout(() => ee.emit('go', 'hi'), 0);
                    const args = await EventEmitter.once(ee, 'go');
                    console.log(args[0]);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hi\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Once_RejectsOnError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, once } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    setTimeout(() => ee.emit('error', new Error('boom')), 0);
                    try {
                        await once(ee, 'never');
                        console.log('NOTREACHED');
                    } catch (e: any) {
                        console.log('caught:' + e.message);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("caught:boom\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Once_RejectsOnAlreadyAbortedSignal(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, once } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    const ac = new AbortController();
                    ac.abort();
                    try {
                        await once(ee, 'x', { signal: ac.signal });
                        console.log('NOTREACHED');
                    } catch (e: any) {
                        console.log('aborted:' + e.name);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("aborted:AbortError\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void On_AsyncIterator_YieldsEventArgs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, on } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    const it = on(ee, 'tick');
                    ee.emit('tick', 1);
                    ee.emit('tick', 2);
                    ee.emit('tick', 3);
                    const out: number[] = [];
                    for await (const evt of it) {
                        out.push(evt[0]);
                        if (out.length === 3) break;
                    }
                    console.log(out.join(','));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void On_AsyncIterator_AbortEndsIteration(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, on } from 'events';
                async function main(): Promise<void> {
                    const ee = new EventEmitter();
                    const ac = new AbortController();
                    const it = on(ee, 'tick', { signal: ac.signal });
                    ee.emit('tick', 1);
                    setTimeout(() => ac.abort(), 0);
                    const out: number[] = [];
                    try {
                        for await (const evt of it) {
                            out.push(evt[0]);
                        }
                    } catch (e: any) {
                        console.log(out.join(',') + '|' + e.name);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1|AbortError\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GetEventListeners_ReturnsListeners(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, getEventListeners } from 'events';
                const ee = new EventEmitter();
                const f = () => {};
                ee.on('z', f);
                ee.on('z', () => {});
                const ls = getEventListeners(ee, 'z');
                console.log(ls.length);
                console.log(ls[0] === f);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SetMaxListeners_PerEmitterAndDefault(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, setMaxListeners, getMaxListeners } from 'events';
                const ee = new EventEmitter();
                setMaxListeners(15, ee);
                console.log(getMaxListeners(ee));
                console.log(EventEmitter.defaultMaxListeners);
                setMaxListeners(7);
                console.log(EventEmitter.defaultMaxListeners);
                EventEmitter.defaultMaxListeners = 10;
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("15\n10\n7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticListenerCount_CountsListeners(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter, listenerCount } from 'events';
                const ee = new EventEmitter();
                ee.on('a', () => {});
                ee.on('a', () => {});
                console.log(listenerCount(ee, 'a'));
                console.log(EventEmitter.listenerCount(ee, 'a'));
                console.log(listenerCount(ee, 'missing'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\n2\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AddAbortListener_FiresOnAbort(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { addAbortListener } from 'events';
                async function main(): Promise<void> {
                    const ac = new AbortController();
                    let fired = 0;
                    addAbortListener(ac.signal, () => { fired++; });
                    ac.abort();
                    await Promise.resolve();
                    console.log('fired:' + fired);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("fired:1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AddAbortListener_DisposePreventsFiring(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { addAbortListener } from 'events';
                async function main(): Promise<void> {
                    const ac = new AbortController();
                    let fired = 0;
                    const disp = addAbortListener(ac.signal, () => { fired++; });
                    disp[Symbol.dispose]();
                    ac.abort();
                    await Promise.resolve();
                    console.log('fired:' + fired);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("fired:0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AddAbortListener_AlreadyAbortedFiresOnMicrotask(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { addAbortListener } from 'events';
                async function main(): Promise<void> {
                    const ac = new AbortController();
                    ac.abort();
                    let fired = 0;
                    addAbortListener(ac.signal, () => { fired++; });
                    console.log('sync:' + fired);
                    await new Promise((r: any) => setTimeout(r, 0));
                    console.log('async:' + fired);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("sync:0\nasync:1\n", output);
    }
}
