using System.IO.Compression;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Zlib compression/decompression transform type.
/// </summary>
public enum ZlibTransformKind
{
    Gzip,
    Gunzip,
    Deflate,
    Inflate,
    DeflateRaw,
    InflateRaw,
    BrotliCompress,
    BrotliDecompress,
    Unzip,
    ZstdCompress,
    ZstdDecompress
}

/// <summary>
/// Runtime representation of a zlib streaming Transform (e.g., createGzip(), createGunzip()).
/// Extends SharpTSTransform to provide compression/decompression as a Transform stream.
/// </summary>
/// <remarks>
/// Accumulates input chunks, then compresses/decompresses on flush.
/// For compression, true streaming is used (output produced per chunk).
/// </remarks>
public class SharpTSZlibTransform : SharpTSTransform
{
    private readonly ZlibTransformKind _kind;
    private ZlibOptions _options;

    // For compression: streaming output
    private MemoryStream? _outputBuffer;
    private Stream? _compressionStream;

    // For decompression: accumulate then decompress
    private MemoryStream? _inputBuffer;

    // Total bytes written INTO the engine (input). Node's bytesWritten; bytesRead is
    // a deprecated alias of the same value.
    private long _bytesWritten;

    public SharpTSZlibTransform(ZlibTransformKind kind, ZlibOptions options)
    {
        _kind = kind;
        _options = options;

        if (IsCompression)
        {
            _outputBuffer = new MemoryStream();
            _compressionStream = CreateCompressionStream(_outputBuffer);
        }
        else
        {
            _inputBuffer = new MemoryStream();
        }

        // Set our internal transform and flush callbacks
        SetTransformCallback(new ZlibTransformCallback(this));
        SetFlushCallback(new ZlibFlushCallback(this));
    }

    private bool IsCompression => _kind is ZlibTransformKind.Gzip
        or ZlibTransformKind.Deflate
        or ZlibTransformKind.DeflateRaw
        or ZlibTransformKind.BrotliCompress
        or ZlibTransformKind.ZstdCompress;

    private Stream CreateCompressionStream(MemoryStream output)
    {
        var level = _options.ToCompressionLevel();
        return _kind switch
        {
            ZlibTransformKind.Gzip => new GZipStream(output, level, leaveOpen: true),
            ZlibTransformKind.Deflate => new ZLibStream(output, level, leaveOpen: true),
            ZlibTransformKind.DeflateRaw => new DeflateStream(output, level, leaveOpen: true),
            ZlibTransformKind.BrotliCompress => new BrotliStream(output, level, leaveOpen: true),
            ZlibTransformKind.ZstdCompress => new ZstdSharp.CompressionStream(output, _options.GetZstdLevel(), 0, leaveOpen: true),
            _ => throw new InvalidOperationException($"Not a compression kind: {_kind}")
        };
    }

    /// <summary>
    /// Gets a zlib-stream-specific member, falling back to the Transform/Duplex base.
    /// Adds the Node zlib control methods (params/flush/reset/close) and the
    /// bytesWritten/bytesRead counters on top of the generic stream surface.
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            // Node's bytesWritten = bytes written into the engine; bytesRead is a
            // deprecated alias returning the same value.
            "bytesWritten" => (double)_bytesWritten,
            "bytesRead" => (double)_bytesWritten,

            "flush" => BuiltInMethod.CreateV2("flush", 0, 2, FlushMethod),
            "params" => BuiltInMethod.CreateV2("params", 2, 3, ParamsMethod),
            "reset" => BuiltInMethod.CreateV2("reset", 0, ResetMethod),
            "close" => BuiltInMethod.CreateV2("close", 0, 1, CloseMethod),

