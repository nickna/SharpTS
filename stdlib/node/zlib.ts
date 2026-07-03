// Node.js 'zlib' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/zlib.html.
//
// Compression cores (BCL GZipStream/ZLibStream/DeflateStream/BrotliStream,
// ZstdSharp) and the stateful Transform streams stay in C# behind
// `primitive:zlib`. This facade owns the Node-shape glue: constants/codes,
// argument shuffling, and the callback (async) forms derived from the sync
// primitives — one implementation shared by interpreter and compiled modes.

import {
    gzipSync as __gzipSync,
    gunzipSync as __gunzipSync,
    deflateSync as __deflateSync,
    inflateSync as __inflateSync,
    deflateRawSync as __deflateRawSync,
    inflateRawSync as __inflateRawSync,
    brotliCompressSync as __brotliCompressSync,
    brotliDecompressSync as __brotliDecompressSync,
    zstdCompressSync as __zstdCompressSync,
    zstdDecompressSync as __zstdDecompressSync,
    unzipSync as __unzipSync,
    createGzip as __createGzip,
    createGunzip as __createGunzip,
    createDeflate as __createDeflate,
    createInflate as __createInflate,
    createDeflateRaw as __createDeflateRaw,
    createInflateRaw as __createInflateRaw,
    createBrotliCompress as __createBrotliCompress,
    createBrotliDecompress as __createBrotliDecompress,
    createZstdCompress as __createZstdCompress,
    createZstdDecompress as __createZstdDecompress,
    createUnzip as __createUnzip,
    crc32 as __crc32,
} from 'primitive:zlib';

// ── Synchronous one-shot APIs ────────────────────────────────────────────────
// Arity-dispatch instead of forwarding `undefined`: primitives default by
// argument count and a forwarded `undefined` options slot is not "absent".

/** Compresses data with gzip. */
export function gzipSync(input: any, options?: any): Buffer {
    return options === undefined ? __gzipSync(input) : __gzipSync(input, options);
}

/** Decompresses a gzip buffer. */
export function gunzipSync(input: any, options?: any): Buffer {
    return options === undefined ? __gunzipSync(input) : __gunzipSync(input, options);
}

/** Compresses data with deflate (zlib header). */
export function deflateSync(input: any, options?: any): Buffer {
    return options === undefined ? __deflateSync(input) : __deflateSync(input, options);
}

/** Decompresses a deflate (zlib header) buffer. */
export function inflateSync(input: any, options?: any): Buffer {
    return options === undefined ? __inflateSync(input) : __inflateSync(input, options);
}

/** Compresses data with raw deflate (no header). */
export function deflateRawSync(input: any, options?: any): Buffer {
    return options === undefined ? __deflateRawSync(input) : __deflateRawSync(input, options);
}

/** Decompresses a raw deflate buffer. */
export function inflateRawSync(input: any, options?: any): Buffer {
    return options === undefined ? __inflateRawSync(input) : __inflateRawSync(input, options);
}

/** Compresses data with Brotli. */
export function brotliCompressSync(input: any, options?: any): Buffer {
    return options === undefined ? __brotliCompressSync(input) : __brotliCompressSync(input, options);
}

/** Decompresses a Brotli buffer. */
export function brotliDecompressSync(input: any, options?: any): Buffer {
    return options === undefined ? __brotliDecompressSync(input) : __brotliDecompressSync(input, options);
}

/** Compresses data with Zstandard. */
export function zstdCompressSync(input: any, options?: any): Buffer {
    return options === undefined ? __zstdCompressSync(input) : __zstdCompressSync(input, options);
}

/** Decompresses a Zstandard buffer. */
export function zstdDecompressSync(input: any, options?: any): Buffer {
    return options === undefined ? __zstdDecompressSync(input) : __zstdDecompressSync(input, options);
}

/** Decompresses gzip or deflate data, auto-detected from the header bytes. */
export function unzipSync(input: any, options?: any): Buffer {
    return options === undefined ? __unzipSync(input) : __unzipSync(input, options);
}

// ── Streaming APIs (Transform streams; stateful primitive) ──────────────────

/** Creates a gzip compression Transform stream. */
export function createGzip(options?: any): any {
    return options === undefined ? __createGzip() : __createGzip(options);
}

/** Creates a gunzip decompression Transform stream. */
export function createGunzip(options?: any): any {
    return options === undefined ? __createGunzip() : __createGunzip(options);
}

/** Creates a deflate compression Transform stream. */
export function createDeflate(options?: any): any {
    return options === undefined ? __createDeflate() : __createDeflate(options);
}

/** Creates an inflate decompression Transform stream. */
export function createInflate(options?: any): any {
    return options === undefined ? __createInflate() : __createInflate(options);
}

/** Creates a raw deflate compression Transform stream. */
export function createDeflateRaw(options?: any): any {
    return options === undefined ? __createDeflateRaw() : __createDeflateRaw(options);
}

/** Creates a raw inflate decompression Transform stream. */
export function createInflateRaw(options?: any): any {
    return options === undefined ? __createInflateRaw() : __createInflateRaw(options);
}

