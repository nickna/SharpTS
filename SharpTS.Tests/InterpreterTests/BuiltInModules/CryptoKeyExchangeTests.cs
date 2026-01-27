using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for Diffie-Hellman and ECDH key exchange.
/// </summary>
public class CryptoKeyExchangeTests
{
    // ============ DIFFIE-HELLMAN TESTS ============

    [Fact]
    public void Crypto_GetDiffieHellman_Modp14()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                const keys = dh.generateKeys('hex');
                console.log(typeof keys === 'string');
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_GetDiffieHellman_InvalidGroup_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.getDiffieHellman('invalid-group');
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
    public void Crypto_DiffieHellman_GenerateKeys_ReturnsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                const keys = dh.generateKeys();
                console.log(Buffer.isBuffer(keys));
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_DiffieHellman_GenerateKeys_HexEncoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                const keys = dh.generateKeys('hex');
                console.log(typeof keys === 'string');
                // Hex encoding should only contain 0-9, a-f
                const validHex = /^[0-9a-f]+$/.test(keys);
                console.log(validHex);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_DiffieHellman_ComputeSecret_TwoParties()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Alice
                const alice = crypto.getDiffieHellman('modp14');
                const alicePub = alice.generateKeys('hex');

                // Bob
                const bob = crypto.getDiffieHellman('modp14');
                const bobPub = bob.generateKeys('hex');

                // Compute shared secrets
                const aliceSecret = alice.computeSecret(bobPub, 'hex', 'hex');
                const bobSecret = bob.computeSecret(alicePub, 'hex', 'hex');

                // Secrets should match
                console.log(aliceSecret === bobSecret);
                console.log(aliceSecret.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_DiffieHellman_GetPrime()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                const prime = dh.getPrime('hex');
                console.log(typeof prime === 'string');
                console.log(prime.length > 0);
                // MODP14 is 2048 bits = 256 bytes = 512 hex chars
                console.log(prime.length === 512);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Crypto_DiffieHellman_GetGenerator()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                const gen = dh.getGenerator('hex');
                console.log(typeof gen === 'string');
                // Generator should be 2 for MODP groups
                console.log(gen === '02');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_DiffieHellman_VerifyError_IsZero()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const dh = crypto.getDiffieHellman('modp14');
                console.log(dh.verifyError === 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    // ============ ECDH TESTS ============

    [Fact]
    public void Crypto_CreateECDH_P256()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                const keys = ecdh.generateKeys('hex');
                console.log(typeof keys === 'string');
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateECDH_P384()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('secp384r1');
                const keys = ecdh.generateKeys('hex');
                console.log(typeof keys === 'string');
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateECDH_P521()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('secp521r1');
                const keys = ecdh.generateKeys('hex');
                console.log(typeof keys === 'string');
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_CreateECDH_InvalidCurve_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.createECDH('invalid-curve');
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
    public void Crypto_ECDH_GenerateKeys_ReturnsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                const keys = ecdh.generateKeys();
                console.log(Buffer.isBuffer(keys));
                console.log(keys.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_ECDH_ComputeSecret_TwoParties()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                // Alice
                const alice = crypto.createECDH('prime256v1');
                const alicePub = alice.generateKeys();

                // Bob
                const bob = crypto.createECDH('prime256v1');
                const bobPub = bob.generateKeys();

                // Compute shared secrets
                const aliceSecret = alice.computeSecret(bobPub, null, 'hex');
                const bobSecret = bob.computeSecret(alicePub, null, 'hex');

                // Secrets should match
                console.log(aliceSecret === bobSecret);
                console.log(aliceSecret.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_ECDH_GetPublicKey()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                ecdh.generateKeys();
                const pubKey = ecdh.getPublicKey('hex');
                console.log(typeof pubKey === 'string');
                console.log(pubKey.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_ECDH_GetPrivateKey()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                ecdh.generateKeys();
                const privKey = ecdh.getPrivateKey('hex');
                console.log(typeof privKey === 'string');
                console.log(privKey.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Crypto_ECDH_DifferentCurves_DifferentKeyLengths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const p256 = crypto.createECDH('prime256v1');
                const p384 = crypto.createECDH('secp384r1');
                const p521 = crypto.createECDH('secp521r1');

                const key256 = p256.generateKeys('hex');
                const key384 = p384.generateKeys('hex');
                const key521 = p521.generateKeys('hex');

                // Larger curves should produce larger keys
                console.log(key384.length > key256.length);
                console.log(key521.length > key384.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}
