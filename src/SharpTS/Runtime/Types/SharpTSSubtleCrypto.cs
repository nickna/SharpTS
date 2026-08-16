using System.Security.Cryptography;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// The WebCrypto global object (#1063): globalThis.crypto === crypto.webcrypto.
/// Holds subtle, getRandomValues, and randomUUID.
/// </summary>
public sealed class SharpTSWebCrypto
{
    /// <summary>Process-wide singleton (registered as the 'crypto' global namespace).</summary>
    public static readonly SharpTSWebCrypto Instance = new();

    /// <summary>The SubtleCrypto instance (crypto.subtle).</summary>
    public SharpTSSubtleCrypto Subtle { get; } = new();

    /// <summary>Property/method access.</summary>
    public object? GetMember(string name) => name switch
    {
        "subtle" => Subtle,
        "getRandomValues" => BuiltInMethod.CreateV2("getRandomValues", 1, (_, _, args) =>
        {
            if (args.Length == 0)
                throw new Exception("crypto.getRandomValues requires a typed array argument");
            return RuntimeValue.FromBoxed(GetRandomValues(args[0].ToObject()));
        }),
        "randomUUID" => BuiltInMethod.CreateV2("randomUUID", 0, (_, _, _) =>
            RuntimeValue.FromString(Guid.NewGuid().ToString())),
        _ => null
    };

    /// <summary>
    /// crypto.getRandomValues(typedArray) — fills the array in place and returns it.
    /// </summary>
    public static object GetRandomValues(object? array)
    {
        switch (array)
        {
            case SharpTSTypedArray typed:
                if (typed.ByteLength > 65536)
                    throw new Exception("QuotaExceededError: crypto.getRandomValues requested more than 65536 bytes");
                RandomNumberGenerator.Fill(typed.Buffer.AsSpan(typed.ByteOffset, typed.ByteLength));
                return typed;
            case SharpTSBuffer buffer:
                if (buffer.Data.Length > 65536)
                    throw new Exception("QuotaExceededError: crypto.getRandomValues requested more than 65536 bytes");
                RandomNumberGenerator.Fill(buffer.Data);
                return buffer;
            default:
                throw new Exception("crypto.getRandomValues: argument must be an integer typed array");
        }
    }
}

/// <summary>
/// WebCrypto SubtleCrypto (#1063). All methods return resolved promises, computed
/// synchronously over the BCL. Algorithms are bounded by what
/// System.Security.Cryptography exposes (AES-CTR, Ed25519/X25519, RSA-OAEP labels,
/// and PSS salt lengths other than the digest length are documented ceilings).
/// </summary>
/// <remarks>
/// NOTE: Must stay behaviorally in sync with the emitted $SubtleCrypto
/// (Compilation/RuntimeEmitter.WebCrypto.cs).
/// </remarks>
public sealed class SharpTSSubtleCrypto
{
    #region member dispatch

    /// <summary>Method access (subtle.digest, subtle.encrypt, ...).</summary>
    public object? GetMember(string name) => name switch
    {
        "digest" => Method("digest", 2, args => Digest(Obj(args, 0), Obj(args, 1))),
        "generateKey" => Method("generateKey", 3, args => GenerateKey(Obj(args, 0), Truthy(args, 1), Obj(args, 2))),
        "importKey" => Method("importKey", 5, args => ImportKey(Str(args, 0), Obj(args, 1), Obj(args, 2), Truthy(args, 3), Obj(args, 4))),
        "exportKey" => Method("exportKey", 2, args => ExportKey(Str(args, 0), Key(args, 1, "exportKey"))),
        "encrypt" => Method("encrypt", 3, args => EncryptDecrypt(Obj(args, 0), Key(args, 1, "encrypt"), Obj(args, 2), encrypt: true)),
        "decrypt" => Method("decrypt", 3, args => EncryptDecrypt(Obj(args, 0), Key(args, 1, "decrypt"), Obj(args, 2), encrypt: false)),
        "sign" => Method("sign", 3, args => Sign(Obj(args, 0), Key(args, 1, "sign"), Obj(args, 2))),
        "verify" => Method("verify", 4, args => Verify(Obj(args, 0), Key(args, 1, "verify"), Obj(args, 2), Obj(args, 3))),
        "deriveBits" => Method("deriveBits", 3, args => DeriveBits(Obj(args, 0), Key(args, 1, "deriveBits"), Num(args, 2))),
        "deriveKey" => Method("deriveKey", 5, args => DeriveKey(Obj(args, 0), Key(args, 1, "deriveKey"), Obj(args, 2), Truthy(args, 3), Obj(args, 4))),
        "wrapKey" => Method("wrapKey", 4, args => WrapKey(Str(args, 0), Key(args, 1, "wrapKey"), Key(args, 2, "wrapKey"), Obj(args, 3))),
        "unwrapKey" => Method("unwrapKey", 7, args => UnwrapKey(Str(args, 0), Obj(args, 1), Key(args, 2, "unwrapKey"), Obj(args, 3), Obj(args, 4), Truthy(args, 5), Obj(args, 6))),
        _ => null
    };

