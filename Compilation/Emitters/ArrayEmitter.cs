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
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the array object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Handle both List<object> and $Array types
        // For $Array, extract the Elements property
        EmitGetListFromArrayOrList(il, ctx);

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

            case "flat":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFlat);
                return true;

            case "flatMap":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFlatMap);
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

            case "sort":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySort);
                return true;

            case "toSorted":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToSorted);
                return true;

            case "splice":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySplice);
                return true;

            case "toSpliced":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToSpliced);
                return true;

            case "findLast":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindLast);
                return true;

            case "findLastIndex":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindLastIndex);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "toReversed":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToReversed);
                return true;

            case "with":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayWith);
                return true;

            case "at":
                EmitSingleArgOrNull(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayAt);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an array receiver.
    /// Currently not implemented - array.length is handled by the runtime.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // Array length is handled by the runtime GetProperty method
        // TODO: Could optimize by directly accessing List<object>.Count
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on an array receiver.
    /// Array properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits code to convert an array value (either List&lt;object&gt; or $Array) to List&lt;object&gt;.
    /// The value is expected to be on the stack; leaves List&lt;object&gt; on the stack.
    /// </summary>
    private static void EmitGetListFromArrayOrList(ILGenerator il, CompilationContext ctx)
    {
        var objLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, objLocal);

        var isListLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's already a List<object>
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Not a List - assume it's $Array, get Elements
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Callvirt, ctx.Runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Br, endLabel);

        // Already a List - cast it
        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits a single argument or null if no arguments provided.
    /// </summary>
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

    /// <summary>
    /// Emits all arguments as an object array.
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments)
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
