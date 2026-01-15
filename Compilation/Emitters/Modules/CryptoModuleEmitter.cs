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
        "createHash", "randomBytes", "randomUUID", "randomInt"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "createHash" => EmitCreateHash(emitter, arguments),
            "randomBytes" => EmitRandomBytes(emitter, arguments),
            "randomUUID" => EmitRandomUUID(emitter),
            "randomInt" => EmitRandomInt(emitter, arguments),
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
            ctx.Types.Resolve("System.Security.Cryptography.RandomNumberGenerator"),
            "GetInt32",
            ctx.Types.Int32, ctx.Types.Int32));

        // Convert to double for JS number
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }
}
