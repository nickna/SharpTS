using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// The shared writable-side machinery for Node stream wrappers. Because
/// <see cref="SharpTSDuplex"/> must inherit <see cref="SharpTSReadable"/>, it cannot
/// also inherit a writable base, so the write side was previously re-implemented
/// verbatim on both <see cref="SharpTSWritable"/> and <see cref="SharpTSDuplex"/>
/// (#1138). Both now compose a <see cref="WritableCore"/> and delegate to it.
/// </summary>
/// <remarks>
/// Behaviour is identical to the prior per-class implementations. The two points
/// where the streams historically differed are constructor parameters:
/// <see cref="_emitPrefinish"/> (Writable emits "prefinish" before "finish") and
/// <see cref="_onFinished"/> (Writable's auto-destroy hook).
/// </remarks>
internal sealed class WritableCore
{
    private readonly SharpTSEventEmitter _owner;
    private readonly Func<bool> _objectMode;
    private readonly Func<int> _highWaterMark;
    private readonly bool _emitPrefinish;
    private readonly Action<Interp>? _onFinished;

    private bool _writable = true;
    private bool _ended;
    private bool _finished;
    private bool _destroyed;
    private bool _errored;
    private bool _corked;
    private readonly List<object?> _corkBuffer = [];
    private ISharpTSCallable? _writeCallback;
    private ISharpTSCallable? _finalCallback;
    private int _pendingWrites;
    private int _writableLength; // total bytes of in-flight writes (backpressure tracking)
    private bool _needDrain;

    /// <param name="owner">The stream that owns this core; events are emitted on it.</param>
    /// <param name="objectMode">Live accessor for the writable-side object mode.</param>
    /// <param name="highWaterMark">Live accessor for the writable-side high water mark.</param>
    /// <param name="emitPrefinish">Whether <c>end()</c> emits "prefinish" before "finish".</param>
    /// <param name="onFinished">Optional hook invoked after "finish" (e.g. auto-destroy).</param>
    public WritableCore(
        SharpTSEventEmitter owner,
        Func<bool> objectMode,
        Func<int> highWaterMark,
        bool emitPrefinish,
        Action<Interp>? onFinished)
    {
        _owner = owner;
        _objectMode = objectMode;
        _highWaterMark = highWaterMark;
        _emitPrefinish = emitPrefinish;
        _onFinished = onFinished;
    }

    public bool IsWritable => _writable && !_ended && !_destroyed;
    public bool Ended => _ended;
    public bool Finished => _finished;
    public bool Destroyed => _destroyed;
    public bool Errored => _errored;
    public bool Corked => _corked;
    public int WritableLength => _writableLength;

    public void SetWriteCallback(ISharpTSCallable callback) => _writeCallback = callback;
    public void SetFinalCallback(ISharpTSCallable callback) => _finalCallback = callback;

    /// <summary>
    /// Shared writable-side member dispatch for the two composing streams: the
    /// write/end/cork/uncork methods and the writable* property values (high-water-mark and
    /// object-mode via the live accessors the owner supplied at construction). Returns null when
    /// the name is not a writable-side member so the owner falls through to its own arms and its
    /// EventEmitter/Readable base. destroy/destroyed stay per-class: Writable's destroy runs its
    /// destroy callback, while Duplex's must tear down both sides and resolves <c>destroyed</c>
    /// to the readable-side flag.
    /// </summary>
    public object? GetWritableMember(string name)
    {
        return name switch
        {
            "write" => BuiltInMethod.CreateV2("write", 1, 3, WriteMember),
            "end" => BuiltInMethod.CreateV2("end", 0, 3, EndMember),
            "cork" => BuiltInMethod.CreateV2("cork", 0, CorkMember),
            "uncork" => BuiltInMethod.CreateV2("uncork", 0, UncorkMember),

            "writable" => IsWritable,
            "writableEnded" => Ended,
            "writableFinished" => Finished,
            "writableLength" => (double)WritableLength,
            "writableCorked" => (double)(Corked ? 1 : 0),
            "writableHighWaterMark" => (double)_highWaterMark(),
            "writableObjectMode" => _objectMode(),

            _ => null
        };
    }

