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
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the WeakMap object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "get":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapGet);
                return true;

            case "set":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapSet);
                return true;

            case "has":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.WeakMapHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
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
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // WeakMap doesn't expose properties
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a WeakMap receiver.
    /// WeakMap properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

}
