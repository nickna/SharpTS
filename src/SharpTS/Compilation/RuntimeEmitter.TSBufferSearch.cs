using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region Search Methods

    /// <summary>
    /// Emits: public double IndexOf(object value, int byteOffset, string encoding)
    /// </summary>
    private void EmitTSBufferIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IndexOf",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32, _types.String]
        );
        runtime.TSBufferIndexOf = method;

        var il = method.GetILGenerator();

        // Local variables
        var searchBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));  // byte[]
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var innerIndexLocal = il.DeclareLocal(_types.Int32);
        var foundLocal = il.DeclareLocal(_types.Boolean);

        var returnMinusOne = il.DefineLabel();
        var afterTypeCheck = il.DefineLabel();

        // if (byteOffset < 0) byteOffset = 0
        var offsetOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, offsetOkLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Starg, 2);
        il.MarkLabel(offsetOkLabel);

        // len = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (byteOffset >= len) return -1
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, returnMinusOne);

        // Check if value is double (single byte)
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // searchBytes = new byte[] { (byte)(int)(double)value }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Stloc, searchBytesLocal);
        il.Emit(OpCodes.Br, afterTypeCheck);

        // Check if value is string
        il.MarkLabel(notDoubleLabel);
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // searchBytes = Encoding.UTF8.GetBytes((string)value)
        // Use UTF8 for now (ignoring encoding parameter for simplicity)
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.EncodingGetBytesFromString);
        il.Emit(OpCodes.Stloc, searchBytesLocal);
        il.Emit(OpCodes.Br, afterTypeCheck);

        // Check if value is $Buffer (this buffer type)
        il.MarkLabel(notStringLabel);
        var notBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Brfalse, notBufferLabel);

        // searchBytes = (($Buffer)value)._data
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Stloc, searchBytesLocal);
        il.Emit(OpCodes.Br, afterTypeCheck);

        // Unknown type - return -1
        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Br, returnMinusOne);

        il.MarkLabel(afterTypeCheck);

        // if (searchBytes.Length == 0) return byteOffset
        var searchNotEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, searchBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, searchNotEmptyLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(searchNotEmptyLabel);

        // if (searchBytes.Length > len - byteOffset) return -1
        var searchFitsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, searchBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ble, searchFitsLabel);
        il.Emit(OpCodes.Br, returnMinusOne);

        il.MarkLabel(searchFitsLabel);

        // Outer loop: for (i = byteOffset; i <= len - searchBytes.Length; i++)
        var outerLoopStart = il.DefineLabel();
        var outerLoopEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(outerLoopStart);
        // Check: i <= len - searchBytes.Length
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, searchBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bgt, outerLoopEnd);

        // found = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, foundLocal);

        // Inner loop: for (j = 0; j < searchBytes.Length; j++)
        var innerLoopStart = il.DefineLabel();
        var innerLoopEnd = il.DefineLabel();
        var innerLoopContinue = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, innerIndexLocal);

        il.MarkLabel(innerLoopStart);
        // Check: j < searchBytes.Length
        il.Emit(OpCodes.Ldloc, innerIndexLocal);
        il.Emit(OpCodes.Ldloc, searchBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, innerLoopEnd);

        // if (_data[i + j] != searchBytes[j]) { found = false; break; }
        var matchContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, innerIndexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, searchBytesLocal);
        il.Emit(OpCodes.Ldloc, innerIndexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Beq, matchContinueLabel);

        // Not equal - found = false and break
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, foundLocal);
        il.Emit(OpCodes.Br, innerLoopEnd);

        il.MarkLabel(matchContinueLabel);
        // j++
        il.Emit(OpCodes.Ldloc, innerIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, innerIndexLocal);
        il.Emit(OpCodes.Br, innerLoopStart);

        il.MarkLabel(innerLoopEnd);

        // if (found) return i
        var notFoundContinueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, foundLocal);
        il.Emit(OpCodes.Brfalse, notFoundContinueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFoundContinueLabel);
        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, outerLoopStart);

        il.MarkLabel(outerLoopEnd);
        il.MarkLabel(returnMinusOne);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool Includes(object value, int byteOffset, string encoding)
    /// </summary>
    private void EmitTSBufferIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Includes",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Object, _types.Int32, _types.String]
        );
        runtime.TSBufferIncludes = method;

        var il = method.GetILGenerator();

        // return IndexOf(value, byteOffset, encoding) != -1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.TSBufferIndexOf);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);  // !(result == -1)
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Swap Methods

    /// <summary>
    /// Emits: public $Buffer Swap16()
    /// </summary>
    private void EmitTSBufferSwap16(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Swap16",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSBufferSwap16 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 2
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brfalse, okLabel);

        il.Emit(OpCodes.Ldstr, "Buffer size must be a multiple of 16-bits");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);

        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var tempLocal = il.DeclareLocal(_types.Byte);

        // len = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // for (i = 0; i < len; i += 2)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Swap _data[i] and _data[i+1]
        // temp = _data[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, tempLocal);

        // _data[i] = _data[i+1]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stelem_I1);

        // _data[i+1] = temp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Stelem_I1);

        // i += 2
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Buffer Swap32()
    /// </summary>
    private void EmitTSBufferSwap32(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Swap32",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSBufferSwap32 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 4
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brfalse, okLabel);

        il.Emit(OpCodes.Ldstr, "Buffer size must be a multiple of 32-bits");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);

        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var temp0Local = il.DeclareLocal(_types.Byte);
        var temp1Local = il.DeclareLocal(_types.Byte);

        // len = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // for (i = 0; i < len; i += 4)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Swap _data[i] <-> _data[i+3]
        EmitSwapBytes(il, indexLocal, 0, 3, temp0Local);

        // Swap _data[i+1] <-> _data[i+2]
        EmitSwapBytes(il, indexLocal, 1, 2, temp0Local);

        // i += 4
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Buffer Swap64()
    /// </summary>
    private void EmitTSBufferSwap64(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Swap64",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSBufferSwap64 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 8
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brfalse, okLabel);

        il.Emit(OpCodes.Ldstr, "Buffer size must be a multiple of 64-bits");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);

        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var tempLocal = il.DeclareLocal(_types.Byte);

        // len = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // for (i = 0; i < len; i += 8)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Swap bytes: 0<->7, 1<->6, 2<->5, 3<->4
        EmitSwapBytes(il, indexLocal, 0, 7, tempLocal);
        EmitSwapBytes(il, indexLocal, 1, 6, tempLocal);
        EmitSwapBytes(il, indexLocal, 2, 5, tempLocal);
        EmitSwapBytes(il, indexLocal, 3, 4, tempLocal);

        // i += 8
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSwapBytes(ILGenerator il, LocalBuilder indexLocal, int offset1, int offset2, LocalBuilder tempLocal)
    {
        // temp = _data[index + offset1]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        if (offset1 > 0)
        {
            il.Emit(OpCodes.Ldc_I4, offset1);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, tempLocal);

        // _data[index + offset1] = _data[index + offset2]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        if (offset1 > 0)
        {
            il.Emit(OpCodes.Ldc_I4, offset1);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4, offset2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stelem_I1);

        // _data[index + offset2] = temp
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4, offset2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Stelem_I1);
    }

    #endregion
}
