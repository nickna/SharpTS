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
        // randomBytes should return array of specified length
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const bytes16 = crypto.randomBytes(16);
                const bytes32 = crypto.randomBytes(32);
                console.log(Array.isArray(bytes16));
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
}
