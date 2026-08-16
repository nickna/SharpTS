using System.Buffers.Binary;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $DataView type for standalone DLLs.
    /// Provides a low-level interface for reading/writing binary data in ArrayBuffer.
    /// </summary>
    private void EmitDataViewType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$DataView",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.DataViewType = typeBuilder;

        // Fields
        var bufferField = typeBuilder.DefineField("_buffer", typeof(byte[]), FieldAttributes.Private | FieldAttributes.InitOnly);
        var byteOffsetField = typeBuilder.DefineField("_byteOffset", _types.Int32, FieldAttributes.Private | FieldAttributes.InitOnly);
        var byteLengthField = typeBuilder.DefineField("_byteLength", _types.Int32, FieldAttributes.Private | FieldAttributes.InitOnly);
        var arrayBufferField = typeBuilder.DefineField("_arrayBuffer", _types.Object, FieldAttributes.Private | FieldAttributes.InitOnly);

        // Constructor: public $DataView(object buffer, int byteOffset, int? byteLength)
        EmitDataViewConstructor(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, arrayBufferField);

        // Properties: ByteLength, ByteOffset, Buffer
        EmitDataViewProperties(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, arrayBufferField);

        // Getter methods: GetInt8, GetUint8, GetInt16, GetUint16, GetInt32, GetUint32, GetFloat32, GetFloat64
        EmitDataViewGetters(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // Setter methods: SetInt8, SetUint8, SetInt16, SetUint16, SetInt32, SetUint32, SetFloat32, SetFloat64
        EmitDataViewSetters(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitDataViewConstructor(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder bufferField,
        FieldBuilder byteOffsetField,
        FieldBuilder byteLengthField,
        FieldBuilder arrayBufferField)
    {
        // Constructor: public $DataView(object buffer, int byteOffset, int? byteLength)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Int32, typeof(int?)]
        );
        runtime.DataViewCtor = ctor;

        var il = ctor.GetILGenerator();

        var byteLenLocal = il.DeclareLocal(_types.Int32); // bufferByteLength
        var maxLenLocal = il.DeclareLocal(_types.Int32);  // maxLength
        var actualLenLocal = il.DeclareLocal(_types.Int32); // actualLength
        var byteArrayLocal = il.DeclareLocal(typeof(byte[])); // backing array

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // Store buffer reference: _arrayBuffer = buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, arrayBufferField);

        // Get ByteLength from buffer - try $ArrayBuffer.ByteLength property first
        var getBufferByteLengthLabel = il.DefineLabel();
        var afterByteLengthLabel = il.DefineLabel();

        // Check if buffer is $ArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
        il.Emit(OpCodes.Brfalse, getBufferByteLengthLabel);

        // It's $ArrayBuffer - call get_ByteLength
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Stloc, byteLenLocal);

        // Get the backing array via GetBuffer()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferGetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Br, afterByteLengthLabel);

        il.MarkLabel(getBufferByteLengthLabel);

        // Check if buffer is $SharedArrayBuffer
        var unsupportedBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Brfalse, unsupportedBufferLabel);

        // It's $SharedArrayBuffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferByteLengthGetter);
        il.Emit(OpCodes.Stloc, byteLenLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
        il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferGetBuffer);
        il.Emit(OpCodes.Stloc, byteArrayLocal);
        il.Emit(OpCodes.Br, afterByteLengthLabel);

        il.MarkLabel(unsupportedBufferLabel);
        il.Emit(OpCodes.Ldstr, "DataView requires emitted ArrayBuffer/SharedArrayBuffer.");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(afterByteLengthLabel);

        // Validate byteOffset: if (byteOffset < 0 || byteOffset > bufferByteLength) throw
        var offsetValid1 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, offsetValid1);
        il.Emit(OpCodes.Ldstr, "RangeError: Invalid DataView offset");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(offsetValid1);

        var offsetValid2 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, byteLenLocal);
        il.Emit(OpCodes.Ble, offsetValid2);
        il.Emit(OpCodes.Ldstr, "RangeError: Invalid DataView offset");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(offsetValid2);

        // maxLength = bufferByteLength - byteOffset
        il.Emit(OpCodes.Ldloc, byteLenLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, maxLenLocal);

        // Determine actualLength from byteLength parameter
        var hasLengthLabel = il.DefineLabel();
        var afterActualLengthLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarga, 3); // byteLength (int?)
        il.Emit(OpCodes.Call, typeof(int?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, hasLengthLabel);

        // Has value - use it
        il.Emit(OpCodes.Ldarga, 3);
        il.Emit(OpCodes.Call, typeof(int?).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, actualLenLocal);
        il.Emit(OpCodes.Br, afterActualLengthLabel);

        il.MarkLabel(hasLengthLabel);
        // No value - use maxLength
        il.Emit(OpCodes.Ldloc, maxLenLocal);
        il.Emit(OpCodes.Stloc, actualLenLocal);

        il.MarkLabel(afterActualLengthLabel);

        // Validate actualLength: if (actualLength < 0 || actualLength > maxLength) throw
        var lengthValid1 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, actualLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, lengthValid1);
        il.Emit(OpCodes.Ldstr, "RangeError: Invalid DataView length");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(lengthValid1);

        var lengthValid2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, actualLenLocal);
        il.Emit(OpCodes.Ldloc, maxLenLocal);
        il.Emit(OpCodes.Ble, lengthValid2);
        il.Emit(OpCodes.Ldstr, "RangeError: Invalid DataView length");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(lengthValid2);

        // Store fields
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, byteArrayLocal);
        il.Emit(OpCodes.Stfld, bufferField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, byteOffsetField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualLenLocal);
        il.Emit(OpCodes.Stfld, byteLengthField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitDataViewProperties(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder bufferField,
        FieldBuilder byteOffsetField,
        FieldBuilder byteLengthField,
        FieldBuilder arrayBufferField)
    {
        // ByteLength property
        var byteLengthProp = typeBuilder.DefineProperty("ByteLength", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var byteLengthGetter = typeBuilder.DefineMethod("get_ByteLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32, Type.EmptyTypes);
        var il = byteLengthGetter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteLengthField);
        il.Emit(OpCodes.Ret);
        byteLengthProp.SetGetMethod(byteLengthGetter);
        runtime.DataViewByteLengthGetter = byteLengthGetter;

        // ByteOffset property
        var byteOffsetProp = typeBuilder.DefineProperty("ByteOffset", PropertyAttributes.None, _types.Int32, Type.EmptyTypes);
        var byteOffsetGetter = typeBuilder.DefineMethod("get_ByteOffset",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32, Type.EmptyTypes);
        il = byteOffsetGetter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ret);
        byteOffsetProp.SetGetMethod(byteOffsetGetter);
        runtime.DataViewByteOffsetGetter = byteOffsetGetter;

        // Buffer property
        var bufferProp = typeBuilder.DefineProperty("Buffer", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var bufferGetter = typeBuilder.DefineMethod("get_Buffer",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        il = bufferGetter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrayBufferField);
        il.Emit(OpCodes.Ret);
        bufferProp.SetGetMethod(bufferGetter);
        runtime.DataViewBufferGetter = bufferGetter;
    }

    private void EmitDataViewGetters(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder bufferField,
        FieldBuilder byteOffsetField,
        FieldBuilder byteLengthField)
    {
        // GetInt8(int byteOffset) -> double
        EmitDataViewGet8(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetInt8", true);

        // GetUint8(int byteOffset) -> double
        EmitDataViewGet8(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetUint8", false);

        // GetInt16(int byteOffset, bool littleEndian) -> double
        EmitDataViewGet16(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetInt16", true);

        // GetUint16(int byteOffset, bool littleEndian) -> double
        EmitDataViewGet16(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetUint16", false);

        // GetInt32(int byteOffset, bool littleEndian) -> double
        EmitDataViewGet32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetInt32", true);

        // GetUint32(int byteOffset, bool littleEndian) -> double
        EmitDataViewGet32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "GetUint32", false);

        // GetFloat32(int byteOffset, bool littleEndian) -> double
        EmitDataViewGetFloat32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // GetFloat64(int byteOffset, bool littleEndian) -> double
        EmitDataViewGetFloat64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // GetBigInt64(int byteOffset, bool littleEndian) -> object (BigInteger)
        EmitDataViewGetBigInt64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, true);

        // GetBigUint64(int byteOffset, bool littleEndian) -> object (BigInteger)
        EmitDataViewGetBigInt64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, false);
    }

    private void EmitDataViewGet8(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32]
        );

        var il = method.GetILGenerator();

        // Bounds check: if (byteOffset < 0 || byteOffset >= _byteLength) throw
        EmitBoundsCheck(il, byteLengthField, 1);

        // return (sbyte/byte) _buffer[_byteOffset + byteOffset]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        if (signed)
        {
            il.Emit(OpCodes.Conv_I1); // Convert to sbyte
        }
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        if (methodName == "GetInt8") runtime.DataViewGetInt8 = method;
        else runtime.DataViewGetUint8 = method;
    }

    private void EmitDataViewGet16(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32, _types.Boolean]
        );

        var il = method.GetILGenerator();

        // Bounds check
        EmitBoundsCheck(il, byteLengthField, 2);

        // Use array indexing directly instead of Span - simpler and avoids conversion issues
        // Calculate absolute offset: _byteOffset + byteOffset
        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Branch on littleEndian
        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: (buffer[offset] << 8) | buffer[offset + 1]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Or);
        if (signed)
            il.Emit(OpCodes.Conv_I2); // Sign extend to short
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: buffer[offset] | (buffer[offset + 1] << 8)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        if (signed)
            il.Emit(OpCodes.Conv_I2); // Sign extend to short
        il.Emit(OpCodes.Conv_R8);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (methodName == "GetInt16") runtime.DataViewGetInt16 = method;
        else runtime.DataViewGetUint16 = method;
    }

    private void EmitDataViewGet32(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32, _types.Boolean]
        );

        var il = method.GetILGenerator();

        // Bounds check
        EmitBoundsCheck(il, byteLengthField, 4);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: (b[0]<<24) | (b[1]<<16) | (b[2]<<8) | b[3]
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Or);
        if (signed)
            il.Emit(OpCodes.Conv_R8);
        else
        {
            // Zero-extend to 64-bit to preserve unsigned value before converting to double
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Conv_R8);
        }
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: b[0] | (b[1]<<8) | (b[2]<<16) | (b[3]<<24)
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        if (signed)
            il.Emit(OpCodes.Conv_R8);
        else
        {
            // Zero-extend to 64-bit to preserve unsigned value before converting to double
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Conv_R8);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (methodName == "GetInt32") runtime.DataViewGetInt32 = method;
        else runtime.DataViewGetUint32 = method;
    }

    private void EmitLoadByte(ILGenerator il, FieldBuilder bufferField, LocalBuilder offsetLocal, int add)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        if (add > 0)
        {
            il.Emit(OpCodes.Ldc_I4, add);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldelem_U1);
    }

    private void EmitDataViewGetFloat32(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField)
    {
        var method = typeBuilder.DefineMethod(
            "GetFloat32",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 4);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Use BitConverter.ToSingle with proper byte order
        // BitConverter always uses system endianness (little on x86/x64)
        // For big endian, we need to reverse the bytes first
        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: reverse bytes manually then use BitConverter
        // Build int from bytes: (b[0]<<24) | (b[1]<<16) | (b[2]<<8) | b[3]
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("Int32BitsToSingle", [typeof(int)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: BitConverter.ToSingle(buffer, offset) works directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToSingle", [typeof(byte[]), typeof(int)])!);
        il.Emit(OpCodes.Conv_R8);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.DataViewGetFloat32 = method;
    }

    private void EmitDataViewGetFloat64(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField)
    {
        var method = typeBuilder.DefineMethod(
            "GetFloat64",
            MethodAttributes.Public,
            _types.Double,
            [_types.Int32, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 8);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: build int64 from bytes and convert
        var int64Local = il.DeclareLocal(typeof(long));
        // (b[0]<<56) | (b[1]<<48) | (b[2]<<40) | (b[3]<<32) | (b[4]<<24) | (b[5]<<16) | (b[6]<<8) | b[7]
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 56);
        il.Emit(OpCodes.Shl);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 48);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 40);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 4);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 5);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 6);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 7);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("Int64BitsToDouble", [typeof(long)])!);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: BitConverter.ToDouble(buffer, offset) works directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToDouble", [typeof(byte[]), typeof(int)])!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.DataViewGetFloat64 = method;
    }

    private void EmitDataViewGetBigInt64(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        bool signed)
    {
        var methodName = signed ? "GetBigInt64" : "GetBigUint64";
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Object, // Returns boxed BigInteger
            [_types.Int32, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 8);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: build int64 from bytes
        // (b[0]<<56) | (b[1]<<48) | (b[2]<<40) | (b[3]<<32) | (b[4]<<24) | (b[5]<<16) | (b[6]<<8) | b[7]
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 56);
        il.Emit(OpCodes.Shl);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 48);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 40);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 4);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 5);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 6);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 7);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Or);
        // Convert long to BigInteger
        if (signed)
        {
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(long)])!);
        }
        else
        {
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(ulong)])!);
        }
        il.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: BitConverter approach or manual byte assembly
        // b[0] | (b[1]<<8) | ... | (b[7]<<56)
        EmitLoadByte(il, bufferField, offsetLocal, 0);
        il.Emit(OpCodes.Conv_I8);
        EmitLoadByte(il, bufferField, offsetLocal, 1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 2);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 3);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 4);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 5);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 40);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 6);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 48);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        EmitLoadByte(il, bufferField, offsetLocal, 7);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldc_I4, 56);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        // Convert long to BigInteger
        if (signed)
        {
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(long)])!);
        }
        else
        {
            il.Emit(OpCodes.Conv_U8);
            il.Emit(OpCodes.Newobj, typeof(System.Numerics.BigInteger).GetConstructor([typeof(ulong)])!);
        }
        il.Emit(OpCodes.Box, typeof(System.Numerics.BigInteger));

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (signed)
            runtime.DataViewGetBigInt64 = method;
        else
            runtime.DataViewGetBigUint64 = method;
    }

    private void EmitDataViewSetters(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField)
    {
        // SetInt8(int byteOffset, object value) -> void
        EmitDataViewSet8(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetInt8", true);

        // SetUint8(int byteOffset, object value) -> void
        EmitDataViewSet8(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetUint8", false);

        // SetInt16(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSet16(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetInt16", true);

        // SetUint16(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSet16(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetUint16", false);

        // SetInt32(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSet32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetInt32", true);

        // SetUint32(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSet32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, "SetUint32", false);

        // SetFloat32(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSetFloat32(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // SetFloat64(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSetFloat64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField);

        // SetBigInt64(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSetBigInt64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, true);

        // SetBigUint64(int byteOffset, object value, bool littleEndian) -> void
        EmitDataViewSetBigInt64(typeBuilder, runtime, bufferField, byteOffsetField, byteLengthField, false);
    }

    private void EmitDataViewSet8(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 1);

        // _buffer[_byteOffset + byteOffset] = (byte)ToInt32(value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);

        // Convert value to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Conv_U1);

        il.Emit(OpCodes.Stelem_I1);
        il.Emit(OpCodes.Ret);

        if (methodName == "SetInt8") runtime.DataViewSetInt8 = method;
        else runtime.DataViewSetUint8 = method;
    }

    private void EmitDataViewSet16(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 2);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Int32);

        // Calculate offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert value to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, valueLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: buffer[offset] = value >> 8; buffer[offset+1] = value & 0xFF
        EmitStoreByte(il, bufferField, offsetLocal, 0, valueLocal, 8); // high byte
        EmitStoreByteMasked(il, bufferField, offsetLocal, 1, valueLocal); // low byte
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: buffer[offset] = value & 0xFF; buffer[offset+1] = value >> 8
        EmitStoreByteMasked(il, bufferField, offsetLocal, 0, valueLocal); // low byte
        EmitStoreByte(il, bufferField, offsetLocal, 1, valueLocal, 8); // high byte

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (methodName == "SetInt16") runtime.DataViewSetInt16 = method;
        else runtime.DataViewSetUint16 = method;
    }

    private void EmitStoreByte(ILGenerator il, FieldBuilder bufferField, LocalBuilder offsetLocal, int add, LocalBuilder valueLocal, int shift)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        if (add > 0)
        {
            il.Emit(OpCodes.Ldc_I4, add);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4, shift);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
    }

    private void EmitStoreByteMasked(ILGenerator il, FieldBuilder bufferField, LocalBuilder offsetLocal, int add, LocalBuilder valueLocal)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        if (add > 0)
        {
            il.Emit(OpCodes.Ldc_I4, add);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4, 0xFF);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
    }

    private void EmitDataViewSet32(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        string methodName, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 4);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Int32);

        // Calculate offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert value to int
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        if (signed)
        {
            // For signed, convert double -> int32 directly
            il.Emit(OpCodes.Conv_I4);
        }
        else
        {
            // For unsigned, must go through uint64 first to handle values > Int32.MaxValue
            // Conv_I4 on doubles > Int32.MaxValue has undefined behavior per ECMA-335
            il.Emit(OpCodes.Conv_U8);  // double -> uint64 (handles values up to 2^64)
            il.Emit(OpCodes.Conv_I4);  // uint64 -> int32 (truncates to low 32 bits)
        }
        il.Emit(OpCodes.Stloc, valueLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: b[0]=v>>24, b[1]=v>>16, b[2]=v>>8, b[3]=v
        EmitStoreByte(il, bufferField, offsetLocal, 0, valueLocal, 24);
        EmitStoreByte(il, bufferField, offsetLocal, 1, valueLocal, 16);
        EmitStoreByte(il, bufferField, offsetLocal, 2, valueLocal, 8);
        EmitStoreByteMasked(il, bufferField, offsetLocal, 3, valueLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: b[0]=v, b[1]=v>>8, b[2]=v>>16, b[3]=v>>24
        EmitStoreByteMasked(il, bufferField, offsetLocal, 0, valueLocal);
        EmitStoreByte(il, bufferField, offsetLocal, 1, valueLocal, 8);
        EmitStoreByte(il, bufferField, offsetLocal, 2, valueLocal, 16);
        EmitStoreByte(il, bufferField, offsetLocal, 3, valueLocal, 24);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (methodName == "SetInt32") runtime.DataViewSetInt32 = method;
        else runtime.DataViewSetUint32 = method;
    }

    private void EmitDataViewSetFloat32(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField)
    {
        var method = typeBuilder.DefineMethod(
            "SetFloat32",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 4);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(_types.Int32); // Store as int bits

        // Calculate offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert value to float then to int bits
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToSingle", [typeof(object)])!);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("SingleToInt32Bits", [typeof(float)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian: b[0]=v>>24, b[1]=v>>16, b[2]=v>>8, b[3]=v
        EmitStoreByte(il, bufferField, offsetLocal, 0, valueLocal, 24);
        EmitStoreByte(il, bufferField, offsetLocal, 1, valueLocal, 16);
        EmitStoreByte(il, bufferField, offsetLocal, 2, valueLocal, 8);
        EmitStoreByteMasked(il, bufferField, offsetLocal, 3, valueLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian: b[0]=v, b[1]=v>>8, b[2]=v>>16, b[3]=v>>24
        EmitStoreByteMasked(il, bufferField, offsetLocal, 0, valueLocal);
        EmitStoreByte(il, bufferField, offsetLocal, 1, valueLocal, 8);
        EmitStoreByte(il, bufferField, offsetLocal, 2, valueLocal, 16);
        EmitStoreByte(il, bufferField, offsetLocal, 3, valueLocal, 24);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.DataViewSetFloat32 = method;
    }

    private void EmitDataViewSetFloat64(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField)
    {
        var method = typeBuilder.DefineMethod(
            "SetFloat64",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 8);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(typeof(long)); // Store as long bits

        // Calculate offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert value to double then to long bits
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("DoubleToInt64Bits", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian
        EmitStoreByte64(il, bufferField, offsetLocal, 0, valueLocal, 56);
        EmitStoreByte64(il, bufferField, offsetLocal, 1, valueLocal, 48);
        EmitStoreByte64(il, bufferField, offsetLocal, 2, valueLocal, 40);
        EmitStoreByte64(il, bufferField, offsetLocal, 3, valueLocal, 32);
        EmitStoreByte64(il, bufferField, offsetLocal, 4, valueLocal, 24);
        EmitStoreByte64(il, bufferField, offsetLocal, 5, valueLocal, 16);
        EmitStoreByte64(il, bufferField, offsetLocal, 6, valueLocal, 8);
        EmitStoreByte64Masked(il, bufferField, offsetLocal, 7, valueLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian
        EmitStoreByte64Masked(il, bufferField, offsetLocal, 0, valueLocal);
        EmitStoreByte64(il, bufferField, offsetLocal, 1, valueLocal, 8);
        EmitStoreByte64(il, bufferField, offsetLocal, 2, valueLocal, 16);
        EmitStoreByte64(il, bufferField, offsetLocal, 3, valueLocal, 24);
        EmitStoreByte64(il, bufferField, offsetLocal, 4, valueLocal, 32);
        EmitStoreByte64(il, bufferField, offsetLocal, 5, valueLocal, 40);
        EmitStoreByte64(il, bufferField, offsetLocal, 6, valueLocal, 48);
        EmitStoreByte64(il, bufferField, offsetLocal, 7, valueLocal, 56);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        runtime.DataViewSetFloat64 = method;
    }

    private void EmitDataViewSetBigInt64(
        TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder bufferField, FieldBuilder byteOffsetField, FieldBuilder byteLengthField,
        bool signed)
    {
        var methodName = signed ? "SetBigInt64" : "SetBigUint64";
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32, _types.Object, _types.Boolean]
        );

        var il = method.GetILGenerator();

        EmitBoundsCheck(il, byteLengthField, 8);

        var offsetLocal = il.DeclareLocal(_types.Int32);
        var valueLocal = il.DeclareLocal(typeof(long)); // Store as long bits (same bit width as ulong)

        // Calculate offset
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteOffsetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, offsetLocal);

        // Convert BigInteger value to long/ulong
        // value is boxed BigInteger - call (long)value or (ulong)value
        // BigInteger has many op_Explicit overloads, find the one returning long or ulong
        var returnType = signed ? typeof(long) : typeof(ulong);
        var bigIntConvertMethod = typeof(System.Numerics.BigInteger)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "op_Explicit"
                && m.ReturnType == returnType
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Call, bigIntConvertMethod);
        // Both long and ulong have the same IL stack representation, so we can store in long local
        il.Emit(OpCodes.Stloc, valueLocal);

        var littleEndianLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, littleEndianLabel);

        // Big endian
        EmitStoreByte64(il, bufferField, offsetLocal, 0, valueLocal, 56);
        EmitStoreByte64(il, bufferField, offsetLocal, 1, valueLocal, 48);
        EmitStoreByte64(il, bufferField, offsetLocal, 2, valueLocal, 40);
        EmitStoreByte64(il, bufferField, offsetLocal, 3, valueLocal, 32);
        EmitStoreByte64(il, bufferField, offsetLocal, 4, valueLocal, 24);
        EmitStoreByte64(il, bufferField, offsetLocal, 5, valueLocal, 16);
        EmitStoreByte64(il, bufferField, offsetLocal, 6, valueLocal, 8);
        EmitStoreByte64Masked(il, bufferField, offsetLocal, 7, valueLocal);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(littleEndianLabel);
        // Little endian
        EmitStoreByte64Masked(il, bufferField, offsetLocal, 0, valueLocal);
        EmitStoreByte64(il, bufferField, offsetLocal, 1, valueLocal, 8);
        EmitStoreByte64(il, bufferField, offsetLocal, 2, valueLocal, 16);
        EmitStoreByte64(il, bufferField, offsetLocal, 3, valueLocal, 24);
        EmitStoreByte64(il, bufferField, offsetLocal, 4, valueLocal, 32);
        EmitStoreByte64(il, bufferField, offsetLocal, 5, valueLocal, 40);
        EmitStoreByte64(il, bufferField, offsetLocal, 6, valueLocal, 48);
        EmitStoreByte64(il, bufferField, offsetLocal, 7, valueLocal, 56);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        if (signed)
            runtime.DataViewSetBigInt64 = method;
        else
            runtime.DataViewSetBigUint64 = method;
    }

    private void EmitStoreByte64(ILGenerator il, FieldBuilder bufferField, LocalBuilder offsetLocal, int add, LocalBuilder valueLocal, int shift)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        if (add > 0)
        {
            il.Emit(OpCodes.Ldc_I4, add);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4, shift);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
    }

    private void EmitStoreByte64Masked(ILGenerator il, FieldBuilder bufferField, LocalBuilder offsetLocal, int add, LocalBuilder valueLocal)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, offsetLocal);
        if (add > 0)
        {
            il.Emit(OpCodes.Ldc_I4, add);
            il.Emit(OpCodes.Add);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I8, 0xFFL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);
    }

    private void EmitBoundsCheck(ILGenerator il, FieldBuilder byteLengthField, int size)
    {
        var validLabel = il.DefineLabel();

        // if (byteOffset < 0) throw
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge_S, validLabel);

        il.Emit(OpCodes.Ldstr, "RangeError: Offset is outside the bounds of the DataView");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(validLabel);

        // if (byteOffset + size > _byteLength) throw
        var valid2Label = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, size);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, byteLengthField);
        il.Emit(OpCodes.Ble_S, valid2Label);

        il.Emit(OpCodes.Ldstr, "RangeError: Offset is outside the bounds of the DataView");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(valid2Label);
    }
}
