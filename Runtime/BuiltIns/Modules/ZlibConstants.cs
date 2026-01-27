using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Node.js zlib constants matching zlib.constants.
/// </summary>
/// <remarks>
/// Provides compression-related constants for:
/// - Compression levels (Z_NO_COMPRESSION through Z_BEST_COMPRESSION)
/// - Strategies (Z_FILTERED, Z_HUFFMAN_ONLY, etc.)
/// - Flush modes (Z_NO_FLUSH through Z_TREES)
/// - Return codes (Z_OK, Z_STREAM_END, etc.)
/// - Brotli parameters (BROTLI_PARAM_MODE, BROTLI_PARAM_QUALITY, etc.)
/// - Zstd parameters (when available)
/// </remarks>
public static class ZlibConstants
{
    #region Compression Levels

    /// <summary>No compression (level 0).</summary>
    public const int Z_NO_COMPRESSION = 0;

    /// <summary>Best speed compression (level 1).</summary>
    public const int Z_BEST_SPEED = 1;

    /// <summary>Best compression (level 9).</summary>
    public const int Z_BEST_COMPRESSION = 9;

    /// <summary>Default compression level (-1, typically equivalent to level 6).</summary>
    public const int Z_DEFAULT_COMPRESSION = -1;

    #endregion

    #region Compression Strategies

    /// <summary>Use for data produced by a filter.</summary>
    public const int Z_FILTERED = 1;

    /// <summary>Force Huffman encoding only (no string match).</summary>
    public const int Z_HUFFMAN_ONLY = 2;

    /// <summary>Run-length encoding compression strategy.</summary>
    public const int Z_RLE = 3;

    /// <summary>Use fixed Huffman codes.</summary>
    public const int Z_FIXED = 4;

    /// <summary>Default compression strategy.</summary>
    public const int Z_DEFAULT_STRATEGY = 0;

    #endregion

    #region Flush Modes

    /// <summary>No flush.</summary>
    public const int Z_NO_FLUSH = 0;

    /// <summary>Partial flush.</summary>
    public const int Z_PARTIAL_FLUSH = 1;

    /// <summary>Sync flush.</summary>
    public const int Z_SYNC_FLUSH = 2;

    /// <summary>Full flush.</summary>
    public const int Z_FULL_FLUSH = 3;

    /// <summary>Finish flush.</summary>
    public const int Z_FINISH = 4;

    /// <summary>Block flush.</summary>
    public const int Z_BLOCK = 5;

    /// <summary>Trees flush.</summary>
    public const int Z_TREES = 6;

    #endregion

    #region Return Codes

    /// <summary>Success.</summary>
    public const int Z_OK = 0;

    /// <summary>Stream end.</summary>
    public const int Z_STREAM_END = 1;

    /// <summary>Need dictionary.</summary>
    public const int Z_NEED_DICT = 2;

    /// <summary>Errno error.</summary>
    public const int Z_ERRNO = -1;

    /// <summary>Stream error.</summary>
    public const int Z_STREAM_ERROR = -2;

    /// <summary>Data error.</summary>
    public const int Z_DATA_ERROR = -3;

    /// <summary>Memory error.</summary>
    public const int Z_MEM_ERROR = -4;

    /// <summary>Buffer error.</summary>
    public const int Z_BUF_ERROR = -5;

    /// <summary>Version error.</summary>
    public const int Z_VERSION_ERROR = -6;

    #endregion

    #region Default Window/Memory Sizes

    /// <summary>Default window bits for zlib.</summary>
    public const int Z_DEFAULT_WINDOWBITS = 15;

    /// <summary>Default memory level.</summary>
    public const int Z_DEFAULT_MEMLEVEL = 8;

    /// <summary>Minimum window bits.</summary>
    public const int Z_MIN_WINDOWBITS = 8;

    /// <summary>Maximum window bits.</summary>
    public const int Z_MAX_WINDOWBITS = 15;

    /// <summary>Minimum memory level.</summary>
    public const int Z_MIN_MEMLEVEL = 1;

    /// <summary>Maximum memory level.</summary>
    public const int Z_MAX_MEMLEVEL = 9;

    /// <summary>Default chunk size for streaming operations.</summary>
    public const int Z_DEFAULT_CHUNK = 16384;

    #endregion

    #region Brotli Constants

    /// <summary>Brotli operation: process input.</summary>
    public const int BROTLI_OPERATION_PROCESS = 0;

    /// <summary>Brotli operation: flush.</summary>
    public const int BROTLI_OPERATION_FLUSH = 1;

