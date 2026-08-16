using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interop;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of fs.createWriteStream() — a Writable that writes to a file.
/// </summary>
/// <remarks>
/// #980 parity pass: honors flags/fd/mode/encoding/start/autoClose/emitClose/signal,
/// emits open→ready, finish, close (suppressed by emitClose:false), and error; exposes
/// path/bytesWritten/pending/destroyed. fd writes share the descriptor via the fd table.
/// </remarks>
public class SharpTSWriteStream : SharpTSWritable
{
    private readonly string _filePath;
    private FileStream? _fileStream;
    private readonly bool _autoClose;
    private readonly bool _ownsStream;
    private readonly string _flags;
    private readonly string _encoding;
    private readonly bool _emitClose;
    private readonly int? _fd;
    private readonly SharpTSAbortSignal? _signal;
    private long _bytesWritten;
    private bool _pending = true;
    private bool _closed;
    // Replay state for late-attached open/ready/close listeners (#980): these
    // events fire during synchronous creation/end before user listeners can
    // attach, so a listener added afterward is invoked immediately (mirrors the
    // base Readable's 'end' replay).
    private bool _openFired, _readyFired, _closeFired;
    private double _openFd;

    public SharpTSWriteStream(string filePath, Dictionary<string, object?>? options = null)
    {
        _filePath = filePath;
        _autoClose = true;
        _flags = "w";
        _encoding = "utf8";
        _emitClose = true;
        long start = 0;

        if (options != null)
        {
            if (options.TryGetValue("flags", out var f) && f is string flagsStr)
                _flags = flagsStr;
            if (options.TryGetValue("autoClose", out var ac) && ac is bool acBool)
                _autoClose = acBool;
            if (options.TryGetValue("encoding", out var enc) && enc is string encStr)
                _encoding = encStr;
            if (options.TryGetValue("emitClose", out var ec) && ec is bool ecBool)
                _emitClose = ecBool;
            if (options.TryGetValue("start", out var s) && s is double startD)
                start = (long)startD;
            if (options.TryGetValue("fd", out var fdv) && fdv is double fdD)
                _fd = (int)fdD;
            if (options.TryGetValue("signal", out var sig) && sig is SharpTSAbortSignal absig)
                _signal = absig;
        }

        if (_fd.HasValue)
        {
            _fileStream = FileDescriptorTable.Instance.Get(_fd.Value);
            _ownsStream = false;
        }
        else
        {
            var mode = _flags switch
            {
                "a" => FileMode.Append,
                "ax" => FileMode.Append,
                "a+" => FileMode.Append,
                "w" => FileMode.Create,
                "w+" => FileMode.Create,
                "wx" => FileMode.CreateNew,
                "r+" => FileMode.Open,
                _ => FileMode.Create
            };
            var access = mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite;
            _fileStream = new FileStream(_filePath, mode, access, FileShare.ReadWrite);
            _ownsStream = true;
        }

        if (start > 0 && _fileStream.CanSeek)
            _fileStream.Seek(start, SeekOrigin.Begin);

        // Internal write callback writes each chunk to the file.
        SetWriteCallback(new WriteStreamCallback(this));

        // Close the file once the writable side finishes (direct end() or piped end).
        AddListenerDirect("finish", new FinishListener(this));
    }

    /// <summary>
    /// Emits 'open' then 'ready' (Node order). Call after construction.
    /// </summary>
    public void EmitOpen(Interp interpreter)
    {
        if (_signal is { Aborted: true })
        {
            EmitEvent(interpreter, "error", [MakeAbortError()]);
            CloseFileStream(interpreter);
            return;
        }
        if (_fileStream != null)
        {
            _openFd = _fd ?? (double)_fileStream.SafeFileHandle.DangerousGetHandle().ToInt64();
            _openFired = true;
            EmitEvent(interpreter, "open", [_openFd]);
            _pending = false;
            _readyFired = true;
            EmitEvent(interpreter, "ready", []);
        }
    }

