using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Buffer static method calls.
/// Handles Buffer.isBuffer() and the other directly lowered statics.
/// </summary>
public sealed class BufferStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Buffer static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "of":
                // Buffer.of(...bytes) — build a List<object?> of the args and call FromArray.
                var ofListLocal = il.DeclareLocal(ctx.Types.ListOfObject);
                il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(ctx.Types.ListOfObject));
                il.Emit(OpCodes.Stloc, ofListLocal);
                var ofAdd = ctx.Types.GetMethod(ctx.Types.ListOfObject, "Add", [ctx.Types.Object])!;
                foreach (var arg in arguments)
                {
                    il.Emit(OpCodes.Ldloc, ofListLocal);
                    emitter.EmitExpression(arg);
                    emitter.EmitBoxIfNeeded(arg);
                    il.Emit(OpCodes.Callvirt, ofAdd);
                }
                il.Emit(OpCodes.Ldloc, ofListLocal);
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().FromArray);
                return true;

            case "copyBytesFrom":
                // Buffer.copyBytesFrom(view[, offset[, length]])
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().CopyBytesFrom);
                return true;

            case "isBuffer":
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().IsBuffer);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "allocUnsafe":
            case "allocUnsafeSlow":
                // Size argument
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().AllocUnsafe);
                return true;

            case "concat":
                // Buffer.concat(buffers, totalLength?)
                // First argument: array of buffers - store in local for reuse
                var listLocal = il.DeclareLocal(ctx.Types.ListOfObject);
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);
                }
                else
                {
                    // Empty list
                    il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(ctx.Types.ListOfObject));
                }
                il.Emit(OpCodes.Stloc, listLocal);
                il.Emit(OpCodes.Ldloc, listLocal);

                // Second argument: totalLength (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    // Calculate total length from buffers using helper method
                    il.Emit(OpCodes.Ldloc, listLocal);
                    il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().CalculateTotalLength);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireBuffer().Concat);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Buffer.poolSize — informational only (SharpTS doesn't pool); emits the Node
    /// default of 8 KiB. Assignment isn't tracked in compiled mode.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "poolSize")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldc_R8, 8192.0);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }
}