            _ => base.GetMember(name)
        };
    }

    /// <summary>
    /// flush([kind][, callback]). Pushes any buffered compressor output and invokes
    /// the callback. BCL ceiling: <c>System.IO.Compression</c> streams cannot emit a
    /// true zlib sync/full-flush boundary, so a flushed point is not independently
    /// decodable the way Node's would be — the data still round-trips at end().
    /// </summary>
    private RuntimeValue FlushMethod(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = ExtractCallback(args);
        if (IsCompression && _compressionStream != null)
        {
            _compressionStream.Flush();
            PushOutputBuffer(interpreter);
        }
        callback?.Call(interpreter, []);
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// params(level, strategy[, callback]). BCL ceiling: the level/strategy of an
    /// in-flight compression stream cannot be retuned, so the new values are recorded
    /// (affecting a subsequent reset()) and the stream is flushed, but they are not
    /// applied to already-written data. The callback is invoked on completion.
    /// </summary>
    private RuntimeValue ParamsMethod(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsNumber)
            _options.Level = (int)args[0].AsNumberUnsafe();
        if (args.Length > 1 && args[1].IsNumber)
            _options.Strategy = (int)args[1].AsNumberUnsafe();

        var callback = args.Length > 2 ? args[2].ToObject() as ISharpTSCallable : null;
        if (IsCompression && _compressionStream != null)
        {
            _compressionStream.Flush();
            PushOutputBuffer(interpreter);
        }
        callback?.Call(interpreter, []);
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// reset(). Restores the stream to its initial state for reuse and zeroes the
    /// byte counter (honoring any params() changes for the fresh stream).
    /// </summary>
    private RuntimeValue ResetMethod(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (IsCompression)
        {
            _compressionStream?.Dispose();
            _outputBuffer = new MemoryStream();
            _compressionStream = CreateCompressionStream(_outputBuffer);
        }
        else
        {
            _inputBuffer = new MemoryStream();
        }
        _bytesWritten = 0;
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// close([callback]). Releases the underlying streams, emits 'close', and invokes
    /// the callback.
    /// </summary>
    private RuntimeValue CloseMethod(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = ExtractCallback(args);
        _compressionStream?.Dispose();
        _compressionStream = null;
        _inputBuffer?.Dispose();
        _inputBuffer = null;
        EmitEvent(interpreter, "close", []);
        callback?.Call(interpreter, []);
        return RuntimeValue.Undefined;
    }

    private static ISharpTSCallable? ExtractCallback(ReadOnlySpan<RuntimeValue> args)
    {
        for (int i = args.Length - 1; i >= 0; i--)
            if (args[i].ToObject() is ISharpTSCallable cb)
                return cb;
        return null;
    }

    private void TransformChunk(Interp interpreter, object? chunk)
    {
        var bytes = ChunkToBytes(chunk);
        if (bytes.Length == 0) return;

        _bytesWritten += bytes.Length;

        if (IsCompression)
        {
            // Write to compression stream, flush, and push whatever comes out
            _compressionStream!.Write(bytes, 0, bytes.Length);
            _compressionStream.Flush();
            PushOutputBuffer(interpreter);
        }
        else
        {
            // Accumulate for decompression
            _inputBuffer!.Write(bytes, 0, bytes.Length);
        }
    }

    private void Flush(Interp interpreter)
    {
        if (IsCompression)
        {
            // Dispose compression stream to write final data
            _compressionStream!.Dispose();
            _compressionStream = null;
            PushOutputBuffer(interpreter);
        }
        else
        {
            // Decompress accumulated data
            var inputBytes = _inputBuffer!.ToArray();
            _inputBuffer.Dispose();
            _inputBuffer = null;

            if (inputBytes.Length > 0)
            {
                var result = Decompress(inputBytes);
                PushToReadableSide(interpreter, new SharpTSBuffer(result));
            }
        }
    }

    private void PushOutputBuffer(Interp interpreter)
    {
        if (_outputBuffer == null || _outputBuffer.Position == 0) return;

        var data = _outputBuffer.ToArray();
        // Reset the buffer for next chunk
        _outputBuffer.SetLength(0);

        if (data.Length > 0)
        {
            PushToReadableSide(interpreter, new SharpTSBuffer(data));
        }
    }

    private byte[] Decompress(byte[] input)
    {
        var actualKind = _kind;
        if (actualKind == ZlibTransformKind.Unzip)
        {
            actualKind = DetectFormat(input);
        }

        return actualKind switch
        {
            ZlibTransformKind.Gunzip => ZlibHelpers.GzipDecompress(input, _options),
            ZlibTransformKind.Inflate => ZlibHelpers.DeflateDecompress(input, _options),
            ZlibTransformKind.InflateRaw => ZlibHelpers.DeflateRawDecompress(input, _options),
            ZlibTransformKind.BrotliDecompress => ZlibHelpers.BrotliDecompress(input, _options),
            ZlibTransformKind.ZstdDecompress => ZlibHelpers.ZstdDecompress(input, _options),
            ZlibTransformKind.Unzip => ZlibHelpers.DeflateRawDecompress(input, _options),
            _ => throw new InvalidOperationException($"Not a decompression kind: {_kind}")
        };
    }

    private static ZlibTransformKind DetectFormat(byte[] input)
    {
        if (input.Length >= 2)
        {
            if (input[0] == 0x1f && input[1] == 0x8b)
                return ZlibTransformKind.Gunzip;
            if (input[0] == 0x78)
                return ZlibTransformKind.Inflate;
        }
        return ZlibTransformKind.InflateRaw;
    }

    private static byte[] ChunkToBytes(object? chunk)
    {
        return chunk switch
        {
            SharpTSBuffer buffer => buffer.Data,
            string str => System.Text.Encoding.UTF8.GetBytes(str),
            _ => []
        };
    }

    public override string ToString() => _kind switch
    {
        ZlibTransformKind.Gzip => "Gzip {}",
        ZlibTransformKind.Gunzip => "Gunzip {}",
        ZlibTransformKind.Deflate => "Deflate {}",
        ZlibTransformKind.Inflate => "Inflate {}",
        ZlibTransformKind.DeflateRaw => "DeflateRaw {}",
        ZlibTransformKind.InflateRaw => "InflateRaw {}",
        ZlibTransformKind.BrotliCompress => "BrotliCompress {}",
        ZlibTransformKind.BrotliDecompress => "BrotliDecompress {}",
        ZlibTransformKind.Unzip => "Unzip {}",
        ZlibTransformKind.ZstdCompress => "ZstdCompress {}",
        ZlibTransformKind.ZstdDecompress => "ZstdDecompress {}",
        _ => "ZlibTransform {}"
    };

    private class ZlibTransformCallback : ISharpTSCallable
    {
        private readonly SharpTSZlibTransform _stream;
        public ZlibTransformCallback(SharpTSZlibTransform stream) => _stream = stream;
        public int Arity() => 3;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;

            try
            {
                _stream.TransformChunk(interpreter, chunk);
                callback?.Call(interpreter, [null]);
            }
            catch (Exception ex)
            {
                callback?.Call(interpreter, [ex.Message]);
            }

            return null;
        }
    }

    private class ZlibFlushCallback : ISharpTSCallable
    {
        private readonly SharpTSZlibTransform _stream;
        public ZlibFlushCallback(SharpTSZlibTransform stream) => _stream = stream;
        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var callback = arguments.Count > 0 ? arguments[0] as ISharpTSCallable : null;

            try
            {
                _stream.Flush(interpreter);
                callback?.Call(interpreter, [null]);
            }
            catch (Exception ex)
            {
                callback?.Call(interpreter, [ex.Message]);
            }

            return null;
        }
    }
}
