using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'dgram' module: UDP socket support.
/// </summary>
public class DgramModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void Dgram_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                console.log(typeof dgram === 'object');
                console.log(typeof dgram.createSocket === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dgram_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createSocket } from 'dgram';
                console.log(createSocket !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region createSocket Tests

    [Theory, ModeData]
    public void Dgram_CreateSocket_Udp4(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                console.log(typeof socket === 'object');
                console.log(typeof socket.bind === 'function');
                console.log(typeof socket.send === 'function');
                console.log(typeof socket.close === 'function');
                console.log(typeof socket.on === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dgram_CreateSocket_HasEventEmitterMethods(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                console.log(typeof socket.on === 'function');
                console.log(typeof socket.once === 'function');
                console.log(typeof socket.emit === 'function');
                console.log(typeof socket.removeListener === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    #endregion

    #region Bind Tests

    [Theory, ModeData]
    public void Dgram_Bind_EmitsListeningEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.on('listening', () => {
                    const addr = socket.address();
                    console.log(typeof addr.port === 'number');
                    console.log(addr.port > 0);
                    socket.close();
                });
                socket.bind(0, '127.0.0.1');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dgram_Bind_WithCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    console.log('bound');
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("bound", output);
    }

    #endregion

    #region Send and Receive Tests

    [Theory, ModeData]
    public void Dgram_SendReceive_BasicMessage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';

                const receiver = dgram.createSocket('udp4');
                receiver.on('message', (msg: any, rinfo: any) => {
                    console.log(msg.toString() === 'hello');
                    console.log(typeof rinfo.port === 'number');
                    console.log(rinfo.address === '127.0.0.1');
                    receiver.close();
                });
                receiver.bind(0, '127.0.0.1', () => {
                    const port = receiver.address().port;
                    const sender = dgram.createSocket('udp4');
                    sender.send('hello', port, '127.0.0.1', (err: any) => {
                        sender.close();
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
    }

    [Theory, ModeData]
    public void Dgram_SendReceive_BufferMessage(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';

                const receiver = dgram.createSocket('udp4');
                receiver.on('message', (msg: any, rinfo: any) => {
                    console.log(msg.toString() === 'world');
                    console.log(rinfo.size === 5);
                    receiver.close();
                });
                receiver.bind(0, '127.0.0.1', () => {
                    const port = receiver.address().port;
                    const sender = dgram.createSocket('udp4');
                    const buf = Buffer.from('world');
                    sender.send(buf, port, '127.0.0.1', (err: any) => {
                        sender.close();
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
    }

    #endregion

    #region Close Tests

    [Theory, ModeData]
    public void Dgram_Close_EmitsCloseEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.on('close', () => {
                    console.log('closed');
                });
                socket.bind(0, '127.0.0.1', () => {
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("closed", output);
    }

    [Theory, ModeData]
    public void Dgram_Close_WithCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    socket.close(() => {
                        console.log('close-callback');
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("close-callback", output);
    }

    #endregion

    #region Address Tests

    [Theory, ModeData]
    public void Dgram_Address_ReturnsInfo(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    const addr = socket.address();
                    console.log(addr.address === '127.0.0.1');
                    console.log(addr.family === 'IPv4');
                    console.log(addr.port > 0);
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Socket Options Tests

    [Theory, ModeData]
    public void Dgram_SetBroadcast(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    socket.setBroadcast(true);
                    console.log('broadcast-set');
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("broadcast-set", output);
    }

    [Theory, ModeData]
    public void Dgram_SetTTL(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    socket.setTTL(128);
                    console.log('ttl-set');
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("ttl-set", output);
    }

    #endregion

    #region Connected Mode Tests

    [Theory, ModeData]
    public void Dgram_Socket_Connect_And_Send(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';

                const receiver = dgram.createSocket('udp4');
                receiver.on('message', (msg: any) => {
                    console.log(msg.toString());
                    receiver.close();
                });
                receiver.bind(0, '127.0.0.1', () => {
                    const port = receiver.address().port;
                    const sender = dgram.createSocket('udp4');
                    sender.connect(port, '127.0.0.1', () => {
                        sender.send('connected-msg', (err: any) => {
                            sender.close();
                        });
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("connected-msg", output);
    }

    [Theory, ModeData]
    public void Dgram_Socket_Disconnect(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    socket.connect(12345, '127.0.0.1', () => {
                        socket.disconnect();
                        let threw = false;
                        try {
                            socket.remoteAddress();
                        } catch (e) {
                            threw = true;
                        }
                        console.log(threw);
                        socket.close();
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
    }

    [Theory, ModeData]
    public void Dgram_Socket_RemoteAddress(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    socket.connect(12345, '127.0.0.1', () => {
                        const addr = socket.remoteAddress();
                        console.log(addr.address === '127.0.0.1');
                        console.log(addr.family === 'IPv4');
                        console.log(addr.port === 12345);
                        socket.close();
                    });
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dgram_Socket_BufferSize_GetSet(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.bind(0, '127.0.0.1', () => {
                    const origRecv = socket.getRecvBufferSize();
                    const origSend = socket.getSendBufferSize();
                    console.log(typeof origRecv === 'number');
                    console.log(typeof origSend === 'number');
                    socket.setRecvBufferSize(65536);
                    socket.setSendBufferSize(65536);
                    console.log('set-ok');
                    socket.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
        Assert.Contains("set-ok", output);
    }

    [Theory, ModeData]
    public void Dgram_Socket_Connect_Event(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                const socket = dgram.createSocket('udp4');
                socket.on('connect', () => {
                    console.log('connect-event');
                    socket.close();
                });
                socket.bind(0, '127.0.0.1', () => {
                    socket.connect(12345, '127.0.0.1');
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("connect-event", output);
    }

    #endregion
}
