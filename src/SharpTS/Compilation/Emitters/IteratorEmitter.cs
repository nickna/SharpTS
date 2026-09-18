using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Iterator method calls (ES2025 Iterator Helpers).
/// Handles lazy methods (map, filter, take, drop, flatMap) and eager methods
/// (reduce, toArray, forEach, some, every, find), plus next()/return() protocol.
/// </summary>
public sealed class IteratorEmitter : ITypeEmitterStrategy
{
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "map":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Map);
                return true;

            case "filter":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Filter);
                return true;

            case "take":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitIntArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Take);
                return true;

            case "drop":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitIntArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Drop);
                return true;

            case "flatMap":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.FlatMap);
                return true;

            case "reduce":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                // callback
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                // initial value (or null)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Ldc_I4_1); // hasInitial = true
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ldc_I4_0); // hasInitial = false
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Reduce);
                return true;

            case "toArray":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.ToArray);
                return true;

            case "forEach":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.ForEach);
                return true;

            case "some":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Some);
                return true;

            case "every":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Every);
                return true;

            case "find":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0);
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Find);
                return true;

            case "next":
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                // The value passed to next(v) is delivered to the resumed yield for
                // generators (#452); a bare next() resumes with undefined. Plain
                // iterators ignore it.
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.Sentinels.UndefinedInstance);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.IteratorHelpers.Next);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        return false;
    }

    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    private static void EmitIntArg(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            // Convert object (boxed double) to int
            il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
            il.Emit(OpCodes.Conv_I4);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
    }
}
