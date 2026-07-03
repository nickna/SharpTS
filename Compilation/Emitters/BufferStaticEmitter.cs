using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Buffer static method calls.
/// Handles Buffer.from(), Buffer.alloc(), Buffer.isBuffer(), etc.
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
                var ofAdd = ctx.Types.ListOfObject.GetMethod("Add", [ctx.Types.Object])!;
                foreach (var arg in arguments)
                {
                    il.Emit(OpCodes.Ldloc, ofListLocal);
                    emitter.EmitExpression(arg);
                    emitter.EmitBoxIfNeeded(arg);
                    il.Emit(OpCodes.Callvirt, ofAdd);
                }
                il.Emit(OpCodes.Ldloc, ofListLocal);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferFromArray);
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
                il.Emit(OpCodes.Call, ctx.Runtime!.BufferCopyBytesFrom);
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
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferIsBuffer);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "from":
                // First argument: data (string, array, or buffer)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Check if it's a string - emit appropriate factory
                // For simplicity, emit $Buffer.FromString for string, else FromArray
                var isStringLabel = il.DefineLabel();
                var endLabel = il.DefineLabel();
                var notStringLabel = il.DefineLabel();

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, ctx.Types.String);
                il.Emit(OpCodes.Brfalse, notStringLabel);

                // String path: convert to string and call FromString
                il.Emit(OpCodes.Castclass, ctx.Types.String);

                // Second argument: encoding (default "utf8")
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    // Convert to string
                    il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "utf8");
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferFromString);
                il.Emit(OpCodes.Br, endLabel);

                // Non-string path: check for buffer, then array-like values
                il.MarkLabel(notStringLabel);

                // Check if it's a $Buffer - copy it
                var notBufferLabel2 = il.DefineLabel();

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, ctx.Runtime!.TSBufferType);
                il.Emit(OpCodes.Brfalse, notBufferLabel2);

                // Buffer path: cast and call FromBuffer
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferFromBuffer);
                il.Emit(OpCodes.Br, endLabel);

                il.MarkLabel(notBufferLabel2);

                // Buffer.from(arrayBuffer) (#1063): wrap the $ArrayBuffer backing array
                // (Node shares the memory; $Buffer wraps the same byte[]).
                if (ctx.Runtime!.ArrayBufferType is not null)
                {
                    var notArrayBufferLabel = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Isinst, ctx.Runtime!.ArrayBufferType);
                    il.Emit(OpCodes.Brfalse, notArrayBufferLabel);
                    il.Emit(OpCodes.Castclass, ctx.Runtime!.ArrayBufferType);
                    il.Emit(OpCodes.Callvirt, ctx.Runtime!.ArrayBufferGetBuffer);
                    il.Emit(OpCodes.Newobj, ctx.Runtime!.TSBufferCtor);
                    il.Emit(OpCodes.Br, endLabel);
                    il.MarkLabel(notArrayBufferLabel);
                }

                // Buffer.from(typedArray): copy the view window (Node copies here).
                if (ctx.Runtime!.TypedArrayBaseType is not null && ctx.Runtime!.BufferCopyBytesFrom is not null)
                {
                    var notTypedArrayLabel = il.DefineLabel();
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Isinst, ctx.Runtime!.TypedArrayBaseType);
                    il.Emit(OpCodes.Brfalse, notTypedArrayLabel);
                    il.Emit(OpCodes.Ldnull); // offset
                    il.Emit(OpCodes.Ldnull); // length
                    il.Emit(OpCodes.Call, ctx.Runtime!.BufferCopyBytesFrom);
                    il.Emit(OpCodes.Br, endLabel);
                    il.MarkLabel(notTypedArrayLabel);
                }

                // Check if it's already a List<object?> (array literal)
                var isListLabel = il.DefineLabel();
                var afterListCheckLabel = il.DefineLabel();

                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, ctx.Types.ListOfObject);
                il.Emit(OpCodes.Brtrue, isListLabel);

                // Not a list - use GetValues to convert object to array
                il.Emit(OpCodes.Call, ctx.Runtime!.GetValues);
                il.Emit(OpCodes.Br, afterListCheckLabel);

                // Already a list - cast directly
                il.MarkLabel(isListLabel);
                il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);

                il.MarkLabel(afterListCheckLabel);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferFromArray);

                il.MarkLabel(endLabel);
                return true;

            case "alloc":
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
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferAlloc);
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
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferAllocUnsafe);
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
                    il.Emit(OpCodes.Call, ctx.Runtime!.CalculateBuffersTotalLength);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferConcat);
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
