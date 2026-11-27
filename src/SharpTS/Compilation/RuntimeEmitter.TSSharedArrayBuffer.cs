using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
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
        var buffer = runtime.RequireSharedArrayBuffer();
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
        buffer.Slice = EmitArrayBufferSlice(typeBuilder, buffer.BufferField, buffer.Ctor);
        buffer.PrototypeSlice = EmitArrayBufferPrototypeSlice(typeBuilder, runtime, buffer.Slice, "SharedArrayBuffer");
        buffer.PrototypeByteLengthGetter = EmitArrayBufferPrototypeByteLengthGetter(
            typeBuilder, runtime, buffer.ByteLengthGetter, "SharedArrayBuffer");

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
        _nativeTypedArrayBuffers?.RecordDeclaration(buffer);
    }
}
