using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Buffer class for standalone Buffer support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSBufferClass(ModuleBuilder moduleBuilder, EmittedBufferRuntime buffer)
    {
        // Define class: public sealed class $Buffer
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Buffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        buffer.Type = typeBuilder;

        // Field: private byte[] _data
        buffer.DataField = typeBuilder.DefineField("_data", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);

        // Constructor: public $Buffer(byte[] data)
        EmitTSBufferCtorBytes(typeBuilder, buffer);

        // Constructor: public $Buffer(int size)
        EmitTSBufferCtorSize(typeBuilder, buffer);

        // Property: public int Length
        EmitTSBufferLengthProperty(typeBuilder, buffer);

        // Static methods
        EmitTSBufferFromString(typeBuilder, buffer);
        EmitTSBufferFromArray(typeBuilder, buffer);
        EmitTSBufferFromBuffer(typeBuilder, buffer);
        EmitTSBufferAlloc(typeBuilder, buffer);
        EmitTSBufferAllocUnsafe(typeBuilder, buffer);
        EmitTSBufferConcat(typeBuilder, buffer);
        EmitCalculateBuffersTotalLength(typeBuilder, buffer);
        EmitTSBufferIsBuffer(typeBuilder, buffer);

        // Instance methods
        EmitTSBufferToStringMethod(typeBuilder, buffer);
        EmitTSBufferSlice(typeBuilder, buffer);
        EmitTSBufferGetData(typeBuilder, buffer);
        EmitTSBufferCopy(typeBuilder, buffer);
        EmitTSBufferCompare(typeBuilder, buffer);
        EmitTSBufferEquals(typeBuilder, buffer);
        EmitTSBufferFill(typeBuilder, buffer);
        EmitTSBufferWrite(typeBuilder, buffer);
        EmitTSBufferReadUInt8(typeBuilder, buffer);
        EmitTSBufferWriteUInt8(typeBuilder, buffer);
        EmitTSBufferToJSON(typeBuilder, buffer);

        // Multi-byte read methods
        EmitTSBufferReadInt8(typeBuilder, buffer);
        EmitTSBufferReadUInt16LE(typeBuilder, buffer);
        EmitTSBufferReadUInt16BE(typeBuilder, buffer);
        EmitTSBufferReadUInt32LE(typeBuilder, buffer);
        EmitTSBufferReadUInt32BE(typeBuilder, buffer);
        EmitTSBufferReadInt16LE(typeBuilder, buffer);
        EmitTSBufferReadInt16BE(typeBuilder, buffer);
        EmitTSBufferReadInt32LE(typeBuilder, buffer);
        EmitTSBufferReadInt32BE(typeBuilder, buffer);
        EmitTSBufferReadFloatLE(typeBuilder, buffer);
        EmitTSBufferReadFloatBE(typeBuilder, buffer);
        EmitTSBufferReadDoubleLE(typeBuilder, buffer);
        EmitTSBufferReadDoubleBE(typeBuilder, buffer);
        EmitTSBufferReadBigInt64LE(typeBuilder, buffer);
        EmitTSBufferReadBigInt64BE(typeBuilder, buffer);
        EmitTSBufferReadBigUInt64LE(typeBuilder, buffer);
        EmitTSBufferReadBigUInt64BE(typeBuilder, buffer);

        // Variable-length integer reads (#1161)
        EmitTSBufferVarIntRead(typeBuilder, buffer, "ReadUIntLE", bigEndian: false, signed: false, m => buffer.ReadUIntLE = m);
        EmitTSBufferVarIntRead(typeBuilder, buffer, "ReadUIntBE", bigEndian: true, signed: false, m => buffer.ReadUIntBE = m);
        EmitTSBufferVarIntRead(typeBuilder, buffer, "ReadIntLE", bigEndian: false, signed: true, m => buffer.ReadIntLE = m);
        EmitTSBufferVarIntRead(typeBuilder, buffer, "ReadIntBE", bigEndian: true, signed: true, m => buffer.ReadIntBE = m);

        // Multi-byte write methods
        EmitTSBufferWriteInt8(typeBuilder, buffer);
        EmitTSBufferWriteUInt16LE(typeBuilder, buffer);
        EmitTSBufferWriteUInt16BE(typeBuilder, buffer);
        EmitTSBufferWriteUInt32LE(typeBuilder, buffer);
        EmitTSBufferWriteUInt32BE(typeBuilder, buffer);
        EmitTSBufferWriteInt16LE(typeBuilder, buffer);
        EmitTSBufferWriteInt16BE(typeBuilder, buffer);
        EmitTSBufferWriteInt32LE(typeBuilder, buffer);
        EmitTSBufferWriteInt32BE(typeBuilder, buffer);
        EmitTSBufferWriteFloatLE(typeBuilder, buffer);
        EmitTSBufferWriteFloatBE(typeBuilder, buffer);
        EmitTSBufferWriteDoubleLE(typeBuilder, buffer);
        EmitTSBufferWriteDoubleBE(typeBuilder, buffer);
        EmitTSBufferWriteBigInt64LE(typeBuilder, buffer);
        EmitTSBufferWriteBigInt64BE(typeBuilder, buffer);
        EmitTSBufferWriteBigUInt64LE(typeBuilder, buffer);
        EmitTSBufferWriteBigUInt64BE(typeBuilder, buffer);

        // Variable-length integer writes (#1161). Signed and unsigned writes produce
        // identical two's-complement bytes, so writeInt*LE/BE route to these.
        EmitTSBufferVarIntWrite(typeBuilder, buffer, "WriteUIntLE", bigEndian: false, m => buffer.WriteUIntLE = m);
        EmitTSBufferVarIntWrite(typeBuilder, buffer, "WriteUIntBE", bigEndian: true, m => buffer.WriteUIntBE = m);

        // Search methods
        EmitTSBufferIndexOf(typeBuilder, buffer);
        EmitTSBufferIncludes(typeBuilder, buffer);

        // Swap methods
        EmitTSBufferSwap16(typeBuilder, buffer);
        EmitTSBufferSwap32(typeBuilder, buffer);
        EmitTSBufferSwap64(typeBuilder, buffer);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Buffer(byte[] data)
    /// </summary>
    private void EmitTSBufferCtorBytes(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.MakeArrayType(_types.Byte)]
        );
        buffer.Ctor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _data = data
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, buffer.DataField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Buffer(int size)
    /// </summary>
    private void EmitTSBufferCtorSize(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        buffer.CtorSize = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _data = new byte[size]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stfld, buffer.DataField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public int Length { get; }
    /// </summary>
    private void EmitTSBufferLengthProperty(TypeBuilder typeBuilder, EmittedBufferRuntime buffer)
    {
        var method = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Int32,
            Type.EmptyTypes
        );
        buffer.LengthGetter = method;

        var il = method.GetILGenerator();

        // return _data.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
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
}
