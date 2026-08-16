using System.Security.Cryptography;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Shared key-input resolution and sign/verify/RSA-cipher cores for the Node
/// <c>crypto</c> module (epic #1054: one-shot sign/verify #1055, constants-driven
/// padding #1056, RSA options #1057).
/// </summary>
/// <remarks>
/// A Node "key" argument is a PEM string, a Buffer holding PEM, a
/// <see cref="SharpTSKeyObject"/>, or an options object
/// <c>{ key, padding, saltLength, dsaEncoding, oaepHash, oaepLabel }</c>.
/// This class parses all of those once so <c>crypto.sign/verify</c>,
/// <c>Sign.sign</c>/<c>Verify.verify</c>, and the four RSA cipher methods agree.
/// </remarks>
internal static class CryptoKeyUtil
{
    // crypto.constants values (kept in sync with CryptoInfoTables)
    internal const int RSA_PKCS1_PADDING = 1;
    internal const int RSA_NO_PADDING = 3;
    internal const int RSA_PKCS1_OAEP_PADDING = 4;
    internal const int RSA_PKCS1_PSS_PADDING = 6;
    internal const int RSA_PSS_SALTLEN_DIGEST = -1;
    internal const int RSA_PSS_SALTLEN_MAX_SIGN = -2;

    /// <summary>Parsed form of a Node key argument.</summary>
    internal sealed class KeyInput
    {
        public string? Pem;
        public SharpTSKeyObject? KeyObject;
        public int Padding = -1;          // RSA_* constant; -1 = default
        public int SaltLength = int.MinValue; // int.MinValue = unspecified
        public string? DsaEncoding;       // "der" (Node default) | "ieee-p1363"
        public string? OaepHash;          // digest name for OAEP
        public bool HasOaepLabel;
    }

    /// <summary>
    /// Parses a key argument (PEM string / Buffer / KeyObject / options object).
    /// </summary>
    internal static KeyInput ParseKeyInput(object? key, string context)
    {
        var input = new KeyInput();
        switch (key)
        {
            case string pem:
                input.Pem = pem;
                return input;
            case SharpTSBuffer buf:
                input.Pem = System.Text.Encoding.UTF8.GetString(buf.Data);
                return input;
            case SharpTSKeyObject keyObj:
                input.KeyObject = keyObj;
                return input;
            case SharpTSObject obj:
                if (obj.Fields.TryGetValue("key", out var k))
                {
                    switch (k)
                    {
                        case string pemStr: input.Pem = pemStr; break;
                        case SharpTSBuffer pemBuf: input.Pem = System.Text.Encoding.UTF8.GetString(pemBuf.Data); break;
                        case SharpTSKeyObject ko: input.KeyObject = ko; break;
                    }
                }
                if (input.Pem == null && input.KeyObject == null)
                    throw new ArgumentException($"{context}: key object must have a 'key' property (PEM string, Buffer, or KeyObject)");
                if (obj.Fields.TryGetValue("padding", out var p) && p is double pd)
                    input.Padding = (int)pd;
                if (obj.Fields.TryGetValue("saltLength", out var s) && s is double sd)
                    input.SaltLength = (int)sd;
                if (obj.Fields.TryGetValue("dsaEncoding", out var d) && d is string ds)
                    input.DsaEncoding = ds;
                if (obj.Fields.TryGetValue("oaepHash", out var oh) && oh is string ohs)
                    input.OaepHash = ohs;
                if (obj.Fields.TryGetValue("oaepLabel", out var ol) && ol is not null)
                    input.HasOaepLabel = true;
                return input;
            default:
                throw new ArgumentException($"{context}: key must be a PEM string, Buffer, KeyObject, or options object");
        }
    }

