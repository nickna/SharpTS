using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region Multi-byte Write Methods

    /// <summary>
    /// Emits: public double WriteInt8(double value, int offset)
    /// </summary>
    private void EmitTSBufferWriteInt8(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteInt8",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteInt8 = method;

        var il = method.GetILGenerator();
        EmitWriteBoundsCheck(il, 1);

        // _data[offset] = (byte)(sbyte)value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_2);  // offset
        il.Emit(OpCodes.Ldarg_1);  // value
        il.Emit(OpCodes.Conv_I1);  // to sbyte
        il.Emit(OpCodes.Stelem_I1);

        // return offset + 1
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWriteBoundsCheck(ILGenerator il, int byteCount)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // if (offset < 0) throw
        il.Emit(OpCodes.Ldarg_2);  // offset is arg2 for write methods
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        // if (offset > _data.Length - byteCount) throw
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, byteCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ble, okLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.ArgumentOutOfRangeExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits: public double WriteUInt16LE(double value, int offset)
    /// </summary>
    private void EmitTSBufferWriteUInt16LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteUInt16LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteUInt16LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 2, false);  // 16-bit, little-endian
    }

    private void EmitTSBufferWriteUInt16BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteUInt16BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteUInt16BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 2, true);  // 16-bit, big-endian
    }

    private void EmitTSBufferWriteUInt32LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteUInt32LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteUInt32LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 4, false);  // 32-bit, little-endian
    }

    private void EmitTSBufferWriteUInt32BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteUInt32BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteUInt32BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 4, true);  // 32-bit, big-endian
    }

    private void EmitTSBufferWriteInt16LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteInt16LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteInt16LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 2, false);  // 16-bit, little-endian
    }

    private void EmitTSBufferWriteInt16BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteInt16BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteInt16BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 2, true);  // 16-bit, big-endian
    }

    private void EmitTSBufferWriteInt32LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteInt32LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteInt32LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 4, false);  // 32-bit, little-endian
    }

    private void EmitTSBufferWriteInt32BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteInt32BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteInt32BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteWrite(il, 4, true);  // 32-bit, big-endian
    }

    private void EmitTSBufferWriteFloatLE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteFloatLE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteFloatLE = method;

        var il = method.GetILGenerator();
        EmitFloatWrite(il, false);  // little-endian
    }

    private void EmitTSBufferWriteFloatBE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteFloatBE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteFloatBE = method;

        var il = method.GetILGenerator();
        EmitFloatWrite(il, true);  // big-endian
    }

    private void EmitTSBufferWriteDoubleLE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteDoubleLE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteDoubleLE = method;

        var il = method.GetILGenerator();
        EmitDoubleWrite(il, false);  // little-endian
    }

    private void EmitTSBufferWriteDoubleBE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteDoubleBE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteDoubleBE = method;

        var il = method.GetILGenerator();
        EmitDoubleWrite(il, true);  // big-endian
    }

    private void EmitTSBufferWriteBigInt64LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteBigInt64LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32]
        );
        runtime.TSBufferWriteBigInt64LE = method;

        var il = method.GetILGenerator();
        EmitBigIntWrite(il, true, false);  // signed, little-endian
    }

    private void EmitTSBufferWriteBigInt64BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteBigInt64BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32]
        );
        runtime.TSBufferWriteBigInt64BE = method;

        var il = method.GetILGenerator();
        EmitBigIntWrite(il, true, true);  // signed, big-endian
    }

    private void EmitTSBufferWriteBigUInt64LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteBigUInt64LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32]
        );
        runtime.TSBufferWriteBigUInt64LE = method;

        var il = method.GetILGenerator();
        EmitBigIntWrite(il, false, false);  // unsigned, little-endian
    }

    private void EmitTSBufferWriteBigUInt64BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteBigUInt64BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32]
        );
        runtime.TSBufferWriteBigUInt64BE = method;

        var il = method.GetILGenerator();
        EmitBigIntWrite(il, false, true);  // unsigned, big-endian
    }

    private void EmitMultiByteWrite(ILGenerator il, int byteCount, bool bigEndian)
    {
        EmitWriteBoundsCheck(il, byteCount);

        var valueLocal = il.DeclareLocal(_types.Int32);

        // Convert value to int
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, valueLocal);

        if (byteCount == 2)
        {
            if (bigEndian)
            {
                // _data[offset] = (byte)(value >> 8)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _tsBufferDataField);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Ldc_I4_8);
                il.Emit(OpCodes.Shr);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Stelem_I1);

                // _data[offset + 1] = (byte)value
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _tsBufferDataField);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Stelem_I1);
            }
            else
            {
                // _data[offset] = (byte)value
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _tsBufferDataField);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Stelem_I1);

                // _data[offset + 1] = (byte)(value >> 8)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _tsBufferDataField);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Ldc_I4_8);
                il.Emit(OpCodes.Shr);
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Stelem_I1);
            }
        }
        else // byteCount == 4
        {
            int[] shifts = bigEndian ? [24, 16, 8, 0] : [0, 8, 16, 24];
            for (int i = 0; i < 4; i++)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, _tsBufferDataField);
                il.Emit(OpCodes.Ldarg_2);
                if (i > 0)
                {
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Add);
                }
                il.Emit(OpCodes.Ldloc, valueLocal);
                if (shifts[i] > 0)
                {
                    il.Emit(OpCodes.Ldc_I4, shifts[i]);
                    il.Emit(OpCodes.Shr);
                }
                il.Emit(OpCodes.Conv_U1);
                il.Emit(OpCodes.Stelem_I1);
            }
        }

        // return offset + byteCount
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, byteCount);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitFloatWrite(ILGenerator il, bool bigEndian)
    {
        EmitWriteBoundsCheck(il, 4);

        var valueLocal = il.DeclareLocal(_types.Int32);

        // Convert double to float, then to int bits
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_R4);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("SingleToInt32Bits", [typeof(float)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        int[] shifts = bigEndian ? [24, 16, 8, 0] : [0, 8, 16, 24];
        for (int i = 0; i < 4; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_2);
            if (i > 0)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldloc, valueLocal);
            if (shifts[i] > 0)
            {
                il.Emit(OpCodes.Ldc_I4, shifts[i]);
                il.Emit(OpCodes.Shr);
            }
            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Stelem_I1);
        }

        // return offset + 4
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDoubleWrite(ILGenerator il, bool bigEndian)
    {
        EmitWriteBoundsCheck(il, 8);

        var valueLocal = il.DeclareLocal(_types.Int64);

        // Convert double to int64 bits
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("DoubleToInt64Bits", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        int[] shifts = bigEndian ? [56, 48, 40, 32, 24, 16, 8, 0] : [0, 8, 16, 24, 32, 40, 48, 56];
        for (int i = 0; i < 8; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_2);
            if (i > 0)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldloc, valueLocal);
            if (shifts[i] > 0)
            {
                il.Emit(OpCodes.Ldc_I4, shifts[i]);
                il.Emit(OpCodes.Shr);
            }
            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Stelem_I1);
        }

        // return offset + 8
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBigIntWrite(ILGenerator il, bool signed, bool bigEndian)
    {
        EmitBigIntWriteBoundsCheck(il);

        var valueLocal = il.DeclareLocal(_types.Int64);

        // Convert BigInteger or double to long
        var isBigIntLabel = il.DefineLabel();
        var afterConvertLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, isBigIntLabel);

        // It's a double, convert to long
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        il.MarkLabel(isBigIntLabel);
        // It's a BigInteger, convert to long
        // Find the specific op_Explicit that converts BigInteger to long (there are multiple overloads)
        var bigIntToLongMethod = typeof(System.Numerics.BigInteger)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "op_Explicit" && m.ReturnType == typeof(long) &&
                   m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Call, bigIntToLongMethod);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(afterConvertLabel);

        int[] shifts = bigEndian ? [56, 48, 40, 32, 24, 16, 8, 0] : [0, 8, 16, 24, 32, 40, 48, 56];
        for (int i = 0; i < 8; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_2);
            if (i > 0)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldloc, valueLocal);
            if (shifts[i] > 0)
            {
                il.Emit(OpCodes.Ldc_I4, shifts[i]);
                il.Emit(OpCodes.Shr);
            }
            il.Emit(OpCodes.Conv_U1);
            il.Emit(OpCodes.Stelem_I1);
        }

        // return offset + 8
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBigIntWriteBoundsCheck(ILGenerator il)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // BigInt writes have offset as arg2 (after object value)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ble, okLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.ArgumentOutOfRangeExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
    }

    #endregion
}