    private class WriteStreamCallback : ISharpTSCallable
    {
        private readonly SharpTSWriteStream _stream;

        public WriteStreamCallback(SharpTSWriteStream stream) => _stream = stream;

        public int Arity() => 3;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var encoding = arguments.Count > 1 && arguments[1] is string e ? e : _stream._encoding;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;

            if (_stream._signal is { Aborted: true })
            {
                _stream.EmitEvent(interpreter, "error", [MakeAbortError()]);
                callback?.Call(interpreter, []);
                return null;
            }

            if (chunk != null && _stream._fileStream != null)
            {
                try
                {
                    byte[] data = chunk is SharpTSBuffer buf
                        ? buf.Data
                        : BufferEncoding.Encode(chunk.ToString() ?? "", encoding);
                    _stream._fileStream.Write(data, 0, data.Length);
                    _stream._bytesWritten += data.Length;
                }
                catch (Exception ex)
                {
                    _stream.EmitEvent(interpreter, "error", [ex.Message]);
                }
            }

            callback?.Call(interpreter, []);
            return null;
        }
    }

    /// <summary>
    /// Listener for 'finish' to close the file stream.
    /// </summary>
    private class FinishListener : ISharpTSCallable
    {
        private readonly SharpTSWriteStream _stream;

        public FinishListener(SharpTSWriteStream stream) => _stream = stream;

        public int Arity() => 0;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            // Close on the next tick so every 'finish' listener runs first — Node
            // emits 'close' after 'finish', not interleaved within it (#980).
            interpreter.Ref();
            interpreter.ScheduleTimer(0, 0, () =>
            {
                try { _stream.CloseFileStream(interpreter); }
                finally { interpreter.Unref(); }
            }, isInterval: false);
            return null;
        }
    }

    private void CloseFileStream(Interp interpreter)
    {
        if (_closed) return;
        if (_fileStream != null && _autoClose)
        {
            _fileStream.Flush();
            if (_ownsStream)
                _fileStream.Dispose();
            else
                FileDescriptorTable.Instance.Close(_fd!.Value);
            _fileStream = null;
        }
        _closed = true;
        if (_emitClose)
        {
            _closeFired = true;
            EmitEvent(interpreter, "close", []);
        }
    }

    /// <summary>Builds a Node-shaped AbortError ({ name, code, message }).</summary>
    private static SharpTSObject MakeAbortError()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = "AbortError",
            ["code"] = "ABORT_ERR",
            ["message"] = "The operation was aborted",
        });
    }

    public override object? GetMember(string name)
    {
        return name switch
        {
            "path" => _filePath,
            "bytesWritten" => (double)_bytesWritten,
            "pending" => _pending,
            "on" or "addListener" or "once" => BuiltInMethod.CreateV2(name, 2,
                (interp, recv, args) => WrapOnReplay(interp, name, args)),
            _ => base.GetMember(name)
        };
    }

    // Wraps on/once/addListener so a listener for open/ready/close added after the
    // event already fired (during synchronous creation/end) is invoked immediately.
    private RuntimeValue WrapOnReplay(Interp interpreter, string method, ReadOnlySpan<RuntimeValue> args)
    {
        (base.GetMember(method) as BuiltInMethod)?.Bind(this).CallV2(interpreter, args);

        var ev = args.Length > 0 ? args[0].ToObject()?.ToString() : null;
        var listener = args.Length > 1 ? args[1].ToObject() as ISharpTSCallable : null;
        if (listener != null)
        {
            if (ev == "open" && _openFired) listener.Call(interpreter, [_openFd]);
            else if (ev == "ready" && _readyFired) listener.Call(interpreter, []);
            else if (ev == "close" && _closeFired) listener.Call(interpreter, []);
        }
        return RuntimeValue.FromObject(this);
    }

    public override string ToString() => $"WriteStream {{ path: '{_filePath}' }}";
}
