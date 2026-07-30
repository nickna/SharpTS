using System.Reflection;
using System.Reflection.Emit;

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
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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
        EmitTSBufferFromBuffer(typeBuilder, runtime);
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

        // Variable-length integer reads (#1161)
        EmitTSBufferVarIntRead(typeBuilder, runtime, "ReadUIntLE", bigEndian: false, signed: false, m => runtime.TSBufferReadUIntLE = m);
        EmitTSBufferVarIntRead(typeBuilder, runtime, "ReadUIntBE", bigEndian: true, signed: false, m => runtime.TSBufferReadUIntBE = m);
        EmitTSBufferVarIntRead(typeBuilder, runtime, "ReadIntLE", bigEndian: false, signed: true, m => runtime.TSBufferReadIntLE = m);
        EmitTSBufferVarIntRead(typeBuilder, runtime, "ReadIntBE", bigEndian: true, signed: true, m => runtime.TSBufferReadIntBE = m);

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

        // Variable-length integer writes (#1161). Signed and unsigned writes produce
        // identical two's-complement bytes, so writeInt*LE/BE route to these.
        EmitTSBufferVarIntWrite(typeBuilder, runtime, "WriteUIntLE", bigEndian: false, m => runtime.TSBufferWriteUIntLE = m);
        EmitTSBufferVarIntWrite(typeBuilder, runtime, "WriteUIntBE", bigEndian: true, m => runtime.TSBufferWriteUIntBE = m);

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
}
