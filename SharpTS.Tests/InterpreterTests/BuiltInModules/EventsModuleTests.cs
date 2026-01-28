using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the events module EventEmitter in interpreter mode.
/// </summary>
public class EventsModuleTests
{
    // ============ IMPORT PATTERNS ============

    [Fact]
    public void Events_NamedImport_EventEmitter()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                console.log(typeof emitter);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void Events_NamespaceImport()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as events from 'events';
                const emitter = new events.EventEmitter();
                console.log(typeof emitter);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("object\n", output);
    }

    // ============ CONSTRUCTOR ============

    [Fact]
    public void EventEmitter_Constructor_CreatesInstance()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ ON / EMIT ============

    [Fact]
    public void EventEmitter_On_Emit_BasicUsage()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("received: hello\n", output);
    }

    [Fact]
    public void EventEmitter_Emit_MultipleArguments()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void EventEmitter_Emit_MultipleListeners()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    [Fact]
    public void EventEmitter_Emit_ReturnsTrue_WhenHasListeners()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    // ============ ONCE ============

    [Fact]
    public void EventEmitter_Once_FiresOnlyOnce()
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
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("fired\n", output);
    }

    [Fact]
    public void EventEmitter_Once_WithArguments()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("value: 42\nfalse\n", output);
    }

    // ============ OFF / REMOVE LISTENER ============

    [Fact]
    public void EventEmitter_Off_RemovesListener()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                const listener = () => console.log('should not fire');
                emitter.on('test', listener);
                emitter.off('test', listener);

                console.log(emitter.emit('test'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void EventEmitter_RemoveListener_Alias()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\n", output);
    }

    // ============ REMOVE ALL LISTENERS ============

    [Fact]
    public void EventEmitter_RemoveAllListeners_SpecificEvent()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("other\n", output);
    }

    [Fact]
    public void EventEmitter_RemoveAllListeners_AllEvents()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\nfalse\n", output);
    }

    // ============ LISTENER INSPECTION ============

    [Fact]
    public void EventEmitter_ListenerCount()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("0\n1\n2\n", output);
    }

    [Fact]
    public void EventEmitter_EventNames()
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
                console.log(names.includes('foo'));
                console.log(names.includes('bar'));
                console.log(names.includes('baz'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void EventEmitter_Listeners_ReturnsArray()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    // ============ PREPEND METHODS ============

    [Fact]
    public void EventEmitter_PrependListener()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("first\nsecond\n", output);
    }

    [Fact]
    public void EventEmitter_PrependOnceListener()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("prepended once\nregular\n---\nregular\n", output);
    }

    // ============ MAX LISTENERS ============

    [Fact]
    public void EventEmitter_GetMaxListeners_DefaultValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();
                console.log(emitter.getMaxListeners());
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void EventEmitter_SetMaxListeners()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void EventEmitter_DefaultMaxListeners_StaticProperty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                console.log(EventEmitter.defaultMaxListeners);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("10\n", output);
    }

    // ============ METHOD CHAINING ============

    [Fact]
    public void EventEmitter_MethodChaining()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';
                const emitter = new EventEmitter();

                emitter
                    .on('a', () => console.log('a'))
                    .on('b', () => console.log('b'))
                    .once('c', () => console.log('c'))
                    .setMaxListeners(100);

                emitter.emit('a');
                emitter.emit('b');
                emitter.emit('c');
                console.log(emitter.getMaxListeners());
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("a\nb\nc\n100\n", output);
    }

    // ============ EDGE CASES ============

    [Fact]
    public void EventEmitter_RemoveDuringEmit()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        // Both should fire first time due to snapshot, then only listener1
        Assert.Equal("listener1\nlistener2\n---\nlistener1\n", output);
    }

    [Fact]
    public void EventEmitter_AddDuringEmit()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        // New listener should NOT fire during current emit, only on next
        Assert.Equal("listener1: 0\n---\nlistener1: 1\nadded\n", output);
    }

    [Fact]
    public void EventEmitter_SameListenerMultipleTimes()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        // Same listener can be added multiple times
        Assert.Equal("3\nfired\nfired\nfired\n", output);
    }

    [Fact]
    public void EventEmitter_AddListener_Alias()
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("addListener works\n", output);
    }
}