    /// <summary>Creates an RSA instance from the parsed key (or returns null if it's an EC key).</summary>
    private static RSA? TryGetRsa(KeyInput input)
    {
        if (input.KeyObject != null)
            return input.KeyObject.RsaKey;

        var pem = input.Pem!;
        if (pem.Contains("EC PRIVATE KEY") || pem.Contains("EC PUBLIC KEY"))
            return null;
        try
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Creates an ECDsa instance from the parsed key (or returns null).</summary>
    private static ECDsa? TryGetEcdsa(KeyInput input)
    {
        if (input.KeyObject != null)
            return input.KeyObject.EcdsaKey;

        try
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(input.Pem!);
            return ecdsa;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool OwnsKey(KeyInput input) => input.KeyObject == null;

    /// <summary>Maps a Node dsaEncoding option to the BCL signature format ('der' is Node's default).</summary>
    private static DSASignatureFormat GetSignatureFormat(string? dsaEncoding, string context)
    {
        return (dsaEncoding ?? "der").ToLowerInvariant() switch
        {
            "der" => DSASignatureFormat.Rfc3279DerSequence,
            "ieee-p1363" => DSASignatureFormat.IeeeP1363FixedFieldConcatenation,
            _ => throw new ArgumentException($"{context}: invalid dsaEncoding '{dsaEncoding}' (expected 'der' or 'ieee-p1363')")
        };
    }

    private static RSASignaturePadding GetRsaSignaturePadding(KeyInput input, string hashAlgorithm, string context)
    {
        switch (input.Padding)
        {
            case -1:
            case RSA_PKCS1_PADDING:
                return RSASignaturePadding.Pkcs1;
            case RSA_PKCS1_PSS_PADDING:
                // .NET's PSS implementation always uses saltLength == digest length
                // (RSA_PSS_SALTLEN_DIGEST). Reject explicit salt lengths it can't honor.
                if (input.SaltLength != int.MinValue &&
                    input.SaltLength >= 0 &&
                    input.SaltLength != HashByteLength(hashAlgorithm))
                {
                    throw new NotSupportedException(
                        $"{context}: PSS saltLength {input.SaltLength} is not supported on this runtime (.NET always uses the digest length; use RSA_PSS_SALTLEN_DIGEST)");
                }
                return RSASignaturePadding.Pss;
            default:
                throw new ArgumentException($"{context}: unsupported RSA signature padding {input.Padding}");
        }
    }

    private static int HashByteLength(string algorithm) => algorithm.ToLowerInvariant() switch
    {
        "md5" => 16,
        "sha1" => 20,
        "sha256" or "sha3-256" => 32,
        "sha384" or "sha3-384" => 48,
        "sha512" or "sha3-512" => 64,
        _ => -1
    };

    /// <summary>
    /// One-shot / streaming sign core: signs <paramref name="data"/> with the given key
    /// argument, honoring <c>{ padding, saltLength, dsaEncoding }</c> options.
    /// </summary>
    internal static byte[] SignData(string? algorithm, byte[] data, object? keyArg, string context = "sign")
    {
        if (string.IsNullOrEmpty(algorithm))
            throw new NotSupportedException(
                $"{context}: signing without a digest algorithm requires Ed25519/Ed448 keys, which are not supported on this runtime (.NET BCL has no EdDSA); pass an explicit algorithm");

        var hashName = CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "signing");
        var input = ParseKeyInput(keyArg, context);

        var ecdsa = TryGetEcdsa(input);
        // A generic PKCS#8/SPKI PEM can import into either type; prefer EC when the
        // PEM is explicitly EC or the KeyObject holds an EC key, matching the
        // pre-existing Sign/Verify behavior. For PEM strings try EC first, RSA fallback.
        if (input.KeyObject?.EcdsaKey != null || (input.KeyObject == null && ecdsa != null && LooksEc(input)))
        {
            try
            {
                var format = GetSignatureFormat(input.DsaEncoding, context);
                return ecdsa!.SignData(data, hashName, format);
            }
            finally
            {
                if (OwnsKey(input)) ecdsa!.Dispose();
            }
        }
        if (OwnsKey(input)) ecdsa?.Dispose();

        var rsa = TryGetRsa(input) ?? throw new ArgumentException($"{context}: unable to parse key (unsupported key format)");
        try
        {
            var padding = GetRsaSignaturePadding(input, algorithm, context);
            return rsa.SignData(data, hashName, padding);
        }
        finally
        {
            if (OwnsKey(input)) rsa.Dispose();
        }
    }

    /// <summary>
    /// One-shot / streaming verify core (see <see cref="SignData"/>).
    /// </summary>
    internal static bool VerifyData(string? algorithm, byte[] data, object? keyArg, byte[] signature, string context = "verify")
    {
        if (string.IsNullOrEmpty(algorithm))
            throw new NotSupportedException(
                $"{context}: verifying without a digest algorithm requires Ed25519/Ed448 keys, which are not supported on this runtime (.NET BCL has no EdDSA); pass an explicit algorithm");

        var hashName = CryptoAlgorithms.ParseHashName(algorithm, stripSignaturePrefix: true, context: "verification");
        var input = ParseKeyInput(keyArg, context);

        var ecdsa = TryGetEcdsa(input);
        if (input.KeyObject?.EcdsaKey != null || (input.KeyObject == null && ecdsa != null && LooksEc(input)))
        {
            try
            {
                var format = GetSignatureFormat(input.DsaEncoding, context);
                return ecdsa!.VerifyData(data, signature, hashName, format);
            }
            finally
            {
                if (OwnsKey(input)) ecdsa!.Dispose();
            }
        }
        if (OwnsKey(input)) ecdsa?.Dispose();

        var rsa = TryGetRsa(input) ?? throw new ArgumentException($"{context}: unable to parse key (unsupported key format)");
        try
        {
            var padding = GetRsaSignaturePadding(input, algorithm, context);
            return rsa.VerifyData(data, signature, hashName, padding);
        }
        finally
        {
            if (OwnsKey(input)) rsa.Dispose();
        }
    }

    /// <summary>
    /// An imported-from-PEM key "looks EC" when the PEM declares EC or when an RSA
    /// import would fail. TryGetEcdsa succeeding on a generic PKCS#8 PEM is not
    /// sufficient on all platforms, so double-check with an RSA probe.
    /// </summary>
    private static bool LooksEc(KeyInput input)
    {
        var pem = input.Pem!;
        if (pem.Contains("EC PRIVATE KEY") || pem.Contains("EC PUBLIC KEY"))
            return true;
        if (pem.Contains("RSA PRIVATE KEY") || pem.Contains("RSA PUBLIC KEY"))
            return false;
        // Generic PKCS#8/SPKI: probe RSA — if RSA imports, it's RSA.
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return false;
        }
        catch (CryptographicException)
        {
            return true;
        }
    }