    private RuntimeValue WriteMember(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => Write(interpreter, args);

    private RuntimeValue EndMember(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => End(interpreter, args);

    private RuntimeValue CorkMember(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        Cork();
        return RuntimeValue.Null;
    }

    private RuntimeValue UncorkMember(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        Uncork(interpreter);
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Implements <c>stream.write(chunk, encoding?, callback?)</c>.
    /// </summary>
    public RuntimeValue Write(Interp interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed || _ended)
        {
            EmitError(interpreter, "write after end");
            return RuntimeValue.False;
        }

        var (chunk, encoding, callback) = StreamArgs.ParseWrite(args);

        if (_corked)
        {
            _corkBuffer.Add(new WriteChunk(chunk, encoding, callback));
            return RuntimeValue.False;
        }

        return RuntimeValue.FromBoxed(DoWrite(interpreter, chunk, encoding, callback));
    }

    private record WriteChunk(object? Chunk, string? Encoding, ISharpTSCallable? Callback);

    private object? DoWrite(Interp interpreter, object? chunk, string? encoding, ISharpTSCallable? callback)
    {
        _pendingWrites++;
        var chunkSize = GetChunkSize(chunk, _objectMode());
        _writableLength += chunkSize;

        if (_writeCallback != null)
        {
            // Custom write callback: (chunk, encoding, callback)
            var cbWrapper = new WriteCallbackWrapper(callback, interpreter, this, chunkSize);
            var writeArgs = new List<object?> { chunk, encoding ?? "utf8", cbWrapper };
            try
            {
                _writeCallback.Call(interpreter, writeArgs);
            }
            catch (Exception ex)
            {
                EmitError(interpreter, ex.Message);
                return false;
            }
        }
        else
        {
            // Default behavior: just accept the data (sync completion)
            _pendingWrites--;
            _writableLength -= chunkSize;
            callback?.Call(interpreter, []);
            CheckDrain(interpreter);
        }

        // Return false when buffered data exceeds highWaterMark (backpressure)
        if (_writableLength >= _highWaterMark())
        {
            _needDrain = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a chunk directly (used by piping); returns false on backpressure or when not writable.
    /// </summary>
    public bool WriteDirect(Interp interpreter, object? chunk, string? encoding)
    {
        if (_destroyed || _ended)
            return false;
        return (bool)(DoWrite(interpreter, chunk, encoding, null) ?? false);
    }

    private void CheckDrain(Interp interpreter)
    {
        if (_needDrain && _writableLength < _highWaterMark())
        {
            _needDrain = false;
            _owner.EmitEvent(interpreter, "drain", []);
        }
    }

    /// <summary>
    /// Implements <c>stream.end(chunk?, encoding?, callback?)</c>.
    /// </summary>
    public RuntimeValue End(Interp interpreter, ReadOnlySpan<RuntimeValue> args)
    {
        if (_ended)
            return RuntimeValue.FromObject(_owner);

        var (chunk, encoding, callback) = StreamArgs.ParseEnd(args);

        _ended = true;
        _writable = false;

        // Write final chunk if provided
        if (chunk != null)
            DoWrite(interpreter, chunk, encoding, null);

        // Flush cork buffer
        if (_corked)
            Uncork(interpreter);

        // Call final callback
        if (_finalCallback != null)
        {
            var finalCbWrapper = new WriteCallbackWrapper(null, interpreter, this, 0);
            try
            {
                _finalCallback.Call(interpreter, [finalCbWrapper]);
            }
            catch (Exception ex)
            {
                EmitError(interpreter, ex.Message);
            }
        }

        _finished = true;
        callback?.Call(interpreter, []);

        if (_emitPrefinish)
            _owner.EmitEvent(interpreter, "prefinish", []);
        _owner.EmitEvent(interpreter, "finish", []);
        _onFinished?.Invoke(interpreter);

        return RuntimeValue.FromObject(_owner);
    }

    /// <summary>Ends the stream directly (used by piping).</summary>
    public void EndDirect(Interp interpreter, object? chunk, string? encoding)
    {
        End(interpreter,
            chunk != null
                ? [RuntimeValue.FromBoxed(chunk), RuntimeValue.FromBoxed(encoding)]
                : []);
    }

    /// <summary>Corks the stream, buffering all writes.</summary>
    public void Cork() => _corked = true;

    /// <summary>Uncorks the stream, flushing the buffered writes.</summary>
    public void Uncork(Interp interpreter)
    {
        if (!_corked)
            return;

        _corked = false;

        foreach (var item in _corkBuffer.Cast<WriteChunk>())
            DoWrite(interpreter, item.Chunk, item.Encoding, item.Callback);
        _corkBuffer.Clear();
    }

    /// <summary>
    /// Marks the writable side destroyed and clears the cork buffer. Event emission /
    /// destroy-callback policy stays with the owning stream, which differs per type.
    /// </summary>
    public void MarkDestroyed()
    {
        _destroyed = true;
        _writable = false;
        _corkBuffer.Clear();
    }

    /// <summary>Emits an "error" event and records the errored state.</summary>
    public void EmitError(Interp interpreter, object? error)
    {
        _errored = true;
        _owner.EmitEvent(interpreter, "error", [error]);
    }

    /// <summary>Resets mutable state for singleton reuse (e.g. process.stdout between runs).</summary>
    public void Reset()
    {
        _writable = true;
        _ended = false;
        _finished = false;
        _destroyed = false;
        _errored = false;
        _corked = false;
        _corkBuffer.Clear();
        _pendingWrites = 0;
        _writableLength = 0;
        _needDrain = false;
    }

    /// <summary>
    /// Gets the byte size of a chunk (or 1 for object mode).
    /// </summary>
    internal static int GetChunkSize(object? chunk, bool objectMode)
    {
        if (objectMode) return 1;
        return chunk switch
        {
            string s => s.Length,
            SharpTSBuffer buf => buf.Length,
            _ => 0
        };
    }

    /// <summary>
    /// Wrapper for write callbacks to match Node.js callback(error?) signature.
    /// </summary>
    private sealed class WriteCallbackWrapper : ISharpTSCallable
    {
        private readonly ISharpTSCallable? _callback;
        private readonly Interp _interpreter;
        private readonly WritableCore _core;
        private readonly int _chunkSize;

        public WriteCallbackWrapper(ISharpTSCallable? callback, Interp interpreter, WritableCore core, int chunkSize)
        {
            _callback = callback;
            _interpreter = interpreter;
            _core = core;
            _chunkSize = chunkSize;
        }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            // Decrement pending writes and buffered length, then run the user's callback.
            _core._pendingWrites--;
            _core._writableLength -= _chunkSize;
            _callback?.Call(_interpreter, []);
            _core.CheckDrain(_interpreter);
            return null;
        }
    }
}
