using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for HTTP Server EventEmitter integration.
/// Verifies that SharpTSHttpServer properly inherits from SharpTSEventEmitter
/// and supports full Node.js-compatible event handling.
/// </summary>
public class HttpServerEventTests
{
    [Fact]
    public void Server_On_RegistersListener()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => {});

                // Verify listener is registered
                console.log(server.listenerCount('test'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Server_Once_FiresOnlyOnce()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let callCount = 0;
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.once('test', () => {
                    callCount = callCount + 1;
                });

                // Emit multiple times
                server.emit('test');
                server.emit('test');
                server.emit('test');

                console.log(callCount);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Server_Off_RemovesListener()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                const handler = () => {};

                server.on('test', handler);
                const beforeCount = server.listenerCount('test');

                server.off('test', handler);
                const afterCount = server.listenerCount('test');

                console.log(beforeCount);
                console.log(afterCount);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n0\n", output);
    }

    [Fact]
    public void Server_RemoveAllListeners_ClearsAllForEvent()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => {});
                server.on('test', () => {});
                server.on('test', () => {});

                const beforeCount = server.listenerCount('test');
                server.removeAllListeners('test');
                const afterCount = server.listenerCount('test');

                console.log(beforeCount);
                console.log(afterCount);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3\n0\n", output);
    }

    [Fact]
    public void Server_ListenerCount_ReturnsCorrectCount()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                console.log(server.listenerCount('test'));

                server.on('test', () => {});
                console.log(server.listenerCount('test'));

                server.on('test', () => {});
                console.log(server.listenerCount('test'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("0\n1\n2\n", output);
    }

    [Fact]
    public void Server_EventNames_ReturnsRegisteredEvents()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('foo', () => {});
                server.on('bar', () => {});
                server.on('baz', () => {});

                const names = server.eventNames();
                console.log(names.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Server_MultipleListeners_ReceiveSameEvent()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let results: number[] = [];
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => {
                    results.push(1);
                });
                server.on('test', () => {
                    results.push(2);
                });
                server.on('test', () => {
                    results.push(3);
                });

                server.emit('test');

                console.log(results.join(','));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1,2,3\n", output);
    }

    [Fact]
    public void Server_Emit_CustomEvent_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let receivedData: string = '';
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('custom', (data: string) => {
                    receivedData = data;
                });

                server.emit('custom', 'hello world');

                console.log(receivedData);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Server_Listeners_ReturnsListenerArray()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                const handler1 = () => {};
                const handler2 = () => {};

                server.on('test', handler1);
                server.on('test', handler2);

                const listeners = server.listeners('test');
                console.log(listeners.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Server_SetMaxListeners_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.setMaxListeners(20);
                console.log(server.getMaxListeners());
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Server_PrependListener_AddsToFront()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let order: number[] = [];
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => order.push(1));
                server.on('test', () => order.push(2));
                server.prependListener('test', () => order.push(0));

                server.emit('test');

                console.log(order.join(','));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        // Prepended listener should fire first
        Assert.Equal("0,1,2\n", output);
    }

    [Fact]
    public void Server_AddListener_IsAliasForOn()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let called = false;
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.addListener('test', () => {
                    called = true;
                });

                server.emit('test');
                console.log(called);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Server_RemoveListener_IsAliasForOff()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                const handler = () => {};
                server.on('test', handler);
                const before = server.listenerCount('test');

                server.removeListener('test', handler);
                const after = server.listenerCount('test');

                console.log(before);
                console.log(after);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("1\n0\n", output);
    }

    [Fact]
    public void Server_MethodChaining_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                // All these methods should return the server for chaining
                const result = server
                    .on('foo', () => {})
                    .once('bar', () => {})
                    .setMaxListeners(15);

                console.log(server.listenerCount('foo') + server.listenerCount('bar'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Server_TypeCheck_EventEmitterMethods()
    {
        // This test verifies that type checking passes for EventEmitter methods
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                // These should all type check successfully
                server.on('request', () => {});
                server.once('listening', () => {});
                server.off('error', () => {});
                server.removeAllListeners('close');
                const count: number = server.listenerCount('request');
                const names: string[] = server.eventNames();
                const max: number = server.getMaxListeners();

                console.log('type check passed');
                """
        };

        // If this executes without type errors, the test passes
        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("type check passed\n", output);
    }

    [Fact]
    public void Server_Emit_ReturnsBoolean()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                // emit returns false when no listeners
                const noListeners = server.emit('test');
                console.log(noListeners);

                // emit returns true when listeners exist
                server.on('test', () => {});
                const hasListeners = server.emit('test');
                console.log(hasListeners);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Server_RawListeners_ReturnsListenerArray()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => {});
                server.once('test', () => {});

                const listeners = server.rawListeners('test');
                console.log(listeners.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Server_PrependOnceListener_AddsOnceListenerToFront()
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { createServer } from 'http';

                let order: number[] = [];
                const server = createServer((req, res) => {
                    res.end('OK');
                });

                server.on('test', () => order.push(1));
                server.prependOnceListener('test', () => order.push(0));

                // First emit - prepended once listener fires first
                server.emit('test');
                console.log(order.join(','));

                // Second emit - once listener removed
                order = [];
                server.emit('test');
                console.log(order.join(','));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("0,1\n1\n", output);
    }
}
