using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js net.Socket.
/// Extends SharpTSDuplex for full duplex stream semantics (Readable + Writable).
/// </summary>
public class SharpTSSocket : SharpTSEventEmitter
{
    protected internal TcpClient? _client;
    protected internal Stream? _stream;
    protected internal Interp? _interpreter;
    private bool _connecting;
    protected internal bool _destroyed;
    private bool _ended;
    private bool _closeEmitted;
    private int _bytesRead;
    private int _bytesWritten;
    private CancellationTokenSource? _readCts;
    protected internal string _encoding = "utf8";
    protected internal bool _readingStarted;
    internal bool _isIpc;
    internal string? _pipePath;

    /// <summary>
    /// Creates a new Socket wrapping an existing TcpClient (server-side).
    /// </summary>
    public SharpTSSocket(TcpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
    }

    /// <summary>
    /// Creates a new unconnected Socket (client-side).
    /// </summary>
    public SharpTSSocket()
    {
    }

    /// <summary>
    /// Creates a new Socket wrapping an IPC pipe stream (server-accepted IPC sockets).
    /// </summary>
    public SharpTSSocket(Stream pipeStream, string pipePath)
    {
        _stream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
        _isIpc = true;
        _pipePath = pipePath;
    }

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // Methods
            "connect" => BuiltInMethod.CreateV2("connect", 1, 3, Connect),
            "write" => BuiltInMethod.CreateV2("write", 1, 3, Write),
            "end" => BuiltInMethod.CreateV2("end", 0, 3, End),
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, Destroy),
            "setEncoding" => BuiltInMethod.CreateV2("setEncoding", 1, SetEncoding),
            "setTimeout" => BuiltInMethod.CreateV2("setTimeout", 1, 2, SetTimeout),
            "setNoDelay" => BuiltInMethod.CreateV2("setNoDelay", 0, 1, SetNoDelay),
            "setKeepAlive" => BuiltInMethod.CreateV2("setKeepAlive", 0, 2, SetKeepAlive),
            "address" => BuiltInMethod.CreateV2("address", 0, Address),
            "ref" => BuiltInMethod.CreateV2("ref", 0, Ref),
            "unref" => BuiltInMethod.CreateV2("unref", 0, Unref),
            "pause" => BuiltInMethod.CreateV2("pause", 0, Pause),
            "resume" => BuiltInMethod.CreateV2("resume", 0, Resume),
            "pipe" => BuiltInMethod.CreateV2("pipe", 1, 2, Pipe),

            // Properties
            "remoteAddress" => _isIpc ? (object?)null : (_client?.Client?.RemoteEndPoint is IPEndPoint rep ? rep.Address.ToString() : null),
            "remotePort" => _isIpc ? (object?)null : (_client?.Client?.RemoteEndPoint is IPEndPoint rep2 ? (double)rep2.Port : null),
            "remoteFamily" => _isIpc ? "pipe" : (_client?.Client?.RemoteEndPoint is IPEndPoint rep3 ? (rep3.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4") : null),
            "localAddress" => _isIpc ? (object?)null : (_client?.Client?.LocalEndPoint is IPEndPoint lep ? lep.Address.ToString() : null),
            "localPort" => _isIpc ? (object?)null : (_client?.Client?.LocalEndPoint is IPEndPoint lep2 ? (double)lep2.Port : null),
            "bytesRead" => (double)_bytesRead,
            "bytesWritten" => (double)_bytesWritten,
            "connecting" => _connecting,
            "destroyed" => _destroyed,
            "readyState" => GetReadyState(),

            // EventEmitter methods
            _ => base.GetMember(name)
        };
    }

    private string GetReadyState()
    {
        if (_connecting) return "opening";
        if (_destroyed) return "closed";
        if (_isIpc) return _stream != null ? "open" : "closed";
        if (_client?.Connected == true) return "open";
        return "closed";
    }

    /// <summary>
    /// Connects to a remote host or IPC pipe.
    /// </summary>
    private RuntimeValue Connect(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter = interpreter;

        // Detect IPC path: string first arg or options.path
        string? ipcPath = null;
        ISharpTSCallable? callback = null;
        var arg0 = args[0].ToObject();

        if (arg0 is string pathArg)
        {
            ipcPath = pathArg;
            callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
        }
        else if (arg0 is SharpTSObject options && options.GetProperty("path") is string optPath)
        {
            ipcPath = optPath;
            callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
        }

        if (ipcPath != null)
        {
            return RuntimeValue.FromBoxed(ConnectIpc(interpreter, ipcPath, callback));
        }

        int port;
        string host = "localhost";

        if (arg0 is SharpTSObject opts)
        {
            port = (int)(double)(opts.GetProperty("port") ?? throw new Exception("Runtime Error: port is required"));
            if (opts.GetProperty("host") is string h) host = h;
            callback = args.Length > 1 ? WrapCallbackArg(args[1].ToObject()) : null;
        }
        else if (arg0 is double portNum)
        {
            port = (int)portNum;
            if (args.Length > 1 && args[1].ToObject() is string h) host = h;
            if (args.Length > 1) callback = WrapCallbackArg(args[1].ToObject());
            if (args.Length > 2) callback = WrapCallbackArg(args[2].ToObject()) ?? callback;
        }
        else
        {
            throw new Exception("Runtime Error: connect requires port number, path string, or options object");
        }

        if (callback != null)
        {
            AddListenerDirect("connect", callback);
        }

        _connecting = true;
        _client = new TcpClient();

        // An in-flight connect is an active handle (Node semantics): keep the event
        // loop alive until the connect resolves, otherwise its 'connect'/'error'
        // continuation can be dropped if the loop's other handles (e.g. the peer
        // server) drain to zero first. Released exactly once when the connect
        // settles, inside the scheduled callback so the handle outlives delivery.
        interpreter.Ref();

        // Start async connect via Task.Run
        var capturedHost = host;
        var capturedPort = port;
        _ = Task.Run(async () =>
        {
            try
            {
                await _client.ConnectAsync(capturedHost, capturedPort);
                _stream = _client.GetStream();
                _connecting = false;
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    EmitEvent(interpreter, "connect", []);
                    StartReading(interpreter);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                _connecting = false;
                var error = CreateSocketError(ex, "connect", $"{capturedHost}:{capturedPort}");
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    EmitEvent(interpreter, "error", [error]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Connects to an IPC pipe/Unix domain socket.
    /// </summary>
    private object? ConnectIpc(Interp interpreter, string path, ISharpTSCallable? callback)
    {
        if (callback != null)
        {
            AddListenerDirect("connect", callback);
        }

        _connecting = true;
        _isIpc = true;
        _pipePath = path;

        // In-flight connect is an active handle until it settles — see Connect().
        interpreter.Ref();

        _ = Task.Run(async () =>
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipeName = ConvertToWindowsPipeName(path);
                    // Node raises ENOENT immediately for a missing pipe. NamedPipeClientStream's
                    // timed Connect cannot distinguish "missing" from "busy" — it retries
                    // CreateFile until the timeout expires — so pre-check existence and keep
                    // the 5s budget only for the exists-but-busy case it is actually for.
                    if (!WindowsPipeExists(pipeName))
                        throw new FileNotFoundException($"no such named pipe '{path}'");
                    var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipeClient.ConnectAsync(5000);
                    _stream = pipeClient;
                }
                else
                {
                    var unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await unixSocket.ConnectAsync(new UnixDomainSocketEndPoint(path));
                    _stream = new NetworkStream(unixSocket, ownsSocket: true);
                }

                _connecting = false;
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    // For IPC: start reading BEFORE emitting 'connect' so the
                    // server can write immediately (Windows InOut pipe requirement)
                    var readReady = new ManualResetEventSlim(false);
                    StartReading(interpreter, readReady);
                    readReady.Wait(5000);
                    EmitEvent(interpreter, "connect", []);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                _connecting = false;
                var error = CreateSocketError(ex, "connect", path);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    EmitEvent(interpreter, "error", [error]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return this;
    }

    /// <summary>
    /// Creates a SharpTSError with Node.js-compatible code and syscall properties.
    /// </summary>
    internal static SharpTSError CreateSocketError(Exception ex, string syscall, string? address = null)
    {
        var code = ex switch
        {
            FileNotFoundException => "ENOENT",
            DirectoryNotFoundException => "ENOENT",
            TimeoutException => "ENOENT", // Named pipe connect timeout = no server listening
            SocketException se when se.SocketErrorCode == SocketError.ConnectionRefused => "ECONNREFUSED",
            SocketException se when se.SocketErrorCode == SocketError.AddressAlreadyInUse => "EADDRINUSE",
            SocketException se when se.SocketErrorCode == SocketError.AddressNotAvailable => "EADDRNOTAVAIL",
            SocketException se when se.SocketErrorCode == SocketError.TimedOut => "ETIMEDOUT",
            IOException when ex.InnerException is SocketException inner
                && inner.SocketErrorCode == SocketError.ConnectionRefused => "ECONNREFUSED",
            _ => "ECONNREFUSED"
        };

        var msg = address != null
            ? $"{code}: {ex.Message}, {syscall} {address}"
            : $"{code}: {ex.Message}, {syscall}";

        return new SharpTSError(msg) { Code = code, Syscall = syscall };
    }

    /// <summary>
    /// Checks whether a Windows named pipe currently exists. Enumerating the pipe
    /// directory is the safe probe — CreateFile-based checks (File.Exists) can
    /// consume a pipe instance or interfere with WaitNamedPipe semantics.
    /// Returns true on enumeration failure so the connect timeout still governs.
    /// </summary>
    internal static bool WindowsPipeExists(string pipeName)
    {
        try
        {
            var fullPath = @"\\.\pipe\" + pipeName;
            foreach (var entry in Directory.EnumerateFiles(@"\\.\pipe\"))
            {
                if (string.Equals(entry, fullPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Converts a path to a Windows named pipe name.
    /// If already in \\.\pipe\name format, extracts the pipe name.
    /// Otherwise uses the filename portion of the path.
    /// </summary>
    internal static string ConvertToWindowsPipeName(string path)
    {
        if (path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            return path.Substring(@"\\.\pipe\".Length);
        // Use just the filename
        return Path.GetFileName(path);
    }

    /// <summary>
    /// Writes data to the socket.
    /// </summary>
    private RuntimeValue Write(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed || _stream == null)
        {
            EmitEvent(interpreter, "error", [new SharpTSError("This socket has been ended by the other party")]);
            return RuntimeValue.False;
        }

        var chunk = args[0].ToObject();
        string? encoding = null;
        ISharpTSCallable? callback = null;

        if (args.Length > 1)
        {
            if (args[1].IsString) encoding = args[1].AsStringUnsafe();
            else if (args[1].ToObject() is ISharpTSCallable cb) callback = cb;
        }
        if (args.Length > 2 && args[2].ToObject() is ISharpTSCallable cb2) callback = cb2;

        byte[] data = ChunkToBytes(chunk, encoding ?? _encoding);
        _interpreter = interpreter;

        if (_isIpc)
        {
            // IPC pipes (Windows InOut) deadlock when sync Write blocks the event loop
            // because the reader may not have started yet. Write asynchronously.
            _ = Task.Run(() =>
            {
                try
                {
                    _stream!.Write(data, 0, data.Length);
                    _bytesWritten += data.Length;
                    if (callback != null)
                    {
                        interpreter.ScheduleTimer(0, 0, () => callback.Call(interpreter, []), false);
                    }
                }
                catch (Exception ex)
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                    }, false);
                }
            });
            return RuntimeValue.True;
        }

        try
        {
            _stream.Write(data, 0, data.Length);
            _bytesWritten += data.Length;
            callback?.Call(interpreter, []);
            return RuntimeValue.True;
        }
        catch (Exception ex)
        {
            EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
            return RuntimeValue.False;
        }
    }

    /// <summary>
    /// Ends the writable side of the socket.
    /// </summary>
    private RuntimeValue End(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_ended) return RuntimeValue.FromObject(this);

        // Write final chunk if provided
        if (args.Length > 0 && args[0].ToObject() is { } first && first is not ISharpTSCallable)
        {
            Write(interpreter, receiver, args);
        }

        _ended = true;

        if (_isIpc)
        {
            // Named pipes don't support half-close; close the stream entirely
            try
            {
                _stream?.Close();
                _stream = null;
            }
            catch
            {
                // May already be closed
            }
        }
        else
        {
            try
            {
                _client?.Client?.Shutdown(SocketShutdown.Send);
            }
            catch
            {
                // May already be disconnected
            }
        }

        ISharpTSCallable? callback = null;
        foreach (var arg in args)
        {
            if (arg.ToObject() is ISharpTSCallable cb) { callback = cb; break; }
        }
        callback?.Call(interpreter, []);

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Destroys the socket.
    /// </summary>
    private RuntimeValue Destroy(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed) return RuntimeValue.FromObject(this);

        _destroyed = true;
        _readCts?.Cancel();

        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch
        {
            // Ignore close errors
        }

        bool hadError = args.Length > 0 && args[0].ToObject() is not null;
        if (hadError)
        {
            EmitEvent(interpreter, "error", [args[0].ToObject()]);
        }

        EmitClose(interpreter, hadError);
        // Only unref if reading was started (StartReading does Ref)
        if (_readingStarted)
        {
            _readingStarted = false;
            _interpreter?.Unref();
        }
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Emits 'close' at most once per socket lifetime (Node semantics): the
    /// read-loop EOF/error paths and Destroy can otherwise both fire it when a
    /// handler destroys the socket in response to the remote end closing.
    /// </summary>
    private void EmitClose(Interp interpreter, bool hadError)
    {
        if (_closeEmitted) return;
        _closeEmitted = true;
        EmitEvent(interpreter, "close", [hadError]);
    }

    private RuntimeValue SetEncoding(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsString)
            _encoding = args[0].AsStringUnsafe().ToLowerInvariant();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue SetTimeout(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var timeout = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
        if (_client?.Client != null)
        {
            _client.Client.ReceiveTimeout = timeout;
            _client.Client.SendTimeout = timeout;
        }
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb)
        {
            AddListenerDirect("timeout", cb);
        }
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue SetNoDelay(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_isIpc) return RuntimeValue.FromObject(this); // No-op for IPC sockets
        var noDelay = args.Length == 0 || !args[0].IsBoolean || args[0].AsBooleanUnsafe();
        _client?.Client?.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, noDelay);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue SetKeepAlive(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_isIpc) return RuntimeValue.FromObject(this); // No-op for IPC sockets
        var enable = args.Length > 0 && args[0].IsBoolean && args[0].AsBooleanUnsafe();
        _client?.Client?.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, enable);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Address(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_client?.Client?.LocalEndPoint is not IPEndPoint ep)
            return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>()));
        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["port"] = (double)ep.Port,
            ["family"] = ep.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4",
            ["address"] = ep.Address.ToString()
        }));
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

    private RuntimeValue Pause(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _readCts?.Cancel();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Resume(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        StartReading(interpreter);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Pipe(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Minimal pipe: forward data events to writable
        if (args.Length < 1) throw new Exception("pipe() requires a destination");
        var dest = args[0].ToObject();
        AddListenerDirect("data", new PipeDataListener(dest, interpreter));
        return RuntimeValue.FromBoxed(dest);
    }

    /// <summary>
    /// Starts reading from the socket asynchronously.
    /// Called after connection is established.
    /// For IPC sockets, if readReady is provided, it is signaled right before
    /// the first ReadAsync call to indicate that a reader is pending (required
    /// because Windows InOut pipes block writes until a reader is active).
    /// </summary>
    internal virtual void StartReading(Interp interpreter, ManualResetEventSlim? readReady = null)
    {
        if (_destroyed || _stream == null) return;

        _interpreter = interpreter;
        _readCts?.Cancel();
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;

        _readingStarted = true;
        interpreter.Ref();

        _ = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            // Signal that we're about to start reading (IPC needs this)
            readReady?.Set();
            try
            {
                while (!token.IsCancellationRequested && _stream != null)
                {
                    int bytesRead;
                    try
                    {
                        bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // Remote end closed
                        interpreter.ScheduleTimer(0, 0, () =>
                        {
                            if (!_destroyed)
                            {
                                EmitEvent(interpreter, "end", []);
                                EmitClose(interpreter, false);
                            }
                            if (_readingStarted)
                            {
                                _readingStarted = false;
                                interpreter.Unref();
                            }
                        }, isInterval: false);
                        break;
                    }

                    _bytesRead += bytesRead;
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);

                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        object chunk = _encoding is "utf8" or "utf-8"
                            ? Encoding.UTF8.GetString(data)
                            : (object)new SharpTSBuffer(data);
                        EmitEvent(interpreter, "data", [chunk]);
                    }, isInterval: false);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !_destroyed)
                {
                    interpreter.ScheduleTimer(0, 0, () =>
                    {
                        EmitEvent(interpreter, "error", [new SharpTSError(ex.Message)]);
                        EmitClose(interpreter, true);
                        if (_readingStarted)
                        {
                            _readingStarted = false;
                            interpreter.Unref();
                        }
                    }, isInterval: false);
                }
            }
        }, token);
    }

    protected internal static byte[] ChunkToBytes(object? chunk, string encoding)
    {
        return chunk switch
        {
            string s => GetEncoding(encoding).GetBytes(s),
            SharpTSBuffer buf => buf.Data,
            _ => Encoding.UTF8.GetBytes(chunk?.ToString() ?? "")
        };
    }

    private static Encoding GetEncoding(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "utf8" or "utf-8" => Encoding.UTF8,
            "ascii" => Encoding.ASCII,
            "latin1" or "binary" => Encoding.Latin1,
            "utf16le" or "ucs2" => Encoding.Unicode,
            _ => Encoding.UTF8
        };
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

    public override string ToString() => $"Socket {{ connecting: {_connecting}, destroyed: {_destroyed} }}";

    private class PipeDataListener : ISharpTSCallable
    {
        private readonly object? _dest;
        private readonly Interp _interpreter;

        public PipeDataListener(object? dest, Interp interpreter)
        {
            _dest = dest;
            _interpreter = interpreter;
        }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            if (_dest is SharpTSWritable writable)
            {
                writable.WriteInternal(_interpreter, arguments.Count > 0 ? arguments[0] : null, "utf8");
            }
            else if (_dest is SharpTSSocket socket)
            {
                var rvArgs = new RuntimeValue[arguments.Count];
                for (int i = 0; i < arguments.Count; i++)
                    rvArgs[i] = RuntimeValue.FromBoxed(arguments[i]);
                socket.Write(_interpreter, RuntimeValue.FromObject(socket), rvArgs);
            }
            return null;
        }
    }
}
