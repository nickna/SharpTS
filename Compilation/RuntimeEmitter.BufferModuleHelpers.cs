using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _bufferCoerceString;
    private MethodBuilder? _bufferBytesOf;

    /// <summary>
    /// Emits the standalone <c>buffer</c> module helper functions (#1160): atob/btoa,
    /// isUtf8/isAscii, transcode, SlowBuffer, and the constants object. All pure-BCL —
    /// atob/btoa/transcode compose the existing $Buffer FromString/ToEncodedString
    /// helpers so they stay byte-identical to <c>BufferModuleHelpers</c> (interpreter).
    /// </summary>
    private void EmitBufferModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitBufferCoerceString(typeBuilder);
        EmitBufferBytesOf(typeBuilder, runtime);
        EmitBufferAtobBtoa(typeBuilder, runtime);
        EmitBufferIsUtf8Ascii(typeBuilder, runtime);
        EmitBufferTranscode(typeBuilder, runtime);
        EmitBufferSlowBuffer(typeBuilder, runtime);
        EmitBufferModuleConstants(typeBuilder, runtime);

        // Buffer.copyBytesFrom needs the $TypedArray runtime type, which is only emitted
        // when a typed array is used. Any caller passes a TypedArray, so HasAnyTypedArray
        // is set whenever this helper is actually reachable.
        if (_features.HasAnyTypedArray)
            EmitBufferCopyBytesFrom(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits <c>object BufferCopyBytesFrom(object view, object offset, object length)</c>:
    /// copies a TypedArray's underlying bytes (offset/length in view elements) into a new
    /// Buffer. Mirrors SharpTSBufferConstructor.BufferCopyBytesFrom.
    /// </summary>
    private void EmitBufferCopyBytesFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BufferCopyBytesFrom",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        runtime.BufferCopyBytesFrom = method;

        var il = method.GetILGenerator();
        var byteArr = _types.MakeArrayType(_types.Byte);
        var arrayCopy = typeof(Array).GetMethod("Copy",
            [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!;

        // ta = view as $TypedArray; if null throw
        var taLocal = il.DeclareLocal(runtime.TypedArrayBaseType);
        var ok = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
        il.Emit(OpCodes.Stloc, taLocal);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Brtrue, ok);
        il.Emit(OpCodes.Ldstr, "Buffer.copyBytesFrom requires a TypedArray argument");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
        il.MarkLabel(ok);

        var backing = il.DeclareLocal(byteArr);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
        il.Emit(OpCodes.Stloc, backing);

        var byteOff = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteOffsetGetter);
        il.Emit(OpCodes.Stloc, byteOff);

        var elemCount = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
        il.Emit(OpCodes.Stloc, elemCount);

        var bpe = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, taLocal);
        il.Emit(OpCodes.Callvirt, _typedArrayBytesPerElementGetter!);
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
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits <c>string BufferCoerceString(object)</c> — null → "", string → itself, else ToString().</summary>
    private void EmitBufferCoerceString(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "BufferCoerceString",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.String, [_types.Object]);
        _bufferCoerceString = method;

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
    private void EmitBufferBytesOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BufferBytesOf",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte), [_types.Object]);
        _bufferBytesOf = method;

        var il = method.GetILGenerator();
        var isStr = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        // $Buffer?
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, isStr);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
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

    private void EmitBufferAtobBtoa(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // atob(data) = Buffer.from(data, 'base64').toString('latin1')
        var atob = typeBuilder.DefineMethod(
            "BufferAtob",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.String, [_types.Object]);
        runtime.BufferAtob = atob;
        var ail = atob.GetILGenerator();
        ail.Emit(OpCodes.Ldarg_0);
        ail.Emit(OpCodes.Call, _bufferCoerceString!);
        ail.Emit(OpCodes.Ldstr, "base64");
        ail.Emit(OpCodes.Call, runtime.TSBufferFromString);
        ail.Emit(OpCodes.Ldstr, "latin1");
        ail.Emit(OpCodes.Callvirt, runtime.TSBufferToString);
        ail.Emit(OpCodes.Ret);

        // btoa(data) = Buffer.from(data, 'latin1').toString('base64')
        var btoa = typeBuilder.DefineMethod(
            "BufferBtoa",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.String, [_types.Object]);
        runtime.BufferBtoa = btoa;
        var bil = btoa.GetILGenerator();
        bil.Emit(OpCodes.Ldarg_0);
        bil.Emit(OpCodes.Call, _bufferCoerceString!);
        bil.Emit(OpCodes.Ldstr, "latin1");
        bil.Emit(OpCodes.Call, runtime.TSBufferFromString);
        bil.Emit(OpCodes.Ldstr, "base64");
        bil.Emit(OpCodes.Callvirt, runtime.TSBufferToString);
        bil.Emit(OpCodes.Ret);
    }

    private void EmitBufferIsUtf8Ascii(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var byteArr = _types.MakeArrayType(_types.Byte);
        var roSpanByte = typeof(ReadOnlySpan<byte>);
        var roSpanOp = roSpanByte.GetMethod("op_Implicit", [byteArr])!;
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
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _bufferBytesOf!);
            il.Emit(OpCodes.Call, roSpanOp);
            il.Emit(OpCodes.Call, isValid);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
        }

        Emit("BufferIsUtf8", utf8IsValid, m => runtime.BufferIsUtf8 = m);
        Emit("BufferIsAscii", asciiIsValid, m => runtime.BufferIsAscii = m);
    }

    private void EmitBufferTranscode(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // transcode(source, from, to) = Buffer.from(decode(sourceBytes, from), to)
        var method = typeBuilder.DefineMethod(
            "BufferTranscode",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        runtime.BufferTranscode = method;

        var il = method.GetILGenerator();
        // str = new $Buffer(BufferBytesOf(source)).ToEncodedString(coerce(from))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _bufferBytesOf!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _bufferCoerceString!);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferToString);
        // Buffer.from(str, coerce(to))
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _bufferCoerceString!);
        il.Emit(OpCodes.Call, runtime.TSBufferFromString);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferSlowBuffer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BufferSlowBuffer",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.BufferSlowBuffer = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, runtime.TSBufferAllocUnsafe);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferModuleConstants(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BufferModuleConstants",
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static,
            _types.Object, Type.EmptyTypes);
        runtime.BufferModuleConstants = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        var objLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Stloc, objLocal);

        void AddConst(string name, double value)
        {
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        }

        AddConst("MAX_LENGTH", 4294967296.0);
        AddConst("MAX_STRING_LENGTH", 536870888.0);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ret);
    }
}
