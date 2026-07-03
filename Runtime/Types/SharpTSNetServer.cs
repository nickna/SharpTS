using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js net.Server.
/// Extends SharpTSEventEmitter for full event handling support.
/// Events: 'connection', 'listening', 'close', 'error'
/// </summary>
public class SharpTSNetServer : SharpTSEventEmitter, IDisposable
{
    /// <inheritdoc />
    public override TypeCategory RuntimeCategory => TypeCategory.Record;

    private TcpListener? _listener;
    private bool _isListening;
    private Interp? _interpreter;
    private CancellationTokenSource? _cts;
    private ISharpTSCallable? _connectionListener;
    private int _port;
    private string _host = "0.0.0.0";
    private readonly List<SharpTSSocket> _connections = [];
    private int _maxConnections = int.MaxValue;
    private bool _isIpc;
    private string? _pipePath;
    private Socket? _unixSocket;
    private bool _isClusterWorker;
    private int? _socketHighWaterMark;
    private SharpTSBlockList? _blockList;
    private bool _socketAllowHalfOpen;

    /// <summary>
    /// Applies createServer(options) settings that configure accepted sockets.
    /// </summary>
    internal void ConfigureFromOptions(SharpTSObject options)
    {
        if (options.GetProperty("highWaterMark") is double hwm && hwm >= 0)
            _socketHighWaterMark = (int)hwm;
        if (options.GetProperty("blockList") is SharpTSBlockList blockList)
            _blockList = blockList;
        if (options.GetProperty("allowHalfOpen") is bool aho)
            _socketAllowHalfOpen = aho;
    }

    /// <summary>
    /// Applies per-server socket settings to a freshly accepted connection, and
    /// hooks the socket's close so the connection list tracks live sockets
    /// (maxConnections / getConnections accuracy, #1070).
    /// </summary>
    private void ConfigureAcceptedSocket(SharpTSSocket socket)
    {
        if (_socketHighWaterMark is int hwm)
            socket._writableHighWaterMark = hwm;
        socket._allowHalfOpen = _socketAllowHalfOpen;
        socket.OnClosed = () => _connections.Remove(socket);
    }

    /// <summary>
    /// Creates a new TCP server with an optional connection listener.
    /// </summary>
    public SharpTSNetServer(ISharpTSCallable? connectionListener = null)
    {
        _connectionListener = connectionListener;
    }

    /// <summary>
    /// Creates a new TCP server from a compiled-mode callback (e.g. $TSFunction).
    /// Activator.CreateInstance matches this overload when the argument is not ISharpTSCallable.
    /// </summary>
    public SharpTSNetServer(object? connectionListener)
        : this(connectionListener as ISharpTSCallable
               ?? (connectionListener != null
                   ? TSFunctionCallableAdapter.WrapCallback(connectionListener)
                   : null))
    { }

    /// <summary>
    /// Whether the server is currently listening.
    /// </summary>
    public bool Listening => _isListening;

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "listening" => Listening,
            "listen" => BuiltInMethod.CreateV2("listen", 0, 4, Listen),
            "close" => BuiltInMethod.CreateV2("close", 0, 1, Close),
            "address" => BuiltInMethod.CreateV2("address", 0, GetAddress),
            "getConnections" => BuiltInMethod.CreateV2("getConnections", 1, GetConnections),
            "ref" => BuiltInMethod.CreateV2("ref", 0, Ref),
            "unref" => BuiltInMethod.CreateV2("unref", 0, Unref),
            "maxConnections" => (double)_maxConnections,
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Sets a member by name.
    /// </summary>
    public void SetMember(string name, object? value)
    {
        if (name == "maxConnections" && value is double d)
            _maxConnections = (int)d;
    }

    /// <summary>
    /// Starts listening on the specified port or IPC path.
    /// </summary>
    private RuntimeValue Listen(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_isListening)
            throw new Exception("Runtime Error: Server is already listening");

        _interpreter = interpreter;

        // Parse arguments: listen(port?, host?, backlog?, callback?)
        // or listen(options, callback?) or listen(path, callback?) for IPC
        ISharpTSCallable? callback = null;

        // Detect IPC path: string first arg or options.path
        if (args.Length > 0 && args[0].IsString)
        {
            callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
            return RuntimeValue.FromBoxed(ListenIpc(interpreter, args[0].AsStringUnsafe(), callback));
        }

