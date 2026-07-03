using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of <c>primitive:zlib</c> — the narrow C#
/// surface behind the stdlib/node/zlib.ts facade.
/// </summary>
/// <remarks>
/// Provides the compression cores and stateful streams TypeScript cannot express:
/// - Sync one-shots: gzipSync/gunzipSync, deflateSync/inflateSync,
///   deflateRawSync/inflateRawSync, brotliCompress/DecompressSync,
///   zstdCompress/DecompressSync, unzipSync
/// - Streaming create*: Transform streams over BCL compression streams
/// - crc32: tight bitwise checksum loop
/// The Node-shape glue (constants/codes objects, async callback forms,
/// argument shuffling) lives in the TS facade.
/// </remarks>
public static class ZlibModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the primitive:zlib module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Gzip
            ["gzipSync"] = BuiltInMethod.CreateV2("gzipSync", 1, 2, GzipSync),
            ["gunzipSync"] = BuiltInMethod.CreateV2("gunzipSync", 1, 2, GunzipSync),

            // Deflate (with zlib header)
            ["deflateSync"] = BuiltInMethod.CreateV2("deflateSync", 1, 2, DeflateSync),
            ["inflateSync"] = BuiltInMethod.CreateV2("inflateSync", 1, 2, InflateSync),

            // DeflateRaw (no header)
            ["deflateRawSync"] = BuiltInMethod.CreateV2("deflateRawSync", 1, 2, DeflateRawSync),
            ["inflateRawSync"] = BuiltInMethod.CreateV2("inflateRawSync", 1, 2, InflateRawSync),

            // Brotli
            ["brotliCompressSync"] = BuiltInMethod.CreateV2("brotliCompressSync", 1, 2, BrotliCompressSync),
            ["brotliDecompressSync"] = BuiltInMethod.CreateV2("brotliDecompressSync", 1, 2, BrotliDecompressSync),

            // Zstd
            ["zstdCompressSync"] = BuiltInMethod.CreateV2("zstdCompressSync", 1, 2, ZstdCompressSync),
            ["zstdDecompressSync"] = BuiltInMethod.CreateV2("zstdDecompressSync", 1, 2, ZstdDecompressSync),

            // Unzip (auto-detect gzip/deflate)
            ["unzipSync"] = BuiltInMethod.CreateV2("unzipSync", 1, 2, UnzipSync),

            // Streaming APIs (Transform streams)
            ["createGzip"] = BuiltInMethod.CreateV2("createGzip", 0, 1, CreateGzip),
            ["createGunzip"] = BuiltInMethod.CreateV2("createGunzip", 0, 1, CreateGunzip),
            ["createDeflate"] = BuiltInMethod.CreateV2("createDeflate", 0, 1, CreateDeflate),
            ["createInflate"] = BuiltInMethod.CreateV2("createInflate", 0, 1, CreateInflate),
            ["createDeflateRaw"] = BuiltInMethod.CreateV2("createDeflateRaw", 0, 1, CreateDeflateRaw),
            ["createInflateRaw"] = BuiltInMethod.CreateV2("createInflateRaw", 0, 1, CreateInflateRaw),
            ["createBrotliCompress"] = BuiltInMethod.CreateV2("createBrotliCompress", 0, 1, CreateBrotliCompress),
            ["createBrotliDecompress"] = BuiltInMethod.CreateV2("createBrotliDecompress", 0, 1, CreateBrotliDecompress),
            ["createZstdCompress"] = BuiltInMethod.CreateV2("createZstdCompress", 0, 1, CreateZstdCompress),
            ["createZstdDecompress"] = BuiltInMethod.CreateV2("createZstdDecompress", 0, 1, CreateZstdDecompress),
            ["createUnzip"] = BuiltInMethod.CreateV2("createUnzip", 0, 1, CreateUnzip),

            // Checksums (Node 22+)
            ["crc32"] = BuiltInMethod.CreateV2("crc32", 1, 2, Crc32Method)
        };
    }

    #region Checksums

    private static RuntimeValue Crc32Method(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "crc32");

        // Optional running value (Node: crc32(data[, value])). A 32-bit unsigned int.
        uint initial = 0;
        if (args.Length > 1 && args[1].IsNumber)
            initial = unchecked((uint)(long)args[1].AsNumberUnsafe());

        return RuntimeValue.FromNumber(ZlibHelpers.Crc32(input, initial));
    }

    #endregion

    #region Gzip

    private static RuntimeValue GzipSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "gzipSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.GzipCompress(input, options);
        return RuntimeValue.FromObject(new SharpTSBuffer(result));
    }

    private static RuntimeValue GunzipSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "gunzipSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.GzipDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region Deflate

    private static RuntimeValue DeflateSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "deflateSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.DeflateCompress(input, options);
        return RuntimeValue.FromObject(new SharpTSBuffer(result));
    }

    private static RuntimeValue InflateSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "inflateSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.DeflateDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region DeflateRaw

    private static RuntimeValue DeflateRawSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "deflateRawSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.DeflateRawCompress(input, options);
        return RuntimeValue.FromObject(new SharpTSBuffer(result));
    }

    private static RuntimeValue InflateRawSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "inflateRawSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.DeflateRawDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region Brotli

    private static RuntimeValue BrotliCompressSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "brotliCompressSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.BrotliCompress(input, options);
        return RuntimeValue.FromObject(new SharpTSBuffer(result));
    }

    private static RuntimeValue BrotliDecompressSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "brotliDecompressSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.BrotliDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: Decompression failed");
        }
    }

    #endregion

    #region Zstd

    private static RuntimeValue ZstdCompressSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "zstdCompressSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.ZstdCompress(input, options);
        return RuntimeValue.FromObject(new SharpTSBuffer(result));
    }

    private static RuntimeValue ZstdDecompressSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "zstdDecompressSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.ZstdDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new Exception($"Error: Zstd decompression failed: {ex.Message}");
        }
    }

    #endregion

    #region Unzip (Auto-detect)

    private static RuntimeValue UnzipSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var input = GetInputBytes(args, 0, "unzipSync");
        var options = GetOptions(args, 1);

        // Try to auto-detect format from magic bytes
        if (input.Length >= 2)
        {
            // Gzip magic: 0x1f 0x8b
            if (input[0] == 0x1f && input[1] == 0x8b)
            {
                var result = ZlibHelpers.GzipDecompress(input, options);
                return RuntimeValue.FromObject(new SharpTSBuffer(result));
            }

            // Zlib header: first byte typically 0x78 (deflate)
            // 0x78 0x01 = no compression
            // 0x78 0x5e = fast compression
            // 0x78 0x9c = default compression
            // 0x78 0xda = best compression
            if (input[0] == 0x78)
            {
                var result = ZlibHelpers.DeflateDecompress(input, options);
                return RuntimeValue.FromObject(new SharpTSBuffer(result));
            }
        }

        // Fallback: try raw deflate
        try
        {
            var result = ZlibHelpers.DeflateRawDecompress(input, options);
            return RuntimeValue.FromObject(new SharpTSBuffer(result));
        }
        catch
        {
            throw new Exception("Error: unknown compression format");
        }
    }

    #endregion

    #region Streaming APIs

    private static RuntimeValue CreateGzip(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.Gzip, GetOptions(args, 0)));

    private static RuntimeValue CreateGunzip(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.Gunzip, GetOptions(args, 0)));

    private static RuntimeValue CreateDeflate(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.Deflate, GetOptions(args, 0)));

    private static RuntimeValue CreateInflate(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.Inflate, GetOptions(args, 0)));

    private static RuntimeValue CreateDeflateRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.DeflateRaw, GetOptions(args, 0)));

    private static RuntimeValue CreateInflateRaw(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.InflateRaw, GetOptions(args, 0)));

    private static RuntimeValue CreateBrotliCompress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.BrotliCompress, GetOptions(args, 0)));

    private static RuntimeValue CreateBrotliDecompress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.BrotliDecompress, GetOptions(args, 0)));

    private static RuntimeValue CreateZstdCompress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.ZstdCompress, GetOptions(args, 0)));

    private static RuntimeValue CreateZstdDecompress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.ZstdDecompress, GetOptions(args, 0)));

    private static RuntimeValue CreateUnzip(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromObject(new SharpTSZlibTransform(ZlibTransformKind.Unzip, GetOptions(args, 0)));

    #endregion

    #region Helpers

    /// <summary>
    /// Extracts input bytes from argument (Buffer or string).
    /// </summary>
    private static byte[] GetInputBytes(ReadOnlySpan<RuntimeValue> args, int index, string methodName)
    {
        if (args.Length <= index || args[index].IsNull)
            throw new Exception($"{methodName} requires a Buffer or string argument");

        return args[index].ToObject() switch
        {
            SharpTSBuffer buffer => buffer.Data,
            string str => System.Text.Encoding.UTF8.GetBytes(str),
            SharpTSArray array => ArrayToBytes(array),
            _ => throw new Exception($"{methodName} requires a Buffer or string argument")
        };
    }

    /// <summary>
    /// Converts a SharpTSArray to byte array.
    /// </summary>
    private static byte[] ArrayToBytes(SharpTSArray array)
    {
        var bytes = new byte[array.Length];
        for (int i = 0; i < array.Length; i++)
        {
            bytes[i] = array[i] switch
            {
                double d => (byte)((int)d & 0xFF),
                int n => (byte)(n & 0xFF),
                _ => 0
            };
        }
        return bytes;
    }

    /// <summary>
    /// Extracts options object from arguments.
    /// </summary>
    private static ZlibOptions GetOptions(ReadOnlySpan<RuntimeValue> args, int index)
    {
        if (args.Length <= index || args[index].IsNull)
            return new ZlibOptions();

        return ZlibOptions.FromValue(args[index].ToObject());
    }

    #endregion
}
