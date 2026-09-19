using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Hash class for standalone crypto hash support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHash
/// </summary>
/// <remarks>
/// $Hash buffers updated data in a MemoryStream and computes the digest one-shot
/// via $CryptoPrimitives.RequireCrypto().HashData — that's what makes hash.copy() (#1058)
/// and the XOF hashes with outputLength (#1062) expressible.
/// </remarks>
public partial class RuntimeEmitter
{
    private sealed record HashConstruction(
        FieldBuilder Algorithm,
        FieldBuilder Data,
        FieldBuilder OutputLength,
        FieldBuilder Finalized);

    private void EmitTSHashClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var crypto = runtime.RequireCrypto();
        // Define class: public sealed class $Hash
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Hash",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        var algorithm = typeBuilder.DefineField("_algorithm", _types.String, FieldAttributes.Private);
        var data = typeBuilder.DefineField("_data", typeof(MemoryStream), FieldAttributes.Private);
        var outputLength = typeBuilder.DefineField("_outputLength", _types.Int32, FieldAttributes.Private);
        var finalized = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);
        var construction = new HashConstruction(algorithm, data, outputLength, finalized);

        // Constructor
        EmitTSHashCtor(construction, typeBuilder, crypto);

        // Methods
        EmitTSHashUpdate(construction, typeBuilder, crypto);
        EmitTSHashDigest(construction, typeBuilder, crypto);
        EmitTSHashCopy(construction, typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Hash(string algorithm, int outputLength)
    /// </summary>
    private void EmitTSHashCtor(HashConstruction construction, TypeBuilder typeBuilder, EmittedCryptoRuntime crypto)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.Int32]
        );
        crypto.HashCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _algorithm = CryptoValidateHashName(algorithm)  (throws on unknown/unsupported)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, crypto.ValidateHashName);
        il.Emit(OpCodes.Stfld, construction.Algorithm);

        // _outputLength = outputLength
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, construction.OutputLength);

        // _data = new MemoryStream()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(MemoryStream).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, construction.Data);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, construction.Finalized);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Hash Update(object data) — accepts string (UTF-8), $Buffer, or byte[].
    /// </summary>
    private void EmitTSHashUpdate(HashConstruction construction, TypeBuilder typeBuilder, EmittedCryptoRuntime crypto)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, construction.Finalized, "Cannot update hash after digest() has been called");

        // var bytes = CryptoBytesFromAny(data)
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, crypto.BytesFromAny);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _data.Write(bytes, 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Data);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(MemoryStream).GetMethod("Write", [typeof(byte[]), typeof(int), typeof(int)])!);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Digest(string? encoding)
    /// </summary>
    private void EmitTSHashDigest(HashConstruction construction, TypeBuilder typeBuilder, EmittedCryptoRuntime crypto)
    {
        var method = typeBuilder.DefineMethod(
            "Digest",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, construction.Finalized, "digest() has already been called");

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, construction.Finalized);

        // return CryptoEncodeBytes(CryptoHashData(_algorithm, _data.ToArray(), _outputLength), encoding)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Algorithm);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Data);
        il.Emit(OpCodes.Callvirt, typeof(MemoryStream).GetMethod("ToArray", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.OutputLength);
        il.Emit(OpCodes.Call, crypto.HashData);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, crypto.EncodeBytes);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Hash Copy(object options) — clones the mid-stream state (#1058).
    /// options may carry { outputLength } (a $Object); -1 inherits this hash's.
    /// </summary>
    private void EmitTSHashCopy(HashConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Copy",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, construction.Finalized, "Cannot copy hash after digest() has been called");

        // int outputLength = _outputLength;
        var outputLengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.OutputLength);
        il.Emit(OpCodes.Stloc, outputLengthLocal);

        // if (options is $Object o && o.GetProperty("outputLength") is double d) outputLength = (int)d;
        var noOptionsLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Brfalse, noOptionsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.ObjectStorage.Type);
        il.Emit(OpCodes.Ldstr, "outputLength");
        il.Emit(OpCodes.Callvirt, runtime.ObjectStorage.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, noOptionsLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, outputLengthLocal);
        il.MarkLabel(noOptionsLabel);

        // var copy = new $Hash(_algorithm, outputLength)
        var copyLocal = il.DeclareLocal(typeBuilder);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Algorithm);
        il.Emit(OpCodes.Ldloc, outputLengthLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireCrypto().HashCtor);
        il.Emit(OpCodes.Stloc, copyLocal);

        // this._data.WriteTo(copy._data)  — same-class private field access is legal
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Data);
        il.Emit(OpCodes.Ldloc, copyLocal);
        il.Emit(OpCodes.Ldfld, construction.Data);
        il.Emit(OpCodes.Callvirt, typeof(MemoryStream).GetMethod("WriteTo", [typeof(Stream)])!);

        il.Emit(OpCodes.Ldloc, copyLocal);
        il.Emit(OpCodes.Ret);
    }
}
