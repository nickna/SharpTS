using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region Multi-byte Read Methods

    /// <summary>
    /// Emits: public double ReadInt8(int offset)
    /// </summary>
    private void EmitTSBufferReadInt8(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadInt8",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadInt8 = method;

        var il = method.GetILGenerator();
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Bounds check: if (offset < 0) throw
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        // if (offset >= _data.Length) throw
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, okLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.ArgumentOutOfRangeExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);

        // return (double)(sbyte)_data[offset]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldelem_I1);  // Load as signed byte
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public double ReadUInt16LE(int offset)
    /// </summary>
    private void EmitTSBufferReadUInt16LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadUInt16LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadUInt16LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 2, false, false, false);  // unsigned, little-endian, 16-bit
    }

    /// <summary>
    /// Emits: public double ReadUInt16BE(int offset)
    /// </summary>
    private void EmitTSBufferReadUInt16BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadUInt16BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadUInt16BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 2, false, true, false);  // unsigned, big-endian, 16-bit
    }

    /// <summary>
    /// Emits: public double ReadUInt32LE(int offset)
    /// </summary>
    private void EmitTSBufferReadUInt32LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadUInt32LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadUInt32LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, false, false, false);  // unsigned, little-endian, 32-bit
    }

    /// <summary>
    /// Emits: public double ReadUInt32BE(int offset)
    /// </summary>
    private void EmitTSBufferReadUInt32BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadUInt32BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadUInt32BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, false, true, false);  // unsigned, big-endian, 32-bit
    }

    /// <summary>
    /// Emits: public double ReadInt16LE(int offset)
    /// </summary>
    private void EmitTSBufferReadInt16LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadInt16LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadInt16LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 2, true, false, false);  // signed, little-endian, 16-bit
    }

    /// <summary>
    /// Emits: public double ReadInt16BE(int offset)
    /// </summary>
    private void EmitTSBufferReadInt16BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadInt16BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadInt16BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 2, true, true, false);  // signed, big-endian, 16-bit
    }

    /// <summary>
    /// Emits: public double ReadInt32LE(int offset)
    /// </summary>
    private void EmitTSBufferReadInt32LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadInt32LE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadInt32LE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, true, false, false);  // signed, little-endian, 32-bit
    }

    /// <summary>
    /// Emits: public double ReadInt32BE(int offset)
    /// </summary>
    private void EmitTSBufferReadInt32BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadInt32BE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadInt32BE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, true, true, false);  // signed, big-endian, 32-bit
    }

    /// <summary>
    /// Emits: public double ReadFloatLE(int offset)
    /// </summary>
    private void EmitTSBufferReadFloatLE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadFloatLE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadFloatLE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, true, false, true);  // float, little-endian
    }

    /// <summary>
    /// Emits: public double ReadFloatBE(int offset)
    /// </summary>
    private void EmitTSBufferReadFloatBE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadFloatBE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadFloatBE = method;

        var il = method.GetILGenerator();
        EmitMultiByteRead(il, 4, true, true, true);  // float, big-endian
    }

    /// <summary>
    /// Emits: public double ReadDoubleLE(int offset)
    /// </summary>
    private void EmitTSBufferReadDoubleLE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadDoubleLE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadDoubleLE = method;

        var il = method.GetILGenerator();
        EmitDoubleRead(il, false);  // little-endian
    }

    /// <summary>
    /// Emits: public double ReadDoubleBE(int offset)
    /// </summary>
    private void EmitTSBufferReadDoubleBE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadDoubleBE",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadDoubleBE = method;

        var il = method.GetILGenerator();
        EmitDoubleRead(il, true);  // big-endian
    }

    /// <summary>
    /// Emits: public BigInteger ReadBigInt64LE(int offset)
    /// </summary>
    private void EmitTSBufferReadBigInt64LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadBigInt64LE",
            MethodAttributes.Public,
            typeof(System.Numerics.BigInteger),
            [_types.Int32]
        );
        runtime.TSBufferReadBigInt64LE = method;

        var il = method.GetILGenerator();
        EmitBigIntRead(il, true, false);  // signed, little-endian
    }

    /// <summary>
    /// Emits: public BigInteger ReadBigInt64BE(int offset)
    /// </summary>
    private void EmitTSBufferReadBigInt64BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadBigInt64BE",
            MethodAttributes.Public,
            typeof(System.Numerics.BigInteger),
            [_types.Int32]
        );
        runtime.TSBufferReadBigInt64BE = method;

        var il = method.GetILGenerator();
        EmitBigIntRead(il, true, true);  // signed, big-endian
    }

    /// <summary>
    /// Emits: public BigInteger ReadBigUInt64LE(int offset)
    /// </summary>
    private void EmitTSBufferReadBigUInt64LE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadBigUInt64LE",
            MethodAttributes.Public,
            typeof(System.Numerics.BigInteger),
            [_types.Int32]
        );
        runtime.TSBufferReadBigUInt64LE = method;

        var il = method.GetILGenerator();
        EmitBigIntRead(il, false, false);  // unsigned, little-endian
    }

    /// <summary>
    /// Emits: public BigInteger ReadBigUInt64BE(int offset)
    /// </summary>
    private void EmitTSBufferReadBigUInt64BE(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadBigUInt64BE",
            MethodAttributes.Public,
            typeof(System.Numerics.BigInteger),
            [_types.Int32]
        );
        runtime.TSBufferReadBigUInt64BE = method;

        var il = method.GetILGenerator();
        EmitBigIntRead(il, false, true);  // unsigned, big-endian
    }

    /// <summary>
    /// Helper to emit multi-byte read logic for 16/32-bit integers and floats.
    /// </summary>
    private void EmitMultiByteRead(ILGenerator il, int byteCount, bool signed, bool bigEndian, bool isFloat)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Bounds check: offset < 0 || offset > _data.Length - byteCount
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
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

        if (byteCount == 2)
        {
            // Read 2 bytes manually
            EmitRead2Bytes(il, signed, bigEndian);
        }
        else if (byteCount == 4)
        {
            // Read 4 bytes manually
            EmitRead4Bytes(il, signed, bigEndian, isFloat);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitRead2Bytes(ILGenerator il, bool signed, bool bigEndian)
    {
        // byte0 = _data[offset], byte1 = _data[offset + 1]
        // LE: result = byte0 | (byte1 << 8)
        // BE: result = (byte0 << 8) | byte1

        if (bigEndian)
        {
            // (byte0 << 8) | byte1
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Or);
        }
        else
        {
            // byte0 | (byte1 << 8)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelem_U1);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
        }

        if (signed)
        {
            il.Emit(OpCodes.Conv_I2);  // Sign extend to short
        }
        il.Emit(OpCodes.Conv_R8);
    }

    private void EmitRead4Bytes(ILGenerator il, bool signed, bool bigEndian, bool isFloat)
    {
        // Read 4 bytes and combine
        var b0Local = il.DeclareLocal(_types.Int32);
        var b1Local = il.DeclareLocal(_types.Int32);
        var b2Local = il.DeclareLocal(_types.Int32);
        var b3Local = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.Int32);

        // Load all 4 bytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, b0Local);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, b1Local);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, b2Local);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, b3Local);

        // Combine based on endianness
        if (bigEndian)
        {
            // (b0 << 24) | (b1 << 16) | (b2 << 8) | b3
            il.Emit(OpCodes.Ldloc, b0Local);
            il.Emit(OpCodes.Ldc_I4, 24);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Ldloc, b1Local);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Ldloc, b2Local);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Ldloc, b3Local);
            il.Emit(OpCodes.Or);
        }
        else
        {
            // b0 | (b1 << 8) | (b2 << 16) | (b3 << 24)
            il.Emit(OpCodes.Ldloc, b0Local);
            il.Emit(OpCodes.Ldloc, b1Local);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Ldloc, b2Local);
            il.Emit(OpCodes.Ldc_I4, 16);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
            il.Emit(OpCodes.Ldloc, b3Local);
            il.Emit(OpCodes.Ldc_I4, 24);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Or);
        }

        if (isFloat)
        {
            // Convert int32 bits to float, then to double
            il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("Int32BitsToSingle", [typeof(int)])!);
            il.Emit(OpCodes.Conv_R8);
        }
        else if (!signed)
        {
            // Unsigned 32-bit - convert to long first to avoid sign issues
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Conv_R8);
        }
        else
        {
            il.Emit(OpCodes.Conv_R8);
        }
    }

    private void EmitDoubleRead(ILGenerator il, bool bigEndian)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Bounds check
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
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

        // Read 8 bytes manually
        var bytesLocal = new LocalBuilder[8];
        for (int i = 0; i < 8; i++)
        {
            bytesLocal[i] = il.DeclareLocal(_types.Int64);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            if (i > 0)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stloc, bytesLocal[i]);
        }

        // Combine based on endianness
        if (bigEndian)
        {
            il.Emit(OpCodes.Ldloc, bytesLocal[0]);
            il.Emit(OpCodes.Ldc_I4, 56);
            il.Emit(OpCodes.Shl);
            for (int i = 1; i < 8; i++)
            {
                il.Emit(OpCodes.Ldloc, bytesLocal[i]);
                il.Emit(OpCodes.Ldc_I4, 56 - i * 8);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Or);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldloc, bytesLocal[0]);
            for (int i = 1; i < 8; i++)
            {
                il.Emit(OpCodes.Ldloc, bytesLocal[i]);
                il.Emit(OpCodes.Ldc_I4, i * 8);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Or);
            }
        }

        // Convert to double
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("Int64BitsToDouble", [typeof(long)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBigIntRead(ILGenerator il, bool signed, bool bigEndian)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Bounds check
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
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

        // Read 8 bytes and combine into int64/uint64
        var bytesLocal = new LocalBuilder[8];
        for (int i = 0; i < 8; i++)
        {
            bytesLocal[i] = il.DeclareLocal(_types.Int64);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsBufferDataField);
            il.Emit(OpCodes.Ldarg_1);
            if (i > 0)
            {
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stloc, bytesLocal[i]);
        }

        // Combine based on endianness
        if (bigEndian)
        {
            il.Emit(OpCodes.Ldloc, bytesLocal[0]);
            il.Emit(OpCodes.Ldc_I4, 56);
            il.Emit(OpCodes.Shl);
            for (int i = 1; i < 8; i++)
            {
                il.Emit(OpCodes.Ldloc, bytesLocal[i]);
                il.Emit(OpCodes.Ldc_I4, 56 - i * 8);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Or);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldloc, bytesLocal[0]);
            for (int i = 1; i < 8; i++)
            {
                il.Emit(OpCodes.Ldloc, bytesLocal[i]);
                il.Emit(OpCodes.Ldc_I4, i * 8);
                il.Emit(OpCodes.Shl);
                il.Emit(OpCodes.Or);
            }
        }

        // Convert to BigInteger
        if (signed)
        {
            // new BigInteger(long)
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(long)])!);
        }
        else
        {
            // new BigInteger(ulong)
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(ulong)])!);
        }

        il.Emit(OpCodes.Ret);
    }

    #endregion
}