/** Creates a Brotli compression Transform stream. */
export function createBrotliCompress(options?: any): any {
    return options === undefined ? __createBrotliCompress() : __createBrotliCompress(options);
}

/** Creates a Brotli decompression Transform stream. */
export function createBrotliDecompress(options?: any): any {
    return options === undefined ? __createBrotliDecompress() : __createBrotliDecompress(options);
}

/** Creates a Zstandard compression Transform stream. */
export function createZstdCompress(options?: any): any {
    return options === undefined ? __createZstdCompress() : __createZstdCompress(options);
}

/** Creates a Zstandard decompression Transform stream. */
export function createZstdDecompress(options?: any): any {
    return options === undefined ? __createZstdDecompress() : __createZstdDecompress(options);
}

/** Creates a Transform stream that auto-detects gzip vs deflate input. */
export function createUnzip(options?: any): any {
    return options === undefined ? __createUnzip() : __createUnzip(options);
}

// ── Checksums ────────────────────────────────────────────────────────────────

/** Computes the CRC-32 checksum of data, optionally continuing from `value`. */
export function crc32(data: any, value?: number): number {
    return value === undefined ? __crc32(data) : __crc32(data, value);
}

// ── Asynchronous callback APIs ───────────────────────────────────────────────
// Derived in TS from the sync primitives: run the operation, then deliver the
// callback on a microtask (never synchronously re-entrant). Errors surface as
// the error's message, matching the historical dual-C# behavior.

function __zlibAsync(fn: () => any, callback: any): void {
    new Promise<any>((resolve: any, reject: any) => {
        try { resolve(fn()); } catch (e) { reject(e); }
    }).then(
        (value: any) => { callback(null, value); },
        (err: any) => {
            callback(err !== null && err !== undefined && err.message !== undefined ? err.message : err);
        }
    );
}

function __splitArgs(optionsOrCb: any, callback: any): any {
    // gzip(buf, cb) and gzip(buf, opts, cb) both arrive here.
    return callback === undefined
        ? { options: undefined, cb: optionsOrCb }
        : { options: optionsOrCb, cb: callback };
}

/** Compresses data with gzip; `callback(err, result)`. */
export function gzip(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => gzipSync(input, a.options), a.cb);
}

/** Decompresses gzip data; `callback(err, result)`. */
export function gunzip(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => gunzipSync(input, a.options), a.cb);
}

/** Compresses data with deflate; `callback(err, result)`. */
export function deflate(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => deflateSync(input, a.options), a.cb);
}

/** Decompresses deflate data; `callback(err, result)`. */
export function inflate(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => inflateSync(input, a.options), a.cb);
}

/** Compresses data with raw deflate; `callback(err, result)`. */
export function deflateRaw(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => deflateRawSync(input, a.options), a.cb);
}

/** Decompresses raw deflate data; `callback(err, result)`. */
export function inflateRaw(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => inflateRawSync(input, a.options), a.cb);
}

/** Compresses data with Brotli; `callback(err, result)`. */
export function brotliCompress(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => brotliCompressSync(input, a.options), a.cb);
}

/** Decompresses Brotli data; `callback(err, result)`. */
export function brotliDecompress(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => brotliDecompressSync(input, a.options), a.cb);
}

/** Compresses data with Zstandard; `callback(err, result)`. */
export function zstdCompress(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => zstdCompressSync(input, a.options), a.cb);
}

/** Decompresses Zstandard data; `callback(err, result)`. */
export function zstdDecompress(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => zstdDecompressSync(input, a.options), a.cb);
}

/** Decompresses gzip or deflate data (auto-detected); `callback(err, result)`. */
export function unzip(input: any, optionsOrCb?: any, callback?: any): void {
    const a = __splitArgs(optionsOrCb, callback);
    if (a.cb === undefined || a.cb === null) return;
    __zlibAsync(() => unzipSync(input, a.options), a.cb);
}

// ── Constants ────────────────────────────────────────────────────────────────

/** zlib.constants — compression levels, strategies, flush modes, return codes,
 * window/memory bounds, codec ids, and Brotli/Zstd parameters. */
