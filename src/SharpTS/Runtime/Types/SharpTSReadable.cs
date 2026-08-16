using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a Node.js-compatible Readable stream.
/// Provides sync push/pull mode for reading data with pipe() support.
/// </summary>
/// <remarks>
/// Extends <see cref="SharpTSEventEmitter"/> for event support (data, end, error, close).
/// Implements a simple pull-based read model suitable for sync operation.
/// </remarks>
public class SharpTSReadable : SharpTSEventEmitter
{
    private readonly Queue<object?> _readBuffer = new();
    private readonly List<object> _pipeDestinations = [];
    private bool _ended;
    private bool _destroyed;
    private string _encoding = "utf8";
    private bool _readable = true;
    private bool? _flowing; // null=initial, true=flowing, false=paused
    private bool _objectMode;
    private int _highWaterMark = 16384; // bytes for binary mode, 16 for object mode

    // Async iteration support (#1024): tracks error state for `for await` rejection,
    // and parks a pending next() pull when the buffer is empty and the stream is live.
    private bool _errored;
    private object? _error;
    private System.Threading.Tasks.TaskCompletionSource<object?>? _iterWaiter;

    /// <summary>
    /// Gets or sets whether this stream operates in object mode.
    /// In object mode, chunks can be any JavaScript value (not just strings/Buffers).
    /// </summary>
    public bool ObjectMode
    {
        get => _objectMode;
        set
        {
            _objectMode = value;
            if (value && _highWaterMark == 16384)
                _highWaterMark = 16; // object mode default
        }
    }

    /// <summary>
    /// Gets or sets the high water mark for this stream.
    /// </summary>
    public int HighWaterMark { get => _highWaterMark; set => _highWaterMark = value; }

