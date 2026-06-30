using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interop;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of fs.createReadStream() — a Readable that reads a file in chunks.
/// </summary>
/// <remarks>
/// #980 parity pass: honors flags/fd/mode/encoding/start/end/highWaterMark/autoClose/
/// emitClose/signal, emits open→ready→(data*)→end→close in Node order, and exposes
/// path/bytesRead/pending/destroyed. The interpreter reads synchronously inside
/// StartReading (our single-threaded event model), so an already-aborted signal is
/// reported before any data; the abort is also re-checked between chunks.
/// </remarks>
public class SharpTSReadStream : SharpTSReadable
{
    private readonly string _filePath;
    private FileStream? _fileStream;
    private readonly bool _autoClose;
    private readonly long _start;
    private readonly long _end;
    private readonly int _chunkSize;
    private readonly string? _encodingOption;
    private readonly int? _fd;
    private readonly string _flags;
    private readonly int? _mode;
    private readonly bool _emitClose;
    private readonly SharpTSAbortSignal? _signal;
    private long _bytesRead;
    private bool _started;
    private bool _pending = true;
    private bool _closed;

    public SharpTSReadStream(string filePath, Dictionary<string, object?>? options = null)
    {
        _filePath = filePath;
        _autoClose = true;
        _start = 0;
        _end = long.MaxValue;
        _chunkSize = 65536;
        _encodingOption = null;
        _flags = "r";
        _emitClose = true;

        if (options != null)
        {
            if (options.TryGetValue("encoding", out var enc) && enc is string encStr)
                _encodingOption = encStr;
            if (options.TryGetValue("start", out var s) && s is double startD)
                _start = (long)startD;
            if (options.TryGetValue("end", out var e) && e is double endD)
                _end = (long)endD;
            if (options.TryGetValue("highWaterMark", out var hwm) && hwm is double hwmD)
                _chunkSize = (int)hwmD;
            if (options.TryGetValue("autoClose", out var ac) && ac is bool acBool)
                _autoClose = acBool;
            if (options.TryGetValue("flags", out var fl) && fl is string flStr)
                _flags = flStr;
            if (options.TryGetValue("fd", out var fdv) && fdv is double fdD)
                _fd = (int)fdD;
            if (options.TryGetValue("mode", out var md) && md is double mdD)
                _mode = (int)mdD;
            if (options.TryGetValue("emitClose", out var ec) && ec is bool ecBool)
                _emitClose = ecBool;
            if (options.TryGetValue("signal", out var sig) && sig is SharpTSAbortSignal absig)
                _signal = absig;
        }
    }

    /// <summary>
    /// Start reading the file and pushing chunks into the stream.
    /// </summary>
    public void StartReading(Interp interpreter)
    {
        if (_started) return;
        _started = true;

        // An already-aborted signal fails before any open/data (Node semantics).
        if (_signal is { Aborted: true })
        {
            EmitEvent(interpreter, "error", [MakeAbortError()]);
            EmitCloseEvent(interpreter);
            return;
        }

        try
        {
            bool ownsStream = !_fd.HasValue;
            if (_fd.HasValue)
            {
                _fileStream = FileDescriptorTable.Instance.Get(_fd.Value);
            }
            else
            {
                var mode = _flags switch
                {
                    "r+" => FileMode.Open,
                    "w" or "w+" => FileMode.Create,
                    "a" or "a+" => FileMode.OpenOrCreate,
                    _ => FileMode.Open
                };
                _fileStream = new FileStream(_filePath, mode, FileAccess.Read, FileShare.ReadWrite);
            }

            var fdNumber = _fd ?? (double)_fileStream.SafeFileHandle.DangerousGetHandle().ToInt64();
            EmitEvent(interpreter, "open", [(double)fdNumber]);
            _pending = false;
            EmitEvent(interpreter, "ready", []);

            if (_start > 0)
                _fileStream.Seek(_start, SeekOrigin.Begin);

            var buffer = new byte[_chunkSize];
            long remaining = _end == long.MaxValue ? long.MaxValue : _end - _start + 1;

            var pushMethod = GetMember("push") as BuiltInMethod;

            while (remaining > 0)
            {
                if (_signal is { Aborted: true })
                {
                    EmitEvent(interpreter, "error", [MakeAbortError()]);
                    CloseFileStream(interpreter, ownsStream);
                    return;
                }

                int toRead = (int)Math.Min(buffer.Length, remaining);
                int bytesRead = _fileStream.Read(buffer, 0, toRead);
                if (bytesRead == 0) break;

                _bytesRead += bytesRead;
                remaining -= bytesRead;

                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);

                object chunk = _encodingOption != null
                    ? System.Text.Encoding.UTF8.GetString(data)
                    : new SharpTSBuffer(data);

                pushMethod?.Bind(this).Call(interpreter, [chunk]);
            }

            // Signal EOF (push null → 'end').
            pushMethod?.Bind(this).Call(interpreter, new List<object?> { null });

            CloseFileStream(interpreter, ownsStream);
        }
        catch (Exception ex)
        {
            EmitEvent(interpreter, "error", [ex.Message]);
            CloseFileStream(interpreter, !_fd.HasValue);
        }
    }

    private void CloseFileStream(Interp interpreter, bool ownsStream)
    {
        if (_closed) return;
        if (_fileStream != null && _autoClose)
        {
            if (ownsStream)
                _fileStream.Dispose();
            else
                FileDescriptorTable.Instance.Close(_fd!.Value);
            _fileStream = null;
        }
        _closed = true;
        EmitCloseEvent(interpreter);
    }

    /// <summary>Emits 'close' unless emitClose:false suppressed it.</summary>
    private void EmitCloseEvent(Interp interpreter)
    {
        if (_emitClose)
            EmitEvent(interpreter, "close", []);
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
            "bytesRead" => (double)_bytesRead,
            "pending" => _pending,
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => $"ReadStream {{ path: '{_filePath}' }}";
}
