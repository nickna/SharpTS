using System.Diagnostics;
using System.IO.Pipes;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js ChildProcess object.
/// Extends EventEmitter and provides pid, exitCode, stdout, stderr, stdin,
/// and IPC methods (send/disconnect) for fork().
/// </summary>
public class SharpTSChildProcess : SharpTSEventEmitter
{
    private double _pid;
    private double? _exitCode;
    private SharpTSReadable? _stdout;
    private SharpTSReadable? _stderr;
    private SharpTSWritable? _stdin;
    private bool _killed;
    private Process? _process;
    private bool _connected;
    private NamedPipeServerStream? _ipcPipeServer;
    private StreamWriter? _ipcWriter;
    private CancellationTokenSource? _ipcCts;
    private string? _signalCode;
    private readonly object _ipcLock = new();
    private readonly List<object?> _pendingSends = new();

    /// <summary>
    /// Sets the underlying OS process for kill() support.
    /// </summary>
    public void SetProcess(Process process) => _process = process;

    /// <summary>
    /// Sets the PID of the spawned process.
    /// </summary>
    public void SetPid(int pid) => _pid = pid;

    /// <summary>
    /// Sets the exit code when the process completes.
    /// </summary>
    public void SetExitCode(int exitCode) => _exitCode = exitCode;

    /// <summary>
    /// Sets the stdout stream for spawn().
    /// </summary>
    public void SetStdoutStream(SharpTSReadable stream) => _stdout = stream;

    /// <summary>
    /// Sets the stderr stream for spawn().
    /// </summary>
    public void SetStderrStream(SharpTSReadable stream) => _stderr = stream;

    /// <summary>
    /// Sets the stdin writable stream for spawn().
    /// </summary>
    public void SetStdinStream(SharpTSWritable stream) => _stdin = stream;

    /// <summary>
    /// Sets up IPC for fork(). Stores the pipe server and writer for send().
    /// </summary>
    public void SetupIpc(NamedPipeServerStream pipeServer, StreamWriter writer, CancellationTokenSource cts)
    {
        lock (_ipcLock)
        {
            _ipcPipeServer = pipeServer;
            _ipcWriter = writer;
            _ipcCts = cts;
            _connected = true;

            // Flush any messages sent before the channel finished connecting (Node buffers
            // these rather than throwing — see Send()).
            foreach (var msg in _pendingSends)
            {
                try
                {
                    writer.WriteLine(IpcSerializer.Serialize(msg));
                    writer.Flush();
                }
                catch { }
            }
            _pendingSends.Clear();
        }
    }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // Properties
            "pid" => _pid,
            "exitCode" => _exitCode ?? (object)null!,
            "killed" => _killed,
            "stdout" => _stdout,
            "stderr" => _stderr,
            "stdin" => _stdin,
            "connected" => _connected,
            "signalCode" => _signalCode ?? (object)null!,

            // Methods
            "kill" => BuiltInMethod.CreateV2("kill", 0, 1, Kill),
            "send" => BuiltInMethod.CreateV2("send", 1, 4, Send),
            "disconnect" => BuiltInMethod.CreateV2("disconnect", 0, Disconnect),
            "ref" => BuiltInMethod.CreateV2("ref", 0, Ref),
            "unref" => BuiltInMethod.CreateV2("unref", 0, Unref),

            // Inherit from EventEmitter
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue Kill(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_killed) return RuntimeValue.False;

        _killed = true;
        var signal = args.Length > 0 && args[0].IsString ? args[0].AsStringUnsafe() : "SIGTERM";
        _signalCode = signal;

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                return RuntimeValue.True;
            }
        }
        catch
        {
            // Process may have already exited
        }
        return RuntimeValue.True;
    }

    private RuntimeValue Send(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var message = args.Length > 0 ? args[0].ToObject() : null;
        lock (_ipcLock)
        {
            // Buffer sends issued before the IPC channel finished connecting (the channel is
            // established on a background task); they flush in SetupIpc. Matches Node, which
            // queues messages until the channel is ready instead of throwing.
            if (!_connected || _ipcWriter == null)
            {
                _pendingSends.Add(message);
                return RuntimeValue.True;
            }
            try
            {
                _ipcWriter.WriteLine(IpcSerializer.Serialize(message));
                _ipcWriter.Flush();
                return RuntimeValue.True;
            }
            catch
            {
                return RuntimeValue.False;
            }
        }
    }

    private RuntimeValue Disconnect(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!_connected) return RuntimeValue.Null;
        _connected = false;

        try
        {
            _ipcCts?.Cancel();
            _ipcWriter?.Dispose();
            _ipcPipeServer?.Dispose();
        }
        catch { }

        EmitDirect("disconnect");
        return RuntimeValue.Null;
    }

    private RuntimeValue Ref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) => RuntimeValue.FromObject(this);
    private RuntimeValue Unref(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) => RuntimeValue.FromObject(this);

    public override string ToString() => "ChildProcess {}";
}
