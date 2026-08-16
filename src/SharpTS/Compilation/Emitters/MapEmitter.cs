using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Map method calls and property access.
/// Handles all TypeScript Map methods like get, set, has, delete, clear, keys, values, entries, forEach.
/// </summary>
public sealed class MapEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a Map receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Map object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "get":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.MapGet);
                return true;

            case "set":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1);
                il.Emit(OpCodes.Call, ctx.Runtime!.MapSet);
                return true;

            case "has":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.MapHas);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "delete":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.MapDelete);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "clear":
                il.Emit(OpCodes.Call, ctx.Runtime!.MapClear);
                il.Emit(OpCodes.Ldnull); // clear returns undefined
                return true;

            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.MapKeys);
                return true;

            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.MapValues);
                return true;

            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.MapEntries);
                return true;

            case "forEach":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.MapForEach);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a Map receiver.
    /// Handles the 'size' property.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        if (propertyName != "size")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        il.Emit(OpCodes.Call, ctx.Runtime!.MapSize);
        il.Emit(OpCodes.Box, ctx.Types.Double);

        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a Map receiver.
    /// Map properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

}
