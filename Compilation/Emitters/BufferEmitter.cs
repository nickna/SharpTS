using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
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
            and not "writeUInt8" and not "toJSON"
            // Multi-byte reads
            and not "readInt8"
            and not "readUInt16LE" and not "readUInt16BE"
            and not "readUInt32LE" and not "readUInt32BE"
            and not "readInt16LE" and not "readInt16BE"
            and not "readInt32LE" and not "readInt32BE"
            and not "readFloatLE" and not "readFloatBE"
            and not "readDoubleLE" and not "readDoubleBE"
            and not "readBigInt64LE" and not "readBigInt64BE"
            and not "readBigUInt64LE" and not "readBigUInt64BE"
            // Multi-byte writes
            and not "writeInt8"
            and not "writeUInt16LE" and not "writeUInt16BE"
            and not "writeUInt32LE" and not "writeUInt32BE"
            and not "writeInt16LE" and not "writeInt16BE"
            and not "writeInt32LE" and not "writeInt32BE"
            and not "writeFloatLE" and not "writeFloatBE"
            and not "writeDoubleLE" and not "writeDoubleBE"
            and not "writeBigInt64LE" and not "writeBigInt64BE"
            and not "writeBigUInt64LE" and not "writeBigUInt64BE"
            // Search methods
            and not "indexOf" and not "includes"
            // Swap methods
            and not "swap16" and not "swap32" and not "swap64")
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

            // Multi-byte read methods
            case "readInt8":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadInt8);
                return true;

            case "readUInt16LE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadUInt16LE);
                return true;

            case "readUInt16BE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadUInt16BE);
                return true;

            case "readUInt32LE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadUInt32LE);
                return true;

            case "readUInt32BE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadUInt32BE);
                return true;

            case "readInt16LE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadInt16LE);
                return true;

            case "readInt16BE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadInt16BE);
                return true;

            case "readInt32LE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadInt32LE);
                return true;

            case "readInt32BE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadInt32BE);
                return true;

            case "readFloatLE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadFloatLE);
                return true;

            case "readFloatBE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadFloatBE);
                return true;

            case "readDoubleLE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadDoubleLE);
                return true;

            case "readDoubleBE":
                EmitReadMethod(emitter, arguments, ctx.Runtime!.TSBufferReadDoubleBE);
                return true;

            case "readBigInt64LE":
                EmitReadBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferReadBigInt64LE);
                return true;

            case "readBigInt64BE":
                EmitReadBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferReadBigInt64BE);
                return true;

            case "readBigUInt64LE":
                EmitReadBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferReadBigUInt64LE);
                return true;

            case "readBigUInt64BE":
                EmitReadBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferReadBigUInt64BE);
                return true;

            // Multi-byte write methods
            case "writeInt8":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteInt8);
                return true;

            case "writeUInt16LE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteUInt16LE);
                return true;

            case "writeUInt16BE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteUInt16BE);
                return true;

            case "writeUInt32LE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteUInt32LE);
                return true;

            case "writeUInt32BE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteUInt32BE);
                return true;

            case "writeInt16LE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteInt16LE);
                return true;

            case "writeInt16BE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteInt16BE);
                return true;

            case "writeInt32LE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteInt32LE);
                return true;

            case "writeInt32BE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteInt32BE);
                return true;

            case "writeFloatLE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteFloatLE);
                return true;

            case "writeFloatBE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteFloatBE);
                return true;

            case "writeDoubleLE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteDoubleLE);
                return true;

            case "writeDoubleBE":
                EmitWriteMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteDoubleBE);
                return true;

            case "writeBigInt64LE":
                EmitWriteBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteBigInt64LE);
                return true;

            case "writeBigInt64BE":
                EmitWriteBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteBigInt64BE);
                return true;

            case "writeBigUInt64LE":
                EmitWriteBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteBigUInt64LE);
                return true;

            case "writeBigUInt64BE":
                EmitWriteBigIntMethod(emitter, arguments, ctx.Runtime!.TSBufferWriteBigUInt64BE);
                return true;

            // Search methods
            case "indexOf":
                EmitIndexOfMethod(emitter, arguments);
                return true;

            case "includes":
                EmitIncludesMethod(emitter, arguments);
                return true;

            // Swap methods
            case "swap16":
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferSwap16);
                return true;

            case "swap32":
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferSwap32);
                return true;

            case "swap64":
                il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferSwap64);
                return true;

            default:
                // This should never be reached due to the early return above,
                // but included for safety
                throw new CompileException($"Unexpected method {methodName} - early check should have filtered this");
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

    #region Helper Methods for Multi-byte Operations

    /// <summary>
    /// Emits a read method call that takes offset and returns double.
    /// Stack: buffer -> result (boxed double)
    /// </summary>
    private void EmitReadMethod(IEmitterContext emitter, List<Expr> arguments, MethodBuilder method)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit offset argument (default 0)
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

        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>
    /// Emits a read method call that takes offset and returns BigInteger.
    /// Stack: buffer -> result (boxed BigInteger)
    /// </summary>
    private void EmitReadBigIntMethod(IEmitterContext emitter, List<Expr> arguments, MethodBuilder method)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit offset argument (default 0)
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

        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));
    }

    /// <summary>
    /// Emits a write method call that takes (value, offset) and returns double.
    /// Stack: buffer -> result (boxed double)
    /// </summary>
    private void EmitWriteMethod(IEmitterContext emitter, List<Expr> arguments, MethodBuilder method)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit value argument (required)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);
        il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);

        // Emit offset argument (default 0)
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

        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>
    /// Emits a BigInt write method call that takes (value, offset) and returns double.
    /// Stack: buffer -> result (boxed double)
    /// </summary>
    private void EmitWriteBigIntMethod(IEmitterContext emitter, List<Expr> arguments, MethodBuilder method)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit value argument (required) - keep as object for BigInt
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit offset argument (default 0)
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

        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>
    /// Emits indexOf method call that takes (value, byteOffset?, encoding?) and returns double.
    /// Stack: buffer -> result (boxed double)
    /// </summary>
    private void EmitIndexOfMethod(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit value argument (required)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit byteOffset argument (default 0)
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

        // Emit encoding argument (default "utf8")
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
            il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "utf8");
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferIndexOf);
        il.Emit(OpCodes.Box, ctx.Types.Double);
    }

    /// <summary>
    /// Emits includes method call that takes (value, byteOffset?, encoding?) and returns boolean.
    /// Stack: buffer -> result (boxed bool)
    /// </summary>
    private void EmitIncludesMethod(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit value argument (required)
        emitter.EmitExpression(arguments[0]);
        emitter.EmitBoxIfNeeded(arguments[0]);

        // Emit byteOffset argument (default 0)
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

        // Emit encoding argument (default "utf8")
        if (arguments.Count > 2)
        {
            emitter.EmitExpression(arguments[2]);
            emitter.EmitBoxIfNeeded(arguments[2]);
            il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "utf8");
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.TSBufferIncludes);
        il.Emit(OpCodes.Box, ctx.Types.Boolean);
    }

    #endregion
}