    /// <summary>Brotli operation: finish.</summary>
    public const int BROTLI_OPERATION_FINISH = 2;

    /// <summary>Brotli operation: emit metadata.</summary>
    public const int BROTLI_OPERATION_EMIT_METADATA = 3;

    // Brotli encoder parameters
    /// <summary>Brotli mode parameter (generic, text, font).</summary>
    public const int BROTLI_PARAM_MODE = 0;

    /// <summary>Brotli quality parameter (0-11).</summary>
    public const int BROTLI_PARAM_QUALITY = 1;

    /// <summary>Brotli LG window parameter (10-24).</summary>
    public const int BROTLI_PARAM_LGWIN = 2;

    /// <summary>Brotli LG block parameter.</summary>
    public const int BROTLI_PARAM_LGBLOCK = 3;

    /// <summary>Disable literal context modeling.</summary>
    public const int BROTLI_PARAM_DISABLE_LITERAL_CONTEXT_MODELING = 4;

    /// <summary>Brotli size hint parameter.</summary>
    public const int BROTLI_PARAM_SIZE_HINT = 5;

    /// <summary>Brotli large window parameter.</summary>
    public const int BROTLI_PARAM_LARGE_WINDOW = 6;

    /// <summary>Brotli NPOSTFIX parameter.</summary>
    public const int BROTLI_PARAM_NPOSTFIX = 7;

    /// <summary>Brotli NDIRECT parameter.</summary>
    public const int BROTLI_PARAM_NDIRECT = 8;

    // Brotli mode values
    /// <summary>Generic mode for mixed or unknown content.</summary>
    public const int BROTLI_MODE_GENERIC = 0;

    /// <summary>Text mode for UTF-8 text.</summary>
    public const int BROTLI_MODE_TEXT = 1;

    /// <summary>Font mode for WOFF 2.0 fonts.</summary>
    public const int BROTLI_MODE_FONT = 2;

    // Brotli quality bounds
    /// <summary>Minimum Brotli quality level.</summary>
    public const int BROTLI_MIN_QUALITY = 0;

    /// <summary>Maximum Brotli quality level.</summary>
    public const int BROTLI_MAX_QUALITY = 11;

    /// <summary>Default Brotli quality level.</summary>
    public const int BROTLI_DEFAULT_QUALITY = 11;

    // Brotli window bounds
    /// <summary>Minimum Brotli window bits.</summary>
    public const int BROTLI_MIN_WINDOW_BITS = 10;

    /// <summary>Maximum Brotli window bits.</summary>
    public const int BROTLI_MAX_WINDOW_BITS = 24;

    /// <summary>Large maximum Brotli window bits.</summary>
    public const int BROTLI_LARGE_MAX_WINDOW_BITS = 30;

    /// <summary>Default Brotli window bits.</summary>
    public const int BROTLI_DEFAULT_WINDOW = 22;

    // Brotli decoder parameters
    /// <summary>Brotli decoder disable ring buffer reallocation.</summary>
    public const int BROTLI_DECODER_PARAM_DISABLE_RING_BUFFER_REALLOCATION = 0;

    /// <summary>Brotli decoder large window.</summary>
    public const int BROTLI_DECODER_PARAM_LARGE_WINDOW = 1;

    #endregion

    #region Zstd Constants

    // Zstd compression parameters
    /// <summary>Zstd compression level (-131072 to 22).</summary>
    public const int ZSTD_c_compressionLevel = 100;

    /// <summary>Zstd window log (10 to 31).</summary>
    public const int ZSTD_c_windowLog = 101;

    /// <summary>Zstd hash log (6 to 30).</summary>
    public const int ZSTD_c_hashLog = 102;

    /// <summary>Zstd chain log (6 to 30).</summary>
    public const int ZSTD_c_chainLog = 103;

    /// <summary>Zstd search log (1 to 30).</summary>
    public const int ZSTD_c_searchLog = 104;

    /// <summary>Zstd min match (3 to 7).</summary>
    public const int ZSTD_c_minMatch = 105;

    /// <summary>Zstd target length (0 to 131072).</summary>
    public const int ZSTD_c_targetLength = 106;

    /// <summary>Zstd strategy (1 to 9).</summary>
    public const int ZSTD_c_strategy = 107;

    /// <summary>Zstd checksum flag.</summary>
    public const int ZSTD_c_checksumFlag = 201;

    /// <summary>Zstd content size flag.</summary>
    public const int ZSTD_c_contentSizeFlag = 200;

