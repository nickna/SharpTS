using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'zlib' module.
/// </summary>
public class ZlibModuleTests
{
    // ============ GZIP TESTS ============

    [Fact]
    public void Zlib_Gzip_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_CompressesData()
    {
        // Compressing repeated data should result in smaller output
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                const input = Buffer.from('hello world '.repeat(100));
                const compressed = zlib.gzipSync(input);
                console.log(compressed.length < input.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_StringInput()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_WithLevel()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ DEFLATE TESTS ============

    [Fact]
    public void Zlib_Deflate_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_DeflateRaw_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Deflate_CompressesData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ BROTLI TESTS ============

    [Fact]
    public void Zlib_Brotli_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Brotli_CompressesData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Brotli_LargeData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ ZSTD TESTS ============

    [Fact]
    public void Zlib_Zstd_RoundTrip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Zstd_CompressesData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Zstd_LargeData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ UNZIP (AUTO-DETECT) TESTS ============

    [Fact]
    public void Zlib_Unzip_DetectsGzip()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Unzip_DetectsDeflate()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ CONSTANTS TESTS ============

    [Fact]
    public void Zlib_Constants_CompressionLevels()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Strategies()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_ReturnCodes()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Brotli()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Zstd()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ RETURN TYPE TESTS ============

    [Fact]
    public void Zlib_ReturnsBuffer()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    // ============ EMPTY INPUT TESTS ============

    [Fact]
    public void Zlib_EmptyInput()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ BINARY DATA TESTS ============

    [Fact]
    public void Zlib_BinaryData()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ NAMED IMPORT TESTS ============

    [Fact]
    public void Zlib_NamedImports()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
