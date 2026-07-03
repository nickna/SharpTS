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

    /// <summary>Minimum chunk size.</summary>
    public const int Z_MIN_CHUNK = 64;

    /// <summary>Minimum compression level.</summary>
    public const int Z_MIN_LEVEL = -1;

    /// <summary>Maximum compression level.</summary>
    public const int Z_MAX_LEVEL = 9;

    /// <summary>Default compression level (zlib's level 6).</summary>
    public const int Z_DEFAULT_LEVEL = 6;

    #endregion

    #region Codec Mode Identifiers

    // Node's internal numeric codec ids (zlib.constants.DEFLATE etc.).
    public const int DEFLATE = 1;
    public const int INFLATE = 2;
    public const int GZIP = 3;
    public const int GUNZIP = 4;
    public const int DEFLATERAW = 5;
    public const int INFLATERAW = 6;
    public const int UNZIP = 7;
    public const int BROTLI_DECODE = 8;
    public const int BROTLI_ENCODE = 9;
    public const int ZSTD_COMPRESS = 10;
    public const int ZSTD_DECOMPRESS = 11;

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

    // Brotli decoder result codes
    /// <summary>Brotli decoder result: error.</summary>
    public const int BROTLI_DECODER_RESULT_ERROR = 0;

    /// <summary>Brotli decoder result: success.</summary>
    public const int BROTLI_DECODER_RESULT_SUCCESS = 1;

    /// <summary>Brotli decoder result: needs more input.</summary>
    public const int BROTLI_DECODER_RESULT_NEEDS_MORE_INPUT = 2;

    /// <summary>Brotli decoder result: needs more output.</summary>
    public const int BROTLI_DECODER_RESULT_NEEDS_MORE_OUTPUT = 3;

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

    // Zstd end-directive (streaming) constants
    /// <summary>Zstd directive: continue collecting more input.</summary>
    public const int ZSTD_e_continue = 0;

    /// <summary>Zstd directive: flush any buffered data.</summary>
    public const int ZSTD_e_flush = 1;

    /// <summary>Zstd directive: flush and end the frame.</summary>
    public const int ZSTD_e_end = 2;

    #endregion

    // NOTE: the exported `zlib.constants` and `zlib.codes` objects are TS object
    // literals in stdlib/node/zlib.ts (shared by interpreter and compiled modes;
    // epic #1096). The const fields above remain the C#-side source for option
    // defaults and clamping in ZlibOptions/ZlibHelpers; the values are frozen by
    // the zlib ABI, so the TS literals cannot drift in practice.
}
