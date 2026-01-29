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
            ["createHash"] = new BuiltInMethod("createHash", 1, CreateHash),
            ["createHmac"] = new BuiltInMethod("createHmac", 2, CreateHmac),
            ["createCipheriv"] = new BuiltInMethod("createCipheriv", 3, CreateCipheriv),
            ["createDecipheriv"] = new BuiltInMethod("createDecipheriv", 3, CreateDecipheriv),
            ["randomBytes"] = new BuiltInMethod("randomBytes", 1, RandomBytes),
            ["randomFillSync"] = new BuiltInMethod("randomFillSync", 1, 3, RandomFillSync),
            ["randomUUID"] = new BuiltInMethod("randomUUID", 0, RandomUUID),
            ["randomInt"] = new BuiltInMethod("randomInt", 1, 2, RandomInt),
            ["pbkdf2Sync"] = new BuiltInMethod("pbkdf2Sync", 5, Pbkdf2Sync),
            ["scryptSync"] = new BuiltInMethod("scryptSync", 3, 4, ScryptSync),
            ["timingSafeEqual"] = new BuiltInMethod("timingSafeEqual", 2, TimingSafeEqual),
            ["createSign"] = new BuiltInMethod("createSign", 1, CreateSign),
            ["createVerify"] = new BuiltInMethod("createVerify", 1, CreateVerify),
            ["getHashes"] = new BuiltInMethod("getHashes", 0, GetHashes),
            ["getCiphers"] = new BuiltInMethod("getCiphers", 0, GetCiphers),
            ["generateKeyPairSync"] = new BuiltInMethod("generateKeyPairSync", 1, 2, GenerateKeyPairSync),
            ["createDiffieHellman"] = new BuiltInMethod("createDiffieHellman", 1, 2, CreateDiffieHellman),
            ["getDiffieHellman"] = new BuiltInMethod("getDiffieHellman", 1, GetDiffieHellman),
            ["createECDH"] = new BuiltInMethod("createECDH", 1, CreateECDH),
            // RSA encryption/decryption
            ["publicEncrypt"] = new BuiltInMethod("publicEncrypt", 2, PublicEncrypt),
            ["privateDecrypt"] = new BuiltInMethod("privateDecrypt", 2, PrivateDecrypt),
            ["privateEncrypt"] = new BuiltInMethod("privateEncrypt", 2, PrivateEncrypt),
            ["publicDecrypt"] = new BuiltInMethod("publicDecrypt", 2, PublicDecrypt),
            // HKDF key derivation
            ["hkdfSync"] = new BuiltInMethod("hkdfSync", 5, HkdfSync),
            // KeyObject API
            ["createSecretKey"] = new BuiltInMethod("createSecretKey", 1, 2, CreateSecretKey),
            ["createPublicKey"] = new BuiltInMethod("createPublicKey", 1, CreatePublicKey),
            ["createPrivateKey"] = new BuiltInMethod("createPrivateKey", 1, CreatePrivateKey)
        };
    }

    private static object? CreateSign(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string algorithm)
            throw new Exception("crypto.createSign requires an algorithm name");

        return new SharpTSSign(algorithm);
    }

    private static object? CreateVerify(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string algorithm)
            throw new Exception("crypto.createVerify requires an algorithm name");

        return new SharpTSVerify(algorithm);
    }

    private static object? CreateHash(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string algorithm)
            throw new Exception("crypto.createHash requires an algorithm name");

        return new SharpTSHash(algorithm);
    }

    private static object? CreateHmac(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2 || args[0] is not string algorithm)
            throw new Exception("crypto.createHmac requires an algorithm name and a key");

        var key = args[1] ?? throw new Exception("crypto.createHmac requires a key");
        return new SharpTSHmac(algorithm, key);
    }

    private static object? CreateCipheriv(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 3 || args[0] is not string algorithm)
            throw new Exception("crypto.createCipheriv requires algorithm, key, and iv arguments");

        var key = ConvertToBytes(args[1]) ?? throw new Exception("crypto.createCipheriv requires a key");
        var iv = ConvertToBytes(args[2]) ?? throw new Exception("crypto.createCipheriv requires an iv");

        return new SharpTSCipher(algorithm, key, iv);
    }

    private static object? CreateDecipheriv(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 3 || args[0] is not string algorithm)
            throw new Exception("crypto.createDecipheriv requires algorithm, key, and iv arguments");

        var key = ConvertToBytes(args[1]) ?? throw new Exception("crypto.createDecipheriv requires a key");
        var iv = ConvertToBytes(args[2]) ?? throw new Exception("crypto.createDecipheriv requires an iv");

        return new SharpTSDecipher(algorithm, key, iv);
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

    private static object? RandomBytes(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not double size)
            throw new Exception("crypto.randomBytes requires a size argument");

        var byteCount = (int)size;
        var bytes = RandomNumberGenerator.GetBytes(byteCount);

        // Return as Buffer (matching Node.js behavior)
        return new SharpTSBuffer(bytes);
    }

    private static object? RandomFillSync(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not SharpTSBuffer buffer)
            throw new Exception("crypto.randomFillSync requires a Buffer argument");

        var data = buffer.Data;

        // Optional offset and size parameters
        int offset = 0;
        int size = data.Length;

        if (args.Count > 1 && args[1] is double offsetArg)
        {
            offset = (int)offsetArg;
            if (offset < 0 || offset > data.Length)
                throw new Exception($"crypto.randomFillSync: offset out of range (0-{data.Length})");
        }

        if (args.Count > 2 && args[2] is double sizeArg)
        {
            size = (int)sizeArg;
        }
        else if (args.Count > 1)
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
        return buffer;
    }

    private static object? RandomUUID(Interp interpreter, object? receiver, List<object?> args)
    {
        return Guid.NewGuid().ToString();
    }

    private static object? RandomInt(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("crypto.randomInt requires at least one argument");

        int min, max;

        if (args.Count == 1)
        {
            // randomInt(max) - range is [0, max)
            min = 0;
            max = args[0] is double d ? (int)d : throw new Exception("crypto.randomInt argument must be a number");
        }
        else
        {
            // randomInt(min, max) - range is [min, max)
            min = args[0] is double d1 ? (int)d1 : throw new Exception("crypto.randomInt min must be a number");
            max = args[1] is double d2 ? (int)d2 : throw new Exception("crypto.randomInt max must be a number");
        }

        return (double)RandomNumberGenerator.GetInt32(min, max);
    }

    private static object? Pbkdf2Sync(Interp interpreter, object? receiver, List<object?> args)
    {
        // pbkdf2Sync(password, salt, iterations, keylen, digest)
        if (args.Count < 5)
            throw new Exception("crypto.pbkdf2Sync requires password, salt, iterations, keylen, and digest arguments");

        var password = ConvertToBytes(args[0]) ?? throw new Exception("crypto.pbkdf2Sync requires a password");
        var salt = ConvertToBytes(args[1]) ?? throw new Exception("crypto.pbkdf2Sync requires a salt");
        var iterations = args[2] is double d ? (int)d : throw new Exception("crypto.pbkdf2Sync iterations must be a number");
        var keylen = args[3] is double k ? (int)k : throw new Exception("crypto.pbkdf2Sync keylen must be a number");
        var digest = args[4] as string ?? throw new Exception("crypto.pbkdf2Sync digest must be a string");

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
        return new SharpTSBuffer(derivedKey);
    }

    private static object? ScryptSync(Interp interpreter, object? receiver, List<object?> args)
    {
        // scryptSync(password, salt, keylen[, options])
        if (args.Count < 3)
            throw new Exception("crypto.scryptSync requires password, salt, and keylen arguments");

        var password = ConvertToBytes(args[0]) ?? throw new Exception("crypto.scryptSync requires a password");
        var salt = ConvertToBytes(args[1]) ?? throw new Exception("crypto.scryptSync requires a salt");
        var keylen = args[2] is double k ? (int)k : throw new Exception("crypto.scryptSync keylen must be a number");

        if (keylen < 0)
            throw new Exception("crypto.scryptSync keylen must be non-negative");

        // Default scrypt parameters (Node.js defaults)
        int N = 16384;  // cost parameter (must be power of 2)
        int r = 8;      // block size
        int p = 1;      // parallelization

        // Parse options if provided
        if (args.Count > 3 && args[3] is SharpTSObject options)
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
        return new SharpTSBuffer(derivedKey);
    }

    private static object? TimingSafeEqual(Interp interpreter, object? receiver, List<object?> args)
    {
        // timingSafeEqual(a, b)
        if (args.Count < 2)
            throw new Exception("crypto.timingSafeEqual requires two arguments");

        var a = ConvertToBytes(args[0]) ?? throw new Exception("crypto.timingSafeEqual: first argument must be a Buffer or string");
        var b = ConvertToBytes(args[1]) ?? throw new Exception("crypto.timingSafeEqual: second argument must be a Buffer or string");

        // Node.js throws if lengths don't match
        if (a.Length != b.Length)
            throw new Exception($"crypto.timingSafeEqual: Input buffers must have the same byte length. Received {a.Length} and {b.Length}");

        // Use .NET's constant-time comparison
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Returns an array of supported hash algorithm names.
    /// </summary>
    private static object? GetHashes(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSArray(new List<object?> { "md5", "sha1", "sha256", "sha384", "sha512" });
    }

    /// <summary>
    /// Returns an array of supported cipher algorithm names.
    /// </summary>
    private static object? GetCiphers(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSArray(new List<object?>
        {
            "aes-128-cbc", "aes-192-cbc", "aes-256-cbc",
            "aes-128-gcm", "aes-192-gcm", "aes-256-gcm"
        });
    }

    /// <summary>
    /// Generates a key pair synchronously for RSA or EC algorithms.
    /// </summary>
    private static object? GenerateKeyPairSync(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string keyType)
            throw new Exception("crypto.generateKeyPairSync requires a key type argument");

        var options = args.Count > 1 ? args[1] as SharpTSObject : null;

        return keyType.ToLowerInvariant() switch
        {
            "rsa" => GenerateRsaKeyPair(options),
            "ec" => GenerateEcKeyPair(options),
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
    private static object? CreateDiffieHellman(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("crypto.createDiffieHellman requires at least one argument");

        // Check if first arg is a number (prime length) or Buffer/string (prime)
        if (args[0] is double primeLength)
        {
            return new SharpTSDiffieHellman((int)primeLength);
        }

        var prime = ConvertToBytes(args[0]) ?? throw new Exception("crypto.createDiffieHellman: prime must be a number, Buffer, or string");
        byte[]? generator = null;
        if (args.Count > 1 && args[1] != null)
        {
            generator = ConvertToBytes(args[1]);
        }

        return new SharpTSDiffieHellman(prime, generator);
    }

    /// <summary>
    /// Gets a predefined Diffie-Hellman group by name.
    /// </summary>
    private static object? GetDiffieHellman(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string groupName)
            throw new Exception("crypto.getDiffieHellman requires a group name");

        return new SharpTSDiffieHellman(groupName, isGroup: true);
    }

    /// <summary>
    /// Creates an Elliptic Curve Diffie-Hellman key exchange object.
    /// </summary>
    private static object? CreateECDH(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string curveName)
            throw new Exception("crypto.createECDH requires a curve name");

        return new SharpTSECDH(curveName);
    }

    #region RSA Encryption/Decryption

    /// <summary>
    /// Encrypts data using a public key with RSA-OAEP padding (SHA-1 by default, matching Node.js).
    /// </summary>
    private static object? PublicEncrypt(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("crypto.publicEncrypt requires key and buffer arguments");

        var keyPem = ExtractKeyPem(args[0]);
        var data = ConvertToBytes(args[1]) ?? throw new Exception("crypto.publicEncrypt: buffer must be a Buffer or string");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);

        // Node.js default is OAEP with SHA-1
        var encrypted = rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA1);
        return new SharpTSBuffer(encrypted);
    }

    /// <summary>
    /// Decrypts data using a private key with RSA-OAEP padding (SHA-1 by default, matching Node.js).
    /// </summary>
    private static object? PrivateDecrypt(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("crypto.privateDecrypt requires key and buffer arguments");

        var keyPem = ExtractKeyPem(args[0]);
        var data = ConvertToBytes(args[1]) ?? throw new Exception("crypto.privateDecrypt: buffer must be a Buffer or string");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);

        // Node.js default is OAEP with SHA-1
        var decrypted = rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA1);
        return new SharpTSBuffer(decrypted);
    }

    /// <summary>
    /// Encrypts data using a private key with PKCS#1 v1.5 padding (signing primitive).
    /// This is the inverse of publicDecrypt and is used for digital signatures.
    /// </summary>
    private static object? PrivateEncrypt(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("crypto.privateEncrypt requires key and buffer arguments");

        var keyPem = ExtractKeyPem(args[0]);
        var data = ConvertToBytes(args[1]) ?? throw new Exception("crypto.privateEncrypt: buffer must be a Buffer or string");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);

        // privateEncrypt uses PKCS#1 v1.5 padding (raw RSA operation with padding)
        // In .NET, we can use Decrypt with Pkcs1 padding as a workaround
        // This performs: result = data^d mod n (the private key operation)
        var encrypted = rsa.Decrypt(data, RSAEncryptionPadding.Pkcs1);
        return new SharpTSBuffer(encrypted);
    }

    /// <summary>
    /// Decrypts data using a public key with PKCS#1 v1.5 padding (verification primitive).
    /// This is the inverse of privateEncrypt and is used for digital signatures.
    /// </summary>
    private static object? PublicDecrypt(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("crypto.publicDecrypt requires key and buffer arguments");

        var keyPem = ExtractKeyPem(args[0]);
        var data = ConvertToBytes(args[1]) ?? throw new Exception("crypto.publicDecrypt: buffer must be a Buffer or string");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(keyPem);

        // publicDecrypt uses PKCS#1 v1.5 padding (raw RSA operation with padding)
        // In .NET, we can use Encrypt with Pkcs1 padding as a workaround
        // This performs: result = data^e mod n (the public key operation)
        var decrypted = rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
        return new SharpTSBuffer(decrypted);
    }

    /// <summary>
    /// Extracts PEM key string from various input formats.
    /// </summary>
    private static string ExtractKeyPem(object? key)
    {
        return key switch
        {
            string pem => pem,
            SharpTSKeyObject keyObj => keyObj.RsaKey != null
                ? (keyObj.Type == KeyObjectType.Private
                    ? keyObj.RsaKey.ExportPkcs8PrivateKeyPem()
                    : keyObj.RsaKey.ExportSubjectPublicKeyInfoPem())
                : throw new Exception("KeyObject must contain an RSA key"),
            SharpTSObject obj when obj.Fields.TryGetValue("key", out var k) && k is string keyStr => keyStr,
            _ => throw new Exception("Key must be a PEM string, KeyObject, or object with 'key' property")
        };
    }

    #endregion

    #region HKDF Key Derivation

    /// <summary>
    /// Synchronous HKDF key derivation (RFC 5869).
    /// </summary>
    private static object? HkdfSync(Interp interpreter, object? receiver, List<object?> args)
    {
        // hkdfSync(digest, ikm, salt, info, keylen)
        if (args.Count < 5)
            throw new Exception("crypto.hkdfSync requires digest, ikm, salt, info, and keylen arguments");

        var digest = args[0] as string ?? throw new Exception("crypto.hkdfSync: digest must be a string");
        var ikm = ConvertToBytes(args[1]) ?? throw new Exception("crypto.hkdfSync: ikm must be a Buffer or string");
        var salt = ConvertToBytes(args[2]) ?? []; // Empty salt is valid
        var info = ConvertToBytes(args[3]) ?? []; // Empty info is valid
        var keylen = args[4] is double k ? (int)k : throw new Exception("crypto.hkdfSync: keylen must be a number");

        if (keylen < 0)
            throw new Exception("crypto.hkdfSync: keylen must be non-negative");

        // Handle zero key length specially - .NET doesn't allow 0 but Node.js does
        if (keylen == 0)
            return new SharpTSBuffer([]);

        var hashAlgorithm = digest.ToLowerInvariant() switch
        {
            "sha1" => HashAlgorithmName.SHA1,
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new Exception($"crypto.hkdfSync: unsupported digest algorithm '{digest}'. Supported: sha1, sha256, sha384, sha512")
        };

        var derivedKey = HKDF.DeriveKey(hashAlgorithm, ikm, keylen, salt, info);
        return new SharpTSBuffer(derivedKey);
    }

    #endregion

    #region KeyObject API

    /// <summary>
    /// Creates a secret (symmetric) KeyObject from a key buffer.
    /// </summary>
    private static object? CreateSecretKey(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("crypto.createSecretKey requires a key argument");

        byte[] keyBytes;

        if (args[0] is string keyStr)
        {
            // If encoding is provided, use it; otherwise default to utf8
            var encoding = args.Count > 1 && args[1] is string enc ? enc : "utf8";
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
            keyBytes = ConvertToBytes(args[0]) ?? throw new Exception("crypto.createSecretKey: key must be a Buffer or string");
        }

        return new SharpTSKeyObject(keyBytes);
    }

    /// <summary>
    /// Creates a public KeyObject from a PEM-encoded public key.
    /// </summary>
    private static object? CreatePublicKey(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("crypto.createPublicKey requires a key argument");

        string pem;

        if (args[0] is string keyStr)
        {
            pem = keyStr;
        }
        else if (args[0] is SharpTSObject obj && obj.Fields.TryGetValue("key", out var keyVal) && keyVal is string keyPem)
        {
            pem = keyPem;
        }
        else if (args[0] is SharpTSBuffer buf)
        {
            // PEM as buffer
            pem = System.Text.Encoding.UTF8.GetString(buf.Data);
        }
        else
        {
            throw new Exception("crypto.createPublicKey: key must be a PEM string, Buffer, or object with 'key' property");
        }

        return SharpTSKeyObject.CreatePublicKey(pem);
    }

    /// <summary>
    /// Creates a private KeyObject from a PEM-encoded private key.
    /// </summary>
    private static object? CreatePrivateKey(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("crypto.createPrivateKey requires a key argument");

        string pem;

        if (args[0] is string keyStr)
        {
            pem = keyStr;
        }
        else if (args[0] is SharpTSObject obj && obj.Fields.TryGetValue("key", out var keyVal) && keyVal is string keyPem)
        {
            pem = keyPem;
        }
        else if (args[0] is SharpTSBuffer buf)
        {
            // PEM as buffer
            pem = System.Text.Encoding.UTF8.GetString(buf.Data);
        }
        else
        {
            throw new Exception("crypto.createPrivateKey: key must be a PEM string, Buffer, or object with 'key' property");
        }

        return SharpTSKeyObject.CreatePrivateKey(pem);
    }

    #endregion
}