    private static RSAEncryptionPadding GetRsaEncryptionPadding(KeyInput input, string context)
    {
        if (input.HasOaepLabel)
            throw new NotSupportedException($"{context}: oaepLabel is not supported on this runtime (.NET BCL OAEP has no label parameter)");

        var padding = input.Padding == -1 ? RSA_PKCS1_OAEP_PADDING : input.Padding;
        switch (padding)
        {
            case RSA_PKCS1_OAEP_PADDING:
                return (input.OaepHash ?? "sha1").ToLowerInvariant() switch
                {
                    "sha1" => RSAEncryptionPadding.OaepSHA1,
                    "sha256" => RSAEncryptionPadding.OaepSHA256,
                    "sha384" => RSAEncryptionPadding.OaepSHA384,
                    "sha512" => RSAEncryptionPadding.OaepSHA512,
                    _ => throw new ArgumentException($"{context}: unsupported oaepHash '{input.OaepHash}' (supported: sha1, sha256, sha384, sha512)")
                };
            case RSA_PKCS1_PADDING:
                return RSAEncryptionPadding.Pkcs1;
            case RSA_NO_PADDING:
                throw new NotSupportedException($"{context}: RSA_NO_PADDING is not supported on this runtime (.NET BCL requires padded RSA)");
            default:
                throw new ArgumentException($"{context}: unsupported RSA padding {padding}");
        }
    }

    /// <summary>
    /// Core for publicEncrypt/privateDecrypt, honoring <c>{ padding, oaepHash }</c>.
    /// </summary>
    internal static byte[] RsaEncryptDecrypt(object? keyArg, byte[] data, bool encrypt, string context)
    {
        var input = ParseKeyInput(keyArg, context);
        var rsa = TryGetRsa(input) ?? throw new ArgumentException($"{context}: key must be an RSA key");
        try
        {
            var padding = GetRsaEncryptionPadding(input, context);
            return encrypt ? rsa.Encrypt(data, padding) : rsa.Decrypt(data, padding);
        }
        finally
        {
            if (OwnsKey(input)) rsa.Dispose();
        }
    }

    /// <summary>
    /// Core for privateEncrypt/publicDecrypt (the PKCS#1 v1.5 signature primitives).
    /// Only RSA_PKCS1_PADDING is supported (as in Node for these two calls, minus
    /// RSA_NO_PADDING which the BCL can't express).
    /// </summary>
    internal static byte[] RsaSignaturePrimitive(object? keyArg, byte[] data, bool privateOp, string context)
    {
        var input = ParseKeyInput(keyArg, context);
        if (input.Padding is not (-1) and not RSA_PKCS1_PADDING)
            throw new NotSupportedException($"{context}: only RSA_PKCS1_PADDING is supported on this runtime");

        var rsa = TryGetRsa(input) ?? throw new ArgumentException($"{context}: key must be an RSA key");
        try
        {
            // privateEncrypt = data^d mod n (BCL: Decrypt w/ Pkcs1); publicDecrypt = data^e mod n (BCL: Encrypt w/ Pkcs1)
            return privateOp ? rsa.Decrypt(data, RSAEncryptionPadding.Pkcs1) : rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
        }
        finally
        {
            if (OwnsKey(input)) rsa.Dispose();
        }
    }
}
