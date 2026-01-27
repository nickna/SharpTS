using System.Reflection.Emit;
using System.Security.Cryptography;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'crypto' module.
/// </summary>
public sealed class CryptoModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "crypto";

    private static readonly string[] _exportedMembers =
    [
        "createHash", "createHmac", "createCipheriv", "createDecipheriv", "randomBytes", "randomUUID", "randomInt",
        "pbkdf2Sync", "scryptSync", "timingSafeEqual", "createSign", "createVerify",
        "getHashes", "getCiphers", "generateKeyPairSync", "createDiffieHellman", "getDiffieHellman", "createECDH"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "createHash" => EmitCreateHash(emitter, arguments),
            "createHmac" => EmitCreateHmac(emitter, arguments),
            "createCipheriv" => EmitCreateCipheriv(emitter, arguments),
            "createDecipheriv" => EmitCreateDecipheriv(emitter, arguments),
            "randomBytes" => EmitRandomBytes(emitter, arguments),
            "randomUUID" => EmitRandomUUID(emitter),
            "randomInt" => EmitRandomInt(emitter, arguments),
            "pbkdf2Sync" => EmitPbkdf2Sync(emitter, arguments),
            "scryptSync" => EmitScryptSync(emitter, arguments),
            "timingSafeEqual" => EmitTimingSafeEqual(emitter, arguments),
            "createSign" => EmitCreateSign(emitter, arguments),
            "createVerify" => EmitCreateVerify(emitter, arguments),
            "getHashes" => EmitGetHashes(emitter),
            "getCiphers" => EmitGetCiphers(emitter),
            "generateKeyPairSync" => EmitGenerateKeyPairSync(emitter, arguments),
            "createDiffieHellman" => EmitCreateDiffieHellman(emitter, arguments),
            "getDiffieHellman" => EmitGetDiffieHellman(emitter, arguments),
            "createECDH" => EmitCreateECDH(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // crypto module has no properties
        return false;
    }

    private static bool EmitCreateHash(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Default to sha256 if no algorithm specified
            il.Emit(OpCodes.Ldstr, "sha256");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        }

        // Call runtime helper to create hash
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateHash);
        return true;
    }

    private static bool EmitCreateHmac(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            // Missing arguments - throw at runtime
            il.Emit(OpCodes.Ldstr, "crypto.createHmac requires algorithm and key arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit algorithm argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Emit key argument - convert to byte[]
        // For string keys, use UTF-8 encoding
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);

        // Check if it's a string and convert to byte[]
        var keyLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        var isStringLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, ctx.Types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // Not a string - convert via ToString() and then UTF-8
        il.Emit(OpCodes.Call, ctx.Types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, ctx.Types.Encoding.GetMethod("GetBytes", [ctx.Types.String])!);
        il.Emit(OpCodes.Br, endLabel);

        // String - convert via UTF-8
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Call, ctx.Types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, ctx.Types.String);
        il.Emit(OpCodes.Callvirt, ctx.Types.Encoding.GetMethod("GetBytes", [ctx.Types.String])!);

        il.MarkLabel(endLabel);

        // Call runtime helper to create HMAC
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateHmac);
        return true;
    }

    private static bool EmitCreateCipheriv(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            // Missing arguments - throw at runtime
            il.Emit(OpCodes.Ldstr, "crypto.createCipheriv requires algorithm, key, and iv arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit algorithm argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Emit key argument - convert to byte[]
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        EmitConvertToByteArray(emitter);

        // Emit iv argument - convert to byte[]
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);
        EmitConvertToByteArray(emitter);

        // Call runtime helper to create cipher
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateCipheriv);
        return true;
    }

    private static bool EmitCreateDecipheriv(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            // Missing arguments - throw at runtime
            il.Emit(OpCodes.Ldstr, "crypto.createDecipheriv requires algorithm, key, and iv arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit algorithm argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Emit key argument - convert to byte[]
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        EmitConvertToByteArray(emitter);

        // Emit iv argument - convert to byte[]
        emitter.EmitExpression(arguments[2]);
        emitter.EmitBoxIfNeeded(arguments[2]);
        EmitConvertToByteArray(emitter);

        // Call runtime helper to create decipher
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateDecipheriv);
        return true;
    }

    /// <summary>
    /// Emits code to convert an object (string or $Buffer) to byte[].
    /// Expects the object on the stack; leaves byte[] on the stack.
    /// </summary>
    private static void EmitConvertToByteArray(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        var keyLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        var isStringLabel = il.DefineLabel();
        var isBufferLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's a string
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, ctx.Types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // Check if it's a $Buffer
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.TSBufferType);
        il.Emit(OpCodes.Brtrue, isBufferLabel);

        // Not a string or buffer - convert via ToString() and then UTF-8
        il.Emit(OpCodes.Call, ctx.Types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, ctx.Types.Encoding.GetMethod("GetBytes", [ctx.Types.String])!);
        il.Emit(OpCodes.Br, endLabel);

        // String - convert via UTF-8
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Call, ctx.Types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, ctx.Types.String);
        il.Emit(OpCodes.Callvirt, ctx.Types.Encoding.GetMethod("GetBytes", [ctx.Types.String])!);
        il.Emit(OpCodes.Br, endLabel);

        // Buffer - get data
        il.MarkLabel(isBufferLabel);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime.TSBufferType);
        il.Emit(OpCodes.Call, ctx.Runtime.TSBufferGetData);

        il.MarkLabel(endLabel);
    }

    private static bool EmitRandomBytes(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldc_I4, 16); // Default size
        }
        else
        {
            emitter.EmitExpressionAsDouble(arguments[0]);
            il.Emit(OpCodes.Conv_I4);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoRandomBytes);
        return true;
    }

    private static bool EmitRandomUUID(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Guid.NewGuid().ToString()
        var guidLocal = il.DeclareLocal(ctx.Types.Guid);
        il.Emit(OpCodes.Call, ctx.Types.GetMethodNoParams(ctx.Types.Guid, "NewGuid"));
        il.Emit(OpCodes.Stloc, guidLocal);
        il.Emit(OpCodes.Ldloca, guidLocal);
        il.Emit(OpCodes.Constrained, ctx.Types.Guid);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        return true;
    }

    private static bool EmitRandomInt(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Return 0 if no arguments (error case, but handle gracefully)
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }

        if (arguments.Count == 1)
        {
            // randomInt(max) - range [0, max)
            il.Emit(OpCodes.Ldc_I4_0);
            emitter.EmitExpressionAsDouble(arguments[0]);
            il.Emit(OpCodes.Conv_I4);
        }
        else
        {
            // randomInt(min, max) - range [min, max)
            emitter.EmitExpressionAsDouble(arguments[0]);
            il.Emit(OpCodes.Conv_I4);
            emitter.EmitExpressionAsDouble(arguments[1]);
            il.Emit(OpCodes.Conv_I4);
        }

        // Call RandomNumberGenerator.GetInt32(min, max)
        il.Emit(OpCodes.Call, ctx.Types.GetMethod(
            ctx.Types.RandomNumberGenerator,
            "GetInt32",
            ctx.Types.Int32, ctx.Types.Int32));

        // Convert to double for JS number
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }

    private static bool EmitPbkdf2Sync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 5)
        {
            il.Emit(OpCodes.Ldstr, "crypto.pbkdf2Sync requires password, salt, iterations, keylen, and digest arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit password - convert to byte[]
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        EmitConvertToByteArray(emitter);

        // Emit salt - convert to byte[]
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        EmitConvertToByteArray(emitter);

        // Emit iterations - convert to int
        emitter.EmitExpressionAsDouble(arguments[2]);
        il.Emit(OpCodes.Conv_I4);

        // Emit keylen - convert to int
        emitter.EmitExpressionAsDouble(arguments[3]);
        il.Emit(OpCodes.Conv_I4);

        // Emit digest - convert to string
        emitter.EmitExpression(arguments[4]);
        emitter.EmitBoxIfNeeded(arguments[4]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoPbkdf2Sync);
        return true;
    }

    private static bool EmitScryptSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 3)
        {
            il.Emit(OpCodes.Ldstr, "crypto.scryptSync requires password, salt, and keylen arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit password - convert to byte[]
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        EmitConvertToByteArray(emitter);

        // Emit salt - convert to byte[]
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        EmitConvertToByteArray(emitter);

        // Emit keylen - convert to int
        emitter.EmitExpressionAsDouble(arguments[2]);
        il.Emit(OpCodes.Conv_I4);

        // Emit options (or null if not provided)
        if (arguments.Count > 3)
        {
            emitter.EmitExpression(arguments[3]);
            emitter.EmitBoxIfNeeded(arguments[3]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoScryptSync);
        return true;
    }

    private static bool EmitTimingSafeEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count < 2)
        {
            il.Emit(OpCodes.Ldstr, "crypto.timingSafeEqual requires two arguments");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit first argument - convert to byte[]
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        EmitConvertToByteArray(emitter);

        // Emit second argument - convert to byte[]
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        EmitConvertToByteArray(emitter);

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoTimingSafeEqual);
        return true;
    }

    private static bool EmitCreateSign(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Default to sha256 if no algorithm specified
            il.Emit(OpCodes.Ldstr, "sha256");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        }

        // Call runtime helper to create Sign
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateSign);
        return true;
    }

    private static bool EmitCreateVerify(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            // Default to sha256 if no algorithm specified
            il.Emit(OpCodes.Ldstr, "sha256");
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        }

        // Call runtime helper to create Verify
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateVerify);
        return true;
    }

    private static bool EmitGetHashes(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper to get hashes array
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoGetHashes);
        return true;
    }

    private static bool EmitGetCiphers(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper to get ciphers array
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoGetCiphers);
        return true;
    }

    private static bool EmitGenerateKeyPairSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "crypto.generateKeyPairSync requires a key type argument");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit key type argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Emit options argument (or null if not provided)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoGenerateKeyPairSync);
        return true;
    }

    private static bool EmitCreateDiffieHellman(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "crypto.createDiffieHellman requires at least one argument");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit first argument (prime length or prime)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit second argument (generator, or null if not provided)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateDiffieHellman);
        return true;
    }

    private static bool EmitGetDiffieHellman(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "crypto.getDiffieHellman requires a group name");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit group name argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoGetDiffieHellman);
        return true;
    }

    private static bool EmitCreateECDH(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
        {
            il.Emit(OpCodes.Ldstr, "crypto.createECDH requires a curve name");
            il.Emit(OpCodes.Newobj, ctx.Types.ArgumentException.GetConstructor([ctx.Types.String])!);
            il.Emit(OpCodes.Throw);
            return true;
        }

        // Emit curve name argument (convert to string)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.CryptoCreateECDH);
        return true;
    }
}
