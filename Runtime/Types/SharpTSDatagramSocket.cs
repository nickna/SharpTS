using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
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
            "addSourceSpecificMembership" => BuiltInMethod.CreateV2("addSourceSpecificMembership", 2, 3, AddSourceSpecificMembership),
            "dropSourceSpecificMembership" => BuiltInMethod.CreateV2("dropSourceSpecificMembership", 2, 3, DropSourceSpecificMembership),
            "setMulticastLoopback" => BuiltInMethod.CreateV2("setMulticastLoopback", 1, SetMulticastLoopback),
            "setMulticastInterface" => BuiltInMethod.CreateV2("setMulticastInterface", 1, SetMulticastInterface),
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
            // An explicit destination on a connected socket is an error (Node).
            if (_connected)
                throw new NodeError("ERR_SOCKET_DGRAM_IS_CONNECTED", "Already connected");
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
        else if (_connected)
        {
            // Connected mode: send(msg[, offset, length][, callback]) — an explicit
            // port/address is rejected (ERR_SOCKET_DGRAM_IS_CONNECTED, Node semantics).
            useConnected = true;
            if (args.Length > 2 && args[1].IsNumber && args[2].IsNumber)
            {
                // send(msg, offset, length[, callback]) — legal while connected
                int offset = (int)args[1].AsNumberUnsafe();
                int length = (int)args[2].AsNumberUnsafe();
                if (offset != 0 || length != data.Length)
                {
                    var slice = new byte[length];
                    Array.Copy(data, offset, slice, 0, length);
                    data = slice;
                }
                if (args.Length > 3 && args[3].ToObject() is ISharpTSCallable c3c) callback = c3c;
            }
            else if (args.Length > 1 && args[1].IsNumber)
            {
                throw new NodeError("ERR_SOCKET_DGRAM_IS_CONNECTED", "Already connected");
            }
            else if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable c1)
            {
                callback = c1;
            }
        }
        else
        {
            // send(msg, port, address?, callback?) — the destination port is
            // required on an unconnected socket (Node semantics).
            if (args.Length < 2 || !args[1].IsNumber)
                throw new NodeError("ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");
            port = (int)args[1].AsNumberUnsafe();
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
            var multicastAddress = IPAddress.Parse(args[0].AsStringUnsafe());
            // Node: dropMembership(multicastAddress[, multicastInterface]). The
            // interface matters: Linux requires IP_DROP_MEMBERSHIP to match the
            // join's (group, interface) tuple — dropping a 127.0.0.1-scoped join
            // with INADDR_ANY throws there (Windows matches by group alone).
            var localAddress = args.Length > 1 && args[1].IsString
                ? IPAddress.Parse(args[1].AsStringUnsafe())
                : null;
            if (localAddress != null && _family == AddressFamily.InterNetwork)
            {
                _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DropMembership,
                    new MulticastOption(multicastAddress, localAddress));
            }
            else
            {
                _client.DropMulticastGroup(multicastAddress);
            }
        }
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Lazily creates the UDP handle so pre-bind option setters work (Node's
    /// handle exists from createSocket; ours is created on first use).
    /// </summary>
    private UdpClient EnsureClient()
    {
        if (_closed)
            throw new NodeError("ERR_SOCKET_DGRAM_NOT_RUNNING", "Not running");
        return _client ??= new UdpClient(_family);
    }

    /// <summary>
    /// addSourceSpecificMembership(sourceAddress, groupAddress[, multicastInterface]) (#1071)
    /// </summary>
    private RuntimeValue AddSourceSpecificMembership(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => SourceSpecificMembership(args, add: true);

    /// <summary>
    /// dropSourceSpecificMembership(sourceAddress, groupAddress[, multicastInterface]) (#1071)
    /// </summary>
    private RuntimeValue DropSourceSpecificMembership(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => SourceSpecificMembership(args, add: false);

    private RuntimeValue SourceSpecificMembership(ReadOnlySpan<RuntimeValue> args, bool add)
    {
        if (args.Length < 2 || !args[0].IsString || !args[1].IsString)
            throw new NodeError("ERR_INVALID_ARG_TYPE", "The \"sourceAddress\" and \"groupAddress\" arguments must be of type string");

        if (_family == AddressFamily.InterNetworkV6)
        {
            // .NET has no portable MCAST_JOIN_SOURCE_GROUP mapping — documented ceiling.
            throw new NodeError("ERR_INVALID_ARG_VALUE",
                "Source-specific multicast is not supported for udp6 sockets on this runtime");
        }

        var source = IPAddress.Parse(args[0].AsStringUnsafe());
        var group = IPAddress.Parse(args[1].AsStringUnsafe());
        var iface = args.Length > 2 && args[2].IsString
            ? IPAddress.Parse(args[2].AsStringUnsafe())
            : IPAddress.Any;

        // ip_mreq_source is 3 in_addr fields whose ORDER differs by platform:
        // Windows: { multiaddr, sourceaddr, interface }; Linux/macOS: { multiaddr, interface, sourceaddr }.
        var mreq = new byte[12];
        group.GetAddressBytes().CopyTo(mreq, 0);
        if (OperatingSystem.IsWindows())
        {
            source.GetAddressBytes().CopyTo(mreq, 4);
            iface.GetAddressBytes().CopyTo(mreq, 8);
        }
        else
        {
            iface.GetAddressBytes().CopyTo(mreq, 4);
            source.GetAddressBytes().CopyTo(mreq, 8);
        }

        EnsureClient().Client.SetSocketOption(
            SocketOptionLevel.IP,
            add ? SocketOptionName.AddSourceMembership : SocketOptionName.DropSourceMembership,
            mreq);
        return RuntimeValue.Null;
    }

    /// <summary>
    /// setMulticastLoopback(flag) (#1071): UdpClient.MulticastLoopback picks the
    /// right option level (IP vs IPv6) from the socket family.
    /// </summary>
    private RuntimeValue SetMulticastLoopback(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var flag = (args[0].IsBoolean && args[0].AsBooleanUnsafe())
            || (args[0].IsNumber && args[0].AsNumberUnsafe() != 0);
        EnsureClient().MulticastLoopback = flag;
        return RuntimeValue.Null;
    }

    /// <summary>
    /// setMulticastInterface(multicastInterface) (#1071): IPv4 takes the interface
    /// address; IPv6 takes the scope id of an "::%N"-style address.
    /// </summary>
    private RuntimeValue SetMulticastInterface(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!args[0].IsString)
            throw new NodeError("ERR_INVALID_ARG_TYPE", "The \"multicastInterface\" argument must be of type string");
        var ip = IPAddress.Parse(args[0].AsStringUnsafe());
        var client = EnsureClient();
        if (_family == AddressFamily.InterNetworkV6)
        {
            client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, (int)ip.ScopeId);
        }
        else
        {
            client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ip.GetAddressBytes());
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
            throw new NodeError("ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");
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
            throw new NodeError("ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");
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