export const constants: any = {
    // Compression levels
    Z_NO_COMPRESSION: 0,
    Z_BEST_SPEED: 1,
    Z_BEST_COMPRESSION: 9,
    Z_DEFAULT_COMPRESSION: -1,

    // Compression strategies
    Z_FILTERED: 1,
    Z_HUFFMAN_ONLY: 2,
    Z_RLE: 3,
    Z_FIXED: 4,
    Z_DEFAULT_STRATEGY: 0,

    // Flush modes
    Z_NO_FLUSH: 0,
    Z_PARTIAL_FLUSH: 1,
    Z_SYNC_FLUSH: 2,
    Z_FULL_FLUSH: 3,
    Z_FINISH: 4,
    Z_BLOCK: 5,
    Z_TREES: 6,

    // Return codes
    Z_OK: 0,
    Z_STREAM_END: 1,
    Z_NEED_DICT: 2,
    Z_ERRNO: -1,
    Z_STREAM_ERROR: -2,
    Z_DATA_ERROR: -3,
    Z_MEM_ERROR: -4,
    Z_BUF_ERROR: -5,
    Z_VERSION_ERROR: -6,

    // Window/memory defaults and bounds
    Z_DEFAULT_WINDOWBITS: 15,
    Z_DEFAULT_MEMLEVEL: 8,
    Z_MIN_WINDOWBITS: 8,
    Z_MAX_WINDOWBITS: 15,
    Z_MIN_MEMLEVEL: 1,
    Z_MAX_MEMLEVEL: 9,
    Z_DEFAULT_CHUNK: 16384,
    Z_MIN_CHUNK: 64,
    Z_MAX_CHUNK: Infinity,
    Z_MIN_LEVEL: -1,
    Z_MAX_LEVEL: 9,
    Z_DEFAULT_LEVEL: 6,

    // Codec mode identifiers
    DEFLATE: 1,
    INFLATE: 2,
    GZIP: 3,
    GUNZIP: 4,
    DEFLATERAW: 5,
    INFLATERAW: 6,
    UNZIP: 7,
    BROTLI_DECODE: 8,
    BROTLI_ENCODE: 9,
    ZSTD_COMPRESS: 10,
    ZSTD_DECOMPRESS: 11,

    // Brotli operations
    BROTLI_OPERATION_PROCESS: 0,
    BROTLI_OPERATION_FLUSH: 1,
    BROTLI_OPERATION_FINISH: 2,
    BROTLI_OPERATION_EMIT_METADATA: 3,

    // Brotli encoder parameters
    BROTLI_PARAM_MODE: 0,
    BROTLI_PARAM_QUALITY: 1,
    BROTLI_PARAM_LGWIN: 2,
    BROTLI_PARAM_LGBLOCK: 3,
    BROTLI_PARAM_DISABLE_LITERAL_CONTEXT_MODELING: 4,
    BROTLI_PARAM_SIZE_HINT: 5,
    BROTLI_PARAM_LARGE_WINDOW: 6,
    BROTLI_PARAM_NPOSTFIX: 7,
    BROTLI_PARAM_NDIRECT: 8,

    // Brotli mode values
    BROTLI_MODE_GENERIC: 0,
    BROTLI_MODE_TEXT: 1,
    BROTLI_MODE_FONT: 2,

    // Brotli quality bounds
    BROTLI_MIN_QUALITY: 0,
    BROTLI_MAX_QUALITY: 11,
    BROTLI_DEFAULT_QUALITY: 11,

    // Brotli window bounds
    BROTLI_MIN_WINDOW_BITS: 10,
    BROTLI_MAX_WINDOW_BITS: 24,
    BROTLI_LARGE_MAX_WINDOW_BITS: 30,
    BROTLI_DEFAULT_WINDOW: 22,

    // Brotli decoder parameters and result codes
    BROTLI_DECODER_PARAM_DISABLE_RING_BUFFER_REALLOCATION: 0,
    BROTLI_DECODER_PARAM_LARGE_WINDOW: 1,
    BROTLI_DECODER_RESULT_ERROR: 0,
    BROTLI_DECODER_RESULT_SUCCESS: 1,
    BROTLI_DECODER_RESULT_NEEDS_MORE_INPUT: 2,
    BROTLI_DECODER_RESULT_NEEDS_MORE_OUTPUT: 3,

    // Zstd compression parameters
    ZSTD_c_compressionLevel: 100,
    ZSTD_c_windowLog: 101,
    ZSTD_c_hashLog: 102,
    ZSTD_c_chainLog: 103,
    ZSTD_c_searchLog: 104,
    ZSTD_c_minMatch: 105,
    ZSTD_c_targetLength: 106,
    ZSTD_c_strategy: 107,
    ZSTD_c_checksumFlag: 201,
    ZSTD_c_contentSizeFlag: 200,
    ZSTD_c_dictIDFlag: 202,
    ZSTD_c_nbWorkers: 400,
    ZSTD_c_jobSize: 401,
    ZSTD_c_overlapLog: 402,

    // Zstd compression level bounds
    ZSTD_minCLevel: -131072,
    ZSTD_maxCLevel: 22,
    ZSTD_defaultCLevel: 3,

    // Zstd end directives
    ZSTD_e_continue: 0,
    ZSTD_e_flush: 1,
    ZSTD_e_end: 2,
};

/** zlib.codes — bidirectional return-code map: name → number and
 * numeric-string → name (matching Node). */
export const codes: any = {
    Z_OK: 0,
    Z_STREAM_END: 1,
    Z_NEED_DICT: 2,
    Z_ERRNO: -1,
    Z_STREAM_ERROR: -2,
    Z_DATA_ERROR: -3,
    Z_MEM_ERROR: -4,
    Z_BUF_ERROR: -5,
    Z_VERSION_ERROR: -6,
    '0': 'Z_OK',
    '1': 'Z_STREAM_END',
    '2': 'Z_NEED_DICT',
    '-1': 'Z_ERRNO',
    '-2': 'Z_STREAM_ERROR',
    '-3': 'Z_DATA_ERROR',
    '-4': 'Z_MEM_ERROR',
    '-5': 'Z_BUF_ERROR',
    '-6': 'Z_VERSION_ERROR',
};