    /// <summary>
    /// Whether this stream has errored (destroyed with an error) — backs stream.isErrored (#1030).
    /// </summary>
    public bool Errored => _errored;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // Readable-specific methods
            "read" => BuiltInMethod.CreateV2("read", 0, 1, Read),
            "push" => BuiltInMethod.CreateV2("push", 1, Push),
            "pipe" => BuiltInMethod.CreateV2("pipe", 1, 2, Pipe),
            "unpipe" => BuiltInMethod.CreateV2("unpipe", 0, 1, Unpipe),
            "setEncoding" => BuiltInMethod.CreateV2("setEncoding", 1, SetEncoding),
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, Destroy),
            "unshift" => BuiltInMethod.CreateV2("unshift", 1, Unshift),
            "pause" => BuiltInMethod.CreateV2("pause", 0, Pause),
            "resume" => BuiltInMethod.CreateV2("resume", 0, Resume),
            "isPaused" => BuiltInMethod.CreateV2("isPaused", 0, IsPaused),
            "toArray" => BuiltInMethod.CreateV2("toArray", 0, ToArray),
            "forEach" => BuiltInMethod.CreateV2("forEach", 1, ForEach),
            "map" => BuiltInMethod.CreateV2("map", 1, Map),
            "filter" => BuiltInMethod.CreateV2("filter", 1, Filter),

            // Async iterator helpers (#1025). Consuming helpers return a Promise;
            // lazy helpers return a new Readable carrying the transformed chunks.
            "reduce" => BuiltInMethod.CreateV2("reduce", 1, 2, Reduce),
            "some" => BuiltInMethod.CreateV2("some", 1, Some),
            "every" => BuiltInMethod.CreateV2("every", 1, Every),
            "find" => BuiltInMethod.CreateV2("find", 1, Find),
            "drop" => BuiltInMethod.CreateV2("drop", 1, Drop),
            "take" => BuiltInMethod.CreateV2("take", 1, Take),
            "flatMap" => BuiltInMethod.CreateV2("flatMap", 1, FlatMap),
            "asIndexedPairs" => BuiltInMethod.CreateV2("asIndexedPairs", 0, AsIndexedPairs),

            // Wrap event methods to drain buffer after 'data' listener is added
            "on" or "addListener" => BuiltInMethod.CreateV2(name, 2, WrapOnForFlowing),
            "once" => BuiltInMethod.CreateV2("once", 2, WrapOnceForFlowing),

            // Properties
            "readable" => _readable && !_ended && !_destroyed,
            "readableEnded" => _ended,
            "readableLength" => (double)_readBuffer.Count,
            "readableHighWaterMark" => (double)_highWaterMark,
            "readableEncoding" => _encoding,
            "readableFlowing" => _flowing ?? (object)false,
            "readableObjectMode" => _objectMode,
            "destroyed" => _destroyed,

            // Inherit from EventEmitter
            _ => base.GetMember(name)
        };
    }

    private RuntimeValue WrapOnForFlowing(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Call base on
        var baseOn = base.GetMember("on") as BuiltInMethod;
        baseOn?.Bind(this).CallV2(interpreter, args);

        var eventName = args.Length > 0 ? args[0].ToObject()?.ToString() : null;

        // If a 'data' listener was added, drain the buffer
        if (eventName == "data" && _flowing == true && _readBuffer.Count > 0)
        {
            DrainBuffer(interpreter);
        }

        // If an 'end' listener was added on an already-ended stream, emit 'end' immediately
        if (eventName == "end" && _ended && _readBuffer.Count == 0)
        {
            EmitEvent(interpreter, "end", []);
        }

        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue WrapOnceForFlowing(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // Call base once
        var baseOnce = base.GetMember("once") as BuiltInMethod;
        baseOnce?.Bind(this).CallV2(interpreter, args);

        var eventName = args.Length > 0 ? args[0].ToObject()?.ToString() : null;

        // If a 'data' listener was added, drain the buffer
        if (eventName == "data" && _flowing == true && _readBuffer.Count > 0)
        {
            DrainBuffer(interpreter);
        }

        // If an 'end' listener was added on an already-ended stream, emit 'end' immediately
        if (eventName == "end" && _ended && _readBuffer.Count == 0)
        {
            EmitEvent(interpreter, "end", []);
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    private RuntimeValue Read(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed || _readBuffer.Count == 0)
        {
            return RuntimeValue.Null;
        }

        // In object mode, always return one object at a time (size parameter is ignored)
        if (_objectMode)
        {
            return RuntimeValue.FromBoxed(_readBuffer.Dequeue());
        }

        int? size = null;
        if (args.Length > 0 && args[0].IsNumber)
        {
            size = (int)args[0].AsNumberUnsafe();
        }

        if (size == null || size <= 0)
        {
            // Read all available data
            if (_readBuffer.Count == 0)
            {
                return RuntimeValue.Null;
            }

            var chunks = new List<object?>();
            while (_readBuffer.Count > 0)
            {
                chunks.Add(_readBuffer.Dequeue());
            }

            // Concatenate all chunks
            return RuntimeValue.FromBoxed(ConcatenateChunks(chunks));
        }
        else
        {
            // Read specified amount
            return RuntimeValue.FromBoxed(ReadSize(size.Value));
        }
    }

    private object? ReadSize(int size)
    {
        if (_readBuffer.Count == 0)
        {
            return null;
        }

        var chunks = new List<object?>();
        int totalRead = 0;

        while (_readBuffer.Count > 0 && totalRead < size)
        {
            var chunk = _readBuffer.Peek();
            var chunkLength = GetChunkLength(chunk);

            if (totalRead + chunkLength <= size)
            {
                chunks.Add(_readBuffer.Dequeue());
                totalRead += chunkLength;
            }
            else
            {
                // Partial read from this chunk
                int needed = size - totalRead;
                var (taken, remaining) = SplitChunk(chunk, needed);
                chunks.Add(taken);
                _readBuffer.Dequeue();
                if (remaining != null)
                {
                    // Put the remaining back at the front
                    var temp = _readBuffer.ToList();
                    _readBuffer.Clear();
                    _readBuffer.Enqueue(remaining);
                    foreach (var item in temp)
                    {
                        _readBuffer.Enqueue(item);
                    }
                }
                totalRead = size;
            }
        }

        return ConcatenateChunks(chunks);
    }

    private static int GetChunkLength(object? chunk)
    {
        return chunk switch
        {
            string s => s.Length,
            SharpTSBuffer buf => buf.Length,
            _ => chunk?.ToString()?.Length ?? 0
        };
    }

    private static (object? taken, object? remaining) SplitChunk(object? chunk, int at)
    {
        if (chunk is string s)
        {
            return (s.Substring(0, Math.Min(at, s.Length)),
                    at < s.Length ? s.Substring(at) : null);
        }
        if (chunk is SharpTSBuffer buf)
        {
            var data = buf.Data;
            var taken = new byte[Math.Min(at, data.Length)];
            Array.Copy(data, taken, taken.Length);
            if (at < data.Length)
            {
                var remaining = new byte[data.Length - at];
                Array.Copy(data, at, remaining, 0, remaining.Length);
                return (new SharpTSBuffer(taken), new SharpTSBuffer(remaining));
            }
            return (new SharpTSBuffer(taken), null);
        }
        return (chunk, null);
    }

    private object? ConcatenateChunks(List<object?> chunks)
    {
        if (chunks.Count == 0)
        {
            return null;
        }
        if (chunks.Count == 1)
        {
            return chunks[0];
        }

        // Check if all chunks are strings
        if (chunks.All(c => c is string))
        {
            return string.Join("", chunks.Cast<string>());
        }

        // Convert all to buffers and concatenate
        var buffers = new List<byte[]>();
        foreach (var chunk in chunks)
        {
            if (chunk is SharpTSBuffer buf)
            {
                buffers.Add(buf.Data);
            }
            else if (chunk is string s)
            {
                buffers.Add(System.Text.Encoding.UTF8.GetBytes(s));
            }
        }

        var totalLength = buffers.Sum(b => b.Length);
        var result = new byte[totalLength];
        int offset = 0;
        foreach (var buffer in buffers)
        {
            Array.Copy(buffer, 0, result, offset, buffer.Length);
            offset += buffer.Length;
        }

        return _encoding == "utf8" || _encoding == "utf-8"
            ? System.Text.Encoding.UTF8.GetString(result)
            : (object)new SharpTSBuffer(result);
    }

    /// <summary>
    /// Pushes a chunk into the stream from host (C#) code — used to feed a programmatically
    /// driven Readable such as a worker's <c>stdout</c>/<c>stderr</c> (#1003). Must be called
    /// on the stream's owner-loop thread. Emits 'data' when flowing (via the parent interpreter
    /// when present, or directly for a compiled parent that has no interpreter), otherwise
    /// buffers until a 'data' listener resumes the stream. Passing null signals EOF ('end').
    /// </summary>
    public void PushFromHost(Interp? interpreter, object? chunk)
    {
        if (_destroyed)
            return;

        if (chunk == null)
        {
            _ended = true;
            _readable = false;
            TryDeliverToIterWaiter(null, end: true);
            if (_flowing == true)
                EmitData(interpreter, "end", null);
            return;
        }

        if (TryDeliverToIterWaiter(chunk, end: false))
            return;

        if (_flowing == true)
            EmitData(interpreter, "data", chunk);
        else
            _readBuffer.Enqueue(chunk);
    }

    private void EmitData(Interp? interpreter, string eventName, object? chunk)
    {
        var args = chunk == null ? new List<object?>() : new List<object?> { chunk };
        if (interpreter != null)
            EmitEvent(interpreter, eventName, args);
        else
            EmitDirect(eventName, args.ToArray());
    }

    /// <summary>
    /// Pushes data into the stream buffer.
    /// </summary>
    private RuntimeValue Push(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed)
        {
            return RuntimeValue.False;
        }

        var chunk = args.Length > 0 ? args[0].ToObject() : null;

        if (chunk == null)
        {
            // EOF signal
            _ended = true;
            _readable = false;
            TryDeliverToIterWaiter(null, end: true);
            if (_flowing == true)
            {
                EmitEvent(interpreter, "end", []);
            }
            else
            {
                EmitEndEvent(interpreter);
            }
            FlushPipes(interpreter);
            return RuntimeValue.False;
        }

        // Hand the chunk directly to a parked `for await` pull, if any (#1024).
        if (TryDeliverToIterWaiter(chunk, end: false))
        {
            return RuntimeValue.FromBoolean(GetBufferSize() < _highWaterMark);
        }

        if (_flowing == true)
        {
            // Flowing mode: emit 'data' event directly, don't buffer
            EmitEvent(interpreter, "data", [chunk]);
            FlushToPipes(interpreter, chunk);
        }
        else
        {
            // Non-flowing mode: buffer as before
            _readBuffer.Enqueue(chunk);
            // In sync mode, immediately pipe to destinations
            FlushToPipes(interpreter, chunk);
        }

        // Return false when buffer exceeds highWaterMark (backpressure signal)
        return RuntimeValue.FromBoolean(GetBufferSize() < _highWaterMark);
    }

    /// <summary>
    /// Pushes data back to the front of the buffer.
    /// </summary>
    private RuntimeValue Unshift(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed || _ended)
        {
            return RuntimeValue.Null;
        }

        var chunk = args.Length > 0 ? args[0].ToObject() : null;
        if (chunk == null)
        {
            return RuntimeValue.Null;
        }

        var temp = _readBuffer.ToList();
        _readBuffer.Clear();
        _readBuffer.Enqueue(chunk);
        foreach (var item in temp)
        {
            _readBuffer.Enqueue(item);
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Pipes this readable to a writable destination.
    /// </summary>
    private RuntimeValue Pipe(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 1)
        {
            throw new Exception("pipe() destination must be a Writable stream");
        }

        var destObj = args[0].ToObject();

        // Accept SharpTSWritable or any Duplex-derived type (which has Writable capabilities)
        if (destObj is not SharpTSWritable && destObj is not SharpTSDuplex)
        {
            throw new Exception("pipe() destination must be a Writable stream");
        }

        // Store the destination for piping
        if (destObj is SharpTSWritable or SharpTSDuplex)
        {
            _pipeDestinations.Add(destObj);
        }

        // Check options for end: false
        bool shouldEnd = true;
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject options)
        {
            var endOption = options.GetProperty("end");
            if (endOption is bool endBool && !endBool)
            {
                shouldEnd = false;
            }
        }

        // Drain existing buffer to the destination
        while (_readBuffer.Count > 0)
        {
            var chunk = _readBuffer.Dequeue();
            var ok = WriteToDestination(interpreter, destObj, chunk, _encoding);
            if (!ok)
            {
                // Destination backpressure — stop draining, stay paused
                RegisterDrainListener(interpreter, destObj);
                return RuntimeValue.FromObject(destObj);
            }
        }

        // Enter flowing mode so future pushes emit 'data' and flow to pipes
        _flowing = true;

        // If already ended, end the destination
        if (shouldEnd && _ended)
        {
            EndDestination(interpreter, destObj);
        }

        return RuntimeValue.FromObject(destObj);
    }

    /// <summary>
    /// Writes a chunk to any writable-like destination. Returns false on backpressure.
    /// </summary>
    private static bool WriteToDestination(Interp interpreter, object destObj, object? chunk, string encoding)
    {
        if (destObj is SharpTSWritable writable)
        {
            return writable.WriteInternal(interpreter, chunk, encoding);
        }
        else if (destObj is SharpTSPassThrough passThrough)
        {
            var writeMethod = passThrough.GetMember("write") as BuiltInMethod;
            var result = writeMethod?.Bind(passThrough).Call(interpreter, [chunk, encoding]);
            return result is not false;
        }
        else if (destObj is SharpTSTransform transform)
        {
            var writeMethod = transform.GetMember("write") as BuiltInMethod;
            var result = writeMethod?.Bind(transform).Call(interpreter, [chunk, encoding]);
            return result is not false;
        }
        else if (destObj is SharpTSDuplex duplex)
        {
            var writeMethod = duplex.GetMember("write") as BuiltInMethod;
            var result = writeMethod?.Bind(duplex).Call(interpreter, [chunk, encoding]);
            return result is not false;
        }
        return true;
    }

    /// <summary>
    /// Ends any writable-like destination.
    /// </summary>
    private static void EndDestination(Interp interpreter, object destObj)
    {
        if (destObj is SharpTSWritable writable)
        {
            writable.EndInternal(interpreter, null, null);
        }
        else if (destObj is SharpTSPassThrough passThrough)
        {
            var endMethod = passThrough.GetMember("end") as BuiltInMethod;
            endMethod?.Bind(passThrough).Call(interpreter, []);
        }
        else if (destObj is SharpTSTransform transform)
        {
            var endMethod = transform.GetMember("end") as BuiltInMethod;
            endMethod?.Bind(transform).Call(interpreter, []);
        }
        else if (destObj is SharpTSDuplex duplex)
        {
            var endMethod = duplex.GetMember("end") as BuiltInMethod;
            endMethod?.Bind(duplex).Call(interpreter, []);
        }
    }

    /// <summary>
    /// Unpipes from a destination or all destinations.
    /// </summary>
    private RuntimeValue Unpipe(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].ToObject() is { } dest)
        {
            _pipeDestinations.Remove(dest);
        }
        else
        {
            _pipeDestinations.Clear();
        }

        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Sets the encoding for string output.
    /// </summary>
    private RuntimeValue SetEncoding(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsString)
        {
            _encoding = args[0].AsStringUnsafe().ToLowerInvariant();
        }
        return RuntimeValue.FromObject(this);
    }

    /// <summary>
    /// Destroys the stream.
    /// </summary>
    private RuntimeValue Destroy(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (_destroyed)
        {
            return RuntimeValue.FromObject(this);
        }

        _destroyed = true;
        _readable = false;
        _readBuffer.Clear();
        _pipeDestinations.Clear();

        var destroyError = args.Length > 0 ? args[0].ToObject() : null;
        if (destroyError is not null)
        {
            _errored = true;
            _error = destroyError;
            // A pending `for await` pull rejects with the destroy error (#1024).
            var faulted = _iterWaiter;
            _iterWaiter = null;
            faulted?.TrySetException(new SharpTSPromiseRejectedException(destroyError));
            // Emit error event
            EmitEvent(interpreter, "error", [destroyError]);
        }
        else
        {
            TryDeliverToIterWaiter(null, end: true);
        }

        EmitEvent(interpreter, "close", []);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Pause(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _flowing = false;
        EmitEvent(interpreter, "pause", []);
        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue Resume(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        _flowing = true;
        EmitEvent(interpreter, "resume", []);

        // Drain existing buffer by emitting 'data' for each queued chunk
        while (_readBuffer.Count > 0)
        {
            var chunk = _readBuffer.Dequeue();
            EmitEvent(interpreter, "data", [chunk]);
        }

        // If already ended, emit 'end'
        if (_ended)
        {
            EmitEvent(interpreter, "end", []);
        }

        return RuntimeValue.FromObject(this);
    }

    private RuntimeValue IsPaused(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromBoolean(_flowing == false);
    }

    private void EmitEndEvent(Interp interpreter)
    {
        EmitEvent(interpreter, "end", []);
    }

    private void FlushToPipes(Interp interpreter, object? chunk)
    {
        foreach (var dest in _pipeDestinations.ToList())
        {
            var ok = WriteToDestination(interpreter, dest, chunk, _encoding);
            if (!ok && _flowing == true)
            {
                // Destination signaled backpressure — pause this readable
                _flowing = false;
                // Register a drain listener on the destination to resume
                RegisterDrainListener(interpreter, dest);
            }
        }
    }

    private void RegisterDrainListener(Interp interpreter, object dest)
    {
        var listener = new DrainResumeListener(this);
        if (dest is SharpTSWritable writable)
        {
            // Use the raw EventEmitter "once" (not Readable's flowing-mode wrapper):
            // "drain" is a writable-side event and must not flip the readable side
            // into flowing mode.
            var onceMethod = writable.GetEventEmitterMember("once") as BuiltInMethod;
            onceMethod?.Bind(writable).Call(interpreter, ["drain", listener]);
        }
        else if (dest is SharpTSDuplex duplex)
        {
            var onceMethod = duplex.GetEventEmitterMember("once") as BuiltInMethod;
            onceMethod?.Bind(duplex).Call(interpreter, ["drain", listener]);
        }
    }

    /// <summary>
    /// Listener that resumes the readable when the writable drains.
    /// Implements ISharpTSCallable for interpreter mode compatibility.
    /// </summary>
    private class DrainResumeListener : ISharpTSCallable
    {
        private readonly SharpTSReadable _readable;

        public DrainResumeListener(SharpTSReadable readable)
        {
            _readable = readable;
        }

        public int Arity() => 0;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            _readable._flowing = true;
            // Drain any buffered data
            while (_readable._readBuffer.Count > 0)
            {
                var chunk = _readable._readBuffer.Dequeue();
                _readable.EmitEvent(interpreter, "data", [chunk]);
                _readable.FlushToPipes(interpreter, chunk);
            }
            return null;
        }
    }

    private void FlushPipes(Interp interpreter)
    {
        foreach (var dest in _pipeDestinations.ToList())
        {
            EndDestination(interpreter, dest);
        }
    }

    /// <summary>
    /// When a 'data' listener is added, enter flowing mode automatically.
    /// The actual buffer drain happens in the wrapped on/addListener methods
    /// which have access to the interpreter.
    /// </summary>
    protected override void OnListenerAdded(string eventName)
    {
        if (eventName == "data" && _flowing != true)
        {
            _flowing = true;
        }
    }

    /// <summary>
    /// Resets mutable state for singleton reuse (e.g., process.stdin between interpreter runs).
    /// </summary>
    internal void ResetReadableState()
    {
        _readBuffer.Clear();
        _pipeDestinations.Clear();
        _ended = false;
        _destroyed = false;
        _readable = true;
        _flowing = null;
        ClearAllListenersInternal();
    }

    /// <summary>
    /// Drains the existing read buffer by emitting 'data' events for each queued chunk.
    /// Called after a 'data' listener is added, when the interpreter is available.
    /// Does NOT emit 'end' — that's handled separately when the 'end' listener is added.
    /// </summary>
    private void DrainBuffer(Interp interpreter)
    {
        while (_readBuffer.Count > 0)
        {
            var chunk = _readBuffer.Dequeue();
            EmitEvent(interpreter, "data", [chunk]);
        }
    }

    /// <summary>
    /// Collects all chunks from the stream into an array.
    /// </summary>
    private RuntimeValue ToArray(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var elements = new SharpTS.Runtime.Deque<object?>();
        while (_readBuffer.Count > 0)
        {
            elements.AddLast(_readBuffer.Dequeue());
        }
        return RuntimeValue.FromObject(new SharpTSArray(elements));
    }

    /// <summary>
    /// Calls fn for each chunk in the stream.
    /// </summary>
    private RuntimeValue ForEach(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("forEach() requires a function argument");

        while (_readBuffer.Count > 0)
        {
            var chunk = _readBuffer.Dequeue();
            fn.Call(interpreter, [chunk]);
        }
        return RuntimeValue.Null;
    }

    /// <summary>
    /// Creates a Transform that applies fn to each chunk.
    /// </summary>
    private RuntimeValue Map(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("map() requires a function argument");

        var transform = new SharpTSTransform();
        transform.ObjectMode = _objectMode;
        transform.SetTransformCallback(new MapTransformCallable(fn));

        // Pipe self to the transform
        var pipeMethod = GetMember("pipe") as BuiltInMethod;
        pipeMethod?.Bind(this).Call(interpreter, [transform]);

        return RuntimeValue.FromObject(transform);
    }

    /// <summary>
    /// Creates a Transform that filters chunks by predicate.
    /// </summary>
    private RuntimeValue Filter(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("filter() requires a function argument");

        var transform = new SharpTSTransform();
        transform.ObjectMode = _objectMode;
        transform.SetTransformCallback(new FilterTransformCallable(fn));

        var pipeMethod = GetMember("pipe") as BuiltInMethod;
        pipeMethod?.Bind(this).Call(interpreter, [transform]);

        return RuntimeValue.FromObject(transform);
    }

    /// <summary>
    /// Transform callable that applies a map function to each chunk.
    /// </summary>
    private class MapTransformCallable : ISharpTSCallable
    {
        private readonly ISharpTSCallable _fn;
        public MapTransformCallable(ISharpTSCallable fn) => _fn = fn;
        public int Arity() => 3;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;
            var result = _fn.Call(interpreter, [chunk]);
            callback?.Call(interpreter, [null, result]);
            return null;
        }
    }

    /// <summary>
    /// Transform callable that filters chunks by predicate.
    /// </summary>
    private class FilterTransformCallable : ISharpTSCallable
    {
        private readonly ISharpTSCallable _fn;
        public FilterTransformCallable(ISharpTSCallable fn) => _fn = fn;
        public int Arity() => 3;
        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;
            var result = _fn.Call(interpreter, [chunk]);
            if (result is true || (result is double d && d != 0))
                callback?.Call(interpreter, [null, chunk]);
            else
                callback?.Call(interpreter, [null]);
            return null;
        }
    }

    /// <summary>
    /// Gets the current buffer size in bytes (or count for object mode).
    /// </summary>
    private int GetBufferSize()
    {
        if (_objectMode)
            return _readBuffer.Count;

        int size = 0;
        foreach (var chunk in _readBuffer)
        {
            size += GetChunkLength(chunk);
        }
        return size;
    }

    // ---- Async iterator helpers (#1025) ----
    // These consume / transform the currently-buffered chunks (the same buffer model as
    // toArray/forEach). Consuming helpers return a resolved Promise; lazy helpers return a
    // new objectMode Readable carrying the transformed chunks. Interp == compiled by construction.

    internal List<object?> DrainBufferToList()
    {
        var items = new List<object?>(_readBuffer.Count);
        while (_readBuffer.Count > 0)
            items.Add(_readBuffer.Dequeue());
        return items;
    }

    private static SharpTSReadable MakeHelperReadable(bool objectMode)
    {
        var r = new SharpTSReadable { ObjectMode = objectMode };
        return r;
    }

    private RuntimeValue Reduce(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("reduce() requires a function argument");
        var items = DrainBufferToList();

        object? acc;
        int start;
        if (args.Length > 1)
        {
            acc = args[1].ToObject();
            start = 0;
        }
        else if (items.Count > 0)
        {
            acc = items[0];
            start = 1;
        }
        else
        {
            // Empty stream with no initial value — yields undefined (matches compiled parity).
            return RuntimeValue.FromObject(SharpTSPromise.Resolve(SharpTSUndefined.Instance));
        }

        for (int i = start; i < items.Count; i++)
            acc = fn.Call(interpreter, [acc, items[i]]);

        return RuntimeValue.FromObject(SharpTSPromise.Resolve(acc));
    }

    private RuntimeValue Some(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("some() requires a function argument");
        foreach (var item in DrainBufferToList())
        {
            if (RuntimeValue.FromBoxed(fn.Call(interpreter, [item])).IsTruthy())
                return RuntimeValue.FromObject(SharpTSPromise.Resolve(true));
        }
        return RuntimeValue.FromObject(SharpTSPromise.Resolve(false));
    }

    private RuntimeValue Every(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("every() requires a function argument");
        foreach (var item in DrainBufferToList())
        {
            if (!RuntimeValue.FromBoxed(fn.Call(interpreter, [item])).IsTruthy())
                return RuntimeValue.FromObject(SharpTSPromise.Resolve(false));
        }
        return RuntimeValue.FromObject(SharpTSPromise.Resolve(true));
    }

    private RuntimeValue Find(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("find() requires a function argument");
        foreach (var item in DrainBufferToList())
        {
            if (RuntimeValue.FromBoxed(fn.Call(interpreter, [item])).IsTruthy())
                return RuntimeValue.FromObject(SharpTSPromise.Resolve(item));
        }
        return RuntimeValue.FromObject(SharpTSPromise.Resolve(SharpTSUndefined.Instance));
    }

    private RuntimeValue Drop(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        int n = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
        var items = DrainBufferToList();
        var result = MakeHelperReadable(_objectMode);
        for (int i = n; i < items.Count; i++)
            result.PushFromHost(interpreter, items[i]);
        result.PushFromHost(interpreter, null);
        return RuntimeValue.FromObject(result);
    }

    private RuntimeValue Take(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        int n = args.Length > 0 && args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
        var items = DrainBufferToList();
        var result = MakeHelperReadable(_objectMode);
        for (int i = 0; i < items.Count && i < n; i++)
            result.PushFromHost(interpreter, items[i]);
        result.PushFromHost(interpreter, null);
        return RuntimeValue.FromObject(result);
    }

    private RuntimeValue FlatMap(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var fn = args.Length > 0 ? args[0].ToObject() as ISharpTSCallable : null;
        if (fn == null)
            throw new Exception("flatMap() requires a function argument");
        var result = MakeHelperReadable(true);
        foreach (var item in DrainBufferToList())
        {
            var mapped = fn.Call(interpreter, [item]);
            if (mapped is SharpTSArray arr)
            {
                foreach (var e in arr)
                    result.PushFromHost(interpreter, e);
            }
            else
            {
                result.PushFromHost(interpreter, mapped);
            }
        }
        result.PushFromHost(interpreter, null);
        return RuntimeValue.FromObject(result);
    }

    private RuntimeValue AsIndexedPairs(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var result = MakeHelperReadable(true);
        int index = 0;
        foreach (var item in DrainBufferToList())
        {
            var pairElements = new SharpTS.Runtime.Deque<object?>();
            pairElements.AddLast((double)index);
            pairElements.AddLast(item);
            result.PushFromHost(interpreter, new SharpTSArray(pairElements));
            index++;
        }
        result.PushFromHost(interpreter, null);
        return RuntimeValue.FromObject(result);
    }

    // ---- Async iteration support (#1024) ----

    private static Dictionary<string, object?> MakeIterResult(object? value, bool done)
        => new() { ["value"] = done ? SharpTSUndefined.Instance : value, ["done"] = done };

    /// <summary>
    /// Pulls the next chunk for <c>[Symbol.asyncIterator]().next()</c>. Returns a Task that
    /// resolves to an <c>{ value, done }</c> record. When the buffer is empty and the stream
    /// is still live, the returned task is parked until the next push / end / error so a slow
    /// (asynchronous) producer is awaited rather than ending the loop prematurely.
    /// </summary>
    internal System.Threading.Tasks.Task<object?> IterNextAsync()
    {
        if (_readBuffer.Count > 0)
            return System.Threading.Tasks.Task.FromResult<object?>(MakeIterResult(_readBuffer.Dequeue(), false));
        if (_errored)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<object?>();
            tcs.SetException(new SharpTSPromiseRejectedException(_error));
            return tcs.Task;
        }
        if (_ended || _destroyed)
            return System.Threading.Tasks.Task.FromResult<object?>(MakeIterResult(null, true));

        // Park until a producer pushes more, ends, or errors.
        _iterWaiter = new System.Threading.Tasks.TaskCompletionSource<object?>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        return _iterWaiter.Task;
    }

    /// <summary>
    /// Cleanup for an early <c>for await</c> exit (break/return/throw) — destroys the stream
    /// and settles any parked pull as done.
    /// </summary>
    internal void IterReturn()
    {
        if (!_destroyed)
        {
            _destroyed = true;
            _readable = false;
            _readBuffer.Clear();
            _pipeDestinations.Clear();
        }
        var waiter = _iterWaiter;
        _iterWaiter = null;
        waiter?.TrySetResult(MakeIterResult(null, true));
    }

    /// <summary>
    /// Hands a value to a parked async-iterator pull, if one is waiting. Returns true when the
    /// value was consumed by the iterator (so the caller must not also buffer / emit it).
    /// </summary>
    private bool TryDeliverToIterWaiter(object? chunk, bool end)
    {
        var waiter = _iterWaiter;
        if (waiter == null)
            return false;
        _iterWaiter = null;
        waiter.TrySetResult(end ? MakeIterResult(null, true) : MakeIterResult(chunk, false));
        return true;
    }

    public override string ToString() => "Readable {}";
}
