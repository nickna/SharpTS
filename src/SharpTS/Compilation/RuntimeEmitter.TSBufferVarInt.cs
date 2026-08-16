using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>double Name(int offset, int byteLength)</c> — a variable-length (1-6 byte)
    /// integer read, little- or big-endian, optionally sign-extended. Mirrors
    /// SharpTSBuffer.ReadUIntLE/BE / ReadIntLE/BE so interpreter and compiled agree.
    /// </summary>
    private void EmitTSBufferVarIntRead(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, bool bigEndian, bool signed, Action<MethodBuilder> store)
    {
        var method = typeBuilder.DefineMethod(
            name, MethodAttributes.Public, _types.Double, [_types.Int32, _types.Int32]);
        store(method);

        var il = method.GetILGenerator();
        EmitVarIntBoundsCheck(il);

        var valLocal = il.DeclareLocal(_types.Int64);
        var iLocal = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();

        // long val = 0; int i = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, valLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        // if (i >= byteLength) break
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Bge, endLoop);

        // byteVal = (long)(byte)_data[offset + i]  (zero-extended)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Conv_U8);
        // shift amount: LE -> 8*i ; BE -> 8*(byteLength-1-i)
        if (bigEndian)
        {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, iLocal);
        }
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Shl);
        // val |= ...
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, valLocal);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(endLoop);

        if (signed)
        {
            // int bits = byteLength * 8
            var bitsLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, bitsLocal);
            // long signBit = 1L << (bits - 1)
            var signBitLocal = il.DeclareLocal(_types.Int64);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldloc, bitsLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Stloc, signBitLocal);
            // if ((val & signBit) != 0) val -= (1L << bits)
            var noSign = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Ldloc, signBitLocal);
            il.Emit(OpCodes.And);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Beq, noSign);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldloc, bitsLocal);
            il.Emit(OpCodes.Shl);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, valLocal);
            il.MarkLabel(noSign);
        }

        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>double Name(double value, int offset, int byteLength)</c> — a variable-length
    /// (1-6 byte) integer write, little- or big-endian. Returns offset + byteLength.
    /// </summary>
    private void EmitTSBufferVarIntWrite(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, bool bigEndian, Action<MethodBuilder> store)
    {
        var method = typeBuilder.DefineMethod(
            name, MethodAttributes.Public, _types.Double, [_types.Double, _types.Int32, _types.Int32]);
        store(method);

        var il = method.GetILGenerator();
        EmitVarIntBoundsCheck(il, valueArg: true);

        // long v = (long)value
        var vLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, vLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();

        // i = bigEndian ? byteLength - 1 : 0
        if (bigEndian)
        {
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
        }
        else
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loop);
        // LE: while (i < byteLength) ; BE: while (i >= 0)
        if (bigEndian)
        {
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, endLoop);
        }
        else
        {
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Bge, endLoop);
        }

        // _data[offset + i] = (byte)(v & 0xFF)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        // v >>= 8
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Stloc, vLocal);
        // i += bigEndian ? -1 : 1
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, bigEndian ? -1 : 1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(endLoop);
        // return offset + byteLength
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Validates byteLength ∈ [1,6] and offset ∈ [0, len - byteLength]. For writes the
    /// value is arg1, so offset/byteLength shift to args 2/3.
    /// </summary>
    private void EmitVarIntBoundsCheck(ILGenerator il, bool valueArg = false)
    {
        int offsetArg = valueArg ? 2 : 1;
        int byteLengthArg = valueArg ? 3 : 2;

        var blOk = il.DefineLabel();
        var blThrow = il.DefineLabel();
        var offOk = il.DefineLabel();
        var offThrow = il.DefineLabel();

        // byteLength in [1,6]
        il.Emit(OpCodes.Ldarg, byteLengthArg);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Blt, blThrow);
        il.Emit(OpCodes.Ldarg, byteLengthArg);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Ble, blOk);
        il.MarkLabel(blThrow);
        il.Emit(OpCodes.Ldstr, "byteLength");
        il.Emit(OpCodes.Newobj, _types.ArgumentOutOfRangeExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(blOk);

        // offset >= 0
        il.Emit(OpCodes.Ldarg, offsetArg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, offThrow);
        // offset + byteLength <= _data.Length
        il.Emit(OpCodes.Ldarg, offsetArg);
        il.Emit(OpCodes.Ldarg, byteLengthArg);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ble, offOk);
        il.MarkLabel(offThrow);
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.ArgumentOutOfRangeExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(offOk);
    }
}
