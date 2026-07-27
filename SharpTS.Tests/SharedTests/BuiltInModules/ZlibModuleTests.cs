using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'zlib' module: gzip, deflate, brotli, zstd compression.
/// Note: Zstd tests are interpreter-only because compiled DLLs require ZstdSharp.dll deployed alongside.
/// </summary>
public class ZlibModuleTests
{
    #region Gzip Tests

    [Theory, ModeData]
    public void Zlib_Gzip_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.gzipSync(input);
                const decompressed = zlib.gunzipSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Gzip_CompressesData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                const compressed = zlib.gzipSync(input);
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Gzip_StringInput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const compressed = zlib.gzipSync('hello world');
                const decompressed = zlib.gunzipSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Gzip_WithLevel(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                // Best compression
                const compressed9 = zlib.gzipSync(input, { level: 9 });
                // Fastest compression
                const compressed1 = zlib.gzipSync(input, { level: 1 });
                // Both should decompress to same value
                const decompressed9 = zlib.gunzipSync(compressed9);
                const decompressed1 = zlib.gunzipSync(compressed1);
                console.log(decompressed9.toString() === input.toString());
                console.log(decompressed1.toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Deflate Tests

    [Theory, ModeData]
    public void Zlib_Deflate_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.deflateSync(input);
                const decompressed = zlib.inflateSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_DeflateRaw_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.deflateRawSync(input);
                const decompressed = zlib.inflateRawSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Deflate_CompressesData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                const compressed = zlib.deflateSync(input);
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Brotli Tests

    [Theory, ModeData]
    public void Zlib_Brotli_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.brotliCompressSync(input);
                const decompressed = zlib.brotliDecompressSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Brotli_CompressesData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                const compressed = zlib.brotliCompressSync(input);
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Brotli_LargeData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('The quick brown fox jumps over the lazy dog. '.repeat(1000));
                const compressed = zlib.brotliCompressSync(input);
                const decompressed = zlib.brotliDecompressSync(compressed);
                console.log(decompressed.toString() === input.toString());
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Zstd Tests (Interpreter Only - requires ZstdSharp.dll for compiled)

    [Theory, ModeData]
    public void Zlib_Zstd_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.zstdCompressSync(input);
                const decompressed = zlib.zstdDecompressSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Zstd_CompressesData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                const compressed = zlib.zstdCompressSync(input);
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Zstd_LargeData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('The quick brown fox jumps over the lazy dog. '.repeat(1000));
                const compressed = zlib.zstdCompressSync(input);
                const decompressed = zlib.zstdDecompressSync(compressed);
                console.log(decompressed.toString() === input.toString());
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Unzip (Auto-Detect) Tests

    [Theory, ModeData]
    public void Zlib_Unzip_DetectsGzip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.gzipSync(input);
                const decompressed = zlib.unzipSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Unzip_DetectsDeflate(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = zlib.deflateSync(input);
                const decompressed = zlib.unzipSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Constants Tests

    [Theory, ModeData]
    public void Zlib_Constants_CompressionLevels(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.Z_NO_COMPRESSION === 0);
                console.log(zlib.constants.Z_BEST_SPEED === 1);
                console.log(zlib.constants.Z_BEST_COMPRESSION === 9);
                console.log(zlib.constants.Z_DEFAULT_COMPRESSION === -1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_Strategies(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.Z_DEFAULT_STRATEGY === 0);
                console.log(zlib.constants.Z_FILTERED === 1);
                console.log(zlib.constants.Z_HUFFMAN_ONLY === 2);
                console.log(zlib.constants.Z_RLE === 3);
                console.log(zlib.constants.Z_FIXED === 4);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_ReturnCodes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.Z_OK === 0);
                console.log(zlib.constants.Z_STREAM_END === 1);
                console.log(zlib.constants.Z_DATA_ERROR === -3);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_Brotli_Extended(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.BROTLI_MIN_QUALITY === 0);
                console.log(zlib.constants.BROTLI_MAX_QUALITY === 11);
                console.log(zlib.constants.BROTLI_DEFAULT_QUALITY === 11);
                console.log(zlib.constants.BROTLI_MODE_GENERIC === 0);
                console.log(zlib.constants.BROTLI_MODE_TEXT === 1);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_Brotli_Basic(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.BROTLI_MIN_QUALITY === 0);
                console.log(zlib.constants.BROTLI_MAX_QUALITY === 11);
                console.log(zlib.constants.BROTLI_DEFAULT_QUALITY === 11);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_Zstd(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.ZSTD_defaultCLevel === 3);
                console.log(zlib.constants.ZSTD_maxCLevel === 22);
                console.log(typeof zlib.constants.ZSTD_c_compressionLevel === 'number');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Return Type Tests

    [Theory, ModeData]
    public void Zlib_ReturnsBuffer_Full(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello');
                console.log(Buffer.isBuffer(zlib.gzipSync(input)));
                console.log(Buffer.isBuffer(zlib.deflateSync(input)));
                console.log(Buffer.isBuffer(zlib.deflateRawSync(input)));
                console.log(Buffer.isBuffer(zlib.brotliCompressSync(input)));
                console.log(Buffer.isBuffer(zlib.zstdCompressSync(input)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_ReturnsBuffer_Basic(ExecutionMode mode)
    {
        // Note: zstdCompressSync and deflateRawSync excluded due to deployment requirements
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello');
                console.log(Buffer.isBuffer(zlib.gzipSync(input)));
                console.log(Buffer.isBuffer(zlib.deflateSync(input)));
                console.log(Buffer.isBuffer(zlib.brotliCompressSync(input)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Empty Input Tests

    [Theory, ModeData]
    public void Zlib_EmptyInput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const empty = Buffer.from('');
                // Gzip empty buffer
                const gzipCompressed = zlib.gzipSync(empty);
                const gzipDecompressed = zlib.gunzipSync(gzipCompressed);
                console.log(gzipDecompressed.length === 0);
                // Deflate empty buffer
                const deflateCompressed = zlib.deflateSync(empty);
                const deflateDecompressed = zlib.inflateSync(deflateCompressed);
                console.log(deflateDecompressed.length === 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Binary Data Tests

    [Theory, ModeData]
    public void Zlib_BinaryData(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                // Create buffer with all byte values using array
                const arr = [];
                for (let i = 0; i < 256; i++) {
                    arr.push(i);
                }
                const input = Buffer.from(arr);
                // Test round-trip preserves binary data
                const compressed = zlib.gzipSync(input);
                const decompressed = zlib.gunzipSync(compressed);
                let match = true;
                for (let i = 0; i < 256; i++) {
                    if (decompressed.readUInt8(i) !== i) {
                        match = false;
                        break;
                    }
                }
                console.log(match);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Named Import Tests

    [Theory, ModeData]
    public void Zlib_NamedImports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { gzipSync, gunzipSync, constants } from 'zlib';
                const input = Buffer.from('hello world');
                const compressed = gzipSync(input);
                const decompressed = gunzipSync(compressed);
                console.log(decompressed.toString() === 'hello world');
                console.log(constants.Z_BEST_COMPRESSION === 9);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Streaming API Tests

    [Theory, ModeData]
    public void Zlib_CreateGzip_WriteAndRead(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const gzip = zlib.createGzip();
                const chunks: Buffer[] = [];

                gzip.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                gzip.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    const decompressed = zlib.gunzipSync(compressed);
                    console.log(decompressed.toString());
                });

                gzip.write('hello world');
                gzip.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void Zlib_CreateDeflate_WriteAndVerify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const deflate = zlib.createDeflate();
                const chunks: Buffer[] = [];

                deflate.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                deflate.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    const decompressed = zlib.inflateSync(compressed);
                    console.log(decompressed.toString());
                });

                deflate.write('compressed data');
                deflate.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("compressed data\n", output);
    }

    [Theory, ModeData]
    public void Zlib_CreateBrotliCompress_WriteAndVerify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const compress = zlib.createBrotliCompress();
                const chunks: Buffer[] = [];

                compress.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                compress.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    const decompressed = zlib.brotliDecompressSync(compressed);
                    console.log(decompressed.toString());
                });

                compress.write('brotli test data');
                compress.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("brotli test data\n", output);
    }

    [Theory, ModeData]
    public void Zlib_CreateGzip_WriteAndCollect(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const gzip = zlib.createGzip();
                const chunks: Buffer[] = [];

                gzip.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                gzip.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    // Verify it's valid gzip by decompressing with sync API
                    const decompressed = zlib.gunzipSync(compressed);
                    console.log(decompressed.toString());
                });

                gzip.write('streaming ');
                gzip.write('compression');
                gzip.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("streaming compression\n", output);
    }

    [Theory, ModeData]
    public void Zlib_CreateDeflateRaw_WriteAndVerify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const deflate = zlib.createDeflateRaw();
                const chunks: Buffer[] = [];

                deflate.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                deflate.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    const decompressed = zlib.inflateRawSync(compressed);
                    console.log(decompressed.toString());
                });

