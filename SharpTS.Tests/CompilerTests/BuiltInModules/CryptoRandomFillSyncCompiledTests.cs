using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Compiled mode tests for crypto.randomFillSync().
/// </summary>
public class CryptoRandomFillSyncCompiledTests
{
    [Fact]
    public void Crypto_RandomFillSync_FillsEntireBuffer_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const buffer = Buffer.alloc(16);
                const result = crypto.randomFillSync(buffer);
                console.log(result === buffer); // Returns same buffer
                console.log(buffer.length === 16);
                // Should have some non-zero bytes (using readUInt8 for compiled mode compatibility)
                let hasNonZero = false;
                for (let i = 0; i < buffer.length; i++) {
                    if (buffer.readUInt8(i) !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_WithOffset_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const buffer = Buffer.alloc(16);
                // Store first 4 bytes as zeros
                crypto.randomFillSync(buffer, 4);
                // First 4 bytes should be 0 (untouched)
                console.log(buffer.readUInt8(0) === 0);
                console.log(buffer.readUInt8(1) === 0);
                console.log(buffer.readUInt8(2) === 0);
                console.log(buffer.readUInt8(3) === 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_WithOffsetAndSize_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const buffer = Buffer.alloc(16);
                // Fill only bytes 4-7 (4 bytes starting at offset 4)
                crypto.randomFillSync(buffer, 4, 4);
                // First 4 and last 8 bytes should still be 0
                console.log(buffer.readUInt8(0) === 0);
                console.log(buffer.readUInt8(3) === 0);
                console.log(buffer.readUInt8(8) === 0);
                console.log(buffer.readUInt8(15) === 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_ReturnsBuffer_Compiled()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const buffer = Buffer.alloc(8);
                const result = crypto.randomFillSync(buffer);
                console.log(Buffer.isBuffer(result));
                console.log(result === buffer);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
