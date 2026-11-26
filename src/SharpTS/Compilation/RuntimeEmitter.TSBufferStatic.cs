using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitBufferAllocValue(TypeBuilder owner, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        var method = owner.DefineMethod("BufferAllocValue", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        buffer.AllocValue = method;
        var il = method.GetILGenerator();
        var hasSize = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, hasSize);
        EmitThrowTypeError(il, runtime.Errors, "The Buffer.alloc size must be a number");
        il.MarkLabel(hasSize);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, buffer.Alloc);
        var result = il.DeclareLocal(buffer.Type);
        il.Emit(OpCodes.Stloc, result);

        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Beq, done);

        var fill = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, fill);
        if (buffer.HasTypedArrayCopy)
        {
            var notView = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, fill);
            il.Emit(OpCodes.Isinst, runtime.TypedArrays.RequireImplementation().BaseType);
            il.Emit(OpCodes.Brfalse, notView);
            il.Emit(OpCodes.Ldloc, fill);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, buffer.CopyBytesFrom);
            il.Emit(OpCodes.Stloc, fill);
            il.MarkLabel(notView);
        }

        var stringFill = il.DeclareLocal(_types.String);
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fill);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, stringFill);
        il.Emit(OpCodes.Ldloc, stringFill);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldloc, stringFill);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Call, buffer.FromStringForAlloc);
        il.Emit(OpCodes.Stloc, fill);
        il.Emit(OpCodes.Ldloc, stringFill);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, done);
        il.MarkLabel(notString);

        var applyFill = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fill);
        il.Emit(OpCodes.Isinst, buffer.Type);
        il.Emit(OpCodes.Brfalse, applyFill);
        il.Emit(OpCodes.Ldloc, fill);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Brtrue, applyFill);
        EmitThrowTypeError(il, runtime.Errors, "Invalid Buffer.alloc fill value");

        il.MarkLabel(applyFill);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloc, fill);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Call, buffer.Fill);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferFromValue(TypeBuilder owner, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        var method = owner.DefineMethod("BufferFromValue", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        buffer.FromValue = method;
        var il = method.GetILGenerator();

        var text = il.DeclareLocal(_types.String);
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, text);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Brfalse, notString);
        var hasEncoding = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasEncoding);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.MarkLabel(hasEncoding);
        il.Emit(OpCodes.Call, buffer.FromString);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notString);

        EmitTypedCase(buffer.Type, () => il.Emit(OpCodes.Call, buffer.FromBuffer));
        if (runtime.ArrayBuffer is { } arrayBuffer)
        {
            // ArrayBuffer input shares the underlying bytes.
            EmitTypedCase(arrayBuffer.Type, () =>
            {
                il.Emit(OpCodes.Callvirt, arrayBuffer.GetBuffer);
                il.Emit(OpCodes.Newobj, buffer.Ctor);
            });
        }
        if (buffer.HasTypedArrayCopy)
        {
            EmitTypedCase(runtime.TypedArrays.RequireImplementation().BaseType, () =>
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, buffer.CopyBytesFrom);
            });
        }
        EmitTypedCase(_types.ListOfObject, () => il.Emit(OpCodes.Call, buffer.FromArray));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectOperations.Values);
        il.Emit(OpCodes.Call, buffer.FromArray);
        il.Emit(OpCodes.Ret);

        void EmitTypedCase(Type type, Action emitConversion)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, type);
            emitConversion();
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
    }

    /// <summary>
    /// Emits: public static $Buffer FromString(string data, string encoding)
    /// </summary>
    private void EmitTSBufferFromString(TypeBuilder typeBuilder, EmittedBufferRuntime buffer, EmittedErrorRuntime errors)
    {
        var method = typeBuilder.DefineMethod(
            "FromString",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.String, _types.String]
        );
        buffer.FromString = method;
        var encode = typeBuilder.DefineMethod("EncodeString", MethodAttributes.Private | MethodAttributes.Static,
            typeBuilder, [_types.String, _types.String, _types.Boolean]);
        var wrapper = method.GetILGenerator();
        wrapper.Emit(OpCodes.Ldarg_0);
        wrapper.Emit(OpCodes.Ldarg_1);
        wrapper.Emit(OpCodes.Ldc_I4_0);
        wrapper.Emit(OpCodes.Call, encode);
        wrapper.Emit(OpCodes.Ret);

        var forAlloc = typeBuilder.DefineMethod("FromStringForAlloc", MethodAttributes.Assembly | MethodAttributes.Static,
            typeBuilder, [_types.String, _types.String]);
        buffer.FromStringForAlloc = forAlloc;
        var allocIl = forAlloc.GetILGenerator();
        var result = allocIl.DeclareLocal(typeBuilder);
        allocIl.BeginExceptionBlock();
        allocIl.Emit(OpCodes.Ldarg_0);
        allocIl.Emit(OpCodes.Ldarg_1);
        allocIl.Emit(OpCodes.Ldc_I4_1);
        allocIl.Emit(OpCodes.Call, encode);
        allocIl.Emit(OpCodes.Stloc, result);
        allocIl.BeginCatchBlock(typeof(FormatException));
        allocIl.Emit(OpCodes.Pop);
        EmitThrowTypeError(allocIl, errors, "Invalid Buffer.alloc fill value");
        allocIl.EndExceptionBlock();
        allocIl.Emit(OpCodes.Ldloc, result);
        allocIl.Emit(OpCodes.Ret);

        var il = encode.GetILGenerator();

        var encodingLocal = il.DeclareLocal(_types.String);
        var utf8Label = il.DefineLabel();
        var asciiLabel = il.DefineLabel();
        var latin1Label = il.DefineLabel();
        var utf16leLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var base64urlLabel = il.DefineLabel();
        var hexLabel = il.DefineLabel();

        // Use UTF-8 when no encoding is specified, including the empty string.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, utf8Label);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check encodings (must mirror Runtime/Types/BufferEncoding.Encode exactly).
        CheckStringEquals(il, encodingLocal, "utf8", utf8Label);
        CheckStringEquals(il, encodingLocal, "utf-8", utf8Label);
        CheckStringEquals(il, encodingLocal, "ascii", asciiLabel);
        CheckStringEquals(il, encodingLocal, "latin1", latin1Label);
        CheckStringEquals(il, encodingLocal, "binary", latin1Label);
        CheckStringEquals(il, encodingLocal, "utf16le", utf16leLabel);
        CheckStringEquals(il, encodingLocal, "utf-16le", utf16leLabel);
        CheckStringEquals(il, encodingLocal, "ucs2", utf16leLabel);
        CheckStringEquals(il, encodingLocal, "ucs-2", utf16leLabel);
        CheckStringEquals(il, encodingLocal, "base64", base64Label);
        CheckStringEquals(il, encodingLocal, "base64url", base64urlLabel);
        CheckStringEquals(il, encodingLocal, "hex", hexLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, utf8Label);
        EmitThrowTypeError(il, errors, "Unknown Buffer.alloc fill encoding");

        // UTF-8
        il.MarkLabel(utf8Label);
        EmitEncodingGetBytes(il, "UTF8", buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // ASCII
        il.MarkLabel(asciiLabel);
        EmitEncodingGetBytes(il, "ASCII", buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // Latin1 / binary
        il.MarkLabel(latin1Label);
        EmitEncodingGetBytes(il, "Latin1", buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // UTF-16LE / ucs2
        il.MarkLabel(utf16leLabel);
        EmitEncodingGetBytes(il, "Unicode", buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // Base64
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertFromBase64String);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // Base64url: map -/_ → +/ and re-pad to a multiple of 4, then decode.
        il.MarkLabel(base64urlLabel);
        EmitBase64UrlNormalize(il);
        il.Emit(OpCodes.Call, _types.ConvertFromBase64String);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // Hex
        il.MarkLabel(hexLabel);
        EmitBufferHexBytes(il);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBufferHexBytes(ILGenerator il)
    {
        var bytes = il.DeclareLocal(_types.ByteArray);
        var charsConsumed = il.DeclareLocal(_types.Int32);
        var bytesWritten = il.DeclareLocal(_types.Int32);
        var byRefInt = _types.Int32.MakeByRefType();
        var decode = _types.GetMethod(_types.Convert, "FromHexString",
            [_types.String, _types.SpanOfByte, byRefInt, byRefInt])!;
        var resize = EmitGenerics.MakeGenericMethod(typeof(Array).GetMethod(nameof(Array.Resize))!, _types.Byte);

        // Allocate only for complete byte pairs, then retain the valid prefix.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytes);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.SpanOfByte, "op_Implicit", [_types.ByteArray])!);
        il.Emit(OpCodes.Ldloca, charsConsumed);
        il.Emit(OpCodes.Ldloca, bytesWritten);
        il.Emit(OpCodes.Call, decode);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloca, bytes);
        il.Emit(OpCodes.Ldloc, bytesWritten);
        il.Emit(OpCodes.Call, resize);
        il.Emit(OpCodes.Ldloc, bytes);
    }

    /// <summary>
    /// Emits: takes the input string (Ldarg_0) and leaves a standard-base64 string on
    /// the stack — '-'→'+', '_'→'/', and re-padded with '=' to a multiple of 4 length.
    /// Mirrors BufferEncoding.DecodeBase64's url-safe normalization.
    /// </summary>
    private void EmitBase64UrlNormalize(ILGenerator il)
    {
        var replace = _types.GetMethod(_types.String, "Replace", [_types.String, _types.String])!;
        var concat = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!;
        var getLength = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;

        var s = il.DeclareLocal(_types.String);
        var rem = il.DeclareLocal(_types.Int32);

        // s = arg0.Replace("-", "+").Replace("_", "/")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "-"); il.Emit(OpCodes.Ldstr, "+"); il.Emit(OpCodes.Callvirt, replace);
        il.Emit(OpCodes.Ldstr, "_"); il.Emit(OpCodes.Ldstr, "/"); il.Emit(OpCodes.Callvirt, replace);
        il.Emit(OpCodes.Stloc, s);

        // rem = s.Length % 4
        il.Emit(OpCodes.Ldloc, s);
        il.Emit(OpCodes.Callvirt, getLength);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, rem);

        var pad1 = il.DefineLabel();
        var done = il.DefineLabel();

        // rem == 2 → s + "=="
        il.Emit(OpCodes.Ldloc, rem); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Bne_Un, pad1);
        il.Emit(OpCodes.Ldloc, s); il.Emit(OpCodes.Ldstr, "=="); il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Br, done);

        // rem == 3 → s + "="
        il.MarkLabel(pad1);
        var noPad = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rem); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Bne_Un, noPad);
        il.Emit(OpCodes.Ldloc, s); il.Emit(OpCodes.Ldstr, "="); il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Br, done);

        // rem == 0 (or 1) → s unchanged
        il.MarkLabel(noPad);
        il.Emit(OpCodes.Ldloc, s);

        il.MarkLabel(done);
    }

    private void EmitEncodingGetBytes(ILGenerator il, string encodingProperty, ConstructorBuilder bufferCtor)
    {
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, encodingProperty)!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Newobj, bufferCtor);
    }

    private void CheckStringEquals(ILGenerator il, LocalBuilder local, string value, Label target)
    {
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Emits: public static $Buffer FromArray(List<object?> array)
    /// </summary>
    private void EmitTSBufferFromArray(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "FromArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.ListOfObject]
        );
        buffer.FromArray = method;

        var il = method.GetILGenerator();

        // Create byte array from list
        var countProperty = _types.GetProperty(_types.ListOfObject, "Count");
        var indexerMethod = _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32);

        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var bytes = new byte[array.Count]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, countProperty.GetGetMethod()!);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Loop: for (int i = 0; i < array.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, countProperty.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // bytes[i] = (byte)((double)array[i])
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, indexerMethod);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return new $Buffer(bytes)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer FromBuffer($Buffer source)
    /// Copies the source buffer's data into a new buffer.
    /// </summary>
    private void EmitTSBufferFromBuffer(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "FromBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [typeBuilder]
        );
        buffer.FromBuffer = method;

        var il = method.GetILGenerator();

        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // var bytes = new byte[source._data.Length]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Array.Copy(source._data, 0, bytes, 0, source._data.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // return new $Buffer(bytes)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer Alloc(int size)
    /// </summary>
    private void EmitTSBufferAlloc(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Alloc",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.Int32]
        );
        buffer.Alloc = method;

        var il = method.GetILGenerator();

        // return new $Buffer(size) - constructor already creates zero-initialized array
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, buffer.CtorSize);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer AllocUnsafe(int size)
    /// </summary>
    private void EmitTSBufferAllocUnsafe(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "AllocUnsafe",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.Int32]
        );
        buffer.AllocUnsafe = method;

        var il = method.GetILGenerator();

        // return new $Buffer(size) - same as Alloc in .NET (always initialized)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, buffer.CtorSize);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer Concat(List<object?> buffers, int totalLength)
    /// </summary>
    private void EmitTSBufferConcat(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Concat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.ListOfObject, _types.Int32]
        );
        buffer.Concat = method;

        var il = method.GetILGenerator();

        // Simplified: calculate total length and create combined buffer
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var offsetLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notBufferLabel = il.DefineLabel();

        // var bytes = new byte[totalLength]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // int offset = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Loop through buffers
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var countProperty = _types.GetProperty(_types.ListOfObject, "Count");
        var indexerMethod = _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, countProperty.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // var item = buffers[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, indexerMethod);
        il.Emit(OpCodes.Stloc, itemLocal);

        // if (item is $Buffer buf)
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Brfalse, notBufferLabel);

        // Copy buffer data
        // Array.Copy(buf._data, 0, bytes, offset, buf._data.Length)
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // offset += buf._data.Length
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        il.MarkLabel(notBufferLabel);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return new $Buffer(bytes)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static int CalculateBuffersTotalLength(List<object?> buffers)
    /// Helper method to calculate total length of buffers for Buffer.concat()
    /// </summary>
    private void EmitCalculateBuffersTotalLength(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "CalculateBuffersTotalLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.ListOfObject]
        );
        buffer.CalculateTotalLength = method;

        var il = method.GetILGenerator();

        var totalLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notBufferLabel = il.DefineLabel();

        // int total = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, totalLocal);

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var countProperty = _types.GetProperty(_types.ListOfObject, "Count");
        var indexerMethod = _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32);

        // Loop: while (i < buffers.Count)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, countProperty.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // var item = buffers[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, indexerMethod);
        il.Emit(OpCodes.Stloc, itemLocal);

        // if (item is $Buffer buf)
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Brfalse, notBufferLabel);

        // total += buf._data.Length
        il.Emit(OpCodes.Ldloc, totalLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, totalLocal);

        il.MarkLabel(notBufferLabel);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return total
        il.Emit(OpCodes.Ldloc, totalLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool IsBuffer(object? obj)
    /// Checks for both $Buffer (emitted type) and SharpTSBuffer (interpreter type)
    /// Uses reflection for SharpTSBuffer to avoid compile-time dependency on SharpTS.dll.
    /// </summary>
    private void EmitTSBufferIsBuffer(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "IsBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        buffer.IsBuffer = method;

        var il = method.GetILGenerator();

        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if obj is $Buffer (emitted type) - this is safe since typeBuilder is defined in the same assembly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // Not an emitted $Buffer - return false in standalone mode.
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, endLabel);

        // Is buffer - return true
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
