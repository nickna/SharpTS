using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// WebCrypto tests (#1063): globalThis.crypto, crypto.webcrypto, crypto.subtle.
/// Dual-mode except where compiled mode has a documented ceiling (asymmetric jwk,
/// jwk import) — those run interpreted-only.
/// </summary>
public class CryptoWebCryptoTests
{
    [Theory, ModeData]
    public void Subtle_Digest_Sha256_KnownVector(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const d = await crypto.subtle.digest('SHA-256', Buffer.from('abc'));
                    console.log(Buffer.from(d).toString('hex') === 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');
                    const d1 = await crypto.subtle.digest({ name: 'SHA-1' }, Buffer.from('abc'));
                    console.log(Buffer.from(d1).toString('hex') === 'a9993e364706816aba3e25717850c26c9cd0d89d');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void GetRandomValues_FillsAndReturnsSameArray(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const arr = new Uint8Array(16);
                const same = crypto.getRandomValues(arr);
                console.log(same === arr);
                let nonzero = false;
                for (let i = 0; i < 16; i++) { if (arr[i] !== 0) nonzero = true; }
                console.log(nonzero);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void GlobalThisCrypto_RandomUUID_And_WebcryptoAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                const uuid = globalThis.crypto.randomUUID();
                console.log(uuid.length === 36);
                console.log(uuid.split('-').length === 5);
                console.log(crypto.webcrypto.subtle === crypto.subtle);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_AesGcm_GenerateEncryptDecrypt_RoundTrip(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    console.log(key.type === 'secret');
                    console.log(key.extractable === true);
                    console.log(key.algorithm.name === 'AES-GCM');
                    console.log(key.algorithm.length === 256);
                    const iv = crypto.getRandomValues(new Uint8Array(12));
                    const ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, key, Buffer.from('hello world'));
                    const pt = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv }, key, ct);
                    console.log(Buffer.from(pt).toString('utf8') === 'hello world');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_AesCbc_RoundTrip_WithAad_Gcm(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const cbcKey = await crypto.subtle.generateKey({ name: 'AES-CBC', length: 128 }, true, ['encrypt', 'decrypt']);
                    const iv = crypto.getRandomValues(new Uint8Array(16));
                    const ct = await crypto.subtle.encrypt({ name: 'AES-CBC', iv: iv }, cbcKey, Buffer.from('cbc data'));
                    const pt = await crypto.subtle.decrypt({ name: 'AES-CBC', iv: iv }, cbcKey, ct);
                    console.log(Buffer.from(pt).toString('utf8') === 'cbc data');

                    const gcmKey = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    const iv2 = crypto.getRandomValues(new Uint8Array(12));
                    const aad = Buffer.from('authctx');
                    const ct2 = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv2, additionalData: aad }, gcmKey, Buffer.from('gcm data'));
                    const pt2 = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv2, additionalData: aad }, gcmKey, ct2);
                    console.log(Buffer.from(pt2).toString('utf8') === 'gcm data');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Hmac_SignVerify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const key = await crypto.subtle.generateKey({ name: 'HMAC', hash: 'SHA-256' }, true, ['sign', 'verify']);
                    const sig = await crypto.subtle.sign('HMAC', key, Buffer.from('data'));
                    console.log(await crypto.subtle.verify('HMAC', key, sig, Buffer.from('data')));
                    console.log(await crypto.subtle.verify('HMAC', key, sig, Buffer.from('tampered')));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Subtle_Hmac_MatchesCreateHmac_InterpOnly(ExecutionMode mode)
    {
        // Interp-only: compiled createHmac's named-import key conversion doesn't
        // handle Buffer keys yet (pre-existing gap in EmitObjectToKeyBytes).
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const key = await crypto.subtle.generateKey({ name: 'HMAC', hash: 'SHA-256' }, true, ['sign', 'verify']);
                    const sig = await crypto.subtle.sign('HMAC', key, Buffer.from('data'));
                    const raw = await crypto.subtle.exportKey('raw', key);
                    const nodeSig = crypto.createHmac('sha256', Buffer.from(raw)).update('data').digest();
                    console.log(Buffer.from(sig).toString('hex') === nodeSig.toString('hex'));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Ecdsa_SignVerify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const kp = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
                    console.log(kp.publicKey.type === 'public');
                    console.log(kp.privateKey.type === 'private');
                    console.log(kp.publicKey.algorithm.namedCurve === 'P-256');
                    const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, kp.privateKey, Buffer.from('data'));
                    // WebCrypto ECDSA signatures are raw r||s — 64 bytes for P-256
                    console.log(Buffer.from(sig).length === 64);
                    console.log(await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, kp.publicKey, sig, Buffer.from('data')));
                    console.log(await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, kp.publicKey, sig, Buffer.from('other')));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Rsa_OaepRoundTrip_And_PssSign(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const oaep = await crypto.subtle.generateKey(
                        { name: 'RSA-OAEP', modulusLength: 2048, publicExponent: 65537, hash: 'SHA-256' },
                        true, ['encrypt', 'decrypt']);
                    const ct = await crypto.subtle.encrypt({ name: 'RSA-OAEP' }, oaep.publicKey, Buffer.from('secret'));
                    const pt = await crypto.subtle.decrypt({ name: 'RSA-OAEP' }, oaep.privateKey, ct);
                    console.log(Buffer.from(pt).toString('utf8') === 'secret');

                    const ssa = await crypto.subtle.generateKey(
                        { name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: 65537, hash: 'SHA-256' },
                        true, ['sign', 'verify']);
                    const sig = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', ssa.privateKey, Buffer.from('data'));
                    console.log(await crypto.subtle.verify('RSASSA-PKCS1-v1_5', ssa.publicKey, sig, Buffer.from('data')));

                    const pss = await crypto.subtle.generateKey(
                        { name: 'RSA-PSS', modulusLength: 2048, publicExponent: 65537, hash: 'SHA-256' },
                        true, ['sign', 'verify']);
                    const psig = await crypto.subtle.sign({ name: 'RSA-PSS', saltLength: 32 }, pss.privateKey, Buffer.from('data'));
                    console.log(await crypto.subtle.verify({ name: 'RSA-PSS', saltLength: 32 }, pss.publicKey, psig, Buffer.from('data')));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Pbkdf2_DeriveBits_MatchesPbkdf2Sync(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const pw = await crypto.subtle.importKey('raw', Buffer.from('password'), 'PBKDF2', false, ['deriveBits']);
                    const bits = await crypto.subtle.deriveBits(
                        { name: 'PBKDF2', salt: Buffer.from('salt'), iterations: 1000, hash: 'SHA-256' }, pw, 256);
                    const expected = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                    console.log(Buffer.from(bits).toString('hex') === expected.toString('hex'));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Hkdf_DeriveBits_MatchesHkdfSync(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const ikm = await crypto.subtle.importKey('raw', Buffer.from('input key material'), 'HKDF', false, ['deriveBits']);
                    const bits = await crypto.subtle.deriveBits(
                        { name: 'HKDF', hash: 'SHA-256', salt: Buffer.from('salt'), info: Buffer.from('info') }, ikm, 256);
                    const expected = crypto.hkdfSync('sha256', 'input key material', 'salt', 'info', 32);
                    console.log(Buffer.from(bits).toString('hex') === expected.toString('hex'));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Subtle_Ecdh_DeriveBits_TwoParty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const a = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
                    const b = await crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, true, ['deriveBits']);
                    const s1 = await crypto.subtle.deriveBits({ name: 'ECDH', public: b.publicKey }, a.privateKey, 256);
                    const s2 = await crypto.subtle.deriveBits({ name: 'ECDH', public: a.publicKey }, b.privateKey, 256);
                    console.log(Buffer.from(s1).toString('hex') === Buffer.from(s2).toString('hex'));
                    console.log(Buffer.from(s1).length === 32);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_DeriveKey_Pbkdf2ToAesGcm(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const pw = await crypto.subtle.importKey('raw', Buffer.from('password'), 'PBKDF2', false, ['deriveKey']);
                    const key = await crypto.subtle.deriveKey(
                        { name: 'PBKDF2', salt: Buffer.from('salt'), iterations: 1000, hash: 'SHA-256' },
                        pw, { name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    console.log(key.type === 'secret');
                    console.log(key.algorithm.name === 'AES-GCM');
                    const iv = crypto.getRandomValues(new Uint8Array(12));
                    const ct = await crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv }, key, Buffer.from('derived!'));
                    const pt = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: iv }, key, ct);
                    console.log(Buffer.from(pt).toString('utf8') === 'derived!');

                    // The derived key matches pbkdf2Sync-derived raw bytes
                    const raw = await crypto.subtle.exportKey('raw', key);
                    const expected = crypto.pbkdf2Sync('password', 'salt', 1000, 32, 'sha256');
                    console.log(Buffer.from(raw).toString('hex') === expected.toString('hex'));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_ImportExport_RawAndSpkiPkcs8_RoundTrips(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    // raw secret round-trip
                    const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    const raw = await crypto.subtle.exportKey('raw', key);
                    const reimported = await crypto.subtle.importKey('raw', raw, 'AES-GCM', true, ['encrypt', 'decrypt']);
                    const raw2 = await crypto.subtle.exportKey('raw', reimported);
                    console.log(Buffer.from(raw).toString('hex') === Buffer.from(raw2).toString('hex'));

                    // EC public spki + raw round-trips; pkcs8 private round-trip signs correctly
                    const kp = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
                    const spki = await crypto.subtle.exportKey('spki', kp.publicKey);
                    const rawPoint = await crypto.subtle.exportKey('raw', kp.publicKey);
                    console.log(Buffer.from(rawPoint).length === 65); // 04 || X(32) || Y(32)
                    const pubFromRaw = await crypto.subtle.importKey('raw', rawPoint, { name: 'ECDSA', namedCurve: 'P-256' }, true, ['verify']);
                    const pubFromSpki = await crypto.subtle.importKey('spki', spki, { name: 'ECDSA', namedCurve: 'P-256' }, true, ['verify']);
                    const pkcs8 = await crypto.subtle.exportKey('pkcs8', kp.privateKey);
                    const privFromPkcs8 = await crypto.subtle.importKey('pkcs8', pkcs8, { name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign']);
                    const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, privFromPkcs8, Buffer.from('x'));
                    console.log(await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, pubFromRaw, sig, Buffer.from('x')));
                    console.log(await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, pubFromSpki, sig, Buffer.from('x')));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_ExportKey_Jwk_SecretOct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 128 }, true, ['encrypt', 'decrypt']);
                    const jwk = await crypto.subtle.exportKey('jwk', key);
                    console.log(jwk.kty === 'oct');
                    console.log(typeof jwk.k === 'string');
                    console.log(jwk.ext === true);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_WrapUnwrapKey_RawAesGcm(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const kek = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['wrapKey', 'unwrapKey', 'encrypt', 'decrypt']);
                    const dek = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    const iv = crypto.getRandomValues(new Uint8Array(12));
                    const wrapped = await crypto.subtle.wrapKey('raw', dek, kek, { name: 'AES-GCM', iv: iv });
                    const unwrapped = await crypto.subtle.unwrapKey('raw', wrapped, kek, { name: 'AES-GCM', iv: iv }, { name: 'AES-GCM', length: 256 }, true, ['encrypt', 'decrypt']);
                    const raw1 = await crypto.subtle.exportKey('raw', dek);
                    const raw2 = await crypto.subtle.exportKey('raw', unwrapped);
                    console.log(Buffer.from(raw1).toString('hex') === Buffer.from(raw2).toString('hex'));
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Subtle_UnsupportedAlgorithms_ThrowClearErrors(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    try {
                        await crypto.subtle.generateKey({ name: 'AES-CTR', length: 128 }, true, ['encrypt']);
                        console.log('no-throw');
                    } catch (e: any) {
                        console.log(String(e).includes('AES-CTR') || String(e).includes('unsupported'));
                    }
                    try {
                        await crypto.subtle.generateKey('Ed25519', true, ['sign']);
                        console.log('no-throw');
                    } catch (e: any) {
                        console.log(String(e).includes('Ed25519') || String(e).includes('ED25519') || String(e).includes('unsupported'));
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Subtle_ExportKey_NotExtractable_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
                    console.log(key.extractable === false);
                    try {
                        await crypto.subtle.exportKey('raw', key);
                        console.log('no-throw');
                    } catch (e: any) {
                        console.log(String(e).includes('not extractable'));
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // ── Interpreted-only: jwk import/export of asymmetric keys is a documented
    //    compiled-mode ceiling (use spki/pkcs8 there). ─────────────────────────

    [Theory, InterpretedOnlyData]
    public void Subtle_Jwk_AsymmetricExportImport_InterpOnly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                async function main() {
                    const kp = await crypto.subtle.generateKey({ name: 'ECDSA', namedCurve: 'P-256' }, true, ['sign', 'verify']);
                    const jwk = await crypto.subtle.exportKey('jwk', kp.publicKey);
                    console.log(jwk.kty === 'EC');
                    console.log(jwk.crv === 'P-256');
                    const back = await crypto.subtle.importKey('jwk', jwk, { name: 'ECDSA', namedCurve: 'P-256' }, true, ['verify']);
                    const sig = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, kp.privateKey, Buffer.from('m'));
                    console.log(await crypto.subtle.verify({ name: 'ECDSA', hash: 'SHA-256' }, back, sig, Buffer.from('m')));

                    const rsa = await crypto.subtle.generateKey(
                        { name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048, publicExponent: 65537, hash: 'SHA-256' },
                        true, ['sign', 'verify']);
                    const rjwk = await crypto.subtle.exportKey('jwk', rsa.privateKey);
                    console.log(rjwk.kty === 'RSA');
                    console.log(typeof rjwk.d === 'string');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void GetRandomValues_QuotaExceeded_InterpOnly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as crypto from 'crypto';
                try {
                    crypto.getRandomValues(new Uint8Array(65537));
                    console.log('no-throw');
                } catch (e: any) {
                    console.log(String(e).includes('QuotaExceededError'));
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}
