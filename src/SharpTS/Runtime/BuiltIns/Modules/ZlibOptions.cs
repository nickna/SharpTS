using System.IO.Compression;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Options for zlib compression/decompression operations.
/// </summary>
/// <remarks>
/// Maps Node.js zlib options to .NET equivalents:
/// - level: Compression level (0=none, 1=fastest, 9=best)
/// - windowBits: Window size (not directly mapped in .NET)
/// - memLevel: Memory usage (not directly mapped in .NET)
/// - strategy: Compression strategy (not directly mapped in .NET)
/// - chunkSize: Buffer size for streaming (used for decompression)
/// - maxOutputLength: Maximum output size (manual check)
/// </remarks>
public class ZlibOptions
{
    /// <summary>
    /// Compression level (0-9, or -1 for default).
    /// </summary>
    public int Level { get; set; } = ZlibConstants.Z_DEFAULT_COMPRESSION;

    /// <summary>
    /// Window bits (8-15 for deflate, 16-31 for gzip, -8 to -15 for raw deflate).
    /// </summary>
    public int WindowBits { get; set; } = ZlibConstants.Z_DEFAULT_WINDOWBITS;

    /// <summary>
    /// Memory level (1-9).
    /// </summary>
    public int MemLevel { get; set; } = ZlibConstants.Z_DEFAULT_MEMLEVEL;

    /// <summary>
    /// Compression strategy.
    /// </summary>
    public int Strategy { get; set; } = ZlibConstants.Z_DEFAULT_STRATEGY;

    /// <summary>
    /// Chunk size for streaming operations.
    /// </summary>
    public int ChunkSize { get; set; } = ZlibConstants.Z_DEFAULT_CHUNK;

    /// <summary>
    /// Maximum output length. If exceeded, an error is thrown.
    /// A value of 0 means no limit.
    /// </summary>
    public long MaxOutputLength { get; set; } = 0;

    /// <summary>
    /// Brotli quality (0-11).
    /// </summary>
    public int BrotliQuality { get; set; } = ZlibConstants.BROTLI_DEFAULT_QUALITY;

    /// <summary>
    /// Brotli window bits (10-24).
    /// </summary>
    public int BrotliLgwin { get; set; } = ZlibConstants.BROTLI_DEFAULT_WINDOW;

    /// <summary>
    /// Brotli mode (generic=0, text=1, font=2).
    /// </summary>
    public int BrotliMode { get; set; } = ZlibConstants.BROTLI_MODE_GENERIC;

    /// <summary>
    /// Zstd compression level (-131072 to 22).
    /// </summary>
    public int ZstdLevel { get; set; } = ZlibConstants.ZSTD_defaultCLevel;

    /// <summary>
    /// Preset compression dictionary, or null. Parsed from a Buffer/TypedArray.
    /// </summary>
    /// <remarks>
    /// BCL ceiling: <c>System.IO.Compression</c> exposes no preset-dictionary API,
    /// so this is accepted but NOT applied. Round-trips still succeed because both
    /// compress and decompress ignore it symmetrically; output will not match a
    /// native zlib stream produced with a dictionary.
    /// </remarks>
    public byte[]? Dictionary { get; set; }

    /// <summary>
    /// Per-write flush behavior for streams (Z_NO_FLUSH, Z_SYNC_FLUSH, ...).
    /// </summary>
    public int Flush { get; set; } = ZlibConstants.Z_NO_FLUSH;

    /// <summary>
    /// Flush behavior when the stream ends (defaults to Z_FINISH).
    /// </summary>
    public int FinishFlush { get; set; } = ZlibConstants.Z_FINISH;

