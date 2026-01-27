using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Buffer instance method calls and property access.
/// Handles Buffer methods like toString, slice, copy, etc.
/// </summary>
public sealed class BufferEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a Buffer receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        // Check if we can handle this method BEFORE emitting anything
        // This prevents emitting the receiver when we can't handle the method,
        // which would leave stale values on the stack for union type fallback handling
        if (methodName is not "toString" and not "slice"
            and not "copy" and not "compare" and not "equals"
            and not "fill" and not "write" and not "readUInt8"
            and not "writeUInt8" and not "toJSON")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Cast to $Buffer
        il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);

        switch (methodName)
        {
            case "toString":
                // Get encoding argument (default "utf8")
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "utf8");
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferToString);
                return true;

            case "slice":
                // Start argument
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

                // End argument
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    // Default to length (we need to get it from the buffer on the stack)
                    // For simplicity, use Int32.MaxValue and let the Slice method handle it
                    il.Emit(OpCodes.Ldc_I4, int.MaxValue);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferSlice);
                return true;

            case "copy":
                // target (required), targetStart (default 0), sourceStart (default 0), sourceEnd (default length)
                // Emit target buffer
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);

                // targetStart (default 0)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                // sourceStart (default 0)
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                // sourceEnd (default length - use MaxValue and let method handle it)
                if (arguments.Count > 3)
                {
                    emitter.EmitExpression(arguments[3]);
                    emitter.EmitBoxIfNeeded(arguments[3]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4, int.MaxValue);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferCopy);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "compare":
                // other (required)
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferCompare);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "equals":
                // other (required)
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferEquals);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "fill":
                // value (required), start (default 0), end (default length), encoding (default "utf8")
                // Emit value
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);

                // start (default 0)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                // end (default length - use MaxValue)
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4, int.MaxValue);
                }

                // encoding (default "utf8")
                if (arguments.Count > 3)
                {
                    emitter.EmitExpression(arguments[3]);
                    emitter.EmitBoxIfNeeded(arguments[3]);
                    il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "utf8");
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferFill);
                return true;

            case "write":
                // data (required), offset (default 0), length (default -1 = use all), encoding (default "utf8")
                // Emit data string
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);

                // offset (default 0)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }

                // length (default -1 = no limit)
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_M1);
                }

                // encoding (default "utf8")
                if (arguments.Count > 3)
                {
                    emitter.EmitExpression(arguments[3]);
                    emitter.EmitBoxIfNeeded(arguments[3]);
                    il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "utf8");
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferWrite);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "readUInt8":
                // offset (default 0)
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
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferReadUInt8);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "writeUInt8":
                // value (required), offset (default 0)
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);

                // offset (default 0)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                    il.Emit(OpCodes.Conv_I4);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_I4_0);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferWriteUInt8);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "toJSON":
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferToJSON);
                return true;

            default:
                // This should never be reached due to the early return above,
                // but included for safety
                throw new InvalidOperationException($"Unexpected method {methodName} - early check should have filtered this");
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a Buffer receiver.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "length":
                // Emit the buffer object
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);

                // Cast to $Buffer and get Length
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSBufferType);
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferLengthGetter);
                il.Emit(OpCodes.Conv_R8);  // Convert to double for TypeScript number
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a Buffer receiver.
    /// Buffer properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }
}
