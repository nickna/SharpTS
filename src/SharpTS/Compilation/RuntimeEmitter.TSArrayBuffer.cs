using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $ArrayBuffer type for standalone DLLs.
    /// Replaces reflection-based SharpTSArrayBuffer creation.
    /// </summary>
    private void EmitArrayBufferType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$ArrayBuffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.ArrayBufferType = typeBuilder;

        // Field: byte[] _buffer
        var bufferField = typeBuilder.DefineField(
            "_buffer",
            typeof(byte[]),
            FieldAttributes.Private | FieldAttributes.InitOnly
        );

        // Field: bool _detached — set when this buffer is transferred away via postMessage's
        // transfer list (#999). Not InitOnly: Detach() mutates it.
        var detachedField = typeBuilder.DefineField(
            "_detached",
            typeof(bool),
            FieldAttributes.Private
        );

        // Constructor: public $ArrayBuffer(int byteLength)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        runtime.ArrayBufferCtor = ctor;

        var ctorIl = ctor.GetILGenerator();

        // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _buffer = new byte[byteLength]
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Newarr, typeof(byte));
        ctorIl.Emit(OpCodes.Stfld, bufferField);

        ctorIl.Emit(OpCodes.Ret);

        // Property: public int ByteLength => _detached ? 0 : _buffer.Length
        EmitArrayBufferByteLength(typeBuilder, runtime, bufferField, detachedField);

        // Method: public byte[] GetBuffer() => _buffer (internal access)
        EmitArrayBufferGetBuffer(typeBuilder, runtime, bufferField);

        // Method: public void Detach() => _detached = true (called by StructuredClone on transfer)
        EmitArrayBufferDetach(typeBuilder, detachedField);

        // Method: public $ArrayBuffer Slice(int begin, int end)
        EmitArrayBufferSlice(typeBuilder, runtime, bufferField, ctor);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public int ByteLength { get; } — returns 0 once detached (Node neuters a
    /// transferred ArrayBuffer; its byteLength becomes 0).
    /// </summary>
    private void EmitArrayBufferByteLength(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField, FieldBuilder detachedField)
    {
        var property = typeBuilder.DefineProperty(
            "ByteLength",
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes
        );

        var getter = typeBuilder.DefineMethod(
            "get_ByteLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.ArrayBufferByteLengthGetter = getter;

        var il = getter.GetILGenerator();
        var notDetached = il.DefineLabel();
        // if (!_detached) goto notDetached
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, detachedField);
        il.Emit(OpCodes.Brfalse, notDetached);
        // return 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDetached);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    /// <summary>
    /// Emits: public void Detach() => _detached = true.
    /// </summary>
    private void EmitArrayBufferDetach(TypeBuilder typeBuilder, FieldBuilder detachedField)
    {
        var method = typeBuilder.DefineMethod(
            "Detach",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, detachedField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public byte[] GetBuffer()
    /// For internal access to the underlying buffer.
    /// </summary>
    private void EmitArrayBufferGetBuffer(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField)
    {
        var method = typeBuilder.DefineMethod(
            "GetBuffer",
            MethodAttributes.Public,
            typeof(byte[]),
            Type.EmptyTypes
        );
        runtime.ArrayBufferGetBuffer = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $ArrayBuffer Slice(int begin, int end)
    /// Handles negative indices (count from end) and clamps to buffer bounds.
    /// </summary>
    private void EmitArrayBufferSlice(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod(
            "Slice",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Int32, _types.Int32]
        );
        runtime.ArrayBufferSlice = method;

        var il = method.GetILGenerator();

        var bufLenLocal = il.DeclareLocal(_types.Int32);
        var beginLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // var bufLen = _buffer.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bufLenLocal);

        // Handle negative begin: if (begin < 0) begin = bufLen + begin
        var beginPositiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, beginPositiveLabel);
        // begin is negative - add to bufLen
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, beginLocal);
        var afterBeginLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterBeginLabel);

        il.MarkLabel(beginPositiveLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, beginLocal);

        il.MarkLabel(afterBeginLabel);

        // Handle end: if (end > bufLen || end < 0 as negative) clamp or convert
        var endPositiveLabel = il.DefineLabel();
        var endOkLabel = il.DefineLabel();
        var afterEndLabel = il.DefineLabel();

        // First check if end is negative
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endPositiveLabel);
        // end is negative - add to bufLen
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, afterEndLabel);

        il.MarkLabel(endPositiveLabel);
        // end is positive - clamp to bufLen
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Ble, endOkLabel);
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, afterEndLabel);

        il.MarkLabel(endOkLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, endLocal);

        il.MarkLabel(afterEndLabel);

        // var length = actualEnd - actualBegin
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, beginLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // var result = new $ArrayBuffer(length)
        var resultLocal = il.DeclareLocal(typeBuilder);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Array.Copy(this._buffer, actualBegin, result._buffer, 0, length)
        var arrayCopy = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldloc, beginLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, arrayCopy);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $SharedArrayBuffer type for standalone DLLs.
    /// Similar to $ArrayBuffer but marked as shared.
    /// </summary>
    private void EmitSharedArrayBufferType(ModuleBuilder module, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$SharedArrayBuffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.SharedArrayBufferType = typeBuilder;

        // Field: byte[] _buffer
        var bufferField = typeBuilder.DefineField(
            "_buffer",
            typeof(byte[]),
            FieldAttributes.Private | FieldAttributes.InitOnly
        );

        // Constructor: public $SharedArrayBuffer(int byteLength)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        runtime.SharedArrayBufferCtor = ctor;

        var ctorIl = ctor.GetILGenerator();

        // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _buffer = new byte[byteLength]
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Newarr, typeof(byte));
        ctorIl.Emit(OpCodes.Stfld, bufferField);

        ctorIl.Emit(OpCodes.Ret);

        // Property: public int ByteLength => _buffer.Length
        EmitSharedArrayBufferByteLength(typeBuilder, runtime, bufferField);

        // Method: public byte[] GetBuffer() => _buffer
        EmitSharedArrayBufferGetBuffer(typeBuilder, runtime, bufferField);

        // Method: public $SharedArrayBuffer Slice(int begin, int end)
        EmitSharedArrayBufferSlice(typeBuilder, runtime, bufferField, ctor);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitSharedArrayBufferByteLength(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField)
    {
        var property = typeBuilder.DefineProperty(
            "ByteLength",
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes
        );

        var getter = typeBuilder.DefineMethod(
            "get_ByteLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.SharedArrayBufferByteLengthGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    private void EmitSharedArrayBufferGetBuffer(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField)
    {
        var method = typeBuilder.DefineMethod(
            "GetBuffer",
            MethodAttributes.Public,
            typeof(byte[]),
            Type.EmptyTypes
        );
        runtime.SharedArrayBufferGetBuffer = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSharedArrayBufferSlice(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder bufferField, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod(
            "Slice",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Int32, _types.Int32]
        );
        runtime.SharedArrayBufferSlice = method;

        var il = method.GetILGenerator();

        var bufLenLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // var bufLen = _buffer.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bufLenLocal);

        // var actualEnd = end > bufLen ? bufLen : end
        var endOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Ble, endOkLabel);
        il.Emit(OpCodes.Ldloc, bufLenLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        var afterEndClamp = il.DefineLabel();
        il.Emit(OpCodes.Br, afterEndClamp);

        il.MarkLabel(endOkLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, endLocal);

        il.MarkLabel(afterEndClamp);

        // var length = actualEnd - begin
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // var result = new $SharedArrayBuffer(length)
        var resultLocal = il.DeclareLocal(typeBuilder);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Array.Copy(this._buffer, begin, result._buffer, 0, length)
        var arrayCopy = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldfld, bufferField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, arrayCopy);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
