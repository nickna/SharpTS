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
            ["randomBytes"] = new BuiltInMethod("randomBytes", 1, RandomBytes),
            ["randomUUID"] = new BuiltInMethod("randomUUID", 0, RandomUUID),
            ["randomInt"] = new BuiltInMethod("randomInt", 1, 2, RandomInt)
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
}