    /// <summary>Zstd dictionary ID flag.</summary>
    public const int ZSTD_c_dictIDFlag = 202;

    /// <summary>Zstd number of workers for parallel compression.</summary>
    public const int ZSTD_c_nbWorkers = 400;

    /// <summary>Zstd job size for parallel compression.</summary>
    public const int ZSTD_c_jobSize = 401;

    /// <summary>Zstd overlap log for parallel compression.</summary>
    public const int ZSTD_c_overlapLog = 402;

    // Zstd compression level bounds
    /// <summary>Minimum Zstd compression level.</summary>
    public const int ZSTD_minCLevel = -131072;

    /// <summary>Maximum Zstd compression level.</summary>
    public const int ZSTD_maxCLevel = 22;

    /// <summary>Default Zstd compression level.</summary>
    public const int ZSTD_defaultCLevel = 3;

    #endregion

    /// <summary>
    /// Creates a SharpTSObject containing all zlib constants.
    /// </summary>
    public static SharpTSObject CreateConstantsObject()
    {
        var constants = new Dictionary<string, object?>
        {
            // Compression levels
            ["Z_NO_COMPRESSION"] = (double)Z_NO_COMPRESSION,
            ["Z_BEST_SPEED"] = (double)Z_BEST_SPEED,
            ["Z_BEST_COMPRESSION"] = (double)Z_BEST_COMPRESSION,
            ["Z_DEFAULT_COMPRESSION"] = (double)Z_DEFAULT_COMPRESSION,

            // Compression strategies
            ["Z_FILTERED"] = (double)Z_FILTERED,
            ["Z_HUFFMAN_ONLY"] = (double)Z_HUFFMAN_ONLY,
            ["Z_RLE"] = (double)Z_RLE,
            ["Z_FIXED"] = (double)Z_FIXED,
            ["Z_DEFAULT_STRATEGY"] = (double)Z_DEFAULT_STRATEGY,

            // Flush modes
            ["Z_NO_FLUSH"] = (double)Z_NO_FLUSH,
            ["Z_PARTIAL_FLUSH"] = (double)Z_PARTIAL_FLUSH,
            ["Z_SYNC_FLUSH"] = (double)Z_SYNC_FLUSH,
            ["Z_FULL_FLUSH"] = (double)Z_FULL_FLUSH,
            ["Z_FINISH"] = (double)Z_FINISH,
            ["Z_BLOCK"] = (double)Z_BLOCK,
            ["Z_TREES"] = (double)Z_TREES,

            // Return codes
            ["Z_OK"] = (double)Z_OK,
            ["Z_STREAM_END"] = (double)Z_STREAM_END,
            ["Z_NEED_DICT"] = (double)Z_NEED_DICT,
            ["Z_ERRNO"] = (double)Z_ERRNO,
            ["Z_STREAM_ERROR"] = (double)Z_STREAM_ERROR,
            ["Z_DATA_ERROR"] = (double)Z_DATA_ERROR,
            ["Z_MEM_ERROR"] = (double)Z_MEM_ERROR,
            ["Z_BUF_ERROR"] = (double)Z_BUF_ERROR,
            ["Z_VERSION_ERROR"] = (double)Z_VERSION_ERROR,

            // Window/memory defaults
            ["Z_DEFAULT_WINDOWBITS"] = (double)Z_DEFAULT_WINDOWBITS,
            ["Z_DEFAULT_MEMLEVEL"] = (double)Z_DEFAULT_MEMLEVEL,
            ["Z_MIN_WINDOWBITS"] = (double)Z_MIN_WINDOWBITS,
            ["Z_MAX_WINDOWBITS"] = (double)Z_MAX_WINDOWBITS,
            ["Z_MIN_MEMLEVEL"] = (double)Z_MIN_MEMLEVEL,
            ["Z_MAX_MEMLEVEL"] = (double)Z_MAX_MEMLEVEL,
            ["Z_DEFAULT_CHUNK"] = (double)Z_DEFAULT_CHUNK,

            // Brotli operations
            ["BROTLI_OPERATION_PROCESS"] = (double)BROTLI_OPERATION_PROCESS,
            ["BROTLI_OPERATION_FLUSH"] = (double)BROTLI_OPERATION_FLUSH,
            ["BROTLI_OPERATION_FINISH"] = (double)BROTLI_OPERATION_FINISH,
            ["BROTLI_OPERATION_EMIT_METADATA"] = (double)BROTLI_OPERATION_EMIT_METADATA,

            // Brotli encoder parameters
            ["BROTLI_PARAM_MODE"] = (double)BROTLI_PARAM_MODE,
            ["BROTLI_PARAM_QUALITY"] = (double)BROTLI_PARAM_QUALITY,
            ["BROTLI_PARAM_LGWIN"] = (double)BROTLI_PARAM_LGWIN,
            ["BROTLI_PARAM_LGBLOCK"] = (double)BROTLI_PARAM_LGBLOCK,
            ["BROTLI_PARAM_DISABLE_LITERAL_CONTEXT_MODELING"] = (double)BROTLI_PARAM_DISABLE_LITERAL_CONTEXT_MODELING,
            ["BROTLI_PARAM_SIZE_HINT"] = (double)BROTLI_PARAM_SIZE_HINT,
            ["BROTLI_PARAM_LARGE_WINDOW"] = (double)BROTLI_PARAM_LARGE_WINDOW,
            ["BROTLI_PARAM_NPOSTFIX"] = (double)BROTLI_PARAM_NPOSTFIX,
            ["BROTLI_PARAM_NDIRECT"] = (double)BROTLI_PARAM_NDIRECT,

            // Brotli mode values
            ["BROTLI_MODE_GENERIC"] = (double)BROTLI_MODE_GENERIC,
            ["BROTLI_MODE_TEXT"] = (double)BROTLI_MODE_TEXT,
            ["BROTLI_MODE_FONT"] = (double)BROTLI_MODE_FONT,

            // Brotli quality bounds
            ["BROTLI_MIN_QUALITY"] = (double)BROTLI_MIN_QUALITY,
            ["BROTLI_MAX_QUALITY"] = (double)BROTLI_MAX_QUALITY,
            ["BROTLI_DEFAULT_QUALITY"] = (double)BROTLI_DEFAULT_QUALITY,

            // Brotli window bounds
            ["BROTLI_MIN_WINDOW_BITS"] = (double)BROTLI_MIN_WINDOW_BITS,
            ["BROTLI_MAX_WINDOW_BITS"] = (double)BROTLI_MAX_WINDOW_BITS,
            ["BROTLI_LARGE_MAX_WINDOW_BITS"] = (double)BROTLI_LARGE_MAX_WINDOW_BITS,
            ["BROTLI_DEFAULT_WINDOW"] = (double)BROTLI_DEFAULT_WINDOW,

            // Brotli decoder parameters
            ["BROTLI_DECODER_PARAM_DISABLE_RING_BUFFER_REALLOCATION"] = (double)BROTLI_DECODER_PARAM_DISABLE_RING_BUFFER_REALLOCATION,
            ["BROTLI_DECODER_PARAM_LARGE_WINDOW"] = (double)BROTLI_DECODER_PARAM_LARGE_WINDOW,

            // Zstd compression parameters
            ["ZSTD_c_compressionLevel"] = (double)ZSTD_c_compressionLevel,
            ["ZSTD_c_windowLog"] = (double)ZSTD_c_windowLog,
            ["ZSTD_c_hashLog"] = (double)ZSTD_c_hashLog,
            ["ZSTD_c_chainLog"] = (double)ZSTD_c_chainLog,
            ["ZSTD_c_searchLog"] = (double)ZSTD_c_searchLog,
            ["ZSTD_c_minMatch"] = (double)ZSTD_c_minMatch,
            ["ZSTD_c_targetLength"] = (double)ZSTD_c_targetLength,
            ["ZSTD_c_strategy"] = (double)ZSTD_c_strategy,
            ["ZSTD_c_checksumFlag"] = (double)ZSTD_c_checksumFlag,
            ["ZSTD_c_contentSizeFlag"] = (double)ZSTD_c_contentSizeFlag,
            ["ZSTD_c_dictIDFlag"] = (double)ZSTD_c_dictIDFlag,
            ["ZSTD_c_nbWorkers"] = (double)ZSTD_c_nbWorkers,
            ["ZSTD_c_jobSize"] = (double)ZSTD_c_jobSize,
            ["ZSTD_c_overlapLog"] = (double)ZSTD_c_overlapLog,

            // Zstd compression level bounds
            ["ZSTD_minCLevel"] = (double)ZSTD_minCLevel,
            ["ZSTD_maxCLevel"] = (double)ZSTD_maxCLevel,
            ["ZSTD_defaultCLevel"] = (double)ZSTD_defaultCLevel
        };

        return new SharpTSObject(constants);
    }
}
