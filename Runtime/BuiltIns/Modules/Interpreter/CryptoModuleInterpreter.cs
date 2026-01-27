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
            ["randomUUID"] = new BuiltInMethod("randomUUID", 0, RandomUUID),
            ["randomInt"] = new BuiltInMethod("randomInt", 1, 2, RandomInt),
            ["pbkdf2Sync"] = new BuiltInMethod("pbkdf2Sync", 5, Pbkdf2Sync),
            ["scryptSync"] = new BuiltInMethod("scryptSync", 3, 4, ScryptSync),
            ["timingSafeEqual"] = new BuiltInMethod("timingSafeEqual", 2, TimingSafeEqual)
        };
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
}
