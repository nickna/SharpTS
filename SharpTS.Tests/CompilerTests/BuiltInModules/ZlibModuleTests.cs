using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'zlib' module in compiled mode.
/// </summary>
public class ZlibModuleCompiledTests
{
    // ============ GZIP TESTS ============

    [Fact]
    public void Zlib_Gzip_RoundTrip_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_CompressesData_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_StringInput_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Gzip_WithLevel_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ DEFLATE TESTS ============

    [Fact]
    public void Zlib_Deflate_RoundTrip_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_DeflateRaw_RoundTrip_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Deflate_CompressesData_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ BROTLI TESTS ============

    [Fact]
    public void Zlib_Brotli_RoundTrip_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Brotli_CompressesData_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Brotli_LargeData_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ ZSTD TESTS ============
    // Note: Zstd compiled tests are skipped because the compiled DLL runs in a separate
    // process and would need ZstdSharp.dll deployed alongside it. The interpreter tests
    // verify that Zstd works correctly when running from within SharpTS.

    // ============ UNZIP (AUTO-DETECT) TESTS ============

    [Fact]
    public void Zlib_Unzip_DetectsGzip_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Zlib_Unzip_DetectsDeflate_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ CONSTANTS TESTS ============

    [Fact]
    public void Zlib_Constants_CompressionLevels_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Strategies_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_ReturnCodes_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Brotli_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Zlib_Constants_Zstd_Compiled()
    {
        // Note: This tests that zstd constants are available, even though
        // zstd compression itself is not available in compiled mode
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as zlib from 'zlib';
                console.log(zlib.constants.ZSTD_defaultCLevel === 3);
                console.log(zlib.constants.ZSTD_maxCLevel === 22);
                console.log(typeof zlib.constants.ZSTD_c_compressionLevel === 'number');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ RETURN TYPE TESTS ============

    [Fact]
    public void Zlib_ReturnsBuffer_Compiled()
    {
        // Note: zstdCompressSync is not included because it requires ZstdSharp.dll
        // to be deployed alongside the compiled output (see interpreter tests for zstd coverage)
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    // ============ EMPTY INPUT TESTS ============

    [Fact]
    public void Zlib_EmptyInput_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ BINARY DATA TESTS ============

    [Fact]
    public void Zlib_BinaryData_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ NAMED IMPORT TESTS ============

    [Fact]
    public void Zlib_NamedImports_Compiled()
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
