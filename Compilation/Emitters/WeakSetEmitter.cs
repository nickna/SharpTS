using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for WeakSet method calls.
/// Handles all TypeScript WeakSet methods: add, has, delete.
/// </summary>
public sealed class WeakSetEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a WeakSet receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the WeakSet object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "add":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakSetAdd);
                return true;

            case "has":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakSetHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakSetDelete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a WeakSet receiver.
    /// WeakSet doesn't have accessible properties.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // WeakSet doesn't expose properties
        return false;
    }

    #region Helper Methods

    private static void EmitSingleArgOrNull(IEmitterContext emitter, List<Expr> arguments)
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

    #endregion
}
