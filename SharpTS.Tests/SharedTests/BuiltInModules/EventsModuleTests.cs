using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the events module EventEmitter class.
/// Note: Due to compiler closure limitations, some tests use arrays (reference types)
/// instead of captured primitives for verification.
/// </summary>
public class EventsModuleTests
{
    #region Import Patterns

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Events_NamedImport_EventEmitter(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                console.log(typeof emitter === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Events_NamespaceImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as events from 'events';
                const emitter = new events.EventEmitter();
                console.log(typeof emitter === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Constructor

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Constructor_CreatesInstance(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                console.log(emitter !== null);
                console.log(emitter !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region On / Emit

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_On_Emit_BasicUsage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', (msg: string) => {
                    console.log('received: ' + msg);
                });

                emitter.emit('test', 'hello');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("received: hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Emit_MultipleArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('data', (a: number, b: number, c: number) => {
                    console.log(a + b + c);
                });

                emitter.emit('data', 1, 2, 3);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Emit_MultipleListeners(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('first'));
                emitter.on('test', () => console.log('second'));
                emitter.on('test', () => console.log('third'));

                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Emit_ReturnsTrue_WhenHasListeners(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => {});

                console.log(emitter.emit('test'));
                console.log(emitter.emit('nonexistent'));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion

    #region Once

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Once_FiresOnlyOnce(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.once('test', () => console.log('fired'));

                emitter.emit('test');
                emitter.emit('test');
                emitter.emit('test');
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("fired\ndone\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Once_WithArguments(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.once('data', (value: number) => console.log('value: ' + value));

                emitter.emit('data', 42);
                console.log(emitter.emit('data', 99));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("value: 42\nfalse\n", output);
    }

    #endregion

    #region Off / RemoveListener

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Off_RemovesListener(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const listener = () => console.log('called');

                emitter.on('test', listener);
                emitter.emit('test');

                emitter.off('test', listener);
                emitter.emit('test');
                console.log('done');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("called\ndone\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_RemoveListener_Alias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const listener = () => console.log('should not fire');
                emitter.addListener('test', listener);
                emitter.removeListener('test', listener);

                console.log(emitter.emit('test'));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region RemoveAllListeners

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_RemoveAllListeners_SpecificEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('test'));
                emitter.on('other', () => console.log('other'));

                emitter.removeAllListeners('test');

                emitter.emit('test');
                emitter.emit('other');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("other\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_RemoveAllListeners_AllEvents(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('test'));
                emitter.on('other', () => console.log('other'));

                emitter.removeAllListeners();

                console.log(emitter.emit('test'));
                console.log(emitter.emit('other'));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\nfalse\n", output);
    }

    #endregion

    #region Listener Inspection

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_ListenerCount(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                console.log(emitter.listenerCount('test'));

                emitter.on('test', () => {});
                console.log(emitter.listenerCount('test'));

                emitter.on('test', () => {});
                console.log(emitter.listenerCount('test'));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_EventNames(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('foo', () => {});
                emitter.on('bar', () => {});
                emitter.on('baz', () => {});

                const names = emitter.eventNames();
                console.log(names.length);
                console.log(names.length === 3);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_Listeners_ReturnsArray(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const fn1 = () => {};
                const fn2 = () => {};
                emitter.on('test', fn1);
                emitter.on('test', fn2);

                const listeners = emitter.listeners('test');
                console.log(listeners.length);
                console.log(listeners[0] === fn1);
                console.log(listeners[1] === fn2);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    #endregion

    #region Prepend Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_PrependListener(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('second'));
                emitter.prependListener('test', () => console.log('first'));

                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("first\nsecond\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_PrependOnceListener_Interpreted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('regular'));
                emitter.prependOnceListener('test', () => console.log('prepended once'));

                emitter.emit('test');
                console.log('---');
                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("prepended once\nregular\n---\nregular\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_PrependOnceListener_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                const results: string[] = [];

                emitter.on('test', () => results.push('regular'));
                emitter.prependOnceListener('test', () => results.push('once-prepended'));

                emitter.emit('test');
                emitter.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("once-prepended,regular,regular\n", output);
    }

    #endregion

    #region Max Listeners

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_GetMaxListeners_DefaultValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                console.log(emitter.getMaxListeners());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_SetMaxListeners(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.setMaxListeners(20);
                console.log(emitter.getMaxListeners());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_DefaultMaxListeners_StaticProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                console.log(EventEmitter.defaultMaxListeners);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Method Chaining

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_MethodChaining(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter
                    .on('a', () => console.log('a'))
                    .on('b', () => console.log('b'))
                    .on('c', () => console.log('c'));

                emitter.emit('a');
                emitter.emit('b');
                emitter.emit('c');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_RemoveDuringEmit_Interpreted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const listener1 = () => {
                    console.log('listener1');
                    emitter.off('test', listener2);
                };

                const listener2 = () => {
                    console.log('listener2');
                };

                emitter.on('test', listener1);
                emitter.on('test', listener2);

                emitter.emit('test');
                console.log('---');
                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        // Both should fire first time due to snapshot, then only listener1
        Assert.Equal("listener1\nlistener2\n---\nlistener1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_RemoveDuringEmit_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                const results: string[] = [];

                const listener1 = () => {
                    results.push('1');
                    emitter.off('test', listener2);
                };
                const listener2 = () => results.push('2');
                const listener3 = () => results.push('3');

                emitter.on('test', listener1);
                emitter.on('test', listener2);
                emitter.on('test', listener3);

                // First emit - all three should run (snapshot taken before removal)
                emitter.emit('test');
                results.push('---');

                // Second emit - listener2 was removed
                emitter.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1,2,3,---,1,3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_AddDuringEmit_Interpreted(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                let count = 0;

                emitter.on('test', () => {
                    console.log('listener1: ' + count);
                    count++;
                    if (count === 1) {
                        emitter.on('test', () => console.log('added'));
                    }
                });

                emitter.emit('test');
                console.log('---');
                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        // New listener should NOT fire during current emit, only on next
        Assert.Equal("listener1: 0\n---\nlistener1: 1\nadded\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_AddDuringEmit_Compiled(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                const results: string[] = [];

                const listener1 = () => {
                    results.push('1');
                    emitter.on('test', () => results.push('new'));
                };
                const listener2 = () => results.push('2');

                emitter.on('test', listener1);
                emitter.on('test', listener2);

                // First emit - only original two should run
                emitter.emit('test');
                results.push('---');

                // Second emit - now includes the new one
                emitter.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1,2,---,1,2,new\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_SameListenerMultipleTimes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const listener = () => console.log('fired');

                emitter.on('test', listener);
                emitter.on('test', listener);
                emitter.on('test', listener);

                console.log(emitter.listenerCount('test'));
                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        // Same listener can be added multiple times
        Assert.Equal("3\nfired\nfired\nfired\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_AddListener_Alias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter.addListener('test', () => console.log('addListener works'));
                emitter.emit('test');
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("addListener works\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.CompiledOnly), MemberType = typeof(ExecutionModes))]
    public void EventEmitter_MultipleEmitters(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter1 = new EventEmitter();
                const emitter2 = new EventEmitter();

                emitter1.on('add', (n: number) => console.log('add:' + n));
                emitter2.on('multiply', (n: number) => console.log('multiply:' + n));

                emitter1.emit('add', 5);
                emitter2.emit('multiply', 3);
                emitter1.emit('add', 2);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("add:5\nmultiply:3\nadd:2\n", output);
    }

    #endregion
}
