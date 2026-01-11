using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for WeakMap method calls.
/// Handles all TypeScript WeakMap methods: get, set, has, delete.
/// </summary>
public sealed class WeakMapEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a WeakMap receiver.
    /// </summary>
    public bool TryEmitMethodCall(ILEmitter emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the WeakMap object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "get":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapGet);
                return true;

            case "set":
                EmitSingleArgOrNull(emitter, arguments);
                EmitSecondArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapSet);
                return true;

            case "has":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapDelete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a WeakMap receiver.
    /// WeakMap doesn't have accessible properties.
    /// </summary>
    public bool TryEmitPropertyGet(ILEmitter emitter, Expr receiver, string propertyName)
    {
        // WeakMap doesn't expose properties
        return false;
    }

    #region Helper Methods

    private static void EmitSingleArgOrNull(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    private static void EmitSecondArgOrNull(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    #endregion
}
