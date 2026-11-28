using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region Search Methods

    private void EmitBufferSearchValues(TypeBuilder owner, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        buffer.IndexOfValue = EmitMethod("BufferIndexOfValue", buffer.IndexOf);
        buffer.IncludesValue = EmitMethod("BufferIncludesValue", buffer.Includes);

        MethodBuilder EmitMethod(string name, MethodBuilder search)
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, search.ReturnType,
                [_types.Object, _types.Object, _types.Object, _types.Object]);
            method.DefineParameter(1, ParameterAttributes.None, "__this");
            method.SetCustomAttribute(runtime.FunctionAttributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
            var il = method.GetILGenerator();
            var receiver = il.DeclareLocal(buffer.Type);
            var valid = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, buffer.Type);
            il.Emit(OpCodes.Stloc, receiver);
            il.Emit(OpCodes.Ldloc, receiver);
            il.Emit(OpCodes.Brtrue, valid);
            EmitThrowTypeError(il, runtime.Errors, "Buffer search requires a Buffer receiver");
            il.MarkLabel(valid);

            var missingNeedle = il.DefineLabel();
            var hasNeedle = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, missingNeedle);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
            il.Emit(OpCodes.Brfalse, hasNeedle);
            il.MarkLabel(missingNeedle);
            EmitThrowTypeError(il, runtime.Errors, "Buffer search requires a value argument");
            il.MarkLabel(hasNeedle);

            // A string in the offset position selects the encoding and starts the search at zero.
            var offset = il.DeclareLocal(_types.Int32);
            var encoding = il.DeclareLocal(_types.String);
            var invoke = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Stloc, encoding);
            il.Emit(OpCodes.Ldloc, encoding);
            il.Emit(OpCodes.Brtrue, invoke);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber);
            il.Emit(OpCodes.Call, typeof(int).GetMethod(nameof(int.CreateSaturating))!.MakeGenericMethod(_types.Double));
            il.Emit(OpCodes.Stloc, offset);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
            il.Emit(OpCodes.Brtrue, invoke);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, runtime.StringCoercion.ToJsString);
            il.Emit(OpCodes.Stloc, encoding);
            il.MarkLabel(invoke);
            il.Emit(OpCodes.Ldloc, receiver);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, offset);
            il.Emit(OpCodes.Ldloc, encoding);
            il.Emit(OpCodes.Call, search);
            il.Emit(OpCodes.Ret);
            return method;
        }
    }

    /// <summary>
    /// Emits: public double IndexOf(object value, int byteOffset, string encoding)
    /// </summary>
    private void EmitTSBufferIndexOf(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "IndexOf",
            MethodAttributes.Public,
            _types.Double,
            [_types.Object, _types.Int32, _types.String]
        );
        buffer.IndexOf = method;

        var il = method.GetILGenerator();

        // Local variables
        var searchBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));  // byte[]
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var innerIndexLocal = il.DeclareLocal(_types.Int32);
        var foundLocal = il.DeclareLocal(_types.Boolean);

        var returnMinusOne = il.DefineLabel();
        var afterTypeCheck = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Stloc, lenLocal);

        // Negative offsets count from the end; clamp both directions to [0, length].
        var nonNegative = il.DefineLabel();
        var normalized = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, nonNegative);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.MathMaxInt32);
        il.Emit(OpCodes.Br, normalized);
        il.MarkLabel(nonNegative);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.MathMinInt32);
        il.MarkLabel(normalized);
        il.Emit(OpCodes.Starg, 2);

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

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, buffer.EncodeString);
        il.Emit(OpCodes.Call, buffer.GetData);
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
        il.Emit(OpCodes.Call, buffer.GetData);
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
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
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
    private void EmitTSBufferIncludes(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Includes",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Object, _types.Int32, _types.String]
        );
        buffer.Includes = method;

        var il = method.GetILGenerator();

        // return IndexOf(value, byteOffset, encoding) != -1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, buffer.IndexOf);
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
    private void EmitTSBufferSwap16(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Swap16",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        buffer.Swap16 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 2
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, tempLocal);

        // _data[i] = _data[i+1]
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stelem_I1);

        // _data[i+1] = temp
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
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
    private void EmitTSBufferSwap32(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Swap32",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        buffer.Swap32 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 4
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        EmitSwapBytes(buffer, il, indexLocal, 0, 3, temp0Local);

        // Swap _data[i+1] <-> _data[i+2]
        EmitSwapBytes(buffer, il, indexLocal, 1, 2, temp0Local);

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
    private void EmitTSBufferSwap64(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Swap64",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        buffer.Swap64 = method;

        var il = method.GetILGenerator();

        // Check length is multiple of 8
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        EmitSwapBytes(buffer, il, indexLocal, 0, 7, tempLocal);
        EmitSwapBytes(buffer, il, indexLocal, 1, 6, tempLocal);
        EmitSwapBytes(buffer, il, indexLocal, 2, 5, tempLocal);
        EmitSwapBytes(buffer, il, indexLocal, 3, 4, tempLocal);

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

    private void EmitSwapBytes(EmittedBufferRuntime buffer, ILGenerator il, LocalBuilder indexLocal, int offset1, int offset2, LocalBuilder tempLocal)
    {
        // temp = _data[index + offset1]
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        if (offset1 > 0)
        {
            il.Emit(OpCodes.Ldc_I4, offset1);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stloc, tempLocal);

        // _data[index + offset1] = _data[index + offset2]
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        if (offset1 > 0)
        {
            il.Emit(OpCodes.Ldc_I4, offset1);
            il.Emit(OpCodes.Add);
        }
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, offset2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stelem_I1);

        // _data[index + offset2] = temp
        EmitBufferRangeStart(il, buffer, OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, offset2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Stelem_I1);
    }

    #endregion
}
