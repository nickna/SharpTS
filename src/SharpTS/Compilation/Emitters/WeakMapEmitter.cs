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
        if (methodName is not ("get" or "set" or "has" or "delete"))
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the WeakMap object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Registered spills keep the receiver and earlier arguments across MoveNext re-entry.
        LocalBuilder[]? argumentLocals = null;
        if (emitter.ArgsContainSuspension(arguments))
        {
            var receiverLocal = emitter.SpillStackToObjectLocal();
            argumentLocals = new LocalBuilder[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
                argumentLocals[i] = emitter.SpillBoxedArg(arguments[i]);
            il.Emit(OpCodes.Ldloc, receiverLocal);
        }

        switch (methodName)
        {
            case "get":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argumentLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireWeakMap().Get);
                return true;

            case "set":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argumentLocals);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1, argumentLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireWeakMap().Set);
                return true;

            case "has":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argumentLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireWeakMap().Has);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argumentLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireWeakMap().Delete);
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