    private static BuiltInMethod Method(string name, int arity, Func<object?[], object?> body)
        => BuiltInMethod.CreateV2(name, arity == 0 ? 0 : 1, arity, (_, _, args) =>
        {
            var boxed = new object?[args.Length];
            for (int i = 0; i < args.Length; i++) boxed[i] = args[i].ToObject();
            return RuntimeValue.FromObject(SharpTSPromise.Resolve(body(boxed)));
        });

    private static object? Obj(object?[] args, int i) => i < args.Length ? args[i] : null;
    private static string Str(object?[] args, int i) => Obj(args, i) as string
        ?? throw new Exception($"crypto.subtle: argument {i + 1} must be a string");
    private static bool Truthy(object?[] args, int i) => Obj(args, i) is bool b && b;
    private static int Num(object?[] args, int i) => Obj(args, i) is double d ? (int)d
        : throw new Exception($"crypto.subtle: argument {i + 1} must be a number");
    private static SharpTSCryptoKey Key(object?[] args, int i, string op) => Obj(args, i) as SharpTSCryptoKey
        ?? throw new Exception($"crypto.subtle.{op}: argument {i + 1} must be a CryptoKey");

    #endregion

    #region normalization helpers

    /// <summary>Reads the algorithm name from a string or { name } object, uppercased.</summary>
    internal static string AlgoName(object? algorithm) => algorithm switch
    {
        string s => s.ToUpperInvariant(),
        SharpTSObject obj when obj.Fields.TryGetValue("name", out var n) && n is string ns => ns.ToUpperInvariant(),
        _ => throw new Exception("crypto.subtle: algorithm must be a string or { name } object")
    };

    private static object? AlgoParam(object? algorithm, string name)
        => algorithm is SharpTSObject obj && obj.Fields.TryGetValue(name, out var v) && v is not SharpTSUndefined ? v : null;

    /// <summary>Maps a WebCrypto digest identifier ('SHA-256' or { name }) to the internal lowercase name.</summary>
    private static string MapHash(object? hash, string context)
    {
        var name = hash switch
        {
            string s => s,
            SharpTSObject obj when obj.Fields.TryGetValue("name", out var n) && n is string ns => ns,
            _ => throw new Exception($"{context}: a hash algorithm is required")
        };
        return name.ToUpperInvariant() switch
        {
            "SHA-1" => "sha1",
            "SHA-256" => "sha256",
            "SHA-384" => "sha384",
            "SHA-512" => "sha512",
            _ => throw new Exception($"{context}: unsupported hash '{name}' (supported: SHA-1, SHA-256, SHA-384, SHA-512)")
        };
    }

    private static string WebCryptoHashName(string lower) => lower switch
    {
        "sha1" => "SHA-1",
        "sha256" => "SHA-256",
        "sha384" => "SHA-384",
        "sha512" => "SHA-512",
        _ => lower
    };

    private static HashAlgorithmName HashAlg(string lower) => CryptoAlgorithms.ParseHashName(lower);

    /// <summary>BufferSource → bytes (TypedArray honoring its view window, ArrayBuffer, Buffer).</summary>
    internal static byte[] ToBytes(object? data, string context)
    {
        return data switch
        {
            SharpTSTypedArray typed => typed.Buffer.AsSpan(typed.ByteOffset, typed.ByteLength).ToArray(),
            SharpTSArrayBuffer ab => ab.AsSpan().ToArray(),
            SharpTSBuffer buf => buf.Data,
            string s => System.Text.Encoding.UTF8.GetBytes(s),
            _ => throw new Exception($"{context}: expected an ArrayBuffer, TypedArray, or Buffer")
        };
    }

    private static SharpTSArrayBuffer ToArrayBuffer(byte[] bytes)
    {
        var ab = new SharpTSArrayBuffer(bytes.Length);
        bytes.CopyTo(ab.AsSpan());
        return ab;
    }

