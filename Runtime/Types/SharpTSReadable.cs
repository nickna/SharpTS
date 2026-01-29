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
    private readonly List<SharpTSWritable> _pipeDestinations = [];
    private bool _ended;
    private bool _destroyed;
    private string _encoding = "utf8";
    private bool _readable = true;

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public new object? GetMember(string name)
    {
        return name switch
        {
            // Readable-specific methods
            "read" => new BuiltInMethod("read", 0, 1, Read),
            "push" => new BuiltInMethod("push", 1, Push),
            "pipe" => new BuiltInMethod("pipe", 1, 2, Pipe),
            "unpipe" => new BuiltInMethod("unpipe", 0, 1, Unpipe),
            "setEncoding" => new BuiltInMethod("setEncoding", 1, SetEncoding),
            "destroy" => new BuiltInMethod("destroy", 0, 1, Destroy),
            "unshift" => new BuiltInMethod("unshift", 1, Unshift),
            "pause" => new BuiltInMethod("pause", 0, Pause),
            "resume" => new BuiltInMethod("resume", 0, Resume),
            "isPaused" => new BuiltInMethod("isPaused", 0, IsPaused),

            // Properties
            "readable" => _readable && !_ended && !_destroyed,
            "readableEnded" => _ended,
            "readableLength" => (double)_readBuffer.Count,
            "readableEncoding" => _encoding,
            "destroyed" => _destroyed,

            // Inherit from EventEmitter
            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// Reads data from the stream.
    /// </summary>
    private object? Read(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed || _readBuffer.Count == 0)
        {
            return null;
        }

        int? size = null;
        if (args.Count > 0 && args[0] is double d)
        {
            size = (int)d;
        }

        if (size == null || size <= 0)
        {
            // Read all available data
            if (_readBuffer.Count == 0)
            {
                return null;
            }

            var chunks = new List<object?>();
            while (_readBuffer.Count > 0)
            {
                chunks.Add(_readBuffer.Dequeue());
            }

            // Concatenate all chunks
            return ConcatenateChunks(chunks);
        }
        else
        {
            // Read specified amount
            return ReadSize(size.Value);
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
    /// Pushes data into the stream buffer.
    /// </summary>
    private object? Push(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed)
        {
            return false;
        }

        var chunk = args.Count > 0 ? args[0] : null;

        if (chunk == null)
        {
            // EOF signal
            _ended = true;
            _readable = false;
            EmitEndEvent(interpreter);
            FlushPipes(interpreter);
            return false;
        }

        _readBuffer.Enqueue(chunk);

        // In sync mode, immediately pipe to destinations
        FlushToPipes(interpreter, chunk);

        return true;
    }

    /// <summary>
    /// Pushes data back to the front of the buffer.
    /// </summary>
    private object? Unshift(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed || _ended)
        {
            return null;
        }

        var chunk = args.Count > 0 ? args[0] : null;
        if (chunk == null)
        {
            return null;
        }

        var temp = _readBuffer.ToList();
        _readBuffer.Clear();
        _readBuffer.Enqueue(chunk);
        foreach (var item in temp)
        {
            _readBuffer.Enqueue(item);
        }

        return this;
    }

    /// <summary>
    /// Pipes this readable to a writable destination.
    /// </summary>
    private object? Pipe(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
        {
            throw new Exception("pipe() destination must be a Writable stream");
        }

        var destObj = args[0];

        // Accept SharpTSWritable or any Duplex-derived type (which has Writable capabilities)
        if (destObj is not SharpTSWritable && destObj is not SharpTSDuplex)
        {
            throw new Exception("pipe() destination must be a Writable stream");
        }

        // Store the destination for piping (only SharpTSWritable, not Duplex - handled separately)
        if (destObj is SharpTSWritable writable)
        {
            _pipeDestinations.Add(writable);
        }

        // Check options for end: false
        bool shouldEnd = true;
        if (args.Count > 1 && args[1] is SharpTSObject options)
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
            WriteToDestination(interpreter, destObj, chunk, _encoding);
        }

        // If already ended, end the destination
        if (shouldEnd && _ended)
        {
            EndDestination(interpreter, destObj);
        }

        return destObj;
    }

    /// <summary>
    /// Writes a chunk to any writable-like destination.
    /// </summary>
    private static void WriteToDestination(Interp interpreter, object destObj, object? chunk, string encoding)
    {
        if (destObj is SharpTSWritable writable)
        {
            writable.WriteInternal(interpreter, chunk, encoding);
        }
        else if (destObj is SharpTSPassThrough passThrough)
        {
            // PassThrough - call via GetMember to get the inherited Transform write method
            var writeMethod = passThrough.GetMember("write") as BuiltInMethod;
            writeMethod?.Bind(passThrough).Call(interpreter, [chunk, encoding]);
        }
        else if (destObj is SharpTSTransform transform)
        {
            // Transform - call via GetMember which returns the TransformWrite method
            var writeMethod = transform.GetMember("write") as BuiltInMethod;
            writeMethod?.Bind(transform).Call(interpreter, [chunk, encoding]);
        }
        else if (destObj is SharpTSDuplex duplex)
        {
            // For Duplex streams, call the write method
            var writeMethod = duplex.GetMember("write") as BuiltInMethod;
            writeMethod?.Bind(duplex).Call(interpreter, [chunk, encoding]);
        }
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
    private object? Unpipe(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count > 0 && args[0] is SharpTSWritable dest)
        {
            _pipeDestinations.Remove(dest);
        }
        else
        {
            _pipeDestinations.Clear();
        }

        return this;
    }

    /// <summary>
    /// Sets the encoding for string output.
    /// </summary>
    private object? SetEncoding(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count > 0 && args[0] is string enc)
        {
            _encoding = enc.ToLowerInvariant();
        }
        return this;
    }

    /// <summary>
    /// Destroys the stream.
    /// </summary>
    private object? Destroy(Interp interpreter, object? receiver, List<object?> args)
    {
        if (_destroyed)
        {
            return this;
        }

        _destroyed = true;
        _readable = false;
        _readBuffer.Clear();
        _pipeDestinations.Clear();

        if (args.Count > 0 && args[0] != null)
        {
            // Emit error event
            EmitEvent(interpreter, "error", [args[0]]);
        }

        EmitEvent(interpreter, "close", []);
        return this;
    }

    private object? Pause(Interp interpreter, object? receiver, List<object?> args)
    {
        // In sync mode, pause is a no-op but we track state for compatibility
        return this;
    }

    private object? Resume(Interp interpreter, object? receiver, List<object?> args)
    {
        // In sync mode, resume is a no-op
        return this;
    }

    private object? IsPaused(Interp interpreter, object? receiver, List<object?> args)
    {
        // In sync mode, always return false
        return false;
    }

    private void EmitEndEvent(Interp interpreter)
    {
        EmitEvent(interpreter, "end", []);
    }

    private void FlushToPipes(Interp interpreter, object? chunk)
    {
        foreach (var dest in _pipeDestinations)
        {
            dest.WriteInternal(interpreter, chunk, _encoding);
        }
    }

    private void FlushPipes(Interp interpreter)
    {
        foreach (var dest in _pipeDestinations)
        {
            dest.EndInternal(interpreter, null, null);
        }
    }

    internal void EmitEvent(Interp interpreter, string eventName, List<object?> args)
    {
        // Call the emit method from the base EventEmitter
        var emit = base.GetMember("emit") as BuiltInMethod;
        if (emit != null)
        {
            var fullArgs = new List<object?> { eventName };
            fullArgs.AddRange(args);
            emit.Bind(this).Call(interpreter, fullArgs);
        }
    }

    public override string ToString() => "Readable {}";
}
