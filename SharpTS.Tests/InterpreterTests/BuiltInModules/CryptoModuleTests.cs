using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'crypto' module.
/// Uses interpreter mode since crypto module hash object method calls aren't fully supported in compiled mode.
/// </summary>
public class CryptoModuleTests
{
    // ============ HASH ALGORITHM TESTS ============

    [Fact]
    public void Crypto_CreateHash_Md5()
    {
        // MD5 hash should produce 32-character hex string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('md5');
                hash.update('hello');
                const digest = hash.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 32);
                // Known MD5 of 'hello': 5d41402abc4b2a76b9719d911017c592
                console.log(digest === '5d41402abc4b2a76b9719d911017c592');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_Sha1()
    {
        // SHA1 hash should produce 40-character hex string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('sha1');
                hash.update('hello');
                const digest = hash.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 40);
                // Known SHA1 of 'hello': aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d
                console.log(digest === 'aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_Sha256()
    {
        // SHA256 hash should produce 64-character hex string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('sha256');
                hash.update('hello');
                const digest = hash.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 64);
                // Known SHA256 of 'hello': 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
                console.log(digest === '2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_Sha512()
    {
        // SHA512 hash should produce 128-character hex string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('sha512');
                hash.update('hello');
                const digest = hash.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 128);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_UpdateMultiple()
    {
        // Multiple updates should produce same result as single update
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Hash with single update
                const hash1 = crypto.createHash('sha256');
                hash1.update('helloworld');
                const digest1 = hash1.digest('hex');
                // Hash with multiple updates
                const hash2 = crypto.createHash('sha256');
                hash2.update('hello');
                hash2.update('world');
                const digest2 = hash2.digest('hex');
                console.log(digest1 === digest2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_EmptyInput()
    {
        // Empty string should produce valid hash
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('sha256');
                hash.update('');
                const digest = hash.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 64);
                // Known SHA256 of empty string: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
                console.log(digest === 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHash_Base64Encoding()
    {
        // Base64 encoding should work
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hash = crypto.createHash('sha256');
                hash.update('hello');
                const digest = hash.digest('base64');
                console.log(typeof digest === 'string');
                // Base64 encoded SHA256 should be 44 chars (with padding)
                console.log(digest.length === 44);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ RANDOM BYTES TESTS ============

    [Fact]
    public void Crypto_RandomBytes_ReturnsCorrectLength()
    {
        // randomBytes returns a Buffer of specified length
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const bytes16 = crypto.randomBytes(16);
                const bytes32 = crypto.randomBytes(32);
                console.log(Buffer.isBuffer(bytes16));
                console.log(bytes16.length === 16);
                console.log(bytes32.length === 32);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomBytes_ValuesInRange()
    {
        // All byte values should be in range 0-255
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const bytes = crypto.randomBytes(100);
                let allInRange = true;
                for (const b of bytes) {
                    if (b < 0 || b > 255) {
                        allInRange = false;
                        break;
                    }
                }
                console.log(allInRange);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_RandomBytes_NotAllZeros()
    {
        // Random data should not be all zeros (probabilistically impossible)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const bytes = crypto.randomBytes(32);
                let hasNonZero = false;
                for (const b of bytes) {
                    if (b !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ RANDOM FILL SYNC TESTS ============

    [Fact]
    public void Crypto_RandomFillSync_FillsEntireBuffer()
    {
        // randomFillSync fills a buffer in place and returns it
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const buffer = Buffer.alloc(16);
                const result = crypto.randomFillSync(buffer);
                console.log(result === buffer); // Returns same buffer
                console.log(buffer.length === 16);
                // Should have some non-zero bytes
                let hasNonZero = false;
                for (const b of buffer) {
                    if (b !== 0) {
                        hasNonZero = true;
                        break;
                    }
                }
                console.log(hasNonZero);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_WithOffset()
    {
        // randomFillSync with offset fills from that position
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_WithOffsetAndSize()
    {
        // randomFillSync with offset and size fills only that range
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomFillSync_ReturnsBuffer()
    {
        // randomFillSync returns the same buffer for chaining
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ RANDOM UUID TESTS ============

    [Fact]
    public void Crypto_RandomUUID_Format()
    {
        // UUID should be in format xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const uuid = crypto.randomUUID();
                console.log(typeof uuid === 'string');
                console.log(uuid.length === 36);
                // Check format: 8-4-4-4-12 with dashes
                const parts = uuid.split('-');
                console.log(parts.length === 5);
                console.log(parts[0].length === 8);
                console.log(parts[1].length === 4);
                console.log(parts[2].length === 4);
                console.log(parts[3].length === 4);
                console.log(parts[4].length === 12);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_RandomUUID_IsUnique()
    {
        // Two UUID calls should return different values
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const uuid1 = crypto.randomUUID();
                const uuid2 = crypto.randomUUID();
                console.log(uuid1 !== uuid2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_RandomUUID_ContainsOnlyValidChars()
    {
        // UUID should only contain hex characters and dashes
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const uuid = crypto.randomUUID();
                const validChars = '0123456789abcdef-';
                let allValid = true;
                for (const c of uuid) {
                    if (!validChars.includes(c)) {
                        allValid = false;
                        break;
                    }
                }
                console.log(allValid);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ RANDOM INT TESTS ============

    [Fact]
    public void Crypto_RandomInt_InRange()
    {
        // randomInt(max) should return values in [0, max)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                let allInRange = true;
                for (let i = 0; i < 100; i++) {
                    const val = crypto.randomInt(10);
                    if (val < 0 || val >= 10) {
                        allInRange = false;
                        break;
                    }
                }
                console.log(allInRange);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_RandomInt_MinMax()
    {
        // randomInt(min, max) should return values in [min, max)
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                let allInRange = true;
                for (let i = 0; i < 100; i++) {
                    const val = crypto.randomInt(5, 15);
                    if (val < 5 || val >= 15) {
                        allInRange = false;
                        break;
                    }
                }
                console.log(allInRange);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_RandomInt_ReturnsInteger()
    {
        // randomInt should return whole numbers
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const val = crypto.randomInt(100);
                console.log(typeof val === 'number');
                console.log(Math.floor(val) === val);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ HMAC TESTS ============

    [Fact]
    public void Crypto_CreateHmac_Sha256()
    {
        // Known HMAC-SHA256 value verification
        // HMAC-SHA256("key", "The quick brown fox jumps over the lazy dog")
        // = f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hmac = crypto.createHmac('sha256', 'key');
                hmac.update('The quick brown fox jumps over the lazy dog');
                const digest = hmac.digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 64);
                console.log(digest === 'f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHmac_AllAlgorithms()
    {
        // Test that all supported algorithms produce valid output lengths
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // MD5 = 32 hex chars
                const md5 = crypto.createHmac('md5', 'secret');
                md5.update('test');
                console.log(md5.digest('hex').length === 32);
                // SHA1 = 40 hex chars
                const sha1 = crypto.createHmac('sha1', 'secret');
                sha1.update('test');
                console.log(sha1.digest('hex').length === 40);
                // SHA256 = 64 hex chars
                const sha256 = crypto.createHmac('sha256', 'secret');
                sha256.update('test');
                console.log(sha256.digest('hex').length === 64);
                // SHA384 = 96 hex chars
                const sha384 = crypto.createHmac('sha384', 'secret');
                sha384.update('test');
                console.log(sha384.digest('hex').length === 96);
                // SHA512 = 128 hex chars
                const sha512 = crypto.createHmac('sha512', 'secret');
                sha512.update('test');
                console.log(sha512.digest('hex').length === 128);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHmac_UpdateMultiple()
    {
        // Multiple updates should produce same result as single update
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // HMAC with single update
                const hmac1 = crypto.createHmac('sha256', 'secret');
                hmac1.update('helloworld');
                const digest1 = hmac1.digest('hex');
                // HMAC with multiple updates
                const hmac2 = crypto.createHmac('sha256', 'secret');
                hmac2.update('hello');
                hmac2.update('world');
                const digest2 = hmac2.digest('hex');
                console.log(digest1 === digest2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateHmac_Base64Encoding()
    {
        // HMAC digest with base64 encoding
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hmac = crypto.createHmac('sha256', 'secret');
                hmac.update('hello');
                const digest = hmac.digest('base64');
                console.log(typeof digest === 'string');
                // Base64 encoded SHA256 HMAC should be 44 chars (with padding)
                console.log(digest.length === 44);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateHmac_DifferentKeys()
    {
        // Same message, different keys should produce different results
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const hmac1 = crypto.createHmac('sha256', 'key1');
                hmac1.update('message');
                const digest1 = hmac1.digest('hex');
                const hmac2 = crypto.createHmac('sha256', 'key2');
                hmac2.update('message');
                const digest2 = hmac2.digest('hex');
                console.log(digest1 !== digest2);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_CreateHmac_MethodChaining()
    {
        // createHmac().update().digest() chain
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const digest = crypto.createHmac('sha256', 'secret').update('test').digest('hex');
                console.log(typeof digest === 'string');
                console.log(digest.length === 64);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
