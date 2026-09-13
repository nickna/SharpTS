using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for DataView instance method calls and property access.
/// Handles DataView getter/setter methods (getInt8, setInt16, etc.) and properties
/// (buffer, byteOffset, byteLength).
/// </summary>
public sealed class DataViewEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a DataView receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        // Check if we can handle this method BEFORE emitting anything
        if (!IsDataViewMethod(methodName))
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            // 8-bit getters (no endianness)
            case "getInt8":
            case "getUint8":
                EmitByteOffsetArg(emitter, arguments, 0);
                il.Emit(OpCodes.Call, GetDataViewMethod(ctx.Runtime!.RequireDataView(), methodName));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // 8-bit setters (no endianness)
            case "setInt8":
            case "setUint8":
                EmitByteOffsetArg(emitter, arguments, 0);
                EmitValueArg(emitter, arguments, 1);
                il.Emit(OpCodes.Call, GetDataViewMethod(ctx.Runtime!.RequireDataView(), methodName));
                return true;

            // 16-bit, 32-bit, float getters (with endianness)
            case "getInt16":
            case "getUint16":
            case "getInt32":
            case "getUint32":
            case "getFloat32":
            case "getFloat64":
                EmitByteOffsetArg(emitter, arguments, 0);
                EmitLittleEndianArg(emitter, arguments, 1);
                il.Emit(OpCodes.Call, GetDataViewMethod(ctx.Runtime!.RequireDataView(), methodName));
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // BigInt getters (with endianness) - returns object (already boxed BigInteger)
            case "getBigInt64":
            case "getBigUint64":
                EmitByteOffsetArg(emitter, arguments, 0);
                EmitLittleEndianArg(emitter, arguments, 1);
                il.Emit(OpCodes.Call, GetDataViewMethod(ctx.Runtime!.RequireDataView(), methodName));
                return true;

            // 16-bit, 32-bit, float setters (with endianness)
            case "setInt16":
            case "setUint16":
            case "setInt32":
            case "setUint32":
            case "setFloat32":
            case "setFloat64":
            case "setBigInt64":
            case "setBigUint64":
                EmitByteOffsetArg(emitter, arguments, 0);
                EmitValueArg(emitter, arguments, 1);
                EmitLittleEndianArg(emitter, arguments, 2);
                il.Emit(OpCodes.Call, GetDataViewMethod(ctx.Runtime!.RequireDataView(), methodName));
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a DataView receiver.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // Check if we can handle this property BEFORE emitting anything
        if (propertyName is not ("buffer" or "byteOffset" or "byteLength"))
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (propertyName)
        {
            case "buffer":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireDataView().GetBuffer);
                return true;

            case "byteOffset":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireDataView().GetByteOffset);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "byteLength":
                il.Emit(OpCodes.Call, ctx.Runtime!.RequireDataView().GetByteLength);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;
        }

        return false;
    }

    /// <summary>
    /// DataView doesn't have settable properties.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        // DataView has no settable properties
        return false;
    }

    private static bool IsDataViewMethod(string methodName)
    {
        return methodName is "getInt8" or "getUint8" or "getInt16" or "getUint16"
            or "getInt32" or "getUint32" or "getFloat32" or "getFloat64"
            or "getBigInt64" or "getBigUint64"
            or "setInt8" or "setUint8" or "setInt16" or "setUint16"
            or "setInt32" or "setUint32" or "setFloat32" or "setFloat64"
            or "setBigInt64" or "setBigUint64";
    }

    private static void EmitByteOffsetArg(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var il = emitter.Context.IL;
        if (arguments.Count > index)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
            il.Emit(OpCodes.Unbox_Any, emitter.Context.Types.Double);
            il.Emit(OpCodes.Conv_I4);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
    }

    private static void EmitValueArg(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var il = emitter.Context.IL;
        if (arguments.Count > index)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    private static void EmitLittleEndianArg(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var il = emitter.Context.IL;
        if (arguments.Count > index)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
            il.Emit(OpCodes.Unbox_Any, emitter.Context.Types.Boolean);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0); // false - big-endian by default
        }
    }

    private static MethodBuilder GetDataViewMethod(EmittedDataViewRuntime view, string methodName)
    {
        return methodName switch
        {
            "getInt8" => view.GetInt8Object,
            "getUint8" => view.GetUint8Object,
            "getInt16" => view.GetInt16Object,
            "getUint16" => view.GetUint16Object,
            "getInt32" => view.GetInt32Object,
            "getUint32" => view.GetUint32Object,
            "getFloat32" => view.GetFloat32Object,
            "getFloat64" => view.GetFloat64Object,
            "getBigInt64" => view.GetBigInt64Object,
            "getBigUint64" => view.GetBigUint64Object,
            "setInt8" => view.SetInt8Object,
            "setUint8" => view.SetUint8Object,
            "setInt16" => view.SetInt16Object,
            "setUint16" => view.SetUint16Object,
            "setInt32" => view.SetInt32Object,
            "setUint32" => view.SetUint32Object,
            "setFloat32" => view.SetFloat32Object,
            "setFloat64" => view.SetFloat64Object,
            "setBigInt64" => view.SetBigInt64Object,
            "setBigUint64" => view.SetBigUint64Object,
            _ => throw new Exception($"Unknown DataView method: {methodName}")
        };
    }
}
