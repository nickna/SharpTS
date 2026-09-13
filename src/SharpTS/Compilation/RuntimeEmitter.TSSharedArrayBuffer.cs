using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $SharedArrayBuffer type for standalone DLLs.
    /// Similar to $ArrayBuffer but marked as shared.
    /// </summary>
    private void EmitSharedArrayBufferType(ModuleBuilder module, EmittedSharedArrayBufferRuntime buffer)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(module,
            "$SharedArrayBuffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        buffer.Type = typeBuilder;

        // Field: byte[] _buffer
        buffer.BufferField = typeBuilder.DefineField(
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
        buffer.Ctor = ctor;

        var ctorIl = ctor.GetILGenerator();

        // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _buffer = new byte[byteLength]
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Newarr, typeof(byte));
        ctorIl.Emit(OpCodes.Stfld, buffer.BufferField);

        ctorIl.Emit(OpCodes.Ret);

        // Property: public int ByteLength => _buffer.Length
        EmitSharedArrayBufferByteLengthProperty(typeBuilder, buffer);

        // Method: public byte[] GetBuffer() => _buffer
        EmitSharedArrayBufferGetBuffer(typeBuilder, buffer);

        // Method: public $SharedArrayBuffer Slice(int begin, int end)
        EmitSharedArrayBufferSliceMethod(typeBuilder, buffer);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitSharedArrayBufferByteLengthProperty(TypeBuilder typeBuilder, EmittedSharedArrayBufferRuntime buffer)
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
        buffer.ByteLengthGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.BufferField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
    }

    private void EmitSharedArrayBufferGetBuffer(TypeBuilder typeBuilder, EmittedSharedArrayBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "GetBuffer",
            MethodAttributes.Public,
            typeof(byte[]),
            Type.EmptyTypes
        );
        buffer.GetBuffer = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.BufferField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSharedArrayBufferSliceMethod(TypeBuilder typeBuilder, EmittedSharedArrayBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "Slice",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Int32, _types.Int32]
        );
        buffer.Slice = method;

        var il = method.GetILGenerator();

        var bufLenLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // var bufLen = _buffer.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.BufferField);
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
        il.Emit(OpCodes.Newobj, buffer.Ctor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Array.Copy(this._buffer, begin, result._buffer, 0, length)
        var arrayCopy = typeof(Array).GetMethod("Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.BufferField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldfld, buffer.BufferField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Call, arrayCopy);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