    /// <summary>
    /// Parses options from a SharpTSObject.
    /// </summary>
    /// <param name="obj">The options object, or null for defaults.</param>
    /// <returns>Parsed options.</returns>
    public static ZlibOptions FromObject(SharpTSObject? obj)
    {
        var options = new ZlibOptions();

        if (obj == null)
            return options;

        // Level
        if (obj.GetProperty("level") is double level)
            options.Level = (int)level;

        // WindowBits
        if (obj.GetProperty("windowBits") is double windowBits)
            options.WindowBits = (int)windowBits;

        // MemLevel
        if (obj.GetProperty("memLevel") is double memLevel)
            options.MemLevel = (int)memLevel;

        // Strategy
        if (obj.GetProperty("strategy") is double strategy)
            options.Strategy = (int)strategy;

        // ChunkSize
        if (obj.GetProperty("chunkSize") is double chunkSize)
            options.ChunkSize = (int)chunkSize;

        // MaxOutputLength
        if (obj.GetProperty("maxOutputLength") is double maxOutputLength)
            options.MaxOutputLength = (long)maxOutputLength;

        // Flush / finishFlush
        if (obj.GetProperty("flush") is double flush)
            options.Flush = (int)flush;
        if (obj.GetProperty("finishFlush") is double finishFlush)
            options.FinishFlush = (int)finishFlush;

        // Preset dictionary (Buffer or TypedArray view)
        switch (obj.GetProperty("dictionary"))
        {
            case SharpTSBuffer dictBuf:
                options.Dictionary = dictBuf.Data;
                break;
            case SharpTSTypedArray dictTa:
                var dictBytes = new byte[dictTa.ByteLength];
                Array.Copy(dictTa.Buffer, dictTa.ByteOffset, dictBytes, 0, dictTa.ByteLength);
                options.Dictionary = dictBytes;
                break;
        }

        // Brotli-specific options (from params object)
        if (obj.GetProperty("params") is SharpTSObject paramsObj)
        {
            // BROTLI_PARAM_QUALITY (1)
            if (paramsObj.GetProperty(ZlibConstants.BROTLI_PARAM_QUALITY.ToString()) is double quality)
                options.BrotliQuality = (int)quality;

            // BROTLI_PARAM_LGWIN (2)
            if (paramsObj.GetProperty(ZlibConstants.BROTLI_PARAM_LGWIN.ToString()) is double lgwin)
                options.BrotliLgwin = (int)lgwin;

            // BROTLI_PARAM_MODE (0)
            if (paramsObj.GetProperty(ZlibConstants.BROTLI_PARAM_MODE.ToString()) is double mode)
                options.BrotliMode = (int)mode;

            // ZSTD_c_compressionLevel (100)
            if (paramsObj.GetProperty(ZlibConstants.ZSTD_c_compressionLevel.ToString()) is double zstdLevel)
                options.ZstdLevel = (int)zstdLevel;
        }

        return options;
    }

    /// <summary>
    /// Parses options from an object (may be SharpTSObject or null).
    /// </summary>
    public static ZlibOptions FromValue(object? value)
    {
        if (value is SharpTSObject obj)
            return FromObject(obj);
        return new ZlibOptions();
    }

    /// <summary>
    /// Converts the compression level to a .NET CompressionLevel enum.
    /// </summary>
    public CompressionLevel ToCompressionLevel()
    {
        return Level switch
        {
            0 => CompressionLevel.NoCompression,
            1 => CompressionLevel.Fastest,
            >= 2 and <= 8 => CompressionLevel.Optimal,
            9 => CompressionLevel.SmallestSize,
            -1 => CompressionLevel.Optimal, // Default
            _ => CompressionLevel.Optimal   // Fallback
        };
    }

    /// <summary>
    /// Builds a <see cref="ZLibCompressionOptions"/> honoring the exact Node
    /// <c>level</c> (-1..9) and <c>strategy</c> (Z_DEFAULT_STRATEGY..Z_FIXED, which
    /// map 1:1 onto <see cref="ZLibCompressionStrategy"/>). This is the deflate
    /// family's source of truth so the BCL honors the precise level rather than the
    /// coarse 4-bucket <see cref="CompressionLevel"/> mapping.
    /// </summary>
    public ZLibCompressionOptions ToZLibCompressionOptions()
    {
        int level = Level is >= -1 and <= 9 ? Level : -1;
        var strategy = Strategy is >= 0 and <= 4
            ? (ZLibCompressionStrategy)Strategy
            : ZLibCompressionStrategy.Default;
        return new ZLibCompressionOptions
        {
            CompressionLevel = level,
            CompressionStrategy = strategy
        };
    }

    /// <summary>
    /// Gets the Brotli compression quality (0-11) mapped to .NET's 0-11 range.
    /// </summary>
    public int GetBrotliQuality()
    {
        // .NET BrotliStream supports quality 0-11
        return Math.Clamp(BrotliQuality, 0, 11);
    }

    /// <summary>
    /// Gets the Brotli window bits (10-24), clamped to the BCL-supported range.
    /// </summary>
    public int GetBrotliWindow()
    {
        return Math.Clamp(BrotliLgwin, 10, 24);
    }

    /// <summary>
    /// Gets the Zstd compression level, clamped to valid range.
    /// </summary>
    public int GetZstdLevel()
    {
        // ZstdSharp typically supports -131072 to 22
        return Math.Clamp(ZstdLevel, -131072, 22);
    }
}
