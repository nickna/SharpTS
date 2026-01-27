using System.IO.Compression;
using ZstdSharp;

namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Shared compression/decompression helper methods for zlib module.
/// Used by both interpreter and compiler modes.
/// </summary>
/// <remarks>
/// Supports:
/// - Gzip: GZipStream (compress/decompress)
/// - Deflate: ZLibStream (compress/decompress) - zlib header
/// - DeflateRaw: DeflateStream (compress/decompress) - no header
/// - Brotli: BrotliStream (compress/decompress)
/// - Zstd: ZstdSharp (compress/decompress)
/// </remarks>
public static class ZlibHelpers
{
    #region Gzip

    /// <summary>
    /// Compresses data using gzip format.
    /// </summary>
    /// <param name="input">Input bytes to compress.</param>
    /// <param name="options">Compression options.</param>
    /// <returns>Gzip-compressed bytes.</returns>
    public static byte[] GzipCompress(byte[] input, ZlibOptions options)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, options.ToCompressionLevel(), leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }
        var result = output.ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "gzip");
        return result;
    }

    /// <summary>
    /// Decompresses gzip-compressed data.
    /// </summary>
    /// <param name="input">Gzip-compressed bytes.</param>
    /// <param name="options">Decompression options.</param>
    /// <returns>Decompressed bytes.</returns>
    public static byte[] GzipDecompress(byte[] input, ZlibOptions options)
    {
        using var inputStream = new MemoryStream(input);
        using var gzip = new GZipStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();

        CopyWithLimit(gzip, output, options);
        return output.ToArray();
    }

    #endregion

    #region Deflate (with zlib header)

    /// <summary>
    /// Compresses data using deflate format with zlib header.
    /// </summary>
    /// <param name="input">Input bytes to compress.</param>
    /// <param name="options">Compression options.</param>
    /// <returns>Deflate-compressed bytes with zlib header.</returns>
    public static byte[] DeflateCompress(byte[] input, ZlibOptions options)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, options.ToCompressionLevel(), leaveOpen: true))
        {
            deflate.Write(input, 0, input.Length);
        }
        var result = output.ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "deflate");
        return result;
    }

    /// <summary>
    /// Decompresses deflate-compressed data with zlib header.
    /// </summary>
    /// <param name="input">Deflate-compressed bytes with zlib header.</param>
    /// <param name="options">Decompression options.</param>
    /// <returns>Decompressed bytes.</returns>
    public static byte[] DeflateDecompress(byte[] input, ZlibOptions options)
    {
        using var inputStream = new MemoryStream(input);
        using var deflate = new ZLibStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();

        CopyWithLimit(deflate, output, options);
        return output.ToArray();
    }

    #endregion

    #region DeflateRaw (no header)

    /// <summary>
    /// Compresses data using raw deflate format (no zlib header).
    /// </summary>
    /// <param name="input">Input bytes to compress.</param>
    /// <param name="options">Compression options.</param>
    /// <returns>Raw deflate-compressed bytes.</returns>
    public static byte[] DeflateRawCompress(byte[] input, ZlibOptions options)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, options.ToCompressionLevel(), leaveOpen: true))
        {
            deflate.Write(input, 0, input.Length);
        }
        var result = output.ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "deflateRaw");
        return result;
    }

    /// <summary>
    /// Decompresses raw deflate-compressed data (no zlib header).
    /// </summary>
    /// <param name="input">Raw deflate-compressed bytes.</param>
    /// <param name="options">Decompression options.</param>
    /// <returns>Decompressed bytes.</returns>
    public static byte[] DeflateRawDecompress(byte[] input, ZlibOptions options)
    {
        using var inputStream = new MemoryStream(input);
        using var deflate = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();

        CopyWithLimit(deflate, output, options);
        return output.ToArray();
    }

    #endregion

    #region Brotli

    /// <summary>
    /// Compresses data using Brotli format.
    /// </summary>
    /// <param name="input">Input bytes to compress.</param>
    /// <param name="options">Compression options (uses BrotliQuality).</param>
    /// <returns>Brotli-compressed bytes.</returns>
    public static byte[] BrotliCompress(byte[] input, ZlibOptions options)
    {
        using var output = new MemoryStream();

        // Use BrotliEncoder for quality control
        var quality = options.GetBrotliQuality();
        var level = quality switch
        {
            0 => CompressionLevel.NoCompression,
            1 or 2 or 3 or 4 => CompressionLevel.Fastest,
            5 or 6 or 7 or 8 or 9 or 10 => CompressionLevel.Optimal,
            11 => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        using (var brotli = new BrotliStream(output, level, leaveOpen: true))
        {
            brotli.Write(input, 0, input.Length);
        }
        var result = output.ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "brotli");
        return result;
    }

    /// <summary>
    /// Decompresses Brotli-compressed data.
    /// </summary>
    /// <param name="input">Brotli-compressed bytes.</param>
    /// <param name="options">Decompression options.</param>
    /// <returns>Decompressed bytes.</returns>
    public static byte[] BrotliDecompress(byte[] input, ZlibOptions options)
    {
        using var inputStream = new MemoryStream(input);
        using var brotli = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();

        CopyWithLimit(brotli, output, options);
        return output.ToArray();
    }

    #endregion

    #region Zstd

    /// <summary>
    /// Compresses data using Zstandard format.
    /// </summary>
    /// <param name="input">Input bytes to compress.</param>
    /// <param name="options">Compression options (uses ZstdLevel).</param>
    /// <returns>Zstd-compressed bytes.</returns>
    public static byte[] ZstdCompress(byte[] input, ZlibOptions options)
    {
        using var compressor = new Compressor(options.GetZstdLevel());
        var result = compressor.Wrap(input).ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "zstd");
        return result;
    }

    /// <summary>
    /// Decompresses Zstandard-compressed data.
    /// </summary>
    /// <param name="input">Zstd-compressed bytes.</param>
    /// <param name="options">Decompression options.</param>
    /// <returns>Decompressed bytes.</returns>
    public static byte[] ZstdDecompress(byte[] input, ZlibOptions options)
    {
        using var decompressor = new Decompressor();
        var result = decompressor.Unwrap(input).ToArray();
        ValidateOutputLength(result, options.MaxOutputLength, "zstd");
        return result;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Copies from a stream to output with maxOutputLength enforcement.
    /// </summary>
    private static void CopyWithLimit(Stream source, MemoryStream output, ZlibOptions options)
    {
        var buffer = new byte[options.ChunkSize > 0 ? options.ChunkSize : ZlibConstants.Z_DEFAULT_CHUNK];
        int read;
        long totalRead = 0;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (options.MaxOutputLength > 0)
            {
                totalRead += read;
                if (totalRead > options.MaxOutputLength)
                {
                    throw new InvalidOperationException(
                        $"Output length exceeds maxOutputLength ({options.MaxOutputLength} bytes)");
                }
            }
            output.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Validates output length against maxOutputLength option.
    /// </summary>
    private static void ValidateOutputLength(byte[] result, long maxOutputLength, string operation)
    {
        if (maxOutputLength > 0 && result.Length > maxOutputLength)
        {
            throw new InvalidOperationException(
                $"{operation}: Output length ({result.Length}) exceeds maxOutputLength ({maxOutputLength} bytes)");
        }
    }

    #endregion
}
