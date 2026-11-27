using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the standalone <c>buffer</c> module helper functions (#1160): atob/btoa,
    /// isUtf8/isAscii, transcode, SlowBuffer, and the constants object. Uses BCL and
    /// emitted Buffer helpers so these functions work in standalone output.
    /// </summary>
    private void EmitBufferModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var buffer = runtime.RequireBuffer();
        EmitBufferCoerceString(buffer, typeBuilder);
        EmitBufferBytesOf(typeBuilder, buffer);
        var invalidCharacterError = EmitBufferInvalidCharacterError(typeBuilder, runtime);
        var requireInput = EmitRequireBase64Input(typeBuilder, runtime.Errors);
        EmitBufferAtobBtoa(typeBuilder, buffer, invalidCharacterError, requireInput, runtime.StringCoercion.ToJsString);
        buffer.AtobValue = EmitBase64Value("BufferAtobValue", "atob", buffer.Atob);
        buffer.BtoaValue = EmitBase64Value("BufferBtoaValue", "btoa", buffer.Btoa);
        var validationBytes = EmitBufferValidationBytes(typeBuilder, runtime, buffer);
        EmitBufferIsUtf8Ascii(typeBuilder, buffer, validationBytes);
        EmitBufferTranscode(typeBuilder, buffer);
        EmitBufferSlowBuffer(typeBuilder, buffer);
        EmitBufferModuleConstants(typeBuilder, runtime);

        runtime.BuiltInModules.Register("buffer", "atob", buffer.Atob);
        runtime.BuiltInModules.Register("buffer", "btoa", buffer.Btoa);
        runtime.BuiltInModules.Register("buffer", "isUtf8", buffer.IsUtf8);
        runtime.BuiltInModules.Register("buffer", "isAscii", buffer.IsAscii);
        runtime.BuiltInModules.Register("buffer", "transcode", buffer.Transcode);
        runtime.BuiltInModules.Register("buffer", "SlowBuffer", buffer.SlowBuffer);

        MethodBuilder EmitBase64Value(string clrName, string name, MethodBuilder target)
        {
            var value = typeBuilder.DefineMethod(clrName,
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                _types.Object, Type.EmptyTypes);
            var il = value.GetILGenerator();
            var parameters = EmitReflectedMethodLiteralViaHandle(il, target);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4_1);
            EmitReflectedFunctionFactory(il, runtime.FunctionConstruction.GetOrCreate, parameters);
            il.Emit(OpCodes.Ret);
            return value;
        }
    }

    /// <summary>
    /// Emits <c>object BufferCopyBytesFrom(object view, object offset, object length)</c>:
    /// copies a TypedArray's underlying bytes (offset/length in view elements) into a new
    /// Buffer. Mirrors SharpTSBufferConstructor.BufferCopyBytesFrom.
    /// </summary>
    private void EmitBufferCopyBytesFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var buffer = runtime.RequireBuffer();
        var method = typeBuilder.DefineMethod(
            "BufferCopyBytesFrom",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        buffer.CopyBytesFrom = method;

        var il = method.GetILGenerator();
        var byteArr = _types.MakeArrayType(_types.Byte);
        var arrayCopy = typeof(Array).GetMethod("Copy",
            [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!;

        // ta = view as $TypedArray; if null throw
        var taLocal = il.DeclareLocal(runtime.TypedArrays.RequireImplementation().BaseType);
        var ok = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrays.RequireImplementation().BaseType);
        il.Emit(OpCodes.Stloc, taLocal);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Brtrue, ok);
        il.Emit(OpCodes.Ldstr, "Buffer.copyBytesFrom requires a TypedArray argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(ok);

        var backing = il.DeclareLocal(byteArr);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().GetBuffer);
        il.Emit(OpCodes.Stloc, backing);

        var byteOff = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().ByteOffsetGetter);
        il.Emit(OpCodes.Stloc, byteOff);

        var elemCount = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().LengthGetter);
        il.Emit(OpCodes.Stloc, elemCount);

        var bpe = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().BytesPerElementGetter);
        il.Emit(OpCodes.Stloc, bpe);

        // int offEl = (offset is double) ? (int)offset : 0; clamp >= 0
        var offEl = il.DeclareLocal(_types.Int32);
        var offDefault = il.DefineLabel();
        var afterOff = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, offDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, offEl);
        il.Emit(OpCodes.Br, afterOff);
        il.MarkLabel(offDefault);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, offEl);
        il.MarkLabel(afterOff);
        var offOk = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, offEl);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, offOk);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, offEl);
        il.MarkLabel(offOk);

        // int lenEl = (length is double) ? (int)length : (elemCount - offEl)
        var lenEl = il.DeclareLocal(_types.Int32);
        var lenDefault = il.DefineLabel();
        var afterLen = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, lenDefault);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenEl);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(lenDefault);
        il.Emit(OpCodes.Ldloc, elemCount);
        il.Emit(OpCodes.Ldloc, offEl);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, lenEl);
        il.MarkLabel(afterLen);
        // if (lenEl < 0) lenEl = 0
        var lenOk1 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenEl);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, lenOk1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenEl);
        il.MarkLabel(lenOk1);
        // if (offEl + lenEl > elemCount) lenEl = max(0, elemCount - offEl)
        var lenOk2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, offEl);
        il.Emit(OpCodes.Ldloc, lenEl);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, elemCount);
        il.Emit(OpCodes.Ble, lenOk2);
        il.Emit(OpCodes.Ldloc, elemCount);
        il.Emit(OpCodes.Ldloc, offEl);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, lenEl);
        il.Emit(OpCodes.Ldloc, lenEl);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, lenOk2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenEl);
        il.MarkLabel(lenOk2);

        // int byteStart = byteOff + offEl * bpe; int byteLen = lenEl * bpe;
        var byteStart = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, byteOff);
        il.Emit(OpCodes.Ldloc, offEl);
        il.Emit(OpCodes.Ldloc, bpe);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, byteStart);
        var byteLen = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, lenEl);
        il.Emit(OpCodes.Ldloc, bpe);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, byteLen);

        // byte[] result = new byte[byteLen]; Array.Copy(backing, byteStart, result, 0, byteLen)
        var result = il.DeclareLocal(byteArr);
        il.Emit(OpCodes.Ldloc, byteLen);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, backing);
        il.Emit(OpCodes.Ldloc, byteStart);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, byteLen);
        il.Emit(OpCodes.Call, arrayCopy);

        // return new $Buffer(result)
        il.Emit(OpCodes.Ldloc, result);
        NativePrototypeFieldsLookup.EmitCarrierCreate(_nativePrototypeFields, il, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits <c>string BufferCoerceString(object)</c> — null → "", string → itself, else ToString().</summary>
    private void EmitBufferCoerceString(EmittedBufferRuntime buffer, TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "BufferCoerceString",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.String, [_types.Object]);
        buffer.CoerceString = method;

        var il = method.GetILGenerator();
        var retEmpty = il.DefineLabel();
        var retVal = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, retEmpty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, retVal);   // it's a string — leave it on the stack
        il.Emit(OpCodes.Pop);              // discard the null from isinst
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(retVal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(retEmpty);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits <c>byte[] BufferBytesOf(object)</c> — $Buffer → GetData, string → UTF8 bytes.</summary>
    private void EmitBufferBytesOf(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "BufferBytesOf",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte), [_types.Object]);
        buffer.BytesOf = method;

        var il = method.GetILGenerator();
        var isStr = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        // $Buffer?
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, buffer.Type);
        il.Emit(OpCodes.Brfalse, isStr);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Callvirt, buffer.GetData);
        il.Emit(OpCodes.Ret);
        // string?
        il.MarkLabel(isStr);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "buffer module helper requires a Buffer or string argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    private MethodBuilder EmitBufferInvalidCharacterError(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("CreateBufferInvalidCharacterError",
            System.Reflection.MethodAttributes.Private | System.Reflection.MethodAttributes.Static,
            _types.Exception, Type.EmptyTypes);
        var il = method.GetILGenerator();
        var errors = runtime.Errors;
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.InvalidCharacterMessage);
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.InvalidCharacterErrorName);
        il.Emit(OpCodes.Newobj, errors.DomExceptionConstructor);
        il.Emit(OpCodes.Call, errors.CreateException);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitRequireBase64Input(TypeBuilder typeBuilder, EmittedErrorRuntime errors)
    {
        var method = typeBuilder.DefineMethod("RequireBase64Input",
            System.Reflection.MethodAttributes.Private | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.ObjectArray]);
        var il = method.GetILGenerator();
        var hasInput = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Brtrue, hasInput);
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.MissingInputMessage);
        il.Emit(OpCodes.Newobj, errors.TypeErrorConstructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.MissingInputCode);
        il.Emit(OpCodes.Callvirt, errors.CodeSetter);
        il.Emit(OpCodes.Call, errors.CreateException);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(hasInput);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitBufferAtobBtoa(TypeBuilder typeBuilder, EmittedBufferRuntime buffer,
        MethodBuilder invalidCharacterError, MethodBuilder requireInput, MethodBuilder toJsString)
    {
        // Mirror BufferModuleHelpers.Atob with BCL calls in the standalone runtime.
        var atob = DefineConverter("BufferAtob");
        buffer.Atob = atob;
        var ail = atob.GetILGenerator();
        var data = ail.DeclareLocal(_types.String);
        var remainder = ail.DeclareLocal(_types.Int32);
        var result = ail.DeclareLocal(_types.String);
        var decode = ail.DefineLabel();
        var decoded = ail.DefineLabel();
        var length = _types.GetProperty(_types.String, nameof(string.Length))!.GetGetMethod()!;

        ail.Emit(OpCodes.Ldarg_0);
        ail.Emit(OpCodes.Call, requireInput);
        ail.Emit(OpCodes.Call, toJsString);
        foreach (var whitespace in BufferModuleHelpers.AtobWhitespace)
            EmitStringReplace(ail, whitespace.ToString(), "");
        ail.Emit(OpCodes.Stloc, data);

        ail.Emit(OpCodes.Ldloc, data);
        ail.Emit(OpCodes.Callvirt, length);
        ail.Emit(OpCodes.Ldc_I4_4);
        ail.Emit(OpCodes.Rem);
        ail.Emit(OpCodes.Stloc, remainder);
        ail.Emit(OpCodes.Ldloc, remainder);
        ail.Emit(OpCodes.Ldc_I4_2);
        ail.Emit(OpCodes.Blt, decode);
        ail.Emit(OpCodes.Ldloc, data);
        ail.Emit(OpCodes.Ldc_I4, (int)'=');
        ail.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, nameof(string.Contains), _types.Char));
        ail.Emit(OpCodes.Brtrue, decode);

        ail.Emit(OpCodes.Ldloc, data);
        ail.Emit(OpCodes.Ldloc, data);
        ail.Emit(OpCodes.Callvirt, length);
        ail.Emit(OpCodes.Ldc_I4_4);
        ail.Emit(OpCodes.Add);
        ail.Emit(OpCodes.Ldloc, remainder);
        ail.Emit(OpCodes.Sub);
        ail.Emit(OpCodes.Ldc_I4, (int)'=');
        ail.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, nameof(string.PadRight), _types.Int32, _types.Char));
        ail.Emit(OpCodes.Stloc, data);

        ail.MarkLabel(decode);
        ail.Emit(OpCodes.Nop);
        ail.BeginExceptionBlock();
        ail.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "Latin1")!.GetGetMethod()!);
        ail.Emit(OpCodes.Ldloc, data);
        ail.Emit(OpCodes.Call, _types.ConvertFromBase64String);
        ail.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetString", _types.ByteArray));
        ail.Emit(OpCodes.Stloc, result);
        ail.Emit(OpCodes.Leave, decoded);
        ail.BeginCatchBlock(_types.Resolve("System.FormatException"));
        ail.Emit(OpCodes.Pop);
        ail.Emit(OpCodes.Call, invalidCharacterError);
        ail.Emit(OpCodes.Throw);
        ail.EndExceptionBlock();
        ail.MarkLabel(decoded);
        ail.Emit(OpCodes.Ldloc, result);
        ail.Emit(OpCodes.Ret);

        // Validate UTF-16 code units before encoding, since Latin1.GetBytes
        // substitutes out-of-range characters instead of rejecting them.
        var btoa = DefineConverter("BufferBtoa");
        buffer.Btoa = btoa;
        var bil = btoa.GetILGenerator();
        var input = bil.DeclareLocal(_types.String);
        var index = bil.DeclareLocal(_types.Int32);
        var checkIndex = bil.DefineLabel();
        var checkCharacter = bil.DefineLabel();
        var nextCharacter = bil.DefineLabel();
        bil.Emit(OpCodes.Ldarg_0);
        bil.Emit(OpCodes.Call, requireInput);
        bil.Emit(OpCodes.Call, toJsString);
        bil.Emit(OpCodes.Stloc, input);
        bil.Emit(OpCodes.Ldc_I4_0);
        bil.Emit(OpCodes.Stloc, index);
        bil.Emit(OpCodes.Br, checkIndex);

        bil.MarkLabel(checkCharacter);
        bil.Emit(OpCodes.Ldloc, input);
        bil.Emit(OpCodes.Ldloc, index);
        bil.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Chars")!.GetGetMethod()!);
        bil.Emit(OpCodes.Ldc_I4, (int)byte.MaxValue);
        bil.Emit(OpCodes.Ble, nextCharacter);
        bil.Emit(OpCodes.Call, invalidCharacterError);
        bil.Emit(OpCodes.Throw);

        bil.MarkLabel(nextCharacter);
        bil.Emit(OpCodes.Ldloc, index);
        bil.Emit(OpCodes.Ldc_I4_1);
        bil.Emit(OpCodes.Add);
        bil.Emit(OpCodes.Stloc, index);
        bil.MarkLabel(checkIndex);
        bil.Emit(OpCodes.Ldloc, index);
        bil.Emit(OpCodes.Ldloc, input);
        bil.Emit(OpCodes.Callvirt, length);
        bil.Emit(OpCodes.Blt, checkCharacter);

        bil.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "Latin1")!.GetGetMethod()!);
        bil.Emit(OpCodes.Ldloc, input);
        bil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", _types.String));
        bil.Emit(OpCodes.Call, _types.ConvertToBase64String);
        bil.Emit(OpCodes.Ret);

        MethodBuilder DefineConverter(string name)
        {
            var converter = typeBuilder.DefineMethod(name,
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                _types.String, [_types.ObjectArray]);
            converter.DefineParameter(1, System.Reflection.ParameterAttributes.None, "args")
                .SetCustomAttribute(_types.GetDefaultConstructor(_types.ParamArrayAttribute), CustomAttributeEncoder.EmptyBlob);
            return converter;
        }
    }

    private MethodBuilder EmitBufferValidationBytes(TypeBuilder owner, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        var intReference = _types.Int32.MakeByRefType();
        var method = owner.DefineMethod("BufferValidationBytes",
            System.Reflection.MethodAttributes.Private | System.Reflection.MethodAttributes.Static,
            _types.ByteArray, [_types.Object, intReference, intReference]);
        var il = method.GetILGenerator();
        var wholeArray = il.DefineLabel();

        if (runtime.ArrayBuffer is { } arrayBuffer)
        {
            var next = il.DefineLabel();
            var attached = il.DefineLabel();
            var input = TestInput(arrayBuffer.Type, next);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Ldfld, arrayBuffer.DetachedField);
            il.Emit(OpCodes.Brfalse, attached);
            il.Emit(OpCodes.Ldstr, BufferModuleHelpers.DetachedValidationMessage);
            il.Emit(OpCodes.Newobj, runtime.Errors.MessageConstructor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, BufferModuleHelpers.DetachedValidationCode);
            il.Emit(OpCodes.Callvirt, runtime.Errors.CodeSetter);
            il.Emit(OpCodes.Call, runtime.Errors.CreateException);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(attached);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Callvirt, arrayBuffer.GetBuffer);
            il.Emit(OpCodes.Br, wholeArray);
            il.MarkLabel(next);
        }

        if (runtime.SharedArrayBuffer is { } shared)
        {
            var next = il.DefineLabel();
            var input = TestInput(shared.Type, next);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Callvirt, shared.GetBuffer);
            il.Emit(OpCodes.Br, wholeArray);
            il.MarkLabel(next);
        }

        if (runtime.TypedArrays.Implementation is { } arrays)
        {
            var next = il.DefineLabel();
            var attached = il.DefineLabel();
            var input = TestInput(arrays.BaseType, next);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Call, arrays.IsDetached);
            il.Emit(OpCodes.Brfalse, attached);
            // Node treats a detached view as an empty byte range.
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Br, wholeArray);
            il.MarkLabel(attached);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Callvirt, arrays.ByteOffsetGetter);
            il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Callvirt, arrays.ByteLengthGetter);
            il.Emit(OpCodes.Stind_I4);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Callvirt, arrays.GetBuffer);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        var invalidInput = il.DefineLabel();
        var bufferInput = TestInput(buffer.Type, invalidInput);
        il.Emit(OpCodes.Ldloc, bufferInput);
        il.Emit(OpCodes.Callvirt, buffer.GetData);
        il.Emit(OpCodes.Br, wholeArray);
        il.MarkLabel(invalidInput);
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.InvalidValidationInputMessage);
        il.Emit(OpCodes.Newobj, runtime.Errors.TypeErrorConstructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, BufferModuleHelpers.InvalidValidationInputCode);
        il.Emit(OpCodes.Callvirt, runtime.Errors.CodeSetter);
        il.Emit(OpCodes.Call, runtime.Errors.CreateException);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(wholeArray);
        var data = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Stloc, data);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ret);
        return method;

        LocalBuilder TestInput(Type type, Label next)
        {
            var input = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Stloc, input);
            il.Emit(OpCodes.Ldloc, input);
            il.Emit(OpCodes.Brfalse, next);
            return input;
        }
    }

    private void EmitBufferIsUtf8Ascii(TypeBuilder typeBuilder, EmittedBufferRuntime buffer, MethodBuilder validationBytes)
    {
        var roSpanByte = typeof(ReadOnlySpan<byte>);
        var fromRange = roSpanByte.GetConstructor([_types.ByteArray, _types.Int32, _types.Int32])!;
        var utf8IsValid = typeof(System.Text.Unicode.Utf8).GetMethod("IsValid", [roSpanByte])!;
        var asciiIsValid = typeof(System.Text.Ascii).GetMethod("IsValid", [roSpanByte])!;

        void Emit(string name, System.Reflection.MethodInfo isValid, Action<MethodBuilder> store)
        {
            var method = typeBuilder.DefineMethod(
                name,
                System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
                _types.Object, [_types.Object]);
            store(method);
            var il = method.GetILGenerator();
            var offset = il.DeclareLocal(_types.Int32);
            var length = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, offset);
            il.Emit(OpCodes.Ldloca, length);
            il.Emit(OpCodes.Call, validationBytes);
            il.Emit(OpCodes.Ldloc, offset);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Newobj, fromRange);
            il.Emit(OpCodes.Call, isValid);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
        }

        Emit("BufferIsUtf8", utf8IsValid, m => buffer.IsUtf8 = m);
        Emit("BufferIsAscii", asciiIsValid, m => buffer.IsAscii = m);
    }

    private void EmitBufferTranscode(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        // transcode(source, from, to) = Buffer.from(decode(sourceBytes, from), to)
        var method = typeBuilder.DefineMethod(
            "BufferTranscode",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        buffer.Transcode = method;

        var il = method.GetILGenerator();
        // str = new $Buffer(BufferBytesOf(source)).ToEncodedString(coerce(from))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.BytesOf);
        NativePrototypeFieldsLookup.EmitCarrierCreate(_nativePrototypeFields, il, buffer.Ctor);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, buffer.CoerceString);
        il.Emit(OpCodes.Callvirt, buffer.ToStringMethod);
        // Buffer.from(str, coerce(to))
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, buffer.CoerceString);
        il.Emit(OpCodes.Call, buffer.FromString);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferSlowBuffer(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "BufferSlowBuffer",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        buffer.SlowBuffer = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, buffer.AllocUnsafe);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferModuleConstants(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var buffer = runtime.RequireBuffer();
        var method = typeBuilder.DefineMethod(
            "BufferModuleConstants",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, Type.EmptyTypes);
        buffer.ModuleConstants = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.ObjectStorage.Constructor);
        var objLocal = il.DeclareLocal(runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Stloc, objLocal);

        void AddConst(string name, double value)
        {
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, runtime.ObjectStorage.SetProperty);
        }

        AddConst("MAX_LENGTH", 4294967296.0);
        AddConst("MAX_STRING_LENGTH", 536870888.0);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ret);
    }
}
