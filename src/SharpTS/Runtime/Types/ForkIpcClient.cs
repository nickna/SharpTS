using System.IO.Pipes;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Client-side IPC handler for child processes spawned via fork().
/// Connects to the parent's named pipe and provides send/receive messaging.
/// </summary>
public sealed class ForkIpcClient : IDisposable
{
    private static ForkIpcClient? _instance;
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private readonly CancellationTokenSource _cts = new();
    private bool _connected;
    private bool _disposed;
    private Interp? _interpreter;
    private readonly object _refLock = new();
    private bool _loopRefed;

    /// <summary>
    /// Gets the singleton instance if this process was forked with IPC.
    /// Returns null if this is not a forked child process.
    /// </summary>
    public static ForkIpcClient? Instance => _instance;

    /// <summary>
    /// Whether this process was started as a forked child with IPC.
    /// </summary>
    public static bool IsForkedChild => _instance != null;

    /// <summary>
    /// Whether the IPC channel is connected.
    /// </summary>
    public bool Connected => _connected && !_disposed;

    private ForkIpcClient(string pipeName)
    {
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        _pipe.Connect(10_000); // 10 second timeout
        _connected = true;

        _writer = new StreamWriter(_pipe) { AutoFlush = true };
        _reader = new StreamReader(_pipe);
    }

    /// <summary>
    /// Initializes the fork IPC client if SHARPTS_IPC_PIPE env var is set.
    /// Called during process startup.
    /// </summary>
    public static void TryInitialize()
    {
        var pipeName = Environment.GetEnvironmentVariable("SHARPTS_IPC_PIPE");
        if (string.IsNullOrEmpty(pipeName)) return;

        try
        {
            // Connect early (the parent's WaitForConnection blocks until we do), but defer
            // reading until AttachLoop wires the child interpreter + event loop.
            _instance = new ForkIpcClient(pipeName);
        }
        catch
        {
            // Failed to connect - not a valid fork() child
            _instance = null;
        }
    }

    /// <summary>
    /// Attaches the child's interpreter + event loop. Refs the loop (so the child stays
    /// alive to receive messages) and starts the IPC reader, which marshals incoming
    /// 'message'/'disconnect' onto the loop thread so the handlers run with an interpreter.
    /// Called once, right after the child's interpreter is created.
    /// </summary>
    private bool _attached;
    public void AttachLoop(Interp interpreter)
    {
        lock (_refLock)
        {
            if (_attached) return; // idempotent — only the first interpreter owns the channel
            _attached = true;
            _interpreter = interpreter;
            if (!_loopRefed && _connected)
            {
                _loopRefed = true;
                interpreter.Ref();
            }
        }
        StartReading();
    }

    private void ReleaseLoopRef()
    {
        lock (_refLock)
        {
            if (_loopRefed)
            {
                _loopRefed = false;
                _interpreter?.Unref();
            }
        }
    }

    /// <summary>
    /// Sends a message to the parent process.
    /// </summary>
    public bool Send(object? message)
    {
        if (!_connected || _disposed) return false;

        try
        {
            var json = IpcSerializer.Serialize(message);
            _writer.WriteLine(json);
            return true;
        }
        catch
        {
            _connected = false;
            return false;
        }
    }

    /// <summary>
    /// Disconnects from the parent's IPC channel.
    /// </summary>
    public void Disconnect()
    {
        if (!_connected) return;
        _connected = false;
        _cts.Cancel();

        try { _writer.Dispose(); } catch { }
        try { _pipe.Dispose(); } catch { }

        // Emit 'disconnect' on the loop thread, then drop the keep-alive ref so the child
        // can exit once its work is done.
        EmitOnLoop("disconnect", null, releaseRef: true);
    }

    /// <summary>
    /// Starts a background task to read incoming IPC messages from the parent. Messages are
    /// marshalled onto the loop thread and emitted as 'message' on the process EventEmitter.
    /// </summary>
    private void StartReading()
    {
        Task.Run(() =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested && _connected)
                {
                    var line = _reader.ReadLine();
                    if (line == null)
                    {
                        _connected = false;
                        EmitOnLoop("disconnect", null, releaseRef: true);
                        break;
                    }

                    var message = IpcSerializer.Deserialize(line);
                    EmitOnLoop("message", message, releaseRef: false);
                }
            }
            catch (OperationCanceledException) { }
            catch
            {
                _connected = false;
                ReleaseLoopRef();
            }
        }, _cts.Token);
    }

    /// <summary>
    /// Emits an event on the process EventEmitter from the loop thread (interpreter-aware, so
    /// handlers that use their interpreter — e.g. process.send inside an 'message' handler —
    /// work). Falls back to a direct emit if no interpreter is attached.
    /// </summary>
    private void EmitOnLoop(string eventName, object? arg, bool releaseRef)
    {
        var interp = _interpreter;
        if (interp == null)
        {
            if (arg == null) SharpTSProcess.Instance.EmitDirect(eventName);
            else SharpTSProcess.Instance.EmitDirect(eventName, arg);
            if (releaseRef) ReleaseLoopRef();
            return;
        }

        interp.EnqueueCallback(() =>
        {
            try
            {
                if (arg == null) SharpTSProcess.Instance.EmitWith(interp, eventName);
                else SharpTSProcess.Instance.EmitWith(interp, eventName, arg);
            }
            finally
            {
                if (releaseRef) ReleaseLoopRef();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;
        _cts.Cancel();
        try { _writer.Dispose(); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _pipe.Dispose(); } catch { }
        _cts.Dispose();
    }
}
