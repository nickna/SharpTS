using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a StatWatcher returned by fs.watchFile().
/// Polls the file at a regular interval and emits 'change' when stat changes.
/// </summary>
public class SharpTSStatWatcher : SharpTSEventEmitter, IDisposable
{
    private Timer? _timer;
    private Interpreter? _interpreter;
    private bool _closed;
    private readonly string _filename;
    private DateTime _lastModified;
    private long _lastSize;
    private readonly int _intervalMs;

    public SharpTSStatWatcher(string filename, Dictionary<string, object?>? options, Interpreter interpreter)
    {
        _filename = Path.GetFullPath(filename);
        _interpreter = interpreter;
        _intervalMs = 5007; // Node.js default

        if (options != null)
        {
            if (options.TryGetValue("interval", out var interval) && interval is double intervalD)
                _intervalMs = (int)intervalD;
        }

        // Capture initial stat
        CaptureCurrentStat(out _lastModified, out _lastSize);

        // Start polling
        _timer = new Timer(PollCallback, null, _intervalMs, _intervalMs);

        // Keep process alive
        var persistent = true;
        if (options != null)
        {
            if (options.TryGetValue("persistent", out var p) && p is bool pBool)
                persistent = pBool;
        }

        if (persistent)
        {
            interpreter.Ref();
        }
    }

    private void CaptureCurrentStat(out DateTime modified, out long size)
    {
        try
        {
            var info = new FileInfo(_filename);
            if (info.Exists)
            {
                modified = info.LastWriteTimeUtc;
                size = info.Length;
            }
            else
            {
                modified = DateTime.MinValue;
                size = 0;
            }
        }
        catch
        {
            modified = DateTime.MinValue;
            size = 0;
        }
    }

    private void PollCallback(object? state)
    {
        var interpreter = _interpreter;
        if (interpreter == null || _closed) return;

        CaptureCurrentStat(out var currentModified, out var currentSize);

        if (currentModified != _lastModified || currentSize != _lastSize)
        {
            var prevStats = CreateStatsObject(_lastModified, _lastSize);
            _lastModified = currentModified;
            _lastSize = currentSize;
            var currStats = CreateStatsObject(currentModified, currentSize);

            interpreter.ScheduleTimer(0, 0, () =>
            {
                EmitChangeEvent(currStats, prevStats);
            }, isInterval: false);
        }
    }

    private static SharpTSObject CreateStatsObject(DateTime modified, long size)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var mtimeMs = modified == DateTime.MinValue ? 0.0 : (modified - epoch).TotalMilliseconds;

        var fields = new Dictionary<string, object?>
        {
            ["size"] = (double)size,
            ["mtimeMs"] = mtimeMs,
            ["mtime"] = modified == DateTime.MinValue ? null : modified.ToString("o"),
            ["isFile"] = BuiltInMethod.CreateV2("isFile", 0, 0,
                (_, _, _) => RuntimeValue.FromBoolean(size > 0 || modified != DateTime.MinValue)),
            ["isDirectory"] = BuiltInMethod.CreateV2("isDirectory", 0, 0,
                (_, _, _) => RuntimeValue.False)
        };
        return new SharpTSObject(fields);
    }

    private void EmitChangeEvent(SharpTSObject current, SharpTSObject previous)
    {
        if (_interpreter == null || _closed) return;

        var emitMethod = base.GetMember("emit") as BuiltInMethod;
        emitMethod?.Call(_interpreter, ["change", current, previous]);
    }

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "close" => BuiltInMethod.CreateV2("close", 0, 0, CloseMethod),
            "ref" => BuiltInMethod.CreateV2("ref", 0, 0, RefMethod),
            "unref" => BuiltInMethod.CreateV2("unref", 0, 0, UnrefMethod),
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue CloseMethod(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        CloseInternal();
        return RuntimeValue.Null;
    }

    private RuntimeValue RefMethod(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Ref();
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue UnrefMethod(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _interpreter?.Unref();
        return RuntimeValue.FromObject(this);
    }

    internal void CloseInternal()
    {
        if (_closed) return;
        _closed = true;

        _timer?.Dispose();
        _timer = null;
        _interpreter?.Unref();
        _interpreter = null;
    }

    public void Dispose()
    {
        CloseInternal();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"StatWatcher {{ path: '{_filename}' }}";
}