                deflate.write('raw deflate test');
                deflate.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("raw deflate test\n", output);
    }

    [Theory, ModeData]
    public void Zlib_CreateUnzip_AutoDetectsGzip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                // Compress with gzip first
                const compressed = zlib.gzipSync('auto detect me');

                // Decompress with createUnzip (auto-detect)
                const unzip = zlib.createUnzip();
                const chunks: Buffer[] = [];
                unzip.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                unzip.on('end', () => {
                    console.log(Buffer.concat(chunks).toString());
                });

                unzip.write(compressed);
                unzip.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("auto detect me\n", output);
    }

    #endregion

    #region Async Callback API Tests

    [Theory, ModeData]
    public void Zlib_Gzip_Async_Callback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                const input = Buffer.from('async gzip test');
                zlib.gzip(input, (err: any, result: Buffer) => {
                    if (err) {
                        console.log('error: ' + err);
                        return;
                    }
                    const decompressed = zlib.gunzipSync(result);
                    console.log(decompressed.toString());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("async gzip test\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Deflate_Async_Callback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                zlib.deflate('async deflate', (err: any, result: Buffer) => {
                    if (err) {
                        console.log('error: ' + err);
                        return;
                    }
                    const decompressed = zlib.inflateSync(result);
                    console.log(decompressed.toString());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("async deflate\n", output);
    }

    [Theory, ModeData]
    public void Zlib_BrotliCompress_Async_Callback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';

                zlib.brotliCompress('async brotli', (err: any, result: Buffer) => {
                    if (err) {
                        console.log('error: ' + err);
                        return;
                    }
                    const decompressed = zlib.brotliDecompressSync(result);
                    console.log(decompressed.toString());
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("async brotli\n", output);
    }

    #endregion

    #region Named Import Tests for Streaming APIs

    [Theory, ModeData]
    public void Zlib_NamedImport_CreateGzip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createGzip, gunzipSync } from 'zlib';

                const gzip = createGzip();
                const chunks: Buffer[] = [];

                gzip.on('data', (chunk: Buffer) => {
                    chunks.push(chunk);
                });
                gzip.on('end', () => {
                    const compressed = Buffer.concat(chunks);
                    const decompressed = gunzipSync(compressed);
                    console.log(decompressed.toString());
                });

                gzip.write('named import streaming');
                gzip.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("named import streaming\n", output);
    }

    #endregion

    #region crc32 / codes / constants (#1162)

    [Theory, ModeData]
    public void Zlib_Crc32_KnownValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // Node's zlib.crc32('hello') === 907060870
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.crc32('hello'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("907060870\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Crc32_EmptyIsZero(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.crc32(Buffer.alloc(0)));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Crc32_RunningValueChains(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // crc32(' world', crc32('hello')) === crc32('hello world')
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const whole = zlib.crc32('hello world');
                const chained = zlib.crc32(' world', zlib.crc32('hello'));
                console.log(whole === chained);
                console.log(whole === zlib.crc32(Buffer.from('hello world')));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Crc32_NamedImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { crc32 } from 'zlib';
                console.log(crc32('hello'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("907060870\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Codes_Bidirectional(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.codes.Z_OK);
                console.log(zlib.codes.Z_STREAM_END);
                console.log(zlib.codes.Z_DATA_ERROR);
                console.log(zlib.codes['0']);
                console.log(zlib.codes['-3']);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("0\n1\n-3\nZ_OK\nZ_DATA_ERROR\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Constants_Completeness(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.Z_DEFAULT_LEVEL);
                console.log(zlib.constants.Z_MIN_CHUNK);
                console.log(zlib.constants.GZIP);
                console.log(zlib.constants.ZSTD_e_end);
                console.log(zlib.constants.BROTLI_DECODER_RESULT_SUCCESS);
                console.log(zlib.constants.Z_MAX_CHUNK === Infinity);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("6\n64\n3\n2\n1\ntrue\n", output);
    }

    #endregion

    #region Compression options (#1163)

    [Theory, ModeData]
    public void Zlib_Deflate_LevelExtremesRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(200));
                const c0 = zlib.deflateSync(input, { level: 0 });
                const c9 = zlib.deflateSync(input, { level: 9 });
                // level 0 (stored) must be larger than level 9 (best)
                console.log(c0.length > c9.length);
                console.log(zlib.inflateSync(c0).toString() === input.toString());
                console.log(zlib.inflateSync(c9).toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Deflate_StrategyRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('aaaaabbbbbcccccddddd'.repeat(50));
                const c = zlib.deflateSync(input, { level: 9, strategy: zlib.constants.Z_RLE });
                console.log(zlib.inflateSync(c).toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Brotli_QualityExtremesDifferAndRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('the quick brown fox '.repeat(200));
                const low = zlib.brotliCompressSync(input, {
                    params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 1 }
                });
                const high = zlib.brotliCompressSync(input, {
                    params: { [zlib.constants.BROTLI_PARAM_QUALITY]: 11 }
                });
                // quality is genuinely honored: q11 compresses strictly better than q1
                console.log(high.length < low.length);
                console.log(zlib.brotliDecompressSync(low).toString() === input.toString());
                console.log(zlib.brotliDecompressSync(high).toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Brotli_WindowRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('compress me with a small window '.repeat(100));
                const c = zlib.brotliCompressSync(input, {
                    params: {
                        [zlib.constants.BROTLI_PARAM_QUALITY]: 11,
                        [zlib.constants.BROTLI_PARAM_LGWIN]: 10
                    }
                });
                console.log(zlib.brotliDecompressSync(c).toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Deflate_DictionaryRoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // dictionary is a documented BCL ceiling (accepted, not applied), so a
            // symmetric compress/decompress with the same option must still round-trip.
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const dict = Buffer.from('hello');
                const input = Buffer.from('hello world hello world');
                const c = zlib.deflateSync(input, { dictionary: dict });
                console.log(zlib.inflateSync(c, { dictionary: dict }).toString() === input.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Zlib_MaxOutputLength_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('x'.repeat(10000));
                const compressed = zlib.gzipSync(input);
                let threw = false;
                try {
                    zlib.gunzipSync(compressed, { maxOutputLength: 10 });
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Stream control methods (#1164)

    [Theory, ModeData]
    public void Zlib_Stream_FlushAndCounters(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const gzip = zlib.createGzip();
                const chunks: Buffer[] = [];
                gzip.on('data', (c: Buffer) => { chunks.push(c); });
                gzip.on('end', () => {
                    const all = Buffer.concat(chunks);
                    console.log(zlib.gunzipSync(all).toString() === 'hello flush world');
                    console.log(gzip.bytesWritten);
                    console.log(gzip.bytesRead);
                });
                gzip.write('hello ');
                gzip.flush(() => { console.log('flushed'); });
                gzip.write('flush world');
                gzip.end();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("flushed\ntrue\n17\n17\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Stream_Close(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const gzip = zlib.createGzip();
                gzip.on('close', () => { console.log('close-event'); });
                gzip.close(() => { console.log('close-cb'); });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("close-event\nclose-cb\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Stream_Reset(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // reset() restores a fresh, usable compression stream and zeroes the
            // counter. (Our streams emit output per-write, so reset is meaningful on a
            // freshly created stream — see #1164 notes.)
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const gzip = zlib.createGzip();
                gzip.reset();
                console.log(gzip.bytesWritten);
                const chunks: Buffer[] = [];
                gzip.on('data', (c: Buffer) => { chunks.push(c); });
                gzip.on('end', () => {
                    const all = Buffer.concat(chunks);
                    console.log(zlib.gunzipSync(all).toString() === 'after reset');
                });
                gzip.end('after reset');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("0\ntrue\n", output);
    }

    #endregion

    #region Facade Tests (stdlib/node/zlib.ts over primitive:zlib)

    [Theory, ModeData]
    public void Zlib_Async_BadInput_InvokesErrorCallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            // The TS facade derives the callback forms from the sync primitives;
            // failures must arrive as callback(err) with no result, in both modes.
            ["main.ts"] = """
                import * as zlib from 'zlib';
                zlib.gunzip(Buffer.from('definitely not gzip data'), (err: any, result: any) => {
                    console.log(err !== null && err !== undefined);
                    console.log(result === undefined);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_Codes_BidirectionalMapping(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.codes.Z_DATA_ERROR);
                console.log(zlib.codes['-3']);
                console.log(zlib.constants.Z_MAX_CHUNK === Infinity);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("-3\nZ_DATA_ERROR\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Zlib_NamedImports_ThroughFacade(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { gzipSync, gunzipSync, crc32, constants } from 'zlib';
                console.log(gunzipSync(gzipSync('named-import')).toString());
                console.log(crc32('abc'));
                console.log(constants.Z_BEST_COMPRESSION);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("named-import\n891568578\n9\n", output);
    }

    #endregion
}
