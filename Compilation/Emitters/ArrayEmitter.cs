using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for array method calls and property access.
/// Handles all TypeScript array methods like push, pop, map, filter, etc.
/// </summary>
public sealed class ArrayEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an array receiver.
    /// </summary>
    public bool TryEmitMethodCall(ILEmitter emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the array object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Cast to List<object>
        il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);

        switch (methodName)
        {
            case "pop":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayPop);
                return true;

            case "shift":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayShift);
                return true;

            case "unshift":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayUnshift);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "push":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayPush);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "slice":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySlice);
                return true;

            case "map":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayMap);
                return true;

            case "filter":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFilter);
                return true;

            case "forEach":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayForEach);
                il.Emit(OpCodes.Ldnull); // forEach returns undefined
                return true;

            case "find":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFind);
                return true;

            case "findIndex":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindIndex);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "some":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySome);
                return true;

            case "every":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayEvery);
                return true;

            case "reduce":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayReduce);
                return true;

            case "join":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayJoin);
                return true;

            case "concat":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayConcat);
                return true;

            case "reverse":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayReverse);
                return true;

            case "includes":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayIncludes);
                return true;

            case "indexOf":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayIndexOf);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an array receiver.
    /// Currently not implemented - array.length is handled by the runtime.
    /// </summary>
    public bool TryEmitPropertyGet(ILEmitter emitter, Expr receiver, string propertyName)
    {
        // Array length is handled by the runtime GetProperty method
        // TODO: Could optimize by directly accessing List<object>.Count
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits a single argument or null if no arguments provided.
    /// </summary>
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

    /// <summary>
    /// Emits all arguments as an object array.
    /// </summary>
    private static void EmitArgsArray(ILEmitter emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    #endregion
}