    private static SharpTSArray UsagesArray(object? usages)
    {
        if (usages is SharpTSArray arr) return arr;
        return new SharpTSArray(new List<object?>());
    }

    private static SharpTSObject AlgObj(params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (k, v) in fields) dict[k] = v;
        return new SharpTSObject(dict);
    }

    private static void ThrowCeiling(string algo) => throw new Exception(algo switch
    {
        "AES-CTR" => "crypto.subtle: AES-CTR is not supported on this runtime (.NET BCL has no CTR mode)",
        "ED25519" or "X25519" or "ED448" or "X448" =>
            $"crypto.subtle: {algo} is not supported on this runtime (.NET BCL has no EdDSA/X-curve support)",
        _ => $"crypto.subtle: unsupported algorithm '{algo}'"
    });

    #endregion

    #region digest

    private static object Digest(object? algorithm, object? data)
    {
        var hash = MapHash(algorithm is string || algorithm is SharpTSObject ? algorithm : null, "crypto.subtle.digest");
        var bytes = ToBytes(data, "crypto.subtle.digest");
        return ToArrayBuffer(CryptoAlgorithms.OneShotHash(hash, bytes));
    }

    #endregion

    #region generateKey

    private static object GenerateKey(object? algorithm, bool extractable, object? usages)
    {
        var name = AlgoName(algorithm);
        var usagesArr = UsagesArray(usages);
        switch (name)
        {
            case "AES-GCM" or "AES-CBC":
            {
                var length = AlgoParam(algorithm, "length") is double d ? (int)d
                    : throw new Exception("crypto.subtle.generateKey: AES requires a length (128, 192, or 256)");
                if (length is not (128 or 192 or 256))
                    throw new Exception("crypto.subtle.generateKey: AES key length must be 128, 192, or 256");
                var material = RandomNumberGenerator.GetBytes(length / 8);
                return new SharpTSCryptoKey("secret", extractable,
                    AlgObj(("name", name), ("length", (double)length)), usagesArr, material, name, null, null);
            }
            case "HMAC":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.generateKey (HMAC)");
                var lengthBits = AlgoParam(algorithm, "length") is double ld
                    ? (int)ld
                    : (hash is "sha384" or "sha512" ? 1024 : 512);
                if (lengthBits < 8 || lengthBits % 8 != 0)
                    throw new Exception("crypto.subtle.generateKey: HMAC length must be a positive multiple of 8");
                var material = RandomNumberGenerator.GetBytes(lengthBits / 8);
                return new SharpTSCryptoKey("secret", extractable,
                    AlgObj(("name", "HMAC"), ("hash", AlgObj(("name", WebCryptoHashName(hash)))), ("length", (double)lengthBits)),
                    usagesArr, material, "HMAC", hash, null);
            }
            case "RSA-OAEP" or "RSASSA-PKCS1-V1_5" or "RSA-PSS":
            {
                var modulusLength = AlgoParam(algorithm, "modulusLength") is double md ? (int)md
                    : throw new Exception("crypto.subtle.generateKey: RSA requires modulusLength");
                ValidatePublicExponent(AlgoParam(algorithm, "publicExponent"));
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.generateKey (RSA)");
                var canonicalName = name == "RSASSA-PKCS1-V1_5" ? "RSASSA-PKCS1-v1_5" : name;

                using var rsa = RSA.Create(modulusLength);
                var alg = AlgObj(("name", canonicalName), ("modulusLength", (double)modulusLength),
                    ("publicExponent", 65537d), ("hash", AlgObj(("name", WebCryptoHashName(hash)))));
                var pub = new SharpTSCryptoKey("public", true, alg, UsagesArray(usages),
                    rsa.ExportSubjectPublicKeyInfo(), name, hash, null);
                var priv = new SharpTSCryptoKey("private", extractable, alg, usagesArr,
                    rsa.ExportPkcs8PrivateKey(), name, hash, null);
                return new SharpTSObject(new Dictionary<string, object?> { ["publicKey"] = pub, ["privateKey"] = priv });
            }
            case "ECDSA" or "ECDH":
            {
                var curveName = AlgoParam(algorithm, "namedCurve") as string
                    ?? throw new Exception("crypto.subtle.generateKey: EC requires namedCurve");
                var (curve, _) = SharpTSECDH.ResolveCurve(curveName);
                using var ec = ECDsa.Create(curve);
                var alg = AlgObj(("name", name), ("namedCurve", CanonicalCurve(curveName)));
                var pub = new SharpTSCryptoKey("public", true, alg, UsagesArray(usages),
                    ec.ExportSubjectPublicKeyInfo(), name, null, CanonicalCurve(curveName));
                var priv = new SharpTSCryptoKey("private", extractable, alg, usagesArr,
                    ec.ExportPkcs8PrivateKey(), name, null, CanonicalCurve(curveName));
                return new SharpTSObject(new Dictionary<string, object?> { ["publicKey"] = pub, ["privateKey"] = priv });
            }
            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    private static string CanonicalCurve(string curveName) => curveName.ToLowerInvariant() switch
    {
        "p-256" or "prime256v1" or "secp256r1" => "P-256",
        "p-384" or "secp384r1" => "P-384",
        "p-521" or "secp521r1" => "P-521",
        var other => other
    };

    private static void ValidatePublicExponent(object? exponent)
    {
        switch (exponent)
        {
            case null:
                return; // default 65537
            case double d when (int)d == 65537:
                return;
            case SharpTSTypedArray typed:
            {
                var bytes = typed.Buffer.AsSpan(typed.ByteOffset, typed.ByteLength).ToArray();
                if (IsExponent65537(bytes)) return;
                break;
            }
            case SharpTSBuffer buf when IsExponent65537(buf.Data):
                return;
        }
        throw new Exception("crypto.subtle: only publicExponent 65537 is supported on this runtime");
    }

    private static bool IsExponent65537(byte[] bytes)
    {
        long value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        return value == 65537;
    }

    #endregion

    #region importKey / exportKey

    private static object ImportKey(string format, object? keyData, object? algorithm, bool extractable, object? usages)
    {
        var name = AlgoName(algorithm);
        var usagesArr = UsagesArray(usages);
        switch (name)
        {
            case "AES-GCM" or "AES-CBC" or "HMAC" or "PBKDF2" or "HKDF":
            {
                byte[] material;
                if (format == "raw")
                {
                    material = ToBytes(keyData, "crypto.subtle.importKey");
                }
                else if (format == "jwk" && keyData is SharpTSObject jwk)
                {
                    if (jwk.Fields.TryGetValue("k", out var k) && k is string ks)
                        material = CryptoEncoding.FromEncoded(ks, "base64url");
                    else
                        throw new Exception("crypto.subtle.importKey: JWK secret key requires 'k'");
                }
                else
                {
                    throw new Exception($"crypto.subtle.importKey: format '{format}' is not valid for {name}");
                }

                string? hash = null;
                SharpTSObject alg;
                if (name == "HMAC")
                {
                    hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.importKey (HMAC)");
                    alg = AlgObj(("name", "HMAC"), ("hash", AlgObj(("name", WebCryptoHashName(hash)))),
                        ("length", (double)(material.Length * 8)));
                }
                else if (name is "AES-GCM" or "AES-CBC")
                {
                    if (material.Length is not (16 or 24 or 32))
                        throw new Exception("crypto.subtle.importKey: AES key must be 128, 192, or 256 bits");
                    alg = AlgObj(("name", name), ("length", (double)(material.Length * 8)));
                }
                else
                {
                    alg = AlgObj(("name", name));
                }
                return new SharpTSCryptoKey("secret", extractable, alg, usagesArr, material, name, hash, null);
            }

            case "RSA-OAEP" or "RSASSA-PKCS1-V1_5" or "RSA-PSS":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.importKey (RSA)");
                var canonicalName = name == "RSASSA-PKCS1-V1_5" ? "RSASSA-PKCS1-v1_5" : name;
                var (material, isPrivate, modulusLength) = ImportRsaMaterial(format, keyData);
                var alg = AlgObj(("name", canonicalName), ("modulusLength", (double)modulusLength),
                    ("publicExponent", 65537d), ("hash", AlgObj(("name", WebCryptoHashName(hash)))));
                return new SharpTSCryptoKey(isPrivate ? "private" : "public", extractable, alg, usagesArr,
                    material, name, hash, null);
            }

            case "ECDSA" or "ECDH":
            {
                var curveName = AlgoParam(algorithm, "namedCurve") as string
                    ?? throw new Exception("crypto.subtle.importKey: EC requires namedCurve");
                var canonical = CanonicalCurve(curveName);
                var (material, isPrivate) = ImportEcMaterial(format, keyData, curveName);
                var alg = AlgObj(("name", name), ("namedCurve", canonical));
                return new SharpTSCryptoKey(isPrivate ? "private" : "public", extractable, alg, usagesArr,
                    material, name, null, canonical);
            }

            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    private static (byte[] Material, bool IsPrivate, int ModulusLength) ImportRsaMaterial(string format, object? keyData)
    {
        switch (format)
        {
            case "spki":
            {
                var der = ToBytes(keyData, "crypto.subtle.importKey");
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(der, out _);
                return (der, false, rsa.KeySize);
            }
            case "pkcs8":
            {
                var der = ToBytes(keyData, "crypto.subtle.importKey");
                using var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(der, out _);
                return (der, true, rsa.KeySize);
            }
            case "jwk" when keyData is SharpTSObject jwk:
            {
                var isPrivate = jwk.Fields.ContainsKey("d");
                var keyObj = SharpTSKeyObject.CreateFromJwk(jwk, isPrivate);
                var rsa = keyObj.RsaKey ?? throw new Exception("crypto.subtle.importKey: JWK is not an RSA key");
                return (isPrivate ? rsa.ExportPkcs8PrivateKey() : rsa.ExportSubjectPublicKeyInfo(), isPrivate, rsa.KeySize);
            }
            default:
                throw new Exception($"crypto.subtle.importKey: format '{format}' is not valid for RSA keys");
        }
    }

    private static (byte[] Material, bool IsPrivate) ImportEcMaterial(string format, object? keyData, string curveName)
    {
        var (curve, fieldLen) = SharpTSECDH.ResolveCurve(curveName);
        switch (format)
        {
            case "raw":
            {
                var point = SharpTSECDH.DecodePoint(ToBytes(keyData, "crypto.subtle.importKey"), fieldLen);
                using var ec = ECDsa.Create();
                ec.ImportParameters(new ECParameters { Curve = curve, Q = point });
                return (ec.ExportSubjectPublicKeyInfo(), false);
            }
            case "spki":
            {
                var der = ToBytes(keyData, "crypto.subtle.importKey");
                using var ec = ECDsa.Create();
                ec.ImportSubjectPublicKeyInfo(der, out _);
                return (der, false);
            }
            case "pkcs8":
            {
                var der = ToBytes(keyData, "crypto.subtle.importKey");
                using var ec = ECDsa.Create();
                ec.ImportPkcs8PrivateKey(der, out _);
                return (der, true);
            }
            case "jwk" when keyData is SharpTSObject jwk:
            {
                var isPrivate = jwk.Fields.ContainsKey("d");
                var keyObj = SharpTSKeyObject.CreateFromJwk(jwk, isPrivate);
                var ec = keyObj.EcdsaKey ?? throw new Exception("crypto.subtle.importKey: JWK is not an EC key");
                return (isPrivate ? ec.ExportPkcs8PrivateKey() : ec.ExportSubjectPublicKeyInfo(), isPrivate);
            }
            default:
                throw new Exception($"crypto.subtle.importKey: format '{format}' is not valid for EC keys");
        }
    }

    private static object ExportKey(string format, SharpTSCryptoKey key)
    {
        if (!key.Extractable)
            throw new Exception("crypto.subtle.exportKey: key is not extractable");

        switch (format)
        {
            case "raw":
                if (key.Type == "secret")
                    return ToArrayBuffer(key.Material);
                if (key is { Type: "public", NamedCurve: not null })
                {
                    using var ec = ECDsa.Create();
                    ec.ImportSubjectPublicKeyInfo(key.Material, out _);
                    var p = ec.ExportParameters(false);
                    var (_, fieldLen) = SharpTSECDH.ResolveCurve(key.NamedCurve);
                    return ToArrayBuffer(SharpTSECDH.EncodePoint(p.Q, fieldLen, "uncompressed"));
                }
                throw new Exception("crypto.subtle.exportKey: 'raw' is only valid for secret and EC public keys");

            case "spki":
                if (key.Type != "public")
                    throw new Exception("crypto.subtle.exportKey: 'spki' is only valid for public keys");
                return ToArrayBuffer(key.Material);

            case "pkcs8":
                if (key.Type != "private")
                    throw new Exception("crypto.subtle.exportKey: 'pkcs8' is only valid for private keys");
                return ToArrayBuffer(key.Material);

            case "jwk":
            {
                if (key.Type == "secret")
                {
                    return new SharpTSObject(new Dictionary<string, object?>
                    {
                        ["kty"] = "oct",
                        ["k"] = (string)CryptoEncoding.ToBufferOrString(key.Material, "base64url"),
                        ["ext"] = key.Extractable
                    });
                }
                var keyObj = SharpTSKeyObject.CreateFromDer(key.Material, key.Type == "private" ? "pkcs8" : "spki", key.Type == "private");
                var jwk = keyObj.ExportJwk();
                jwk.SetProperty("ext", key.Extractable);
                return jwk;
            }

            default:
                throw new Exception($"crypto.subtle.exportKey: unsupported format '{format}'");
        }
    }

    #endregion

    #region encrypt / decrypt

    private static object EncryptDecrypt(object? algorithm, SharpTSCryptoKey key, object? data, bool encrypt)
    {
        var name = AlgoName(algorithm);
        var op = encrypt ? "encrypt" : "decrypt";
        var input = ToBytes(data, $"crypto.subtle.{op}");
        switch (name)
        {
            case "AES-GCM":
            {
                var iv = ToBytes(AlgoParam(algorithm, "iv"), $"crypto.subtle.{op} (AES-GCM iv)");
                byte[]? aad = AlgoParam(algorithm, "additionalData") is { } ad ? ToBytes(ad, op) : null;
                var tagBits = AlgoParam(algorithm, "tagLength") is double t ? (int)t : 128;
                if (tagBits is < 96 or > 128 || tagBits % 8 != 0)
                    throw new Exception($"crypto.subtle.{op}: tagLength {tagBits} is not supported on this runtime (.NET AesGcm supports 96-128 in steps of 8)");
                var tagLen = tagBits / 8;
                using var gcm = new AesGcm(key.Material, tagLen);
                if (encrypt)
                {
                    // WebCrypto AES-GCM output is ciphertext || tag
                    var ciphertext = new byte[input.Length];
                    var tag = new byte[tagLen];
                    gcm.Encrypt(iv, input, ciphertext, tag, aad);
                    var result = new byte[input.Length + tagLen];
                    ciphertext.CopyTo(result, 0);
                    tag.CopyTo(result, input.Length);
                    return ToArrayBuffer(result);
                }
                else
                {
                    if (input.Length < tagLen)
                        throw new Exception("crypto.subtle.decrypt: ciphertext too short");
                    var ciphertext = input[..^tagLen];
                    var tag = input[^tagLen..];
                    var plaintext = new byte[ciphertext.Length];
                    gcm.Decrypt(iv, ciphertext, tag, plaintext, aad);
                    return ToArrayBuffer(plaintext);
                }
            }
            case "AES-CBC":
            {
                var iv = ToBytes(AlgoParam(algorithm, "iv"), $"crypto.subtle.{op} (AES-CBC iv)");
                using var aes = Aes.Create();
                aes.Key = key.Material;
                return ToArrayBuffer(encrypt
                    ? aes.EncryptCbc(input, iv, PaddingMode.PKCS7)
                    : aes.DecryptCbc(input, iv, PaddingMode.PKCS7));
            }
            case "RSA-OAEP":
            {
                if (AlgoParam(algorithm, "label") is not null)
                    throw new Exception($"crypto.subtle.{op}: RSA-OAEP labels are not supported on this runtime (.NET BCL OAEP has no label parameter)");
                var padding = (key.HashName ?? "sha1") switch
                {
                    "sha1" => RSAEncryptionPadding.OaepSHA1,
                    "sha256" => RSAEncryptionPadding.OaepSHA256,
                    "sha384" => RSAEncryptionPadding.OaepSHA384,
                    "sha512" => RSAEncryptionPadding.OaepSHA512,
                    var other => throw new Exception($"crypto.subtle.{op}: unsupported OAEP hash '{other}'")
                };
                using var rsa = RSA.Create();
                if (encrypt)
                {
                    if (key.Type != "public")
                        throw new Exception("crypto.subtle.encrypt: RSA-OAEP requires a public key");
                    rsa.ImportSubjectPublicKeyInfo(key.Material, out _);
                    return ToArrayBuffer(rsa.Encrypt(input, padding));
                }
                if (key.Type != "private")
                    throw new Exception("crypto.subtle.decrypt: RSA-OAEP requires a private key");
                rsa.ImportPkcs8PrivateKey(key.Material, out _);
                return ToArrayBuffer(rsa.Decrypt(input, padding));
            }
            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    #endregion

    #region sign / verify

    private static byte[] SignCore(object? algorithm, SharpTSCryptoKey key, byte[] data)
    {
        var name = AlgoName(algorithm);
        switch (name)
        {
            case "HMAC":
                return HmacCore(key, data);
            case "RSASSA-PKCS1-V1_5":
            {
                using var rsa = ImportRsaPrivate(key, "sign");
                return rsa.SignData(data, HashAlg(key.HashName!), RSASignaturePadding.Pkcs1);
            }
            case "RSA-PSS":
            {
                ValidatePssSaltLength(algorithm, key, "sign");
                using var rsa = ImportRsaPrivate(key, "sign");
                return rsa.SignData(data, HashAlg(key.HashName!), RSASignaturePadding.Pss);
            }
            case "ECDSA":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.sign (ECDSA)");
                using var ec = ECDsa.Create();
                ec.ImportPkcs8PrivateKey(key.Material, out _);
                // WebCrypto ECDSA signatures are raw r||s (IEEE P1363)
                return ec.SignData(data, HashAlg(hash), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    private static object Sign(object? algorithm, SharpTSCryptoKey key, object? data)
        => ToArrayBuffer(SignCore(algorithm, key, ToBytes(data, "crypto.subtle.sign")));

    private static object Verify(object? algorithm, SharpTSCryptoKey key, object? signature, object? data)
    {
        var name = AlgoName(algorithm);
        var sig = ToBytes(signature, "crypto.subtle.verify");
        var input = ToBytes(data, "crypto.subtle.verify");
        switch (name)
        {
            case "HMAC":
                return CryptographicOperations.FixedTimeEquals(HmacCore(key, input), sig);
            case "RSASSA-PKCS1-V1_5":
            {
                using var rsa = ImportRsaPublicOrPrivate(key);
                return rsa.VerifyData(input, sig, HashAlg(key.HashName!), RSASignaturePadding.Pkcs1);
            }
            case "RSA-PSS":
            {
                ValidatePssSaltLength(algorithm, key, "verify");
                using var rsa = ImportRsaPublicOrPrivate(key);
                return rsa.VerifyData(input, sig, HashAlg(key.HashName!), RSASignaturePadding.Pss);
            }
            case "ECDSA":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.verify (ECDSA)");
                using var ec = ECDsa.Create();
                if (key.Type == "public")
                    ec.ImportSubjectPublicKeyInfo(key.Material, out _);
                else
                    ec.ImportPkcs8PrivateKey(key.Material, out _);
                return ec.VerifyData(input, sig, HashAlg(hash), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    private static byte[] HmacCore(SharpTSCryptoKey key, byte[] data)
    {
        return (key.HashName ?? "sha256") switch
        {
            "sha1" => HMACSHA1.HashData(key.Material, data),
            "sha256" => HMACSHA256.HashData(key.Material, data),
            "sha384" => HMACSHA384.HashData(key.Material, data),
            "sha512" => HMACSHA512.HashData(key.Material, data),
            var other => throw new Exception($"crypto.subtle: unsupported HMAC hash '{other}'")
        };
    }

    private static RSA ImportRsaPrivate(SharpTSCryptoKey key, string op)
    {
        if (key.Type != "private")
            throw new Exception($"crypto.subtle.{op}: an RSA private key is required");
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(key.Material, out _);
        return rsa;
    }

    private static RSA ImportRsaPublicOrPrivate(SharpTSCryptoKey key)
    {
        var rsa = RSA.Create();
        if (key.Type == "public")
            rsa.ImportSubjectPublicKeyInfo(key.Material, out _);
        else
            rsa.ImportPkcs8PrivateKey(key.Material, out _);
        return rsa;
    }

    private static void ValidatePssSaltLength(object? algorithm, SharpTSCryptoKey key, string op)
    {
        if (AlgoParam(algorithm, "saltLength") is double salt)
        {
            var digestLen = key.HashName switch
            {
                "sha1" => 20, "sha256" => 32, "sha384" => 48, "sha512" => 64, _ => -1
            };
            if ((int)salt != digestLen)
                throw new Exception($"crypto.subtle.{op}: RSA-PSS saltLength {(int)salt} is not supported on this runtime (.NET always uses the digest length, {digestLen})");
        }
    }

    #endregion

    #region deriveBits / deriveKey

    private static byte[] DeriveBitsCore(object? algorithm, SharpTSCryptoKey baseKey, int lengthBits)
    {
        var name = AlgoName(algorithm);
        if (lengthBits <= 0 || lengthBits % 8 != 0)
            throw new Exception("crypto.subtle.deriveBits: length must be a positive multiple of 8 on this runtime");
        var lengthBytes = lengthBits / 8;

        switch (name)
        {
            case "PBKDF2":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.deriveBits (PBKDF2)");
                var salt = ToBytes(AlgoParam(algorithm, "salt"), "crypto.subtle.deriveBits (PBKDF2 salt)");
                var iterations = AlgoParam(algorithm, "iterations") is double it ? (int)it
                    : throw new Exception("crypto.subtle.deriveBits: PBKDF2 requires iterations");
                return Rfc2898DeriveBytes.Pbkdf2(baseKey.Material, salt, iterations, HashAlg(hash), lengthBytes);
            }
            case "HKDF":
            {
                var hash = MapHash(AlgoParam(algorithm, "hash"), "crypto.subtle.deriveBits (HKDF)");
                var salt = AlgoParam(algorithm, "salt") is { } s ? ToBytes(s, "HKDF salt") : [];
                var info = AlgoParam(algorithm, "info") is { } i ? ToBytes(i, "HKDF info") : [];
                return HKDF.DeriveKey(HashAlg(hash), baseKey.Material, lengthBytes, salt, info);
            }
            case "ECDH":
            {
                if (AlgoParam(algorithm, "public") is not SharpTSCryptoKey publicKey)
                    throw new Exception("crypto.subtle.deriveBits: ECDH requires { public: CryptoKey }");
                if (baseKey.Type != "private")
                    throw new Exception("crypto.subtle.deriveBits: ECDH baseKey must be a private key");

                using var priv = ECDiffieHellman.Create();
                priv.ImportPkcs8PrivateKey(baseKey.Material, out _);
                using var pub = ECDiffieHellman.Create();
                pub.ImportSubjectPublicKeyInfo(publicKey.Material, out _);
                var secret = priv.DeriveRawSecretAgreement(pub.PublicKey);
                if (lengthBytes > secret.Length)
                    throw new Exception($"crypto.subtle.deriveBits: requested {lengthBits} bits but the ECDH secret is {secret.Length * 8} bits");
                return secret[..lengthBytes];
            }
            default:
                ThrowCeiling(name);
                return null!;
        }
    }

    private static object DeriveBits(object? algorithm, SharpTSCryptoKey baseKey, int lengthBits)
        => ToArrayBuffer(DeriveBitsCore(algorithm, baseKey, lengthBits));

    private static object DeriveKey(object? algorithm, SharpTSCryptoKey baseKey, object? derivedKeyType, bool extractable, object? usages)
    {
        var targetName = AlgoName(derivedKeyType);
        int lengthBits = targetName switch
        {
            "AES-GCM" or "AES-CBC" => AlgoParam(derivedKeyType, "length") is double d ? (int)d
                : throw new Exception("crypto.subtle.deriveKey: AES target requires length"),
            "HMAC" => AlgoParam(derivedKeyType, "length") is double hd ? (int)hd
                : MapHash(AlgoParam(derivedKeyType, "hash"), "deriveKey (HMAC)") is "sha384" or "sha512" ? 1024 : 512,
            _ => throw new Exception($"crypto.subtle.deriveKey: unsupported derived key type '{targetName}'")
        };
        var bits = DeriveBitsCore(algorithm, baseKey, lengthBits);
        return ImportKey("raw", ToArrayBuffer(bits), derivedKeyType, extractable, usages);
    }

    #endregion

    #region wrapKey / unwrapKey

    private static object WrapKey(string format, SharpTSCryptoKey key, SharpTSCryptoKey wrappingKey, object? wrapAlgo)
    {
        if (format == "jwk")
            throw new Exception("crypto.subtle.wrapKey: the 'jwk' format is not supported on this runtime (use raw/spki/pkcs8)");
        var exported = ExportKey(format, key);
        return EncryptDecrypt(wrapAlgo, wrappingKey, exported, encrypt: true);
    }

    private static object UnwrapKey(string format, object? wrappedKey, SharpTSCryptoKey unwrappingKey,
        object? unwrapAlgo, object? unwrappedKeyAlgo, bool extractable, object? usages)
    {
        if (format == "jwk")
            throw new Exception("crypto.subtle.unwrapKey: the 'jwk' format is not supported on this runtime (use raw/spki/pkcs8)");
        var decrypted = EncryptDecrypt(unwrapAlgo, unwrappingKey, wrappedKey, encrypt: false);
        return ImportKey(format, decrypted, unwrappedKeyAlgo, extractable, usages);
    }

    #endregion
}
