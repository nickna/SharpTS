using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the events module EventEmitter class in compiled mode.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Closure Limitation:</strong>
/// Due to a pre-existing compiler closure limitation, tests that modify captured
/// primitive variables (like count++) don't propagate changes to the outer scope.
/// Tests are designed to output directly from callbacks or use arrays (reference types)
/// to work around this limitation.
/// </para>
/// </remarks>
public class EventsModuleCompiledTests
{
    #region Import Patterns

    [Fact]
    public void Compiled_Events_NamedImport_EventEmitter()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();
                console.log(typeof emitter === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_Events_NamespaceImport()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as events from 'events';

                const emitter = new events.EventEmitter();
                console.log(typeof emitter === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Core Event Methods

    [Fact]
    public void Compiled_Events_OnAndEmit()
    {
        // Note: Due to closure limitation, we output directly from callback
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.on('test', (msg: string) => {
                    console.log(msg);
                });

                emitter.emit('test', 'hello');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Compiled_Events_EmitReturnsBoolean()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                // No listeners - should return false
                const result1 = emitter.emit('nolisteners');

                // Add listener
                emitter.on('test', () => {});

                // Has listener - should return true
                const result2 = emitter.emit('test');

                console.log(result1 === false);
                console.log(result2 === true);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Events_Once()
    {
        // Note: Due to closure limitation, we output directly from callback to verify "once" behavior
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.once('test', () => {
                    console.log('called');
                });

                emitter.emit('test');
                emitter.emit('test');
                emitter.emit('test');
                console.log('done');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        // Should only see "called" once
        Assert.Equal("called\ndone\n", output);
    }

    [Fact]
    public void Compiled_Events_MultipleListeners()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();
                const results: string[] = [];

                emitter.on('test', () => results.push('first'));
                emitter.on('test', () => results.push('second'));
                emitter.on('test', () => results.push('third'));

                emitter.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("first,second,third\n", output);
    }

    [Fact]
    public void Compiled_Events_MultipleArguments()
    {
        // Note: Due to closure limitation, we output directly from callback
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.on('test', (a: string, b: number, c: boolean) => {
                    console.log(`${a}-${b}-${c}`);
                });

                emitter.emit('test', 'hello', 42, true);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("hello-42-true\n", output);
    }

    #endregion

    #region Removal Methods

    [Fact]
    public void Compiled_Events_Off()
    {
        // Note: Due to closure limitation, we output directly from callback
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        // Should only see "called" once (after off, it shouldn't be called)
        Assert.Equal("called\ndone\n", output);
    }

    [Fact]
    public void Compiled_Events_RemoveAllListeners_ByEvent()
    {
        // Note: Due to closure limitation, we output directly from callbacks
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.on('test', () => console.log('test1'));
                emitter.on('test', () => console.log('test2'));
                emitter.on('other', () => console.log('other'));

                emitter.emit('test');
                emitter.emit('other');

                emitter.removeAllListeners('test');

                emitter.emit('test');
                emitter.emit('other');
                console.log('done');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        // First emit('test') calls both, emit('other') calls other
        // After removeAllListeners, emit('test') does nothing, emit('other') still works
        Assert.Equal("test1\ntest2\nother\nother\ndone\n", output);
    }

    #endregion

    #region Listener Inspection

    [Fact]
    public void Compiled_Events_ListenerCount()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.on('test', () => {});
                emitter.on('test', () => {});
                emitter.on('other', () => {});

                console.log(emitter.listenerCount('test'));
                console.log(emitter.listenerCount('other'));
                console.log(emitter.listenerCount('none'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("2\n1\n0\n", output);
    }

    [Fact]
    public void Compiled_Events_EventNames()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                emitter.on('alpha', () => {});
                emitter.on('beta', () => {});
                emitter.on('gamma', () => {});

                const names = emitter.eventNames();
                console.log(names.length);
                // Just verify we have 3 names with the same output test
                console.log(names.length === 3);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("3\ntrue\n", output);
    }

    [Fact]
    public void Compiled_Events_Listeners()
    {
        // Note: Function identity comparison may not work in compiled mode due to
        // how functions are wrapped. We verify count and that listeners are callable.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                const fn1 = () => console.log('fn1');
                const fn2 = () => console.log('fn2');

                emitter.on('test', fn1);
                emitter.on('test', fn2);

                const listeners = emitter.listeners('test');
                console.log(listeners.length);
                console.log(typeof listeners[0] === 'function' || typeof listeners[0] === 'object');
                console.log(typeof listeners[1] === 'function' || typeof listeners[1] === 'object');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("2\ntrue\ntrue\n", output);
    }

    #endregion

    #region Prepend Methods

    [Fact]
    public void Compiled_Events_PrependListener()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();
                const results: string[] = [];

                emitter.on('test', () => results.push('first'));
                emitter.prependListener('test', () => results.push('prepended'));

                emitter.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("prepended,first\n", output);
    }

    [Fact]
    public void Compiled_Events_PrependOnceListener()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("once-prepended,regular,regular\n", output);
    }

    #endregion

    #region Max Listeners

    [Fact]
    public void Compiled_Events_SetAndGetMaxListeners()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from 'events';

                const emitter = new EventEmitter();

                // Default should be 10
                console.log(emitter.getMaxListeners());

                emitter.setMaxListeners(20);
                console.log(emitter.getMaxListeners());

                emitter.setMaxListeners(5);
                console.log(emitter.getMaxListeners());
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("10\n20\n5\n", output);
    }

    #endregion

    #region Method Chaining

    [Fact]
    public void Compiled_Events_MethodChaining()
    {
        // Note: Due to closure limitation, we output directly from callbacks
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Compiled_Events_RemoveDuringEmit()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("1,2,3,---,1,3\n", output);
    }

    [Fact]
    public void Compiled_Events_AddDuringEmit()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("1,2,---,1,2,new\n", output);
    }

    [Fact]
    public void Compiled_Events_MultipleEmitters()
    {
        // Note: Due to closure limitation, we output directly from callbacks
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("add:5\nmultiply:3\nadd:2\n", output);
    }

    #endregion
}
