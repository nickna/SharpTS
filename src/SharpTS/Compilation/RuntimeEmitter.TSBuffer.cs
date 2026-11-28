using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Buffer class for standalone Buffer support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSBuffer
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitTSBufferClass(ModuleBuilder moduleBuilder, EmittedBufferRuntime buffer, EmittedErrorRuntime errors, EmittedArrayBufferRuntime? arrayBuffer,
        Type undefinedType, ConstructorInfo padUndefined)
    {
        // Define class: public sealed class $Buffer
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Buffer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        buffer.Type = typeBuilder;

        // Field: private byte[] _data
        buffer.DataField = typeBuilder.DefineField("_data", _types.ByteArray, FieldAttributes.Assembly | FieldAttributes.InitOnly);

        buffer.OffsetField = typeBuilder.DefineField("_byteOffset", _types.Int32, FieldAttributes.Assembly | FieldAttributes.InitOnly);
        buffer.LengthField = typeBuilder.DefineField("_length", _types.Int32, FieldAttributes.Private | FieldAttributes.InitOnly);
        var owner = typeBuilder.DefineField("_owner", _types.Object, FieldAttributes.Private);
        var viewCtor = typeBuilder.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard,
            [_types.ByteArray, _types.Int32, _types.Int32, _types.Object]);
        buffer.ViewCtor = viewCtor;
        var viewIL = viewCtor.GetILGenerator();
        viewIL.Emit(OpCodes.Ldarg_0);
        viewIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        viewIL.Emit(OpCodes.Ldarg_0);
        viewIL.Emit(OpCodes.Ldarg_1);
        viewIL.Emit(OpCodes.Stfld, buffer.DataField);
        viewIL.Emit(OpCodes.Ldarg_0);
        viewIL.Emit(OpCodes.Ldarg_2);
        viewIL.Emit(OpCodes.Stfld, buffer.OffsetField);
        viewIL.Emit(OpCodes.Ldarg_0);
        viewIL.Emit(OpCodes.Ldarg_3);
        viewIL.Emit(OpCodes.Stfld, buffer.LengthField);
        viewIL.Emit(OpCodes.Ldarg_0);
        viewIL.Emit(OpCodes.Ldarg_S, (byte)4);
        viewIL.Emit(OpCodes.Stfld, owner);
        viewIL.Emit(OpCodes.Ret);

        buffer.OwnerGetter = typeBuilder.DefineMethod("get_buffer", MethodAttributes.Assembly, _types.Object, Type.EmptyTypes);
        var ownerIL = buffer.OwnerGetter.GetILGenerator();
        if (arrayBuffer is not null)
        {
            var ready = ownerIL.DefineLabel();
            ownerIL.Emit(OpCodes.Ldarg_0);
            ownerIL.Emit(OpCodes.Ldfld, owner);
            ownerIL.Emit(OpCodes.Brtrue, ready);
            ownerIL.Emit(OpCodes.Ldarg_0);
            ownerIL.Emit(OpCodes.Ldarg_0);
            ownerIL.Emit(OpCodes.Ldfld, buffer.DataField);
            ownerIL.Emit(OpCodes.Newobj, arrayBuffer.BackingCtor);
            ownerIL.Emit(OpCodes.Stfld, owner);
            ownerIL.MarkLabel(ready);
        }
        ownerIL.Emit(OpCodes.Ldarg_0);
        ownerIL.Emit(OpCodes.Ldfld, owner);
        ownerIL.Emit(OpCodes.Ret);

        // Constructor: public $Buffer(byte[] data)
        EmitTSBufferCtorBytes(typeBuilder, buffer);

        // Constructor: public $Buffer(int size)
        EmitTSBufferCtorSize(typeBuilder, buffer);

        // Property: public int Length
        buffer.LengthGetter = EmitBufferViewBound(typeBuilder, "Length", buffer.LengthField, owner, arrayBuffer);
        buffer.ByteOffsetGetter = EmitBufferViewBound(typeBuilder, "ByteOffset", buffer.OffsetField, owner, arrayBuffer);

        // Static methods
        EmitTSBufferGetData(typeBuilder, buffer);
        EmitTSBufferFromString(typeBuilder, buffer, errors);
        EmitTSBufferFromArray(typeBuilder, buffer);
        EmitTSBufferFromBuffer(typeBuilder, buffer);
        EmitTSBufferAlloc(typeBuilder, buffer);
        EmitTSBufferAllocUnsafe(typeBuilder, buffer);
        EmitTSBufferIsBuffer(typeBuilder, buffer);

        // Instance methods
        EmitTSBufferToStringMethod(typeBuilder, buffer);
        EmitTSBufferSlice(typeBuilder, buffer);
        EmitTSBufferCopy(typeBuilder, buffer);
        EmitTSBufferCompare(typeBuilder, buffer);
        EmitTSBufferEquals(typeBuilder, buffer);
        EmitTSBufferFill(typeBuilder, buffer);
        EmitTSBufferWrite(typeBuilder, buffer, errors);
        EmitTSBufferReadUInt8(typeBuilder, buffer);
        EmitBufferReadUInt8Value(typeBuilder, buffer, errors, undefinedType, padUndefined);
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

    // Leave the backing array and the view's starting byte offset on the stack.
    private static void EmitBufferRangeStart(ILGenerator il, EmittedBufferRuntime buffer, OpCode receiver)
    {
        il.Emit(receiver);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(receiver);
        il.Emit(OpCodes.Ldfld, buffer.OffsetField);
    }

    private static void EmitBufferSpan(ILGenerator il, EmittedBufferRuntime buffer, OpCode receiver)
    {
        il.Emit(receiver);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Ldfld, buffer.DataField);
        il.Emit(receiver);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Ldfld, buffer.OffsetField);
        il.Emit(receiver);
        il.Emit(OpCodes.Castclass, buffer.Type);
        il.Emit(OpCodes.Call, buffer.LengthGetter);
        il.Emit(OpCodes.Newobj, typeof(Span<byte>).GetConstructor([typeof(byte[]), typeof(int), typeof(int)])!);
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

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, buffer.ViewCtor);
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

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Call, buffer.Ctor);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitBufferViewBound(TypeBuilder type, string name, FieldBuilder bound,
        FieldBuilder owner, EmittedArrayBufferRuntime? arrayBuffer)
    {
        var getter = type.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Int32, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        if (arrayBuffer is not null)
        {
            var attached = il.DefineLabel();
            var ab = il.DeclareLocal(arrayBuffer.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, owner);
            il.Emit(OpCodes.Isinst, arrayBuffer.Type);
            il.Emit(OpCodes.Stloc, ab);
            il.Emit(OpCodes.Ldloc, ab);
            il.Emit(OpCodes.Brfalse, attached);
            il.Emit(OpCodes.Ldloc, ab);
            il.Emit(OpCodes.Ldfld, arrayBuffer.DetachedField);
            il.Emit(OpCodes.Brfalse, attached);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(attached);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, bound);
        il.Emit(OpCodes.Ret);
        type.DefineProperty(name, PropertyAttributes.None, _types.Int32, Type.EmptyTypes).SetGetMethod(getter);
        return getter;
    }
}
