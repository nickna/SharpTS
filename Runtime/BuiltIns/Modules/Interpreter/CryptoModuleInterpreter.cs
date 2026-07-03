using System.Security.Cryptography;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'crypto' module.
/// </summary>
/// <remarks>
/// Provides cryptographic functionality including:
/// - createHash() - create hash objects for MD5, SHA1, SHA256, SHA512
/// - createHmac() - create HMAC objects for keyed-hash message authentication
/// - randomBytes() - generate cryptographically secure random bytes
/// - randomUUID() - generate a random UUID
/// - randomInt() - generate a random integer in a range
/// </remarks>
public static class CryptoModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the crypto module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createHash"] = BuiltInMethod.CreateV2("createHash", 1, 2, CreateHash),
            ["createHmac"] = BuiltInMethod.CreateV2("createHmac", 2, CreateHmac),
            ["createCipheriv"] = BuiltInMethod.CreateV2("createCipheriv", 3, 4, CreateCipheriv),
            ["createDecipheriv"] = BuiltInMethod.CreateV2("createDecipheriv", 3, 4, CreateDecipheriv),
            ["randomBytes"] = BuiltInMethod.CreateV2("randomBytes", 1, RandomBytes),
            ["randomFillSync"] = BuiltInMethod.CreateV2("randomFillSync", 1, 3, RandomFillSync),
            ["randomUUID"] = BuiltInMethod.CreateV2("randomUUID", 0, RandomUUID),
            ["randomInt"] = BuiltInMethod.CreateV2("randomInt", 1, 2, RandomInt),
            ["pbkdf2Sync"] = BuiltInMethod.CreateV2("pbkdf2Sync", 5, Pbkdf2Sync),
            ["scryptSync"] = BuiltInMethod.CreateV2("scryptSync", 3, 4, ScryptSync),
            ["timingSafeEqual"] = BuiltInMethod.CreateV2("timingSafeEqual", 2, TimingSafeEqual),
            ["createSign"] = BuiltInMethod.CreateV2("createSign", 1, CreateSign),
            ["createVerify"] = BuiltInMethod.CreateV2("createVerify", 1, CreateVerify),
            ["getHashes"] = BuiltInMethod.CreateV2("getHashes", 0, GetHashes),
            ["getCiphers"] = BuiltInMethod.CreateV2("getCiphers", 0, GetCiphers),
            ["generateKeyPairSync"] = BuiltInMethod.CreateV2("generateKeyPairSync", 1, 2, GenerateKeyPairSync),
            ["createDiffieHellman"] = BuiltInMethod.CreateV2("createDiffieHellman", 1, 2, CreateDiffieHellman),
            ["getDiffieHellman"] = BuiltInMethod.CreateV2("getDiffieHellman", 1, GetDiffieHellman),
            ["createECDH"] = BuiltInMethod.CreateV2("createECDH", 1, CreateECDH),
            // ECDH "class" surface: only the static convertKey is exposed (#1060)
            ["ECDH"] = new SharpTSObject(new Dictionary<string, object?>
            {
                ["convertKey"] = BuiltInMethod.CreateV2("convertKey", 2, 5, EcdhConvertKey)
            }),
            // RSA encryption/decryption
            ["publicEncrypt"] = BuiltInMethod.CreateV2("publicEncrypt", 2, PublicEncrypt),
            ["privateDecrypt"] = BuiltInMethod.CreateV2("privateDecrypt", 2, PrivateDecrypt),
            ["privateEncrypt"] = BuiltInMethod.CreateV2("privateEncrypt", 2, PrivateEncrypt),
            ["publicDecrypt"] = BuiltInMethod.CreateV2("publicDecrypt", 2, PublicDecrypt),
            // HKDF key derivation
            ["hkdfSync"] = BuiltInMethod.CreateV2("hkdfSync", 5, HkdfSync),
            // KeyObject API
            ["createSecretKey"] = BuiltInMethod.CreateV2("createSecretKey", 1, 2, CreateSecretKey),
            ["createPublicKey"] = BuiltInMethod.CreateV2("createPublicKey", 1, CreatePublicKey),
            ["createPrivateKey"] = BuiltInMethod.CreateV2("createPrivateKey", 1, CreatePrivateKey),
            // Async (callback-based) key derivation
            ["pbkdf2"] = BuiltInMethod.CreateV2("pbkdf2", 6, Pbkdf2Async),
            ["scrypt"] = BuiltInMethod.CreateV2("scrypt", 4, 5, ScryptAsync),
            ["generateKeyPair"] = BuiltInMethod.CreateV2("generateKeyPair", 2, 3, GenerateKeyPairAsync),
            ["hkdf"] = BuiltInMethod.CreateV2("hkdf", 6, HkdfAsync),
            // One-shot digest/sign/verify (#1055)
            ["hash"] = BuiltInMethod.CreateV2("hash", 2, 3, HashOneShot),
            ["sign"] = BuiltInMethod.CreateV2("sign", 3, 4, SignOneShot),
            ["verify"] = BuiltInMethod.CreateV2("verify", 4, 5, VerifyOneShot),
            // crypto.constants (#1056)
            ["constants"] = BuildConstants(),
            // Cipher/curve discovery (#1057, #1058)
            ["getCipherInfo"] = BuiltInMethod.CreateV2("getCipherInfo", 1, 2, GetCipherInfo),
            ["getCurves"] = BuiltInMethod.CreateV2("getCurves", 0, GetCurves),
            // Small wins (#1058)
            ["randomFill"] = BuiltInMethod.CreateV2("randomFill", 2, 4, RandomFillAsync),
            ["generateKey"] = BuiltInMethod.CreateV2("generateKey", 3, GenerateKeyAsync),
            ["generateKeySync"] = BuiltInMethod.CreateV2("generateKeySync", 2, GenerateKeySync),
            // DH/ECDH completeness + FIPS shims (#1060)
            ["diffieHellman"] = BuiltInMethod.CreateV2("diffieHellman", 1, DiffieHellmanOneShot),
            ["createDiffieHellmanGroup"] = BuiltInMethod.CreateV2("createDiffieHellmanGroup", 1, GetDiffieHellman),
            ["getFips"] = BuiltInMethod.CreateV2("getFips", 0, GetFips),
            ["setFips"] = BuiltInMethod.CreateV2("setFips", 1, SetFips),
            ["fips"] = false,
            // Primes (#1062)
            ["generatePrime"] = BuiltInMethod.CreateV2("generatePrime", 2, 3, GeneratePrimeAsync),
            ["generatePrimeSync"] = BuiltInMethod.CreateV2("generatePrimeSync", 1, 2, GeneratePrimeSync),
            ["checkPrime"] = BuiltInMethod.CreateV2("checkPrime", 2, 3, CheckPrimeAsync),
            ["checkPrimeSync"] = BuiltInMethod.CreateV2("checkPrimeSync", 1, 2, CheckPrimeSync),
            // WebCrypto (#1063): module surface — same objects as globalThis.crypto
            ["webcrypto"] = SharpTSWebCrypto.Instance,
            ["subtle"] = SharpTSWebCrypto.Instance.Subtle,
            ["getRandomValues"] = BuiltInMethod.CreateV2("getRandomValues", 1, (_, _, args) =>
            {
                if (args.Length == 0)
                    throw new Exception("crypto.getRandomValues requires a typed array argument");
                return RuntimeValue.FromBoxed(SharpTSWebCrypto.GetRandomValues(args[0].ToObject()));
            })
        };
    }

    private static RuntimeValue CreateSign(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.createSign requires an algorithm name");
        var algorithm = args[0].AsStringUnsafe();

        return RuntimeValue.FromObject(new SharpTSSign(algorithm));
    }

    private static RuntimeValue CreateVerify(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.createVerify requires an algorithm name");
        var algorithm = args[0].AsStringUnsafe();

        return RuntimeValue.FromObject(new SharpTSVerify(algorithm));
    }

    private static RuntimeValue CreateHash(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.createHash requires an algorithm name");
        var algorithm = args[0].AsStringUnsafe();

        // Optional options: { outputLength } for the XOF hashes (shake128/shake256)
        int outputLength = -1;
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject options &&
            options.Fields.TryGetValue("outputLength", out var ol) && ol is double d)
            outputLength = (int)d;

        return RuntimeValue.FromObject(new SharpTSHash(algorithm, outputLength));
    }

    private static RuntimeValue CreateHmac(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2 || !args[0].IsString)
            throw new Exception("crypto.createHmac requires an algorithm name and a key");
        var algorithm = args[0].AsStringUnsafe();

        var key = args[1].ToObject() ?? throw new Exception("crypto.createHmac requires a key");
        return RuntimeValue.FromObject(new SharpTSHmac(algorithm, key));
    }

    private static RuntimeValue CreateCipheriv(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 3 || !args[0].IsString)
            throw new Exception("crypto.createCipheriv requires algorithm, key, and iv arguments");
        var algorithm = args[0].AsStringUnsafe();

        var key = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.createCipheriv requires a key");
        var iv = ConvertToBytes(args[2].ToObject()) ?? throw new Exception("crypto.createCipheriv requires an iv");

        return RuntimeValue.FromObject(new SharpTSCipher(algorithm, key, iv, ParseAuthTagLength(args)));
    }

    /// <summary>Reads the { authTagLength } cipher option (4th argument), if present (#1057).</summary>
    private static int ParseAuthTagLength(ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 3 && args[3].ToObject() is SharpTSObject options &&
            options.Fields.TryGetValue("authTagLength", out var atl) && atl is double d)
            return (int)d;
        return -1;
    }

    private static RuntimeValue CreateDecipheriv(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 3 || !args[0].IsString)
            throw new Exception("crypto.createDecipheriv requires algorithm, key, and iv arguments");
        var algorithm = args[0].AsStringUnsafe();

        var key = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.createDecipheriv requires a key");
        var iv = ConvertToBytes(args[2].ToObject()) ?? throw new Exception("crypto.createDecipheriv requires an iv");

        return RuntimeValue.FromObject(new SharpTSDecipher(algorithm, key, iv, ParseAuthTagLength(args)));
    }

    /// <summary>
    /// Converts a value to a byte array for crypto operations.
    /// </summary>
    private static byte[]? ConvertToBytes(object? value)
    {
        return value switch
        {
            null => null,
            string s => System.Text.Encoding.UTF8.GetBytes(s),
            SharpTSBuffer buf => buf.Data,
            byte[] bytes => bytes,
            _ => throw new Exception("Value must be a string or Buffer")
        };
    }

    private static RuntimeValue RandomBytes(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception("crypto.randomBytes requires a size argument");
        var size = args[0].AsNumberUnsafe();

        var byteCount = (int)size;
        var bytes = RandomNumberGenerator.GetBytes(byteCount);

        // Return as Buffer (matching Node.js behavior)
        return RuntimeValue.FromObject(new SharpTSBuffer(bytes));
    }

    private static RuntimeValue RandomFillSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not SharpTSBuffer buffer)
            throw new Exception("crypto.randomFillSync requires a Buffer argument");

        var data = buffer.Data;

        // Optional offset and size parameters
        int offset = 0;
        int size = data.Length;

        if (args.Length > 1 && args[1].IsNumber)
        {
            offset = (int)args[1].AsNumberUnsafe();
            if (offset < 0 || offset > data.Length)
                throw new Exception($"crypto.randomFillSync: offset out of range (0-{data.Length})");
        }

        if (args.Length > 2 && args[2].IsNumber)
        {
            size = (int)args[2].AsNumberUnsafe();
        }
        else if (args.Length > 1)
        {
            // If only offset is provided, size is rest of buffer
            size = data.Length - offset;
        }

        if (size < 0 || offset + size > data.Length)
            throw new Exception($"crypto.randomFillSync: size out of range");

        // Fill the specified range with random bytes
        var randomBytes = RandomNumberGenerator.GetBytes(size);
        Array.Copy(randomBytes, 0, data, offset, size);

        // Return the buffer (same reference)
        return RuntimeValue.FromObject(buffer);
    }

    private static RuntimeValue RandomUUID(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromString(Guid.NewGuid().ToString());
    }

    private static RuntimeValue RandomInt(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.randomInt requires at least one argument");

        int min, max;

        if (args.Length == 1)
        {
            // randomInt(max) - range is [0, max)
            min = 0;
            max = args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : throw new Exception("crypto.randomInt argument must be a number");
        }
        else
        {
            // randomInt(min, max) - range is [min, max)
            min = args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : throw new Exception("crypto.randomInt min must be a number");
            max = args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : throw new Exception("crypto.randomInt max must be a number");
        }

        return RuntimeValue.FromNumber(RandomNumberGenerator.GetInt32(min, max));
    }

    private static RuntimeValue Pbkdf2Sync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // pbkdf2Sync(password, salt, iterations, keylen, digest)
        if (args.Length < 5)
            throw new Exception("crypto.pbkdf2Sync requires password, salt, iterations, keylen, and digest arguments");

        var password = ConvertToBytes(args[0].ToObject()) ?? throw new Exception("crypto.pbkdf2Sync requires a password");
        var salt = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.pbkdf2Sync requires a salt");
        var iterations = args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : throw new Exception("crypto.pbkdf2Sync iterations must be a number");
        var keylen = args[3].IsNumber ? (int)args[3].AsNumberUnsafe() : throw new Exception("crypto.pbkdf2Sync keylen must be a number");
        var digest = args[4].ToObject() as string ?? throw new Exception("crypto.pbkdf2Sync digest must be a string");

        if (iterations < 1)
            throw new Exception("crypto.pbkdf2Sync iterations must be at least 1");
        if (keylen < 0)
            throw new Exception("crypto.pbkdf2Sync keylen must be non-negative");

        var hashAlgorithm = digest.ToLowerInvariant() switch
        {
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            // Note: MD5 is not supported for PBKDF2 in .NET - use SHA family instead
            _ => throw new Exception($"crypto.pbkdf2Sync: unsupported digest algorithm '{digest}'. Supported: sha1, sha256, sha384, sha512")
        };

        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, hashAlgorithm, keylen);
        return RuntimeValue.FromObject(new SharpTSBuffer(derivedKey));
    }

    private static RuntimeValue ScryptSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // scryptSync(password, salt, keylen[, options])
        if (args.Length < 3)
            throw new Exception("crypto.scryptSync requires password, salt, and keylen arguments");

        var password = ConvertToBytes(args[0].ToObject()) ?? throw new Exception("crypto.scryptSync requires a password");
        var salt = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.scryptSync requires a salt");
        var keylen = args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : throw new Exception("crypto.scryptSync keylen must be a number");

        if (keylen < 0)
            throw new Exception("crypto.scryptSync keylen must be non-negative");

        // Default scrypt parameters (Node.js defaults)
        int N = 16384;  // cost parameter (must be power of 2)
        int r = 8;      // block size
        int p = 1;      // parallelization

        // Parse options if provided
        if (args.Length > 3 && args[3].ToObject() is SharpTSObject options)
        {
            var fields = options.Fields;
            if (fields.TryGetValue("N", out var costObj) && costObj is double costVal)
                N = (int)costVal;
            if (fields.TryGetValue("cost", out var cost2Obj) && cost2Obj is double cost2Val)
                N = (int)cost2Val;
            if (fields.TryGetValue("r", out var rObj) && rObj is double rVal)
                r = (int)rVal;
            if (fields.TryGetValue("blockSize", out var bsObj) && bsObj is double bsVal)
                r = (int)bsVal;
            if (fields.TryGetValue("p", out var pObj) && pObj is double pVal)
                p = (int)pVal;
            if (fields.TryGetValue("parallelization", out var parObj) && parObj is double parVal)
                p = (int)parVal;
        }

        // Validate N is a power of 2
        if (N < 2 || (N & (N - 1)) != 0)
            throw new Exception("crypto.scryptSync: N must be a power of 2 greater than 1");

        // Use shared scrypt implementation
        var derivedKey = SharpTS.Compilation.ScryptImpl.DeriveBytes(password, salt, N, r, p, keylen);
        return RuntimeValue.FromObject(new SharpTSBuffer(derivedKey));
    }

    private static RuntimeValue TimingSafeEqual(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // timingSafeEqual(a, b)
        if (args.Length < 2)
            throw new Exception("crypto.timingSafeEqual requires two arguments");

        var a = ConvertToBytes(args[0].ToObject()) ?? throw new Exception("crypto.timingSafeEqual: first argument must be a Buffer or string");
        var b = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.timingSafeEqual: second argument must be a Buffer or string");

        // Node.js throws if lengths don't match
        if (a.Length != b.Length)
            throw new Exception($"crypto.timingSafeEqual: Input buffers must have the same byte length. Received {a.Length} and {b.Length}");

        // Use .NET's constant-time comparison
        return RuntimeValue.FromBoolean(CryptographicOperations.FixedTimeEquals(a, b));
    }

    /// <summary>
    /// Returns an array of supported hash algorithm names.
    /// </summary>
    private static RuntimeValue GetHashes(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(new SharpTSArray(CryptoAlgorithms.SupportedHashNames()));
    }

    /// <summary>
    /// Returns an array of supported cipher algorithm names.
    /// </summary>
    private static RuntimeValue GetCiphers(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(new SharpTSArray(new List<object?>
        {
            "aes-128-cbc", "aes-192-cbc", "aes-256-cbc",
            "aes-128-gcm", "aes-192-gcm", "aes-256-gcm"
        }));
    }

    /// <summary>
    /// Generates a key pair synchronously for RSA or EC algorithms.
    /// </summary>
    private static RuntimeValue GenerateKeyPairSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.generateKeyPairSync requires a key type argument");
        var keyType = args[0].AsStringUnsafe();

        var options = args.Length > 1 ? args[1].ToObject() as SharpTSObject : null;

        return keyType.ToLowerInvariant() switch
        {
            "rsa" => RuntimeValue.FromObject(GenerateRsaKeyPair(options)),
            "ec" => RuntimeValue.FromObject(GenerateEcKeyPair(options)),
            "ed25519" or "ed448" or "x25519" or "x448" => throw new Exception(
                $"crypto.generateKeyPairSync: '{keyType}' keys are not supported on this runtime (.NET BCL has no EdDSA/X-curve support)"),
            _ => throw new Exception($"crypto.generateKeyPairSync: unsupported key type '{keyType}'")
        };
    }

    private static SharpTSObject GenerateRsaKeyPair(SharpTSObject? options)
    {
        int modulusLength = 2048;
        if (options?.Fields.TryGetValue("modulusLength", out var ml) == true && ml is double d)
            modulusLength = (int)d;

        using var rsa = RSA.Create(modulusLength);
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["publicKey"] = rsa.ExportSubjectPublicKeyInfoPem(),
            ["privateKey"] = rsa.ExportPkcs8PrivateKeyPem()
        });
    }

    private static SharpTSObject GenerateEcKeyPair(SharpTSObject? options)
    {
        var curveName = "prime256v1";
        if (options?.Fields.TryGetValue("namedCurve", out var nc) == true && nc is string s)
            curveName = s;

        var curve = curveName.ToLowerInvariant() switch
        {
            "prime256v1" or "secp256r1" or "p-256" => ECCurve.NamedCurves.nistP256,
            "secp384r1" or "p-384" => ECCurve.NamedCurves.nistP384,
            "secp521r1" or "p-521" => ECCurve.NamedCurves.nistP521,
            _ => throw new Exception($"crypto.generateKeyPairSync: unsupported curve '{curveName}'")
        };

        using var ecdsa = ECDsa.Create(curve);
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["publicKey"] = ecdsa.ExportSubjectPublicKeyInfoPem(),
            ["privateKey"] = ecdsa.ExportPkcs8PrivateKeyPem()
        });
    }

    /// <summary>
    /// Creates a Diffie-Hellman key exchange object.
    /// </summary>
    private static RuntimeValue CreateDiffieHellman(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.createDiffieHellman requires at least one argument");

        // Check if first arg is a number (prime length) or Buffer/string (prime)
        if (args[0].IsNumber)
        {
            return RuntimeValue.FromObject(new SharpTSDiffieHellman((int)args[0].AsNumberUnsafe()));
        }

        var prime = ConvertToBytes(args[0].ToObject()) ?? throw new Exception("crypto.createDiffieHellman: prime must be a number, Buffer, or string");
        byte[]? generator = null;
        if (args.Length > 1 && !args[1].IsNull)
        {
            generator = ConvertToBytes(args[1].ToObject());
        }

        return RuntimeValue.FromObject(new SharpTSDiffieHellman(prime, generator));
    }

    /// <summary>
    /// Gets a predefined Diffie-Hellman group by name.
    /// </summary>
    private static RuntimeValue GetDiffieHellman(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.getDiffieHellman requires a group name");
        var groupName = args[0].AsStringUnsafe();

        return RuntimeValue.FromObject(new SharpTSDiffieHellman(groupName, isGroup: true));
    }

    /// <summary>
    /// Creates an Elliptic Curve Diffie-Hellman key exchange object.
    /// </summary>
    private static RuntimeValue CreateECDH(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("crypto.createECDH requires a curve name");
        var curveName = args[0].AsStringUnsafe();

        return RuntimeValue.FromObject(new SharpTSECDH(curveName));
    }

    /// <summary>
    /// ECDH.convertKey(key, curve[, inputEncoding[, outputEncoding[, format]]]) (#1060).
    /// </summary>
    private static RuntimeValue EcdhConvertKey(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2 || !args[1].IsString)
            throw new Exception("ECDH.convertKey requires key and curve arguments");

        var key = args[0].ToObject() ?? throw new Exception("ECDH.convertKey: key must not be null");
        var curve = args[1].AsStringUnsafe();
        var inputEncoding = args.Length > 2 ? args[2].ToObject() as string : null;
        var outputEncoding = args.Length > 3 ? args[3].ToObject() as string : null;
        var format = args.Length > 4 ? args[4].ToObject() as string : null;

        return RuntimeValue.FromBoxed(SharpTSECDH.ConvertKey(key, curve, inputEncoding, outputEncoding, format));
    }

    #region RSA Encryption/Decryption

    /// <summary>
    /// Encrypts data using a public key. Defaults to RSA-OAEP-SHA1 (matching Node);
    /// honors { padding, oaepHash } options (#1056/#1057).
    /// </summary>
    private static RuntimeValue PublicEncrypt(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("crypto.publicEncrypt requires key and buffer arguments");

        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.publicEncrypt: buffer must be a Buffer or string");
        var encrypted = CryptoKeyUtil.RsaEncryptDecrypt(args[0].ToObject(), data, encrypt: true, "crypto.publicEncrypt");
        return RuntimeValue.FromObject(new SharpTSBuffer(encrypted));
    }

    /// <summary>
    /// Decrypts data using a private key. Defaults to RSA-OAEP-SHA1 (matching Node);
    /// honors { padding, oaepHash } options (#1056/#1057).
    /// </summary>
    private static RuntimeValue PrivateDecrypt(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("crypto.privateDecrypt requires key and buffer arguments");

        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.privateDecrypt: buffer must be a Buffer or string");
        var decrypted = CryptoKeyUtil.RsaEncryptDecrypt(args[0].ToObject(), data, encrypt: false, "crypto.privateDecrypt");
        return RuntimeValue.FromObject(new SharpTSBuffer(decrypted));
    }

    /// <summary>
    /// Encrypts data using a private key with PKCS#1 v1.5 padding (signing primitive).
    /// This is the inverse of publicDecrypt and is used for digital signatures.
    /// </summary>
    private static RuntimeValue PrivateEncrypt(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("crypto.privateEncrypt requires key and buffer arguments");

        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.privateEncrypt: buffer must be a Buffer or string");
        var encrypted = CryptoKeyUtil.RsaSignaturePrimitive(args[0].ToObject(), data, privateOp: true, "crypto.privateEncrypt");
        return RuntimeValue.FromObject(new SharpTSBuffer(encrypted));
    }

    /// <summary>
    /// Decrypts data using a public key with PKCS#1 v1.5 padding (verification primitive).
    /// This is the inverse of privateEncrypt and is used for digital signatures.
    /// </summary>
    private static RuntimeValue PublicDecrypt(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("crypto.publicDecrypt requires key and buffer arguments");

        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.publicDecrypt: buffer must be a Buffer or string");
        var decrypted = CryptoKeyUtil.RsaSignaturePrimitive(args[0].ToObject(), data, privateOp: false, "crypto.publicDecrypt");
        return RuntimeValue.FromObject(new SharpTSBuffer(decrypted));
    }

    #endregion

    #region HKDF Key Derivation

    /// <summary>
    /// Synchronous HKDF key derivation (RFC 5869).
    /// </summary>
    private static RuntimeValue HkdfSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        // hkdfSync(digest, ikm, salt, info, keylen)
        if (args.Length < 5)
            throw new Exception("crypto.hkdfSync requires digest, ikm, salt, info, and keylen arguments");

        var digest = args[0].ToObject() as string ?? throw new Exception("crypto.hkdfSync: digest must be a string");
        var ikm = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.hkdfSync: ikm must be a Buffer or string");
        var salt = ConvertToBytes(args[2].ToObject()) ?? []; // Empty salt is valid
        var info = ConvertToBytes(args[3].ToObject()) ?? []; // Empty info is valid
        var keylen = args[4].IsNumber ? (int)args[4].AsNumberUnsafe() : throw new Exception("crypto.hkdfSync: keylen must be a number");

        if (keylen < 0)
            throw new Exception("crypto.hkdfSync: keylen must be non-negative");

        // Handle zero key length specially - .NET doesn't allow 0 but Node.js does
        if (keylen == 0)
            return RuntimeValue.FromObject(new SharpTSBuffer([]));

        var hashAlgorithm = digest.ToLowerInvariant() switch
        {
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new Exception($"crypto.hkdfSync: unsupported digest algorithm '{digest}'. Supported: sha1, sha256, sha384, sha512")
        };

        var derivedKey = HKDF.DeriveKey(hashAlgorithm, ikm, keylen, salt, info);
        return RuntimeValue.FromObject(new SharpTSBuffer(derivedKey));
    }

    #endregion

    #region KeyObject API

    /// <summary>
    /// Creates a secret (symmetric) KeyObject from a key buffer.
    /// </summary>
    private static RuntimeValue CreateSecretKey(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.createSecretKey requires a key argument");

        byte[] keyBytes;

        if (args[0].IsString)
        {
            var keyStr = args[0].AsStringUnsafe();
            // If encoding is provided, use it; otherwise default to utf8
            var encoding = args.Length > 1 && args[1].IsString ? args[1].AsStringUnsafe() : "utf8";
            keyBytes = encoding.ToLowerInvariant() switch
            {
                "utf8" or "utf-8" => System.Text.Encoding.UTF8.GetBytes(keyStr),
                "hex" => Convert.FromHexString(keyStr),
                "base64" => Convert.FromBase64String(keyStr),
                "latin1" or "binary" => System.Text.Encoding.Latin1.GetBytes(keyStr),
                _ => throw new Exception($"crypto.createSecretKey: unsupported encoding '{encoding}'")
            };
        }
        else
        {
            keyBytes = ConvertToBytes(args[0].ToObject()) ?? throw new Exception("crypto.createSecretKey: key must be a Buffer or string");
        }

        return RuntimeValue.FromObject(new SharpTSKeyObject(keyBytes));
    }

    /// <summary>
    /// Creates a public KeyObject. Accepts a PEM string/Buffer, a private KeyObject
    /// (derives the public key), or an options object with { key, format: 'pem'|'der'|'jwk', type }.
    /// </summary>
    private static RuntimeValue CreatePublicKey(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.createPublicKey requires a key argument");

        return RuntimeValue.FromObject(CreateAsymmetricKey(args[0], isPrivate: false, "crypto.createPublicKey"));
    }

    /// <summary>
    /// Creates a private KeyObject. Accepts a PEM string/Buffer or an options object
    /// with { key, format: 'pem'|'der'|'jwk', type: 'pkcs8'|'pkcs1'|'sec1' }.
    /// </summary>
    private static RuntimeValue CreatePrivateKey(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.createPrivateKey requires a key argument");

        return RuntimeValue.FromObject(CreateAsymmetricKey(args[0], isPrivate: true, "crypto.createPrivateKey"));
    }

    /// <summary>Shared input handling for createPublicKey/createPrivateKey (#1059: pem/der/jwk).</summary>
    private static SharpTSKeyObject CreateAsymmetricKey(RuntimeValue arg, bool isPrivate, string context)
    {
        if (arg.IsString)
            return CreateFromPem(arg.AsStringUnsafe(), isPrivate);

        switch (arg.ToObject())
        {
            case SharpTSBuffer buf:
                return CreateFromPem(System.Text.Encoding.UTF8.GetString(buf.Data), isPrivate);

            // createPublicKey(privateKeyObject) derives the public key
            case SharpTSKeyObject keyObj when !isPrivate:
                if (keyObj.RsaKey != null || keyObj.EcdsaKey != null)
                    return SharpTSKeyObject.CreateFromDer(
                        keyObj.RsaKey != null
                            ? keyObj.RsaKey.ExportSubjectPublicKeyInfo()
                            : keyObj.EcdsaKey!.ExportSubjectPublicKeyInfo(),
                        "spki", isPrivate: false);
                throw new Exception($"{context}: cannot derive a public key from a secret KeyObject");

            case SharpTSObject obj:
            {
                var format = obj.Fields.TryGetValue("format", out var f) ? f as string : null;
                obj.Fields.TryGetValue("key", out var keyVal);
                var type = obj.Fields.TryGetValue("type", out var t) ? t as string : null;

                switch (format?.ToLowerInvariant())
                {
                    case "jwk":
                        if (keyVal is not SharpTSObject jwk)
                            throw new Exception($"{context}: JWK format requires an object 'key'");
                        return SharpTSKeyObject.CreateFromJwk(jwk, isPrivate);
                    case "der":
                        if (keyVal is not SharpTSBuffer derBuf)
                            throw new Exception($"{context}: DER format requires a Buffer 'key'");
                        return SharpTSKeyObject.CreateFromDer(derBuf.Data, type, isPrivate);
                    default:
                        return keyVal switch
                        {
                            string pemStr => CreateFromPem(pemStr, isPrivate),
                            SharpTSBuffer pemBuf => CreateFromPem(System.Text.Encoding.UTF8.GetString(pemBuf.Data), isPrivate),
                            _ => throw new Exception($"{context}: key must be a PEM string, Buffer, KeyObject, or JWK object")
                        };
                }
            }

            default:
                throw new Exception($"{context}: key must be a PEM string, Buffer, KeyObject, or options object");
        }
    }

    private static SharpTSKeyObject CreateFromPem(string pem, bool isPrivate)
        => isPrivate ? SharpTSKeyObject.CreatePrivateKey(pem) : SharpTSKeyObject.CreatePublicKey(pem);

    #endregion

    #region Async (Callback-based) Key Derivation

    /// <summary>
    /// Extracts the callback function from the last argument.
    /// </summary>
    private static ISharpTSCallable GetCallback(ReadOnlySpan<RuntimeValue> args)
    {
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: callback is required");
        return callback;
    }

    /// <summary>
    /// Schedules an async callback on the interpreter's event loop and decrements the handle count.
    /// </summary>
    private static void ScheduleCallbackAndUnref(Interp interpreter, ISharpTSCallable callback, object? error, object? result)
    {
        interpreter.ScheduleTimer(0, 0, () =>
        {
            try
            {
                interpreter.InvokeGuestCallback(callback, [error, result]);
            }
            finally
            {
                interpreter.Unref();
            }
        }, isInterval: false);
    }

    /// <summary>
    /// Creates a Node.js-style error object for async callbacks.
    /// </summary>
    private static SharpTSObject CreateCryptoError(Exception ex, string method)
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["code"] = "ERR_CRYPTO_INVALID_STATE",
            ["message"] = $"crypto.{method}: {ex.Message}"
        });
    }

    /// <summary>
    /// crypto.pbkdf2(password, salt, iterations, keylen, digest, callback)
    /// </summary>
    private static RuntimeValue Pbkdf2Async(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var password = ConvertToBytes(args[0].ToObject());
        var salt = ConvertToBytes(args[1].ToObject());
        var iterations = args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : 0;
        var keylen = args[3].IsNumber ? (int)args[3].AsNumberUnsafe() : 0;
        var digest = args[4].ToObject() as string;

        interpreter.Ref(); // Keep event loop alive until callback fires
        _ = Task.Run(() =>
        {
            try
            {
                if (password == null) throw new Exception("password is required");
                if (salt == null) throw new Exception("salt is required");
                if (digest == null) throw new Exception("digest must be a string");
                if (iterations < 1) throw new Exception("iterations must be at least 1");

                var hashAlgorithm = digest.ToLowerInvariant() switch
                {
                    "sha1" => HashAlgorithmName.SHA1,
                    "sha256" => HashAlgorithmName.SHA256,
                    "sha384" => HashAlgorithmName.SHA384,
                    "sha512" => HashAlgorithmName.SHA512,
                    _ => throw new Exception($"unsupported digest algorithm '{digest}'")
                };

                var derivedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, hashAlgorithm, keylen);
                ScheduleCallbackAndUnref(interpreter, callback, null, new SharpTSBuffer(derivedKey));
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "pbkdf2"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// crypto.scrypt(password, salt, keylen[, options], callback)
    /// </summary>
    private static RuntimeValue ScryptAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var password = ConvertToBytes(args[0].ToObject());
        var salt = ConvertToBytes(args[1].ToObject());
        var keylen = args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : 0;

        // Options are between keylen and callback
        SharpTSObject? options = null;
        if (args.Length > 4 && args[3].ToObject() is SharpTSObject opt)
            options = opt;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                if (password == null) throw new Exception("password is required");
                if (salt == null) throw new Exception("salt is required");

                int N = 16384, r = 8, p = 1;
                if (options != null)
                {
                    var fields = options.Fields;
                    if (fields.TryGetValue("N", out var costObj) && costObj is double costVal) N = (int)costVal;
                    if (fields.TryGetValue("cost", out var cost2Obj) && cost2Obj is double cost2Val) N = (int)cost2Val;
                    if (fields.TryGetValue("r", out var rObj) && rObj is double rVal) r = (int)rVal;
                    if (fields.TryGetValue("blockSize", out var bsObj) && bsObj is double bsVal) r = (int)bsVal;
                    if (fields.TryGetValue("p", out var pObj) && pObj is double pVal) p = (int)pVal;
                    if (fields.TryGetValue("parallelization", out var parObj) && parObj is double parVal) p = (int)parVal;
                }

                if (N < 2 || (N & (N - 1)) != 0)
                    throw new Exception("N must be a power of 2 greater than 1");

                var derivedKey = SharpTS.Compilation.ScryptImpl.DeriveBytes(password, salt, N, r, p, keylen);
                ScheduleCallbackAndUnref(interpreter, callback, null, new SharpTSBuffer(derivedKey));
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "scrypt"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// crypto.generateKeyPair(type[, options], callback)
    /// </summary>
    private static RuntimeValue GenerateKeyPairAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var keyType = args[0].ToObject() as string;
        SharpTSObject? options = null;
        if (args.Length > 2 && args[1].ToObject() is SharpTSObject opt)
            options = opt;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                if (keyType == null) throw new Exception("key type is required");
                var result = keyType.ToLowerInvariant() switch
                {
                    "rsa" => GenerateRsaKeyPair(options),
                    "ec" => GenerateEcKeyPair(options),
                    "ed25519" or "ed448" or "x25519" or "x448" => throw new Exception(
                        $"'{keyType}' keys are not supported on this runtime (.NET BCL has no EdDSA/X-curve support)"),
                    _ => throw new Exception($"unsupported key type '{keyType}'")
                };
                // Node.js generateKeyPair callback is (err, publicKey, privateKey)
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result.GetProperty("publicKey"), result.GetProperty("privateKey")]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "generateKeyPair"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// crypto.hkdf(digest, ikm, salt, info, keylen, callback)
    /// </summary>
    private static RuntimeValue HkdfAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var digest = args[0].ToObject() as string;
        var ikm = ConvertToBytes(args[1].ToObject());
        var salt = ConvertToBytes(args[2].ToObject()) ?? [];
        var info = ConvertToBytes(args[3].ToObject()) ?? [];
        var keylen = args[4].IsNumber ? (int)args[4].AsNumberUnsafe() : 0;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                if (digest == null) throw new Exception("digest must be a string");
                if (ikm == null) throw new Exception("ikm must be a Buffer or string");

                if (keylen == 0)
                {
                    ScheduleCallbackAndUnref(interpreter, callback, null, new SharpTSBuffer([]));
                    return;
                }

                var hashAlgorithm = digest.ToLowerInvariant() switch
                {
                    "sha1" => HashAlgorithmName.SHA1,
                    "sha256" => HashAlgorithmName.SHA256,
                    "sha384" => HashAlgorithmName.SHA384,
                    "sha512" => HashAlgorithmName.SHA512,
                    _ => throw new Exception($"unsupported digest algorithm '{digest}'")
                };

                var derivedKey = HKDF.DeriveKey(hashAlgorithm, ikm, keylen, salt, info);
                ScheduleCallbackAndUnref(interpreter, callback, null, new SharpTSBuffer(derivedKey));
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "hkdf"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    #endregion

    #region One-shot digest / sign / verify (#1055)

    /// <summary>
    /// crypto.hash(algorithm, data[, outputEncoding]) — one-shot digest (Node 21+).
    /// Default outputEncoding is 'hex' (unlike hash.digest(), which defaults to Buffer).
    /// </summary>
    private static RuntimeValue HashOneShot(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2 || !args[0].IsString)
            throw new Exception("crypto.hash requires algorithm and data arguments");

        var algorithm = args[0].AsStringUnsafe();
        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.hash: data must be a string or Buffer");
        var encoding = args.Length > 2 && args[2].ToObject() is string enc ? enc : "hex";

        var digest = CryptoAlgorithms.OneShotHash(algorithm, data);
        return RuntimeValue.FromBoxed(encoding == "buffer"
            ? new SharpTSBuffer(digest)
            : CryptoEncoding.ToBufferOrString(digest, encoding));
    }

    /// <summary>
    /// crypto.sign(algorithm, data, key[, callback]) — one-shot sign. Wraps the
    /// streaming Sign core; honors { padding, saltLength, dsaEncoding } key options.
    /// </summary>
    private static RuntimeValue SignOneShot(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 3)
            throw new Exception("crypto.sign requires algorithm, data, and key arguments");

        var algorithm = args[0].IsString ? args[0].AsStringUnsafe() : null;
        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.sign: data must be a string or Buffer");
        var key = args[2].ToObject();

        if (args.Length > 3 && args[3].ToObject() is ISharpTSCallable callback)
        {
            interpreter.Ref();
            try
            {
                var sig = CryptoKeyUtil.SignData(algorithm, data, key, "crypto.sign");
                ScheduleCallbackAndUnref(interpreter, callback, null, new SharpTSBuffer(sig));
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "sign"), null);
            }
            return RuntimeValue.Undefined;
        }

        var signature = CryptoKeyUtil.SignData(algorithm, data, key, "crypto.sign");
        return RuntimeValue.FromObject(new SharpTSBuffer(signature));
    }

    /// <summary>
    /// crypto.verify(algorithm, data, key, signature[, callback]) — one-shot verify.
    /// </summary>
    private static RuntimeValue VerifyOneShot(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 4)
            throw new Exception("crypto.verify requires algorithm, data, key, and signature arguments");

        var algorithm = args[0].IsString ? args[0].AsStringUnsafe() : null;
        var data = ConvertToBytes(args[1].ToObject()) ?? throw new Exception("crypto.verify: data must be a string or Buffer");
        var key = args[2].ToObject();
        var signature = ConvertToBytes(args[3].ToObject()) ?? throw new Exception("crypto.verify: signature must be a Buffer");

        if (args.Length > 4 && args[4].ToObject() is ISharpTSCallable callback)
        {
            interpreter.Ref();
            try
            {
                var ok = CryptoKeyUtil.VerifyData(algorithm, data, key, signature, "crypto.verify");
                ScheduleCallbackAndUnref(interpreter, callback, null, ok);
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "verify"), null);
            }
            return RuntimeValue.Undefined;
        }

        var result = CryptoKeyUtil.VerifyData(algorithm, data, key, signature, "crypto.verify");
        return RuntimeValue.FromBoolean(result);
    }

    #endregion

    #region constants / cipher info / curves (#1056, #1057, #1058)

    /// <summary>Builds the crypto.constants object from the shared table.</summary>
    private static SharpTSObject BuildConstants()
    {
        var fields = new Dictionary<string, object?>();
        foreach (var (name, value) in CryptoInfoTables.NumericConstants)
            fields[name] = value;
        foreach (var (name, value) in CryptoInfoTables.StringConstants)
            fields[name] = value;
        return new SharpTSObject(fields);
    }

    /// <summary>
    /// crypto.getCipherInfo(nameOrNid[, options]) → { name, nid, blockSize, ivLength, keyLength, mode } or undefined.
    /// </summary>
    private static RuntimeValue GetCipherInfo(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return RuntimeValue.Undefined;

        CryptoInfoTables.CipherInfo? match = null;
        if (args[0].IsString)
        {
            var name = args[0].AsStringUnsafe().ToLowerInvariant();
            foreach (var info in CryptoInfoTables.CipherInfos)
                if (info.Name == name) { match = info; break; }
        }
        else if (args[0].IsNumber)
        {
            var nid = (int)args[0].AsNumberUnsafe();
            foreach (var info in CryptoInfoTables.CipherInfos)
                if (info.Nid == nid) { match = info; break; }
        }

        if (match is not { } m)
            return RuntimeValue.Undefined;

        // Test options: a keyLength/ivLength differing from the cipher's fixed
        // lengths means the combination is unsupported → undefined (Node behavior).
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject options)
        {
            if (options.Fields.TryGetValue("keyLength", out var kl) && kl is double kld && (int)kld != m.KeyLength)
                return RuntimeValue.Undefined;
            if (options.Fields.TryGetValue("ivLength", out var il) && il is double ild && (int)ild != m.IvLength)
                return RuntimeValue.Undefined;
        }

        return RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
        {
            ["name"] = m.Name,
            ["nid"] = (double)m.Nid,
            ["blockSize"] = (double)m.BlockSize,
            ["ivLength"] = (double)m.IvLength,
            ["keyLength"] = (double)m.KeyLength,
            ["mode"] = m.Mode
        }));
    }

    /// <summary>crypto.getCurves() → supported EC curve names.</summary>
    private static RuntimeValue GetCurves(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(new SharpTSArray(
            CryptoInfoTables.SupportedCurves.Select(c => (object?)c).ToList()));
    }

    #endregion

    #region randomFill / generateKey (#1058)

    /// <summary>
    /// crypto.randomFill(buffer[, offset][, size], callback) — async randomFillSync.
    /// </summary>
    private static RuntimeValue RandomFillAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        if (args.Length < 2 || args[0].ToObject() is not SharpTSBuffer buffer)
            throw new Exception("crypto.randomFill requires a Buffer and a callback");

        int offset = args.Length > 2 && args[1].IsNumber ? (int)args[1].AsNumberUnsafe() : 0;
        int size = args.Length > 3 && args[2].IsNumber ? (int)args[2].AsNumberUnsafe() : buffer.Data.Length - offset;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                if (offset < 0 || offset > buffer.Data.Length)
                    throw new Exception($"offset out of range (0-{buffer.Data.Length})");
                if (size < 0 || offset + size > buffer.Data.Length)
                    throw new Exception("size out of range");

                var randomBytes = RandomNumberGenerator.GetBytes(size);
                Array.Copy(randomBytes, 0, buffer.Data, offset, size);
                ScheduleCallbackAndUnref(interpreter, callback, null, buffer);
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "randomFill"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>Shared core for generateKey/generateKeySync: 'hmac' | 'aes' with { length } in bits.</summary>
    internal static SharpTSKeyObject GenerateSecretKey(string? type, SharpTSObject? options, string context)
    {
        if (type is not ("hmac" or "aes"))
            throw new Exception($"{context}: type must be 'hmac' or 'aes'");

        if (options?.Fields.TryGetValue("length", out var l) != true || l is not double lengthBits)
            throw new Exception($"{context}: options.length is required");

        var length = (int)lengthBits;
        if (type == "aes")
        {
            if (length is not (128 or 192 or 256))
                throw new Exception($"{context}: AES key length must be 128, 192, or 256 bits");
        }
        else
        {
            if (length < 8 || length % 8 != 0)
                throw new Exception($"{context}: HMAC key length must be a positive multiple of 8 bits");
        }

        return new SharpTSKeyObject(RandomNumberGenerator.GetBytes(length / 8));
    }

    /// <summary>crypto.generateKeySync(type, options) → KeyObject.</summary>
    private static RuntimeValue GenerateKeySync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var type = args.Length > 0 ? args[0].ToObject() as string : null;
        var options = args.Length > 1 ? args[1].ToObject() as SharpTSObject : null;
        return RuntimeValue.FromObject(GenerateSecretKey(type, options, "crypto.generateKeySync"));
    }

    /// <summary>crypto.generateKey(type, options, callback).</summary>
    private static RuntimeValue GenerateKeyAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var type = args[0].ToObject() as string;
        var options = args.Length > 1 ? args[1].ToObject() as SharpTSObject : null;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var key = GenerateSecretKey(type, options, "crypto.generateKey");
                ScheduleCallbackAndUnref(interpreter, callback, null, key);
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "generateKey"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    #endregion

    #region One-shot diffieHellman / FIPS shims (#1060)

    /// <summary>
    /// crypto.diffieHellman({ privateKey, publicKey }) — one-shot key agreement over
    /// EC KeyObjects (raw shared secret, matching Node). x25519/x448 are a documented
    /// .NET ceiling; classic DH KeyObjects are not supported by createPrivateKey.
    /// </summary>
    private static RuntimeValue DiffieHellmanOneShot(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not SharpTSObject options)
            throw new Exception("crypto.diffieHellman requires an options object with privateKey and publicKey");

        options.Fields.TryGetValue("privateKey", out var priv);
        options.Fields.TryGetValue("publicKey", out var pub);
        if (priv is not SharpTSKeyObject privateKey || pub is not SharpTSKeyObject publicKey)
            throw new Exception("crypto.diffieHellman: privateKey and publicKey must be KeyObjects");

        if (privateKey.EcdsaKey == null || publicKey.EcdsaKey == null)
            throw new Exception("crypto.diffieHellman: only EC keys are supported (x25519/x448 are not available on this runtime)");

        using var privEcdh = ECDiffieHellman.Create();
        privEcdh.ImportParameters(privateKey.EcdsaKey.ExportParameters(true));
        using var pubEcdh = ECDiffieHellman.Create();
        pubEcdh.ImportParameters(publicKey.EcdsaKey.ExportParameters(false));

        var secret = privEcdh.DeriveRawSecretAgreement(pubEcdh.PublicKey);
        return RuntimeValue.FromObject(new SharpTSBuffer(secret));
    }

    /// <summary>crypto.getFips() → 0 (no FIPS mode on this runtime).</summary>
    private static RuntimeValue GetFips(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromNumber(0);

    /// <summary>crypto.setFips(bool) — enabling FIPS throws (non-FIPS build), disabling is a no-op.</summary>
    private static RuntimeValue SetFips(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length > 0 && args[0].IsTruthy())
            throw new Exception("Cannot set FIPS mode in a non-FIPS build.");
        return RuntimeValue.Undefined;
    }

    #endregion

    #region Primes (#1062)

    private static System.Numerics.BigInteger ParsePrimeCandidate(object? candidate, string context)
    {
        return candidate switch
        {
            SharpTSBigInt bi => bi.Value,
            System.Numerics.BigInteger raw => raw,
            SharpTSBuffer buf => CryptoPrimes.FromUnsignedBigEndian(buf.Data),
            _ => throw new Exception($"{context}: candidate must be a bigint or Buffer")
        };
    }

    private static (int Checks, bool Safe, bool AsBigInt) ParsePrimeOptions(object? options, string context)
    {
        int checks = 0;
        bool safe = false, asBigInt = false;
        if (options is SharpTSObject obj)
        {
            if (obj.Fields.TryGetValue("checks", out var c) && c is double cd)
                checks = (int)cd;
            if (obj.Fields.TryGetValue("safe", out var s) && s is bool sb)
                safe = sb;
            if (obj.Fields.TryGetValue("bigint", out var b) && b is bool bb)
                asBigInt = bb;
            if (obj.Fields.ContainsKey("add") || obj.Fields.ContainsKey("rem"))
                throw new Exception($"{context}: the add/rem options are not supported on this runtime");
        }
        return (checks, safe, asBigInt);
    }

    /// <summary>crypto.checkPrimeSync(candidate[, options]) → boolean.</summary>
    private static RuntimeValue CheckPrimeSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            throw new Exception("crypto.checkPrimeSync requires a candidate argument");
        var candidate = ParsePrimeCandidate(args[0].ToObject(), "crypto.checkPrimeSync");
        var (checks, _, _) = ParsePrimeOptions(args.Length > 1 ? args[1].ToObject() : null, "crypto.checkPrimeSync");
        return RuntimeValue.FromBoolean(CryptoPrimes.IsProbablyPrime(candidate, checks));
    }

    /// <summary>crypto.checkPrime(candidate[, options], callback).</summary>
    private static RuntimeValue CheckPrimeAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var candidateObj = args[0].ToObject();
        var optionsObj = args.Length > 2 ? args[1].ToObject() : null;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var candidate = ParsePrimeCandidate(candidateObj, "crypto.checkPrime");
                var (checks, _, _) = ParsePrimeOptions(optionsObj, "crypto.checkPrime");
                ScheduleCallbackAndUnref(interpreter, callback, null, CryptoPrimes.IsProbablyPrime(candidate, checks));
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "checkPrime"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>crypto.generatePrimeSync(size[, options]) → Buffer (or bigint with { bigint: true }).</summary>
    private static RuntimeValue GeneratePrimeSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsNumber)
            throw new Exception("crypto.generatePrimeSync requires a size (bits) argument");
        var bits = (int)args[0].AsNumberUnsafe();
        var (_, safe, asBigInt) = ParsePrimeOptions(args.Length > 1 ? args[1].ToObject() : null, "crypto.generatePrimeSync");

        var prime = CryptoPrimes.GeneratePrime(bits, safe);
        return asBigInt
            ? RuntimeValue.FromBigInt(prime)
            : RuntimeValue.FromObject(new SharpTSBuffer(CryptoPrimes.ToUnsignedBigEndian(prime)));
    }

    /// <summary>crypto.generatePrime(size[, options], callback).</summary>
    private static RuntimeValue GeneratePrimeAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var callback = GetCallback(args);
        var bits = args[0].IsNumber ? (int)args[0].AsNumberUnsafe() : 0;
        var optionsObj = args.Length > 2 ? args[1].ToObject() : null;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var (_, safe, asBigInt) = ParsePrimeOptions(optionsObj, "crypto.generatePrime");
                var prime = CryptoPrimes.GeneratePrime(bits, safe);
                object result = asBigInt
                    ? new SharpTSBigInt(prime)
                    : new SharpTSBuffer(CryptoPrimes.ToUnsignedBigEndian(prime));
                ScheduleCallbackAndUnref(interpreter, callback, null, result);
            }
            catch (Exception ex)
            {
                ScheduleCallbackAndUnref(interpreter, callback, CreateCryptoError(ex, "generatePrime"), null);
            }
        });

        return RuntimeValue.Undefined;
    }

    #endregion
}
