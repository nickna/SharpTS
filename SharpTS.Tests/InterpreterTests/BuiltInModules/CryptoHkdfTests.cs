using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the crypto module's hkdfSync function.
/// Uses interpreter mode for testing.
/// </summary>
public class CryptoHkdfTests
{
    [Fact]
    public void Crypto_HkdfSync_ReturnsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('input key material');
                const salt = Buffer.from('salt');
                const info = Buffer.from('info');
                const key = crypto.hkdfSync('sha256', ikm, salt, info, 32);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_DifferentKeyLengths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('input key material');
                const salt = Buffer.from('salt');
                const info = Buffer.from('info');

                const key16 = crypto.hkdfSync('sha256', ikm, salt, info, 16);
                const key32 = crypto.hkdfSync('sha256', ikm, salt, info, 32);
                const key64 = crypto.hkdfSync('sha256', ikm, salt, info, 64);

                console.log(key16.length === 16);
                console.log(key32.length === 32);
                console.log(key64.length === 64);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_Deterministic()
    {
        // Same inputs should produce same output
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('input key material');
                const salt = Buffer.from('salt');
                const info = Buffer.from('info');

                const key1 = crypto.hkdfSync('sha256', ikm, salt, info, 32);
                const key2 = crypto.hkdfSync('sha256', ikm, salt, info, 32);

                console.log(key1.equals(key2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_DifferentInputsProduceDifferentOutput()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm1 = Buffer.from('key1');
                const ikm2 = Buffer.from('key2');
                const salt = Buffer.from('salt');
                const info = Buffer.from('info');

                const key1 = crypto.hkdfSync('sha256', ikm1, salt, info, 32);
                const key2 = crypto.hkdfSync('sha256', ikm2, salt, info, 32);

                console.log(!key1.equals(key2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_DifferentSalts()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('key');
                const salt1 = Buffer.from('salt1');
                const salt2 = Buffer.from('salt2');
                const info = Buffer.from('info');

                const key1 = crypto.hkdfSync('sha256', ikm, salt1, info, 32);
                const key2 = crypto.hkdfSync('sha256', ikm, salt2, info, 32);

                console.log(!key1.equals(key2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_DifferentInfo()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('key');
                const salt = Buffer.from('salt');
                const info1 = Buffer.from('info1');
                const info2 = Buffer.from('info2');

                const key1 = crypto.hkdfSync('sha256', ikm, salt, info1, 32);
                const key2 = crypto.hkdfSync('sha256', ikm, salt, info2, 32);

                console.log(!key1.equals(key2));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_EmptySaltAndInfo()
    {
        // Empty salt and info should be valid
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('input key material');
                const emptySalt = Buffer.alloc(0);
                const emptyInfo = Buffer.alloc(0);

                const key = crypto.hkdfSync('sha256', ikm, emptySalt, emptyInfo, 32);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_StringInputs()
    {
        // String inputs should be converted to UTF-8 bytes
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.hkdfSync('sha256', 'password', 'salt', 'info', 32);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 32);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_SHA1()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.hkdfSync('sha1', 'key', 'salt', 'info', 20);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 20);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_SHA384()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.hkdfSync('sha384', 'key', 'salt', 'info', 48);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 48);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_SHA512()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.hkdfSync('sha512', 'key', 'salt', 'info', 64);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 64);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_InvalidDigest_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                try {
                    crypto.hkdfSync('md5', 'key', 'salt', 'info', 16);
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_ZeroKeyLength()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const key = crypto.hkdfSync('sha256', 'key', 'salt', 'info', 0);

                console.log(Buffer.isBuffer(key));
                console.log(key.length === 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_HkdfSync_RFC5869_TestVector1()
    {
        // RFC 5869 Test Case 1 (SHA-256)
        // IKM  = 0x0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b (22 octets)
        // salt = 0x000102030405060708090a0b0c (13 octets)
        // info = 0xf0f1f2f3f4f5f6f7f8f9 (10 octets)
        // L    = 42
        // Expected OKM = 0x3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';

                const ikm = Buffer.from('0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b', 'hex');
                const salt = Buffer.from('000102030405060708090a0b0c', 'hex');
                const info = Buffer.from('f0f1f2f3f4f5f6f7f8f9', 'hex');

                const key = crypto.hkdfSync('sha256', ikm, salt, info, 42);
                const expected = '3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865';

                console.log(key.toString('hex') === expected);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