        if (args.Length > 0 && args[0].ToObject() is SharpTSObject options)
        {
            if (options.GetProperty("path") is string optPath)
            {
                callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
                return RuntimeValue.FromBoxed(ListenIpc(interpreter, optPath, callback));
            }
            if (options.GetProperty("port") is double p) _port = (int)p;
            if (options.GetProperty("host") is string h) _host = h;
            callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
        }
        else
        {
            int argIdx = 0;
            if (argIdx < args.Length && args[argIdx].IsNumber)
            {
                _port = (int)args[argIdx].AsNumberUnsafe();
                argIdx++;
            }
            if (argIdx < args.Length && args[argIdx].IsString)
            {
                _host = args[argIdx].AsStringUnsafe();
                argIdx++;
            }
            // Skip backlog (number)
            if (argIdx < args.Length && args[argIdx].IsNumber)
                argIdx++;
            if (argIdx < args.Length)
                callback = WrapCallbackArg(args[argIdx].ToObject());
            // Also check: listen(port, callback) where callback is arg[1]
            if (callback == null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var wrapped = WrapCallbackArg(args[i].ToObject());
                    if (wrapped != null) { callback = wrapped; break; }
                }
            }
        }

        // Cluster worker mode: register with shared listener instead of binding directly
        if (ClusterContext.IsWorker)
            return RuntimeValue.FromBoxed(ListenAsClusterWorker(interpreter, callback));

        var ipAddress = _host == "0.0.0.0" || _host == "::"
            ? IPAddress.Any
            : IPAddress.TryParse(_host, out var parsed) ? parsed : IPAddress.Loopback;

        _listener = new TcpListener(ipAddress, _port);

        try
        {
            _listener.Start();
        }
        catch (Exception ex)
        {
            EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
            return RuntimeValue.FromObject(this);
        }

        // If port was 0, get the assigned port
        if (_port == 0)
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _isListening = true;
        _cts = new CancellationTokenSource();

        // Keep event loop alive
        interpreter.Ref();

        // Emit 'listening' event and call callback
        if (callback != null)
            callback.Call(interpreter, []);
        EmitEvent(interpreter, "listening", []);

        // Start accepting connections
        StartAccepting(interpreter);

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Listens on an IPC path (named pipe on Windows, Unix domain socket on Linux/macOS).
    /// </summary>
    private object? ListenIpc(Interp interpreter, string path, ISharpTSCallable? callback)
    {
        _isIpc = true;
        _pipePath = path;
        _cts = new CancellationTokenSource();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                _isListening = true;
                interpreter.Ref();

                // Start accept loop and wait until the first pipe is ready before
                // calling the callback (which may create a client that connects immediately).
                var pipeReady = new ManualResetEventSlim(false);
                StartAcceptingIpcWindows(interpreter, pipeReady);
                pipeReady.Wait(5000); // Wait up to 5s for pipe to be ready

                callback?.Call(interpreter, []);
                EmitEvent(interpreter, "listening", []);
            }
            else
            {
                // Unix domain socket
                // Delete stale socket file (standard Node.js behavior)
                if (File.Exists(path))
                    File.Delete(path);

                _unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _unixSocket.Bind(new UnixDomainSocketEndPoint(path));
                _unixSocket.Listen(511);

                _isListening = true;
                interpreter.Ref();

                callback?.Call(interpreter, []);
                EmitEvent(interpreter, "listening", []);

                StartAcceptingIpcUnix(interpreter);
            }
        }
        catch (Exception ex)
        {
            var error = SharpTSSocket.CreateSocketError(ex, "listen", path);
            EmitEvent(interpreter, "error", [error]);
        }

        return this;
    }

    /// <summary>
    /// Windows named pipe accept loop.
    /// Signals pipeReady once the first WaitForConnectionAsync is pending,
    /// so the caller can safely invoke the callback (which may create clients).
    /// </summary>
    private void StartAcceptingIpcWindows(Interp interpreter, ManualResetEventSlim pipeReady)
    {
        var token = _cts!.Token;
        var pipeName = SharpTSSocket.ConvertToWindowsPipeName(_pipePath!);

        _ = Task.Run(async () =>
        {
            NamedPipeServerStream? currentPipe = null;
            bool first = true;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    currentPipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    // Start the async wait BEFORE signaling ready.
                    // This ensures the overlapped I/O is pending when
                    // the callback creates a client that connects immediately.
                    var waitTask = currentPipe.WaitForConnectionAsync(token);

                    if (first)
                    {
                        first = false;
                        pipeReady.Set();
                    }

                    await waitTask;

                    if (_connections.Count >= _maxConnections)
                    {
                        currentPipe.Dispose();
                        currentPipe = null;
                        continue;
                    }

                    var acceptedPipe = currentPipe;
                    currentPipe = null; // Ownership transferred to the socket
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        var socket = new SharpTSSocket(acceptedPipe, _pipePath!);
                        ConfigureAcceptedSocket(socket);
                        _connections.Add(socket);
                        // For IPC: start reading BEFORE user callback so writes
                        // don't block (Windows InOut pipes need a pending reader)
                        var readReady = new ManualResetEventSlim(false);
                        socket.StartReading(interpreter, readReady);
                        readReady.Wait(5000);
                        _connectionListener?.Call(interpreter, [socket]);
                        EmitEvent(interpreter, "connection", [socket]);
                    }, isInterval: false);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (IOException) { break; }
                finally
                {
                    // Dispose pipe if ownership was not transferred
                    currentPipe?.Dispose();
                    currentPipe = null;
                }
            }
        }, token);
    }

    /// <summary>
    /// Unix domain socket accept loop.
    /// </summary>
    private void StartAcceptingIpcUnix(Interp interpreter)
    {
        var token = _cts!.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _unixSocket != null)
            {
                Socket? clientSocket = null;
                try
                {
                    clientSocket = await _unixSocket.AcceptAsync(token);

                    if (_connections.Count >= _maxConnections)
                    {
                        clientSocket.Close();
                        clientSocket = null;
                        continue;
                    }

                    var accepted = clientSocket;
                    clientSocket = null; // Ownership transferred to the closure
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        var stream = new NetworkStream(accepted, ownsSocket: true);
                        var socket = new SharpTSSocket(stream, _pipePath!);
                        ConfigureAcceptedSocket(socket);
                        _connections.Add(socket);
                        socket.StartReading(interpreter);
                        _connectionListener?.Call(interpreter, [socket]);
                        EmitEvent(interpreter, "connection", [socket]);
                    }, isInterval: false);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }
                finally
                {
                    // Dispose socket if ownership was not transferred
                    clientSocket?.Dispose();
                }
            }
        }, token);
    }

    /// <summary>
    /// Listens as a cluster worker by registering with the shared listener registry.
    /// </summary>
    private object? ListenAsClusterWorker(Interp interpreter, ISharpTSCallable? callback)
    {
        _isClusterWorker = true;
        var registry = ClusterSingleton.Instance.SharedListeners;

        registry.RegisterTcpWorker(_port, _host, ClusterContext.WorkerId, tcpClient =>
        {
            interpreter.ScheduleTimer(0, 0, () =>
            {
                var socket = new SharpTSSocket(tcpClient);
                ConfigureAcceptedSocket(socket);
                _connections.Add(socket);
                _connectionListener?.Call(interpreter, [socket]);
                EmitEvent(interpreter, "connection", [socket]);
                socket.StartReading(interpreter);
            }, isInterval: false);
        }, interpreter);

        _isListening = true;
        interpreter.Ref();

        callback?.Call(interpreter, []);
        EmitEvent(interpreter, "listening", []);

        // Notify the primary so cluster.on('listening', (worker, address)) fires (#1168).
        ClusterContext.CurrentWorker?.NotifyListening(_host, _port, _host.Contains(':') ? 6 : 4);

        return this;
    }

    /// <summary>
    /// Starts accepting connections asynchronously.
    /// </summary>
    private void StartAccepting(Interp interpreter)
    {
        var token = _cts!.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(token);

                    // BlockList rejection: close silently, no event (Node semantics).
                    if (_blockList != null
                        && tcpClient.Client.RemoteEndPoint is IPEndPoint remote
                        && _blockList.IsBlocked(remote.Address))
                    {
                        tcpClient.Close();
                        continue;
                    }

                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        // maxConnections gate on the loop thread, where _connections
                        // is mutated — a refused connection emits 'drop' with the
                        // Node data shape, then closes (#1070).
                        if (_connections.Count >= _maxConnections)
                        {
                            EmitEvent(interpreter, "drop", [BuildDropData(tcpClient)]);
                            try { tcpClient.Close(); } catch { }
                            return;
                        }

                        var socket = new SharpTSSocket(tcpClient);
                        ConfigureAcceptedSocket(socket);
                        _connections.Add(socket);

                        // Call connection listener if set
                        _connectionListener?.Call(interpreter, [socket]);

                        // Emit 'connection' event
                        EmitEvent(interpreter, "connection", [socket]);

                        // Start reading on the socket
                        socket.StartReading(interpreter);
                    }, isInterval: false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }, token);
    }

    /// <summary>
    /// Builds the 'drop' event payload: local/remote endpoint triads (Node shape).
    /// </summary>
    private static SharpTSObject BuildDropData(TcpClient tcpClient)
    {
        var data = new Dictionary<string, object?>();
        try
        {
            if (tcpClient.Client.LocalEndPoint is IPEndPoint local)
            {
                data["localAddress"] = local.Address.ToString();
                data["localPort"] = (double)local.Port;
                data["localFamily"] = local.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
            }
            if (tcpClient.Client.RemoteEndPoint is IPEndPoint remote)
            {
                data["remoteAddress"] = remote.Address.ToString();
                data["remotePort"] = (double)remote.Port;
                data["remoteFamily"] = remote.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
            }
        }
        catch
        {
            // Endpoint may be unavailable if the peer already disconnected
        }
        return new SharpTSObject(data);
    }

    /// <summary>
    /// Closes the server.
    /// </summary>
    private RuntimeValue Close(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_isListening)
            return RuntimeValue.FromObject(this);

        _cts?.Cancel();

        if (_isClusterWorker)
        {
            ClusterSingleton.Instance.SharedListeners.UnregisterTcpWorker(_port, ClusterContext.WorkerId);
        }
        else if (_isIpc)
        {
            // IPC cleanup
            try { _unixSocket?.Close(); } catch { }
            // Delete Unix socket file
            if (!OperatingSystem.IsWindows() && _pipePath != null && File.Exists(_pipePath))
            {
                try { File.Delete(_pipePath); } catch { }
            }
        }
        else
        {
            try { _listener?.Stop(); } catch { }
        }

        _isListening = false;
        _interpreter?.Unref();

        ISharpTSCallable? callback = args.Length > 0 ? WrapCallbackArg(args[0].ToObject()) : null;
        callback?.Call(interpreter, []);

        EmitEvent(interpreter, "close", []);

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Gets the server address information.
    /// </summary>
    private RuntimeValue GetAddress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_isListening) return RuntimeValue.Null;

        if (_isIpc)
        {
            // Node.js returns the pipe path as a string for IPC servers
            return RuntimeValue.FromBoxed(_pipePath);
        }

        if (_isClusterWorker)
        {
            return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
            {
                ["address"] = _host,
                ["family"] = "IPv4",
                ["port"] = (double)_port
            }));
        }

        if (_listener == null) return RuntimeValue.Null;

        var ep = (IPEndPoint)_listener.LocalEndpoint;
        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["address"] = ep.Address.ToString(),
            ["family"] = ep.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
            ["port"] = (double)ep.Port
        }));
    }

    /// <summary>
    /// Gets the number of concurrent connections.
    /// </summary>
    private RuntimeValue GetConnections(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable callback)
        {
            callback.Call(interpreter, [null, (double)_connections.Count]);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Ref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Ref();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Unref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Unref();
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Wraps a callback argument that may be a compiled-mode $TSFunction into ISharpTSCallable.
    /// Returns null if the argument is not callable (e.g. a string or number).
    /// </summary>
    private static ISharpTSCallable? WrapCallbackArg(object? arg)
    {
        if (arg == null || arg is string || arg is double || arg is bool) return null;
        if (arg is ISharpTSCallable callable) return callable;
        return TSFunctionCallableAdapter.WrapCallback(arg);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        try { _unixSocket?.Close(); } catch { }
        if (!OperatingSystem.IsWindows() && _isIpc && _pipePath != null && File.Exists(_pipePath))
        {
            try { File.Delete(_pipePath); } catch { }
        }
        _cts?.Dispose();
        foreach (var conn in _connections)
        {
            try { conn.GetMember("destroy"); } catch { }
        }
        _connections.Clear();
    }

    public override string ToString() => $"Server {{ listening: {Listening} }}";
}
