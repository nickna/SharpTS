using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'zlib' module.
/// </summary>
/// <remarks>
/// Provides compression/decompression functionality:
/// - gzipSync/gunzipSync: Gzip format
/// - deflateSync/inflateSync: Deflate with zlib header
/// - deflateRawSync/inflateRawSync: Raw deflate (no header)
/// - brotliCompressSync/brotliDecompressSync: Brotli format
/// - zstdCompressSync/zstdDecompressSync: Zstandard format
/// - constants: Compression constants object
/// </remarks>
public static class ZlibModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the zlib module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Gzip
            ["gzipSync"] = new BuiltInMethod("gzipSync", 1, 2, GzipSync),
            ["gunzipSync"] = new BuiltInMethod("gunzipSync", 1, 2, GunzipSync),

            // Deflate (with zlib header)
            ["deflateSync"] = new BuiltInMethod("deflateSync", 1, 2, DeflateSync),
            ["inflateSync"] = new BuiltInMethod("inflateSync", 1, 2, InflateSync),

            // DeflateRaw (no header)
            ["deflateRawSync"] = new BuiltInMethod("deflateRawSync", 1, 2, DeflateRawSync),
            ["inflateRawSync"] = new BuiltInMethod("inflateRawSync", 1, 2, InflateRawSync),

            // Brotli
            ["brotliCompressSync"] = new BuiltInMethod("brotliCompressSync", 1, 2, BrotliCompressSync),
            ["brotliDecompressSync"] = new BuiltInMethod("brotliDecompressSync", 1, 2, BrotliDecompressSync),

            // Zstd
            ["zstdCompressSync"] = new BuiltInMethod("zstdCompressSync", 1, 2, ZstdCompressSync),
            ["zstdDecompressSync"] = new BuiltInMethod("zstdDecompressSync", 1, 2, ZstdDecompressSync),

            // Unzip (auto-detect gzip/deflate)
            ["unzipSync"] = new BuiltInMethod("unzipSync", 1, 2, UnzipSync),

            // Constants
            ["constants"] = ZlibConstants.CreateConstantsObject()
        };
    }

    #region Gzip

    private static object? GzipSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "gzipSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.GzipCompress(input, options);
        return new SharpTSBuffer(result);
    }

    private static object? GunzipSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "gunzipSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.GzipDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region Deflate

    private static object? DeflateSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "deflateSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.DeflateCompress(input, options);
        return new SharpTSBuffer(result);
    }

    private static object? InflateSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "inflateSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.DeflateDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region DeflateRaw

    private static object? DeflateRawSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "deflateRawSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.DeflateRawCompress(input, options);
        return new SharpTSBuffer(result);
    }

    private static object? InflateRawSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "inflateRawSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.DeflateRawDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: incorrect header check");
        }
    }

    #endregion

    #region Brotli

    private static object? BrotliCompressSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "brotliCompressSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.BrotliCompress(input, options);
        return new SharpTSBuffer(result);
    }

    private static object? BrotliDecompressSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "brotliDecompressSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.BrotliDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch (InvalidDataException)
        {
            throw new Exception("Error: Decompression failed");
        }
    }

    #endregion

    #region Zstd

    private static object? ZstdCompressSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "zstdCompressSync");
        var options = GetOptions(args, 1);

        var result = ZlibHelpers.ZstdCompress(input, options);
        return new SharpTSBuffer(result);
    }

    private static object? ZstdDecompressSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var input = GetInputBytes(args, 0, "zstdDecompressSync");
        var options = GetOptions(args, 1);

        try
        {
            var result = ZlibHelpers.ZstdDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new Exception($"Error: Zstd decompression failed: {ex.Message}");
        }
    }

    #endregion

    #region Unzip (Auto-detect)

    private static object? UnzipSync(Interp interpreter, object? receiver, List<object?> args)
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
                return new SharpTSBuffer(result);
            }

            // Zlib header: first byte typically 0x78 (deflate)
            // 0x78 0x01 = no compression
            // 0x78 0x5e = fast compression
            // 0x78 0x9c = default compression
            // 0x78 0xda = best compression
            if (input[0] == 0x78)
            {
                var result = ZlibHelpers.DeflateDecompress(input, options);
                return new SharpTSBuffer(result);
            }
        }

        // Fallback: try raw deflate
        try
        {
            var result = ZlibHelpers.DeflateRawDecompress(input, options);
            return new SharpTSBuffer(result);
        }
        catch
        {
            throw new Exception("Error: unknown compression format");
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Extracts input bytes from argument (Buffer or string).
    /// </summary>
    private static byte[] GetInputBytes(List<object?> args, int index, string methodName)
    {
        if (args.Count <= index || args[index] == null)
            throw new Exception($"{methodName} requires a Buffer or string argument");

        return args[index] switch
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
        var bytes = new byte[array.Elements.Count];
        for (int i = 0; i < array.Elements.Count; i++)
        {
            bytes[i] = array.Elements[i] switch
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
    private static ZlibOptions GetOptions(List<object?> args, int index)
    {
        if (args.Count <= index || args[index] == null)
            return new ZlibOptions();

        return ZlibOptions.FromValue(args[index]);
    }

    #endregion
}
