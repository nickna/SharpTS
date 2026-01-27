using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Buffer class for standalone Buffer support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsBufferDataField = null!;

    private void EmitTSBufferClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Buffer
        var typeBuilder = moduleBuilder.DefineType(
            "$Buffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSBufferType = typeBuilder;

        // Field: private byte[] _data
        _tsBufferDataField = typeBuilder.DefineField("_data", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);

        // Constructor: public $Buffer(byte[] data)
        EmitTSBufferCtorBytes(typeBuilder, runtime);

        // Constructor: public $Buffer(int size)
        EmitTSBufferCtorSize(typeBuilder, runtime);

        // Property: public int Length
        EmitTSBufferLengthProperty(typeBuilder, runtime);

        // Static methods
        EmitTSBufferFromString(typeBuilder, runtime);
        EmitTSBufferFromArray(typeBuilder, runtime);
        EmitTSBufferAlloc(typeBuilder, runtime);
        EmitTSBufferAllocUnsafe(typeBuilder, runtime);
        EmitTSBufferConcat(typeBuilder, runtime);
        EmitCalculateBuffersTotalLength(typeBuilder, runtime);
        EmitTSBufferIsBuffer(typeBuilder, runtime);

        // Instance methods
        EmitTSBufferToStringMethod(typeBuilder, runtime);
        EmitTSBufferSlice(typeBuilder, runtime);
        EmitTSBufferGetData(typeBuilder, runtime);
        EmitTSBufferCopy(typeBuilder, runtime);
        EmitTSBufferCompare(typeBuilder, runtime);
        EmitTSBufferEquals(typeBuilder, runtime);
        EmitTSBufferFill(typeBuilder, runtime);
        EmitTSBufferWrite(typeBuilder, runtime);
        EmitTSBufferReadUInt8(typeBuilder, runtime);
        EmitTSBufferWriteUInt8(typeBuilder, runtime);
        EmitTSBufferToJSON(typeBuilder, runtime);

        // Multi-byte read methods
        EmitTSBufferReadInt8(typeBuilder, runtime);
        EmitTSBufferReadUInt16LE(typeBuilder, runtime);
        EmitTSBufferReadUInt16BE(typeBuilder, runtime);
        EmitTSBufferReadUInt32LE(typeBuilder, runtime);
        EmitTSBufferReadUInt32BE(typeBuilder, runtime);
        EmitTSBufferReadInt16LE(typeBuilder, runtime);
        EmitTSBufferReadInt16BE(typeBuilder, runtime);
        EmitTSBufferReadInt32LE(typeBuilder, runtime);
        EmitTSBufferReadInt32BE(typeBuilder, runtime);
        EmitTSBufferReadFloatLE(typeBuilder, runtime);
        EmitTSBufferReadFloatBE(typeBuilder, runtime);
        EmitTSBufferReadDoubleLE(typeBuilder, runtime);
        EmitTSBufferReadDoubleBE(typeBuilder, runtime);
        EmitTSBufferReadBigInt64LE(typeBuilder, runtime);
        EmitTSBufferReadBigInt64BE(typeBuilder, runtime);
        EmitTSBufferReadBigUInt64LE(typeBuilder, runtime);
        EmitTSBufferReadBigUInt64BE(typeBuilder, runtime);

        // Multi-byte write methods
        EmitTSBufferWriteInt8(typeBuilder, runtime);
        EmitTSBufferWriteUInt16LE(typeBuilder, runtime);
        EmitTSBufferWriteUInt16BE(typeBuilder, runtime);
        EmitTSBufferWriteUInt32LE(typeBuilder, runtime);
        EmitTSBufferWriteUInt32BE(typeBuilder, runtime);
        EmitTSBufferWriteInt16LE(typeBuilder, runtime);
        EmitTSBufferWriteInt16BE(typeBuilder, runtime);
        EmitTSBufferWriteInt32LE(typeBuilder, runtime);
        EmitTSBufferWriteInt32BE(typeBuilder, runtime);
        EmitTSBufferWriteFloatLE(typeBuilder, runtime);
        EmitTSBufferWriteFloatBE(typeBuilder, runtime);
        EmitTSBufferWriteDoubleLE(typeBuilder, runtime);
        EmitTSBufferWriteDoubleBE(typeBuilder, runtime);
        EmitTSBufferWriteBigInt64LE(typeBuilder, runtime);
        EmitTSBufferWriteBigInt64BE(typeBuilder, runtime);
        EmitTSBufferWriteBigUInt64LE(typeBuilder, runtime);
        EmitTSBufferWriteBigUInt64BE(typeBuilder, runtime);

        // Search methods
        EmitTSBufferIndexOf(typeBuilder, runtime);
        EmitTSBufferIncludes(typeBuilder, runtime);

        // Swap methods
        EmitTSBufferSwap16(typeBuilder, runtime);
        EmitTSBufferSwap32(typeBuilder, runtime);
        EmitTSBufferSwap64(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Buffer(byte[] data)
    /// </summary>
    private void EmitTSBufferCtorBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.MakeArrayType(_types.Byte)]
        );
        runtime.TSBufferCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _data = data
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsBufferDataField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Buffer(int size)
    /// </summary>
    private void EmitTSBufferCtorSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        runtime.TSBufferCtorSize = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _data = new byte[size]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stfld, _tsBufferDataField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int Length { get; }
    /// </summary>
    private void EmitTSBufferLengthProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TSBufferLengthGetter = method;

        var il = method.GetILGenerator();

        // return _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        // Define property
        var property = typeBuilder.DefineProperty(
            "Length",
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes
        );
        property.SetGetMethod(method);
    }

    /// <summary>
    /// Emits: public static $Buffer FromString(string data, string encoding)
    /// </summary>
    private void EmitTSBufferFromString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FromString",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.String, _types.String]
        );
        runtime.TSBufferFromString = method;

        var il = method.GetILGenerator();

        var encodingLocal = il.DeclareLocal(_types.String);
        var utf8Label = il.DefineLabel();
        var asciiLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var hexLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Lowercase the encoding
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check encodings
        CheckStringEquals(il, encodingLocal, "utf8", utf8Label);
        CheckStringEquals(il, encodingLocal, "utf-8", utf8Label);
        CheckStringEquals(il, encodingLocal, "ascii", asciiLabel);
        CheckStringEquals(il, encodingLocal, "base64", base64Label);
        CheckStringEquals(il, encodingLocal, "hex", hexLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        // UTF-8
        il.MarkLabel(utf8Label);
        EmitEncodingGetBytes(il, "UTF8", runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        // ASCII
        il.MarkLabel(asciiLabel);
        EmitEncodingGetBytes(il, "ASCII", runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        // Base64
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        // Hex
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        // Default to UTF-8
        il.MarkLabel(defaultLabel);
        EmitEncodingGetBytes(il, "UTF8", runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitEncodingGetBytes(ILGenerator il, string encodingProperty, ConstructorBuilder bufferCtor)
    {
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty(encodingProperty)!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Newobj, bufferCtor);
    }

    private void CheckStringEquals(ILGenerator il, LocalBuilder local, string value, Label target)
    {
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Emits: public static $Buffer FromArray(List<object?> array)
    /// </summary>
    private void EmitTSBufferFromArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "FromArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.ListOfObject]
        );
        runtime.TSBufferFromArray = method;

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
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer Alloc(int size)
    /// </summary>
    private void EmitTSBufferAlloc(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Alloc",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.Int32]
        );
        runtime.TSBufferAlloc = method;

        var il = method.GetILGenerator();

        // return new $Buffer(size) - constructor already creates zero-initialized array
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtorSize);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer AllocUnsafe(int size)
    /// </summary>
    private void EmitTSBufferAllocUnsafe(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AllocUnsafe",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.Int32]
        );
        runtime.TSBufferAllocUnsafe = method;

        var il = method.GetILGenerator();

        // return new $Buffer(size) - same as Alloc in .NET (always initialized)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtorSize);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $Buffer Concat(List<object?> buffers, int totalLength)
    /// </summary>
    private void EmitTSBufferConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Concat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.ListOfObject, _types.Int32]
        );
        runtime.TSBufferConcat = method;

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
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);

        // offset += buf._data.Length
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Castclass, typeBuilder);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
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
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static int CalculateBuffersTotalLength(List<object?> buffers)
    /// Helper method to calculate total length of buffers for Buffer.concat()
    /// </summary>
    private void EmitCalculateBuffersTotalLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CalculateBuffersTotalLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.ListOfObject]
        );
        runtime.CalculateBuffersTotalLength = method;

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
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
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
    /// </summary>
    private void EmitTSBufferIsBuffer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsBuffer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.TSBufferIsBuffer = method;

        var il = method.GetILGenerator();

        // return obj is $Buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public string ToEncodedString(string encoding)
    /// Named differently from ToString to avoid reflection ambiguity with Object.ToString()
    /// </summary>
    private void EmitTSBufferToStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToEncodedString",
            MethodAttributes.Public,
            _types.String,
            [_types.String]
        );
        runtime.TSBufferToString = method;

        var il = method.GetILGenerator();

        var encodingLocal = il.DeclareLocal(_types.String);
        var utf8Label = il.DefineLabel();
        var asciiLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var hexLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Handle null encoding - default to "utf8"
        var hasEncodingLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, hasEncodingLabel);
        // Encoding is null - default to utf8
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stloc, encodingLocal);
        var afterEncodingLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterEncodingLabel);

        // Lowercase the encoding
        il.MarkLabel(hasEncodingLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);
        il.MarkLabel(afterEncodingLabel);

        // Check encodings
        CheckStringEquals(il, encodingLocal, "utf8", utf8Label);
        CheckStringEquals(il, encodingLocal, "utf-8", utf8Label);
        CheckStringEquals(il, encodingLocal, "ascii", asciiLabel);
        CheckStringEquals(il, encodingLocal, "base64", base64Label);
        CheckStringEquals(il, encodingLocal, "hex", hexLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        // UTF-8
        il.MarkLabel(utf8Label);
        EmitEncodingGetString(il, "UTF8");
        il.Emit(OpCodes.Ret);

        // ASCII
        il.MarkLabel(asciiLabel);
        EmitEncodingGetString(il, "ASCII");
        il.Emit(OpCodes.Ret);

        // Base64
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBase64String", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Ret);

        // Hex
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToHexString", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // Default to UTF-8
        il.MarkLabel(defaultLabel);
        EmitEncodingGetString(il, "UTF8");
        il.Emit(OpCodes.Ret);
    }

    private void EmitEncodingGetString(ILGenerator il, string encodingProperty)
    {
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty(encodingProperty)!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.MakeArrayType(_types.Byte)])!);
    }

    /// <summary>
    /// Emits: public $Buffer Slice(int start, int end)
    /// </summary>
    private void EmitTSBufferSlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Slice",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Int32, _types.Int32]
        );
        runtime.TSBufferSlice = method;

        var il = method.GetILGenerator();

        var lenLocal = il.DeclareLocal(_types.Int32);
        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // int len = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // Handle negative start index
        var startPositiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startPositiveLabel);

        // start = Math.Max(0, len + start)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);
        var afterStartLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterStartLabel);

        il.MarkLabel(startPositiveLabel);
        // start = Math.Min(start, len)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);

        il.MarkLabel(afterStartLabel);

        // Handle negative end index
        var endPositiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endPositiveLabel);

        // end = Math.Max(0, len + end)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);
        var afterEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterEndLabel);

        il.MarkLabel(endPositiveLabel);
        // end = Math.Min(end, len)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);

        il.MarkLabel(afterEndLabel);

        // Check if start >= end, return empty buffer
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Blt, notEmptyLabel);

        // return new $Buffer(new byte[0])
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmptyLabel);

        // var sliceLen = end - start
        var sliceLenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, sliceLenLocal);

        // var bytes = new byte[sliceLen]
        il.Emit(OpCodes.Ldloc, sliceLenLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Array.Copy(_data, start, bytes, 0, sliceLen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, sliceLenLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);

        // return new $Buffer(bytes)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public byte[] GetData() - for internal use
    /// </summary>
    private void EmitTSBufferGetData(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetData",
            MethodAttributes.Public,
            _types.MakeArrayType(_types.Byte),
            Type.EmptyTypes
        );
        runtime.TSBufferGetData = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int Copy($Buffer target, int targetStart, int sourceStart, int sourceEnd)
    /// </summary>
    private void EmitTSBufferCopy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Copy",
            MethodAttributes.Public,
            _types.Int32,
            [typeBuilder, _types.Int32, _types.Int32, _types.Int32]
        );
        runtime.TSBufferCopy = method;

        var il = method.GetILGenerator();

        // Locals
        var sourceEndLocal = il.DeclareLocal(_types.Int32);
        var targetStartLocal = il.DeclareLocal(_types.Int32);
        var sourceStartLocal = il.DeclareLocal(_types.Int32);
        var bytesToCopyLocal = il.DeclareLocal(_types.Int32);
        var thisLenLocal = il.DeclareLocal(_types.Int32);
        var targetLenLocal = il.DeclareLocal(_types.Int32);

        // thisLen = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, thisLenLocal);

        // targetLen = target._data.Length
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, targetLenLocal);

        // Clamp targetStart: Math.Max(0, Math.Min(targetStart, targetLen))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, targetLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, targetStartLocal);

        // Clamp sourceStart: Math.Max(0, Math.Min(sourceStart, thisLen))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, sourceStartLocal);

        // Clamp sourceEnd: Math.Max(sourceStart, Math.Min(sourceEnd, thisLen))
        il.Emit(OpCodes.Ldloc, sourceStartLocal);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, sourceEndLocal);

        // bytesToCopy = Math.Min(sourceEnd - sourceStart, targetLen - targetStart)
        il.Emit(OpCodes.Ldloc, sourceEndLocal);
        il.Emit(OpCodes.Ldloc, sourceStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, targetLenLocal);
        il.Emit(OpCodes.Ldloc, targetStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, bytesToCopyLocal);

        // if (bytesToCopy <= 0) return 0
        var copyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bytesToCopyLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, copyLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(copyLabel);

        // Array.Copy(this._data, sourceStart, target._data, targetStart, bytesToCopy)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, sourceStartLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, targetStartLocal);
        il.Emit(OpCodes.Ldloc, bytesToCopyLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);

        // return bytesToCopy
        il.Emit(OpCodes.Ldloc, bytesToCopyLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int Compare($Buffer other)
    /// </summary>
    private void EmitTSBufferCompare(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Compare",
            MethodAttributes.Public,
            _types.Int32,
            [typeBuilder]
        );
        runtime.TSBufferCompare = method;

        var il = method.GetILGenerator();

        // Locals
        var thisLenLocal = il.DeclareLocal(_types.Int32);
        var otherLenLocal = il.DeclareLocal(_types.Int32);
        var minLenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);

        // thisLen = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, thisLenLocal);

        // otherLen = other._data.Length
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, otherLenLocal);

        // minLen = Math.Min(thisLen, otherLen)
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Ldloc, otherLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, minLenLocal);

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var checkGreater = il.DefineLabel();
        var continueLoop = il.DefineLabel();

        // Loop: for (int i = 0; i < minLen; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, minLenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (this._data[i] < other._data[i]) return -1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Bge_Un, checkGreater);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);

        // if (this._data[i] > other._data[i]) return 1
        il.MarkLabel(checkGreater);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ble_Un, continueLoop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // i++
        il.MarkLabel(continueLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return thisLen.CompareTo(otherLen)
        il.Emit(OpCodes.Ldloca, thisLenLocal);
        il.Emit(OpCodes.Ldloc, otherLenLocal);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("CompareTo", [typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public bool BufferEquals($Buffer other)
    /// </summary>
    private void EmitTSBufferEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BufferEquals",
            MethodAttributes.Public,
            _types.Boolean,
            [typeBuilder]
        );
        runtime.TSBufferEquals = method;

        var il = method.GetILGenerator();

        // Locals
        var thisLenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);

        // if (other == null) return false
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);

        // thisLen = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, thisLenLocal);

        // if (thisLen != other._data.Length) return false
        var sameLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Beq, sameLengthLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sameLengthLabel);

        // int i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = 0; i < thisLen; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (this._data[i] != other._data[i]) return false
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Beq, continueLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // i++
        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Buffer Fill(object value, int start, int end, string encoding)
    /// </summary>
    private void EmitTSBufferFill(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Fill",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object, _types.Int32, _types.Int32, _types.String]
        );
        runtime.TSBufferFill = method;

        var il = method.GetILGenerator();

        // Locals
        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var thisLenLocal = il.DeclareLocal(_types.Int32);
        var fillBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var indexLocal = il.DeclareLocal(_types.Int32);

        // thisLen = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, thisLenLocal);

        // Clamp start: Math.Max(0, Math.Min(start, thisLen))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, startLocal);

        // Clamp end: Math.Max(start, Math.Min(end, thisLen))
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, endLocal);

        // if (start >= end) return this
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Blt, continueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLabel);

        // Determine fill bytes based on value type
        var isDoubleLabel = il.DefineLabel();
        var isStringLabel = il.DefineLabel();
        var afterFillBytesLabel = il.DefineLabel();

        // if (value is double d)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, isStringLabel);

        // fillBytes = new byte[] { (byte)((int)d & 0xFF) }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Stloc, fillBytesLocal);
        il.Emit(OpCodes.Br, afterFillBytesLabel);

        // if (value is string s)
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        var defaultFillLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, defaultFillLabel);

        // fillBytes = Encoding.UTF8.GetBytes(s) (simplified - always use UTF8)
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, fillBytesLocal);

        // if fillBytes.Length == 0 return this
        var afterEmptyCheck = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fillBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, afterEmptyCheck);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(afterEmptyCheck);
        il.Emit(OpCodes.Br, afterFillBytesLabel);

        // default: fillBytes = new byte[] { 0 }
        il.MarkLabel(defaultFillLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, fillBytesLocal);

        il.MarkLabel(afterFillBytesLabel);

        // Loop: for (int i = start; i < end; i++)
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // this._data[i] = fillBytes[(i - start) % fillBytes.Length]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, fillBytesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, fillBytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Stelem_I1);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int Write(string data, int offset, int length, string encoding)
    /// </summary>
    private void EmitTSBufferWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Int32,
            [_types.String, _types.Int32, _types.Int32, _types.String]
        );
        runtime.TSBufferWrite = method;

        var il = method.GetILGenerator();

        // Locals
        var encodedLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var maxWriteLocal = il.DeclareLocal(_types.Int32);
        var bytesToWriteLocal = il.DeclareLocal(_types.Int32);
        var thisLenLocal = il.DeclareLocal(_types.Int32);

        // thisLen = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, thisLenLocal);

        // Encode string to bytes (always UTF8 for simplicity)
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, encodedLocal);

        // maxWrite = thisLen - offset
        il.Emit(OpCodes.Ldloc, thisLenLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, maxWriteLocal);

        // if (maxWrite <= 0) return 0
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, maxWriteLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, continueLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLabel);

        // bytesToWrite = Math.Min(Math.Min(length, encoded.Length), maxWrite)
        // Handle length = -1 (indicates no limit) by using encoded.Length
        var hasLengthLabel = il.DefineLabel();
        var afterLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Bne_Un, hasLengthLabel);

        // No length specified, use encoded.Length
        il.Emit(OpCodes.Ldloc, encodedLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, maxWriteLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, bytesToWriteLocal);
        il.Emit(OpCodes.Br, afterLengthLabel);

        il.MarkLabel(hasLengthLabel);
        // Min(length, encoded.Length), then min with maxWrite
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, encodedLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ldloc, maxWriteLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, bytesToWriteLocal);

        il.MarkLabel(afterLengthLabel);

        // Array.Copy(encoded, 0, this._data, offset, bytesToWrite)
        il.Emit(OpCodes.Ldloc, encodedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, bytesToWriteLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);

        // return bytesToWrite
        il.Emit(OpCodes.Ldloc, bytesToWriteLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public double ReadUInt8(int offset)
    /// Returns the byte value at offset as a double.
    /// </summary>
    private void EmitTSBufferReadUInt8(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadUInt8",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );
        runtime.TSBufferReadUInt8 = method;

        var il = method.GetILGenerator();
        var boundsOkLabel = il.DefineLabel();

        // Check bounds: offset < 0 || offset >= _data.Length
        // if (offset < 0) throw
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, boundsOkLabel); // if offset >= 0, check length

        // offset < 0, throw
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(ArgumentOutOfRangeException), [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(boundsOkLabel);

        // if (offset >= _data.Length) throw
        var lengthOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldarg_0);  // this
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);  // _data
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, lengthOkLabel);  // if offset < length, ok

        // offset >= length, throw
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(typeof(ArgumentOutOfRangeException), [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(lengthOkLabel);

        // return (double)_data[offset]
        il.Emit(OpCodes.Ldarg_0);  // this
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);  // _data
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldelem_U1);  // byte value
        il.Emit(OpCodes.Conv_R8);  // convert to double
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int WriteUInt8(double value, int offset)
    /// </summary>
    private void EmitTSBufferWriteUInt8(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WriteUInt8",
            MethodAttributes.Public,
            _types.Int32,
            [_types.Double, _types.Int32]
        );
        runtime.TSBufferWriteUInt8 = method;

        var il = method.GetILGenerator();

        // Check bounds
        var boundsOkLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, boundsOkLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Offset is out of bounds");
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(boundsOkLabel);

        // this._data[offset] = (byte)((int)value & 0xFF)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // return offset + 1
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object ToJSON()
    /// Returns a dictionary with "type" = "Buffer" and "data" = array of bytes
    /// </summary>
    private void EmitTSBufferToJSON(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToJSON",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TSBufferToJSON = method;

        var il = method.GetILGenerator();

        // Locals
        var dataListLocal = il.DeclareLocal(_types.ListOfObject);
        var resultDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);

        // len = this._data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // var dataList = new List<object?>(len)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor([_types.Int32])!);
        il.Emit(OpCodes.Stloc, dataListLocal);

        // Loop: for (int i = 0; i < len; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // dataList.Add((double)this._data[i])
        il.Emit(OpCodes.Ldloc, dataListLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // var resultDict = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultDictLocal);

        // resultDict["type"] = "Buffer"
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldstr, "Buffer");
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // resultDict["data"] = dataList
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Ldloc, dataListLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // return resultDict
        il.Emit(OpCodes.Ldloc, resultDictLocal);
        il.Emit(OpCodes.Ret);
    }

    #region Multi-byte Read Methods

    /// <summary>
    /// Helper to emit bounds validation for multi-byte reads.
    /// </summary>
    private void EmitBoundsCheck(ILGenerator il, int byteCount)
    {
        // Calculate maxOffset = _data.Length - byteCount
        // if (offset < 0 || offset > maxOffset) throw

        var okLabel = il.DefineLabel();
        var lenLocal = il.DeclareLocal(_types.Int32);
        var maxOffsetLocal = il.DeclareLocal(_types.Int32);

        // lenLocal = _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsBufferDataField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // maxOffset = len - byteCount
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, byteCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, maxOffsetLocal);

        // if (offset >= 0 && offset <= maxOffset) goto ok
        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel(il, byteCount, maxOffsetLocal));

        il.Emit(OpCodes.Ldarg_1);  // offset
        il.Emit(OpCodes.Ldloc, maxOffsetLocal);
        il.Emit(OpCodes.Ble, okLabel);

        // throw
        il.Emit(OpCodes.Ldstr, "offset");
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);

        Label throwLabel(ILGenerator ilGen, int bytes, LocalBuilder maxOff)
        {
            var lbl = ilGen.DefineLabel();
            return lbl;
        }
    }

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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
    }

    #endregion

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
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
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
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
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
