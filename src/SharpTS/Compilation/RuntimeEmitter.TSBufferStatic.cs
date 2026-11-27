using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using SharpTS.Runtime.Types;

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
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        method.SetCustomAttribute(runtime.FunctionAttributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
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
        il.Emit(OpCodes.Ldc_I4_1); // rejectUnknownEncoding
        il.Emit(OpCodes.Call, buffer.EncodeString);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notString);

        EmitTypedCase(buffer.Type, () => il.Emit(OpCodes.Call, buffer.FromBuffer));
        var view = il.DefineLabel();
        if (runtime.ArrayBuffer is { } arrayBuffer)
            EmitArrayBufferCase(arrayBuffer.Type, arrayBuffer.GetBuffer);
        if (runtime.SharedArrayBuffer is { } shared)
            EmitArrayBufferCase(shared.Type, shared.GetBuffer);
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

        if (runtime.ArrayBuffer is not null || runtime.SharedArrayBuffer is not null)
        {
            il.MarkLabel(view);
            EmitBufferViewFromArguments(il, runtime, buffer);
        }

        void EmitArrayBufferCase(Type type, MethodInfo getBytes)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, type);
            il.Emit(OpCodes.Callvirt, getBytes);
            il.Emit(OpCodes.Br, view);
            il.MarkLabel(next);
        }

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

    // The backing array is on the stack. Node compares unrounded values against
    // the available bytes before converting the accepted view bounds to integers.
    private void EmitBufferViewFromArguments(ILGenerator il, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        var data = il.DeclareLocal(_types.ByteArray);
        var offset = il.DeclareLocal(_types.Double);
        var length = il.DeclareLocal(_types.Double);
        var available = il.DeclareLocal(_types.Double);
        var numericOffset = il.DefineLabel();
        var offsetInBounds = il.DefineLabel();
        var explicitLength = il.DefineLabel();
        var lengthInBounds = il.DefineLabel();
        var validStart = il.DefineLabel();
        var positiveLength = il.DefineLabel();

        il.Emit(OpCodes.Stloc, data);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber);
        il.Emit(OpCodes.Stloc, offset);
        il.Emit(OpCodes.Ldloc, offset);
        il.Emit(OpCodes.Call, typeof(double).GetMethod(nameof(double.IsNaN))!);
        il.Emit(OpCodes.Brfalse, numericOffset);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Stloc, offset);
        il.MarkLabel(numericOffset);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, offset);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, available);
        il.Emit(OpCodes.Ldloc, available);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Bge, offsetInBounds);
        EmitBoundsError("offset");
        il.MarkLabel(offsetInBounds);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brfalse, explicitLength);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, offset);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Truncate), [typeof(double)])!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Br, lengthInBounds);

        il.MarkLabel(explicitLength);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.NumericCoercion.ToNumber);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldloc, available);
        il.Emit(OpCodes.Ble_Un, lengthInBounds);
        EmitBoundsError("length");

        il.MarkLabel(lengthInBounds);
        il.Emit(OpCodes.Ldloc, offset);
        il.Emit(OpCodes.Ldc_R8, -1d);
        il.Emit(OpCodes.Bgt, validStart);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor,
            "Invalid start offset");
        il.MarkLabel(validStart);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Bgt, positiveLength);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Stloc, length);
        il.MarkLabel(positiveLength);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ldloc, offset);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, buffer.ViewCtor);
        il.Emit(OpCodes.Ret);

        void EmitBoundsError(string argument)
        {
            il.Emit(OpCodes.Ldstr, $"\"{argument}\" is outside of buffer bounds");
            il.Emit(OpCodes.Newobj, runtime.Errors.RangeErrorConstructor);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, "ERR_BUFFER_OUT_OF_BOUNDS");
            il.Emit(OpCodes.Callvirt, runtime.Errors.CodeSetter);
            il.Emit(OpCodes.Call, runtime.Errors.CreateException);
            il.Emit(OpCodes.Throw);
        }
    }

    private void EmitBufferByteLengthValue(TypeBuilder owner, EmittedRuntime runtime, EmittedBufferRuntime buffer)
    {
        var method = owner.DefineMethod("BufferByteLengthValue", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        buffer.ByteLengthValue = method;
        var il = method.GetILGenerator();
        var text = il.DeclareLocal(_types.String);
        var encoding = il.DeclareLocal(_types.String);
        var length = il.DeclareLocal(_types.Int32);
        var notBuffer = il.DefineLabel();
        var isString = il.DefineLabel();
        var utf8 = il.DefineLabel();
        var singleByte = il.DefineLabel();
        var utf16le = il.DefineLabel();
        var base64 = il.DefineLabel();
        var hex = il.DefineLabel();
        var haveLength = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, buffer.Type);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notBuffer);
        il.Emit(OpCodes.Callvirt, buffer.LengthGetter);
        il.Emit(OpCodes.Br, haveLength);

        il.MarkLabel(notBuffer);
        il.Emit(OpCodes.Pop);

        if (runtime.TypedArrays.Implementation is { } arrays)
        {
            Type[] binaryTypes = [runtime.RequireArrayBuffer().Type, runtime.RequireSharedArrayBuffer().Type,
                arrays.BaseType, runtime.RequireDataView().Type];
            var readViewLength = il.DefineLabel();
            var notView = il.DefineLabel();
            foreach (var type in binaryTypes)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Isinst, type);
                il.Emit(OpCodes.Brtrue, readViewLength);
            }
            il.Emit(OpCodes.Br, notView);

            il.MarkLabel(readViewLength);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "byteLength");
            il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notView);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, text);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Brtrue, isString);
        EmitThrowTypeError(il, runtime.Errors, "Buffer.byteLength requires a string, Buffer, ArrayBuffer, or ArrayBuffer view");

        il.MarkLabel(isString);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Brfalse, singleByte);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Brfalse, utf8);
        // As in Node, encoding + "" uses default-hint primitive conversion.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Call, runtime.Operators.Add);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant"));
        il.Emit(OpCodes.Stloc, encoding);
        EmitBufferEncodingChecks(il, encoding, (utf8, singleByte, singleByte, utf16le, base64, hex));

        // Unknown encodings fall through to UTF-8.
        il.MarkLabel(utf8);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetByteCount", _types.String));
        il.Emit(OpCodes.Br, haveLength);

        il.MarkLabel(utf16le);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Br, haveLength);

        il.MarkLabel(hex);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Br, haveLength);

        il.MarkLabel(base64);
        EmitTrimBase64Padding(0);
        EmitTrimBase64Padding(1);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Shr_Un);
        il.Emit(OpCodes.Br, haveLength);

        il.MarkLabel(singleByte);
        il.Emit(OpCodes.Ldloc, length);
        il.MarkLabel(haveLength);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // Mirrors BufferEncoding.GetBase64ByteLength's two padding checks.
        void EmitTrimBase64Padding(int minimumLength)
        {
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldc_I4, minimumLength);
            il.Emit(OpCodes.Ble, done);
            il.Emit(OpCodes.Ldloc, text);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
            il.Emit(OpCodes.Ldc_I4, (int)'=');
            il.Emit(OpCodes.Bne_Un, done);
            il.Emit(OpCodes.Ldloc, length);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, length);
            il.MarkLabel(done);
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
        var encode = typeBuilder.DefineMethod("EncodeString", MethodAttributes.Assembly | MethodAttributes.Static,
            typeBuilder, [_types.String, _types.String, _types.Boolean]);
        buffer.EncodeString = encode;
        var wrapper = method.GetILGenerator();
        wrapper.Emit(OpCodes.Ldarg_0);
        wrapper.Emit(OpCodes.Ldarg_1);
        wrapper.Emit(OpCodes.Ldc_I4_0); // rejectUnknownEncoding
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
        allocIl.Emit(OpCodes.Ldc_I4_1); // rejectUnknownEncoding
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
        var hexLabel = il.DefineLabel();

        // Use UTF-8 when no encoding is specified, including the empty string.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty", [_types.String])!);
        il.Emit(OpCodes.Brtrue, utf8Label);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        EmitBufferEncodingChecks(il, encodingLocal, (utf8Label, asciiLabel, latin1Label, utf16leLabel, base64Label, hexLabel));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, utf8Label);
        il.Emit(OpCodes.Ldstr, "Unknown encoding: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Newobj, errors.TypeErrorConstructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "ERR_UNKNOWN_ENCODING");
        il.Emit(OpCodes.Callvirt, errors.CodeSetter);
        il.Emit(OpCodes.Call, errors.CreateException);
        il.Emit(OpCodes.Throw);

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

        // Base64 and Base64url accept both alphabets.
        il.MarkLabel(base64Label);
        EmitBufferBase64Bytes(il);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);

        // Hex
        il.MarkLabel(hexLabel);
        EmitBufferHexBytes(il);
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    // Keep the aliases in sync with BufferEncoding.Normalize.
    private void EmitBufferEncodingChecks(ILGenerator il, LocalBuilder encoding,
        (Label Utf8, Label Ascii, Label Latin1, Label Utf16le, Label Base64, Label Hex) target)
    {
        CheckStringEquals(il, encoding, "utf8", target.Utf8);
        CheckStringEquals(il, encoding, "utf-8", target.Utf8);
        CheckStringEquals(il, encoding, "ascii", target.Ascii);
        CheckStringEquals(il, encoding, "latin1", target.Latin1);
        CheckStringEquals(il, encoding, "binary", target.Latin1);
        CheckStringEquals(il, encoding, "utf16le", target.Utf16le);
        CheckStringEquals(il, encoding, "utf-16le", target.Utf16le);
        CheckStringEquals(il, encoding, "ucs2", target.Utf16le);
        CheckStringEquals(il, encoding, "ucs-2", target.Utf16le);
        CheckStringEquals(il, encoding, "base64", target.Base64);
        CheckStringEquals(il, encoding, "base64url", target.Base64);
        CheckStringEquals(il, encoding, "hex", target.Hex);
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

    // Mirrors BufferEncoding.DecodeBase64. Leaves the decoded byte array on the stack.
    private void EmitBufferBase64Bytes(ILGenerator il)
    {
        var stringBuilder = typeof(StringBuilder);
        var getLength = _types.GetProperty(_types.String, "Length")!.GetGetMethod()!;
        var builderLength = _types.GetProperty(stringBuilder, "Length")!;
        var appendChar = _types.GetMethod(stringBuilder, "Append", _types.Char);

        var builder = il.DeclareLocal(stringBuilder);
        var length = il.DeclareLocal(_types.Int32);
        var index = il.DeclareLocal(_types.Int32);
        var character = il.DeclareLocal(_types.Char);
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var normalizeSlash = il.DefineLabel();
        var checkAlphabet = il.DefineLabel();
        var finish = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getLength);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(stringBuilder, _types.Int32));
        il.Emit(OpCodes.Stloc, builder);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, finish);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Conv_U1); // Node reads the low byte of each UTF-16 code unit.
        il.Emit(OpCodes.Stloc, character);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Beq, finish);

        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Bne_Un, normalizeSlash);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Stloc, character);
        il.Emit(OpCodes.Br, checkAlphabet);
        il.MarkLabel(normalizeSlash);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Ldc_I4, (int)'_');
        il.Emit(OpCodes.Bne_Un, checkAlphabet);
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Stloc, character);

        il.MarkLabel(checkAlphabet);
        il.Emit(OpCodes.Ldstr, BufferEncoding.Base64Alphabet);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.Char));
        il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Ldloc, character);
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);

        var discardSextet = il.DefineLabel();
        var padTwo = il.DefineLabel();
        var padOne = il.DefineLabel();
        var decode = il.DefineLabel();
        il.MarkLabel(finish);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Callvirt, builderLength.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, length);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Switch, new[] { decode, discardSextet, padTwo, padOne });
        il.Emit(OpCodes.Br, decode);

        il.MarkLabel(discardSextet);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, builderLength.GetSetMethod()!);
        il.Emit(OpCodes.Br, decode);

        il.MarkLabel(padTwo);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(padOne);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Callvirt, appendChar);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(decode);
        il.Emit(OpCodes.Ldloc, builder);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(stringBuilder, "ToString", Type.EmptyTypes));
        il.Emit(OpCodes.Call, _types.ConvertFromBase64String);
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
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Array.Copy(source._data, 0, bytes, 0, source._data.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.GetData);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        il.Emit(OpCodes.Call, buffer.GetData);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // offset += buf._data.Length
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
        il.Emit(OpCodes.Call, buffer.LengthGetter);
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
