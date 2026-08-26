using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Parity tests for crypto KeyObject completeness (#1059) and ECDH raw-point
/// encodings + FIPS shims (#1060).
///
/// Covers ECDH raw-point conversion/agreement, KeyObject JWK/DER import and
/// export, structural equality, public-key derivation, one-shot Diffie-Hellman,
/// key generation, and the non-FIPS shims in both execution modes.
/// </summary>
public class CryptoKeyEcdhParityTests
{
    // ----- Dual-mode (compiled + interpreted) -----

    [Theory, ModeData]
    public void Ecdh_TwoParty_RawPoint_Agreement(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const alice = crypto.createECDH('prime256v1');
                const alicePub = alice.generateKeys('hex');       // uncompressed raw point
                const bob = crypto.createECDH('prime256v1');
                const bobPub = bob.generateKeys('hex');
                const s1 = alice.computeSecret(bobPub, 'hex', 'hex');
                const s2 = bob.computeSecret(alicePub, 'hex', 'hex');
                console.log(s1 === s2);
                // uncompressed P-256 point = 1 + 2*32 bytes = 65 -> 130 hex chars, prefix '04'
                console.log(alicePub.length === 130);
                console.log(alicePub.substring(0, 2) === '04');
                """
        };
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Ecdh_GetPrivateKey_RawScalar(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                ecdh.generateKeys();
                const priv = ecdh.getPrivateKey('hex');
                // Raw P-256 scalar is <= 32 bytes (64 hex chars), NOT a PKCS8 blob.
                console.log(priv.length <= 64);
                console.log(priv.length > 0);
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Ecdh_GetPublicKey_CompressedFormat(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                ecdh.generateKeys();
                const comp = ecdh.getPublicKey('hex', 'compressed');
                // compressed P-256 = 1 + 32 bytes = 33 -> 66 hex chars, prefix 02 or 03
                console.log(comp.length === 66);
                const p = comp.substring(0, 2);
                console.log(p === '02' || p === '03');
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void AsymmetricKeyDetails_NamedCurve_P384(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'secp384r1' });
                const key = crypto.createPublicKey(publicKey);
                console.log(key.asymmetricKeyType === 'ec');
                console.log(key.asymmetricKeyDetails.namedCurve === 'secp384r1');
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void GenerateKeySync_Aes256(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const key = crypto.generateKeySync('aes', { length: 256 });
                console.log(key.type === 'secret');
                console.log(key.symmetricKeySize === 32);
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void GenerateKey_Callback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                crypto.generateKey('hmac', { length: 256 }, (err: any, key: any) => {
                    console.log(err === null || err === undefined);
                    console.log(key.type === 'secret');
                    console.log(key.symmetricKeySize === 32);
                });
                """
        };
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Fips_Shims(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                console.log(crypto.getFips() === 0);
                console.log(crypto.fips === false);
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    // ----- KeyObject / static ECDH parity -----

    [Theory, ModeData]
    public void KeyObject_JwkRoundTrip_Rsa(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const key = crypto.createPublicKey(publicKey);
                const jwk = key.export({ format: 'jwk' });
                console.log(jwk.kty === 'RSA');
                console.log(typeof jwk.n === 'string');
                const back = crypto.createPublicKey({ key: jwk, format: 'jwk' });
                console.log(back.asymmetricKeyType === 'rsa');
                """
        };
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void KeyObject_DerRoundTrip_Rsa(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const pair = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const pub = crypto.createPublicKey(pair.publicKey);
                const pubDer = pub.export({ format: 'der', type: 'spki' });
                const pubBack = crypto.createPublicKey({ key: pubDer, format: 'der', type: 'spki' });
                console.log(pub.equals(pubBack));
                const priv = crypto.createPrivateKey(pair.privateKey);
                const privDer = priv.export({ format: 'der', type: 'pkcs8' });
                const privBack = crypto.createPrivateKey({ key: privDer, format: 'der', type: 'pkcs8' });
                console.log(priv.equals(privBack));
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Ecdh_ComputeSecret_AcceptsCompressedPoints(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const alice = crypto.createECDH('prime256v1');
                const bob = crypto.createECDH('prime256v1');
                const alicePub = alice.generateKeys('hex', 'compressed');
                const bobPub = bob.generateKeys('hex', 'compressed');
                const s1 = alice.computeSecret(bobPub, 'hex', 'hex');
                const s2 = bob.computeSecret(alicePub, 'hex', 'hex');
                console.log(s1 === s2);
                """
        };
        Assert.Equal("true\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Ecdh_ConvertKey_AllSupportedNistCurves(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                for (const curve of ['prime256v1', 'secp384r1', 'secp521r1']) {
                    const ecdh = crypto.createECDH(curve);
                    const uncompressed = ecdh.generateKeys('hex');
                    const compressed = crypto.ECDH.convertKey(
                        uncompressed, curve, 'hex', 'hex', 'compressed');
                    const roundtrip = crypto.ECDH.convertKey(
                        compressed, curve, 'hex', 'hex', 'uncompressed');
                    console.log(roundtrip === uncompressed);
                }
                """
        };
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void KeyObject_Equals(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { publicKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const a = crypto.createPublicKey(publicKey);
                const b = crypto.createPublicKey(publicKey);
                console.log(a.equals(b));
                const { publicKey: other } = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                console.log(a.equals(crypto.createPublicKey(other)) === false);
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void CreatePublicKey_FromPrivateKeyObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const { privateKey } = crypto.generateKeyPairSync('rsa', { modulusLength: 2048 });
                const priv = crypto.createPrivateKey(privateKey);
                const pub = crypto.createPublicKey(priv);
                console.log(pub.type === 'public');
                console.log(pub.asymmetricKeyType === 'rsa');
                """
        };
        Assert.Equal("true\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void Ecdh_ConvertKey_And_OneShotDiffieHellman(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const ecdh = crypto.createECDH('prime256v1');
                const uncompressed = ecdh.generateKeys('hex');
                const compressed = crypto.ECDH.convertKey(uncompressed, 'prime256v1', 'hex', 'hex', 'compressed');
                console.log(compressed.length === 66);
                const roundtrip = crypto.ECDH.convertKey(compressed, 'prime256v1', 'hex', 'hex', 'uncompressed');
                console.log(roundtrip === uncompressed);

                const alice = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const bob = crypto.generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
                const s1 = crypto.diffieHellman({
                    privateKey: crypto.createPrivateKey(alice.privateKey),
                    publicKey: crypto.createPublicKey(bob.publicKey),
                });
                const s2 = crypto.diffieHellman({
                    privateKey: crypto.createPrivateKey(bob.privateKey),
                    publicKey: crypto.createPublicKey(alice.publicKey),
                });
                console.log(s1.toString('hex') === s2.toString('hex'));
                """
        };
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
