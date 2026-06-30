using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js dgram.Socket (UDP socket).
/// Extends SharpTSEventEmitter for event-driven patterns.
/// </summary>
public class SharpTSDatagramSocket : SharpTSEventEmitter
{
    private UdpClient? _client;
    private Interp? _interpreter;
    private readonly AddressFamily _family;
    private bool _bound;
    private bool _closed;
    private bool _connected;
    private IPEndPoint? _connectedRemote;
    private CancellationTokenSource? _receiveCts;

    public SharpTSDatagramSocket(string type = "udp4")
    {
        _family = type == "udp6" ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
    }

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "bind" => BuiltInMethod.CreateV2("bind", 0, 3, Bind),
            "send" => BuiltInMethod.CreateV2("send", 1, 6, Send),
            "close" => BuiltInMethod.CreateV2("close", 0, 1, Close),
            "address" => BuiltInMethod.CreateV2("address", 0, Address),
            "setBroadcast" => BuiltInMethod.CreateV2("setBroadcast", 1, SetBroadcast),
            "setTTL" => BuiltInMethod.CreateV2("setTTL", 1, SetTTL),
            "setMulticastTTL" => BuiltInMethod.CreateV2("setMulticastTTL", 1, SetMulticastTTL),
            "addMembership" => BuiltInMethod.CreateV2("addMembership", 1, 2, AddMembership),
            "dropMembership" => BuiltInMethod.CreateV2("dropMembership", 1, 2, DropMembership),
            "ref" => BuiltInMethod.CreateV2("ref", 0, Ref),
            "unref" => BuiltInMethod.CreateV2("unref", 0, Unref),
            "connect" => BuiltInMethod.CreateV2("connect", 1, 3, Connect),
            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, Disconnect),
            "remoteAddress" => BuiltInMethod.CreateV2("remoteAddress", 0, RemoteAddress),
            "getRecvBufferSize" => BuiltInMethod.CreateV2("getRecvBufferSize", 0, GetRecvBufferSize),
            "setRecvBufferSize" => BuiltInMethod.CreateV2("setRecvBufferSize", 1, SetRecvBufferSize),
            "getSendBufferSize" => BuiltInMethod.CreateV2("getSendBufferSize", 0, GetSendBufferSize),
            "setSendBufferSize" => BuiltInMethod.CreateV2("setSendBufferSize", 1, SetSendBufferSize),

            // EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Binds the socket to a local port and optional address.
    /// Signature: bind(port?, address?, callback?)
    ///            bind(options?, callback?)
    /// </summary>
    private RuntimeValue Bind(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter = interpreter;
        int port = 0;
        string address = _family == AddressFamily.InterNetworkV6 ? "::" : "0.0.0.0";
        ISharpTSCallable? callback = null;

        if (args.Length > 0)
        {
            var arg0 = args[0].ToObject();
            if (arg0 is double p) port = (int)p;
            else if (arg0 is SharpTSObject options)
            {
                if (options.GetProperty("port") is double op) port = (int)op;
                if (options.GetProperty("address") is string oa) address = oa;
                if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb) callback = cb;
            }
            else if (arg0 is ISharpTSCallable cb0)
            {
                callback = cb0;
            }
        }
        if (args.Length > 1 && args[1].IsString) address = args[1].AsStringUnsafe();
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb1 && callback == null) callback = cb1;
        if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable cb2) callback = cb2;

        if (callback != null)
        {
            Once("listening", callback);
        }

        try
        {
            var ep = new IPEndPoint(IPAddress.Parse(address), port);
            _client = new UdpClient(_family);
            _client.Client.Bind(ep);
            _bound = true;

            interpreter.Ref();

            // Start receive loop
            StartReceiving(interpreter);

            // Emit 'listening' event
            interpreter.ScheduleTimer(0, 0, () =>
            {
                EmitEvent(interpreter, "listening", []);
            }, isInterval: false);
        }
        catch (Exception ex)
        {
            interpreter.ScheduleTimer(0, 0, () =>
            {
                EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
            }, isInterval: false);
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Sends a datagram.
    /// Signature: send(msg, port, address?, callback?)
    ///            send(msg, offset, length, port, address?, callback?)
    /// </summary>
    private RuntimeValue Send(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter = interpreter;

        if (_client == null)
        {
            // Auto-bind if not bound
            _client = new UdpClient(_family);
        }

        byte[] data;
        int port = 0;
        string address = _family == AddressFamily.InterNetworkV6 ? "::1" : "127.0.0.1";
        ISharpTSCallable? callback = null;

        // Get message data
        if (args[0].ToObject() is SharpTSBuffer buf)
        {
            data = buf.Data;
        }
        else if (args[0].IsString)
        {
            data = System.Text.Encoding.UTF8.GetBytes(args[0].AsStringUnsafe());
        }
        else
        {
            data = System.Text.Encoding.UTF8.GetBytes(args[0].ToObject()?.ToString() ?? "");
        }

        // Parse remaining args - detect if offset/length form or direct port form
        bool useConnected = false;
        if (args.Length >= 4 && args[1].IsNumber && args[2].IsNumber && args[3].IsNumber)
        {
            // send(msg, offset, length, port, address?, callback?)
            int offset = (int)args[1].AsNumberUnsafe();
            int length = (int)args[2].AsNumberUnsafe();
            if (offset != 0 || length != data.Length)
            {
                var slice = new byte[length];
                Array.Copy(data, offset, slice, 0, length);
                data = slice;
            }
            port = (int)args[3].AsNumberUnsafe();
            if (args.Length > 4 && args[4].IsString) address = args[4].AsStringUnsafe();
            if (args.Length > 4 && args[4].ToObject() is ISharpTSCallable c4) callback = c4;
            if (args.Length > 5 && args[5].ToObject() is ISharpTSCallable c5) callback = c5;
        }
        else if (_connected && (args.Length < 2 || !args[1].IsNumber))
        {
            // Connected mode: send(msg, callback?)
            useConnected = true;
            if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable c1) callback = c1;
        }
        else
        {
            // send(msg, port, address?, callback?)
            port = args.Length > 1 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
            if (args.Length > 2 && args[2].IsString) address = args[2].AsStringUnsafe();
            if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable c2) callback = c2;
            if (args.Length > 3 && args[3].ToObject() is ISharpTSCallable c3) callback = c3;
        }

        var sendData = data;
        var sendCallback = callback;
        var sendPort = port;
        var sendAddress = address;
        var sendConnected = useConnected;
        var sendClient = _client; // Capture locally so Close() can't null it mid-send

        Task.Run(async () =>
        {
            try
            {
                if (sendConnected)
                {
                    await sendClient.SendAsync(sendData, sendData.Length);
                }
                else
                {
                    var ep = new IPEndPoint(IPAddress.Parse(sendAddress), sendPort);
                    await sendClient.SendAsync(sendData, sendData.Length, ep);
                }
                if (sendCallback != null)
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        sendCallback.Call(interpreter, [null]);
                    }, isInterval: false);
                }
            }
            catch (Exception ex)
            {
                if (sendCallback != null)
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        sendCallback.Call(interpreter, [new SharpTSError(ex.Message)]);
                    }, isInterval: false);
                }
                else
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                    }, isInterval: false);
                }
            }
        });

        return RuntimeValue.Null;
    }

    /// <summary>
    /// Closes the socket.
    /// </summary>
    private RuntimeValue Close(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        ISharpTSCallable? callback = null;
        if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable cb) callback = cb;

        if (_closed) return RuntimeValue.Null;
        _closed = true;

        _receiveCts?.Cancel();
        _client?.Close();
        _client?.Dispose();
        _client = null;

        if (_bound && _interpreter != null)
        {
            _interpreter.Unref();
        }

        var closeInterpreter = interpreter ?? _interpreter;
        if (closeInterpreter != null)
        {
            if (callback != null)
            {
                closeInterpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(closeInterpreter, []);
                }, isInterval: false);
            }

            closeInterpreter.ScheduleTimer(0, 0, () =>
            {
                EmitEvent(closeInterpreter, "close", []);
            }, isInterval: false);
        }

        return RuntimeValue.Null;
    }

    /// <summary>
    /// Returns the address information for the socket.
    /// </summary>
    private RuntimeValue Address(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client?.Client?.LocalEndPoint is IPEndPoint ep)
        {
            return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
            {
                ["address"] = ep.Address.ToString(),
                ["family"] = ep.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
                ["port"] = (double)ep.Port
            }));
        }
        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>()));
    }

    private RuntimeValue SetBroadcast(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client != null && args.Length > 0)
        {
            _client.EnableBroadcast = (args[0].IsBoolean && args[0].AsBooleanUnsafe())
                || (args[0].IsNumber && args[0].AsNumberUnsafe() != 0);
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue SetTTL(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client != null && args.Length > 0 && args[0].IsNumber)
        {
            _client.Ttl = (short)args[0].AsNumberUnsafe();
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue SetMulticastTTL(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client != null && args.Length > 0 && args[0].IsNumber)
        {
            _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, (int)args[0].AsNumberUnsafe());
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue AddMembership(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client != null && args.Length > 0 && args[0].IsString)
        {
            var multicastAddress = args[0].AsStringUnsafe();
            string? localAddress = args.Length > 1 ? args[1].ToObject() as string : null;
            if (localAddress != null)
            {
                _client.JoinMulticastGroup(IPAddress.Parse(multicastAddress), IPAddress.Parse(localAddress));
            }
            else
            {
                _client.JoinMulticastGroup(IPAddress.Parse(multicastAddress));
            }
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue DropMembership(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client != null && args.Length > 0 && args[0].IsString)
        {
            _client.DropMulticastGroup(IPAddress.Parse(args[0].AsStringUnsafe()));
        }
        return RuntimeValue.Null;
    }

    private RuntimeValue Ref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        interpreter.Ref();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Unref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        interpreter.Unref();
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Connects the socket to a remote address. After connect, send() can be called without port/address.
    /// Signature: connect(port, address?, callback?)
    /// </summary>
    private RuntimeValue Connect(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter = interpreter;
        int port = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
        string address = _family == AddressFamily.InterNetworkV6 ? "::1" : "127.0.0.1";
        ISharpTSCallable? callback = null;

        if (args.Length > 1 && args[1].IsString) address = args[1].AsStringUnsafe();
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb1) callback = cb1;
        if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable cb2) callback = cb2;

        if (callback != null)
        {
            Once("connect", callback);
        }

        try
        {
            if (_client == null)
            {
                _client = new UdpClient(_family);
            }
            _client.Connect(IPAddress.Parse(address), port);
            _connectedRemote = new IPEndPoint(IPAddress.Parse(address), port);
            _connected = true;

            interpreter.ScheduleTimer(0, 0, () =>
            {
                EmitEvent(interpreter, "connect", []);
            }, isInterval: false);
        }
        catch (Exception ex)
        {
            interpreter.ScheduleTimer(0, 0, () =>
            {
                EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
            }, isInterval: false);
        }

        return RuntimeValue.Null;
    }

    /// <summary>
    /// Disconnects the socket from a remote address.
    /// </summary>
    private RuntimeValue Disconnect(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_connected)
        {
            throw new Exception("Runtime Error: Not connected");
        }

        try
        {
            _client?.Client.Connect(new IPEndPoint(
                _family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
        }
        catch
        {
            // On macOS/BSD, connecting to Any:0 may throw — socket is still logically disconnected
        }

        _connected = false;
        _connectedRemote = null;

        return RuntimeValue.Null;
    }

    /// <summary>
    /// Returns the remote address info for a connected socket.
    /// </summary>
    private RuntimeValue RemoteAddress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_connected || _connectedRemote == null)
        {
            throw new Exception("Runtime Error: Not connected");
        }

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["address"] = _connectedRemote.Address.ToString(),
            ["family"] = _connectedRemote.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
            ["port"] = (double)_connectedRemote.Port
        }));
    }

    private RuntimeValue GetRecvBufferSize(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client == null)
            throw new Exception("Runtime Error: Socket is not bound");
        return RuntimeValue.FromNumber(_client.Client.ReceiveBufferSize);
    }

    private RuntimeValue SetRecvBufferSize(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client == null)
            throw new Exception("Runtime Error: Socket is not bound");
        if (args.Length > 0 && args[0].IsNumber)
            _client.Client.ReceiveBufferSize = (int)args[0].AsNumberUnsafe();
        return RuntimeValue.Null;
    }

    private RuntimeValue GetSendBufferSize(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client == null)
            throw new Exception("Runtime Error: Socket is not bound");
        return RuntimeValue.FromNumber(_client.Client.SendBufferSize);
    }

    private RuntimeValue SetSendBufferSize(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client == null)
            throw new Exception("Runtime Error: Socket is not bound");
        if (args.Length > 0 && args[0].IsNumber)
            _client.Client.SendBufferSize = (int)args[0].AsNumberUnsafe();
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Starts the async receive loop.
    /// </summary>
    private void StartReceiving(Interp interpreter)
    {
        if (_client == null) return;

        _receiveCts = new CancellationTokenSource();
        var token = _receiveCts.Token;
        var client = _client;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && client.Client != null)
                {
                    var result = await client.ReceiveAsync(token);
                    var msgBuffer = new SharpTSBuffer(result.Buffer);
                    var rinfo = new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["address"] = result.RemoteEndPoint.Address.ToString(),
                        ["family"] = result.RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
                        ["port"] = (double)result.RemoteEndPoint.Port,
                        ["size"] = (double)result.Buffer.Length
                    });

                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        EmitEvent(interpreter, "message", [msgBuffer, rinfo]);
                    }, isInterval: false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (ObjectDisposedException)
            {
                // Socket was closed
            }
            catch (SocketException ex)
            {
                if (!_closed)
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                    }, isInterval: false);
                }
            }
        }, token);
    }

    private void Once(string eventName, ISharpTSCallable callback)
    {
        var onceMethod = base.GetMember("once") as BuiltInMethod;
        onceMethod?.Bind(this).Call(null!, new List<object?> { eventName, callback });
    }
}
