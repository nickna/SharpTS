using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Hash class for standalone crypto hash support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHash
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsHashField = null!;
    private FieldBuilder _tsHashFinalizedField = null!;

    private void EmitTSHashClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Hash
        var typeBuilder = moduleBuilder.DefineType(
            "$Hash",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSHashType = typeBuilder;

        // Fields
        _tsHashField = typeBuilder.DefineField("_hash", _types.IncrementalHash, FieldAttributes.Private);
        _tsHashFinalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);

        // Constructor
        EmitTSHashCtor(typeBuilder, runtime);

        // Methods
        EmitTSHashUpdate(typeBuilder, runtime);
        EmitTSHashDigest(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Hash(string algorithm)
    /// </summary>
    private void EmitTSHashCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSHashCtor = ctor;

        var il = ctor.GetILGenerator();

        // Local for algorithm string (lowercased)
        var lowerAlgorithmLocal = il.DeclareLocal(_types.String);
        var hashNameLocal = il.DeclareLocal(_types.HashAlgorithmName);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // lowerAlgorithm = algorithm.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerAlgorithmLocal);

        // Switch on algorithm name
        var md5Label = il.DefineLabel();
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var createHashLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "md5"
        il.Emit(OpCodes.Ldloc, lowerAlgorithmLocal);
        il.Emit(OpCodes.Ldstr, "md5");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, md5Label);

        // Check "sha1"
        il.Emit(OpCodes.Ldloc, lowerAlgorithmLocal);
        il.Emit(OpCodes.Ldstr, "sha1");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha1Label);

        // Check "sha256"
        il.Emit(OpCodes.Ldloc, lowerAlgorithmLocal);
        il.Emit(OpCodes.Ldstr, "sha256");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha256Label);

        // Check "sha384"
        il.Emit(OpCodes.Ldloc, lowerAlgorithmLocal);
        il.Emit(OpCodes.Ldstr, "sha384");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha384Label);

        // Check "sha512"
        il.Emit(OpCodes.Ldloc, lowerAlgorithmLocal);
        il.Emit(OpCodes.Ldstr, "sha512");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha512Label);

        // Default - throw exception
        il.Emit(OpCodes.Br, defaultLabel);

        // MD5
        il.MarkLabel(md5Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("MD5")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHashLabel);

        // SHA1
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHashLabel);

        // SHA256
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHashLabel);

        // SHA384
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHashLabel);

        // SHA512
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHashLabel);

        // Default - throw ArgumentException
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported hash algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create hash
        il.MarkLabel(createHashLabel);

        // _hash = IncrementalHash.CreateHash(hashName)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hashNameLocal);
        il.Emit(OpCodes.Call, _types.IncrementalHash.GetMethod("CreateHash", [_types.HashAlgorithmName])!);
        il.Emit(OpCodes.Stfld, _tsHashField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsHashFinalizedField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Hash Update(string data)
    /// </summary>
    private void EmitTSHashUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        runtime.TSHashUpdateMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHashFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "Cannot update hash after digest() has been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // var bytes = Encoding.UTF8.GetBytes(data)
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _hash.AppendData(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHashField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, _types.IncrementalHash.GetMethod("AppendData", [_types.MakeArrayType(_types.Byte)])!);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Digest(string? encoding)
    /// </summary>
    private void EmitTSHashDigest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Digest",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSHashDigestMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHashFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "digest() has already been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsHashFinalizedField);

        // var hashBytes = _hash.GetHashAndReset()
        var hashBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHashField);
        il.Emit(OpCodes.Callvirt, _types.IncrementalHash.GetMethod("GetHashAndReset", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, hashBytesLocal);

        // Check encoding
        var checkHexLabel = il.DefineLabel();
        var checkBase64Label = il.DefineLabel();
        var returnArrayLabel = il.DefineLabel();
        var lowerEncodingLocal = il.DeclareLocal(_types.String);

        // if (encoding == null) goto returnArray
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnArrayLabel);

        // lowerEncoding = encoding.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerEncodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, lowerEncodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkHexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, lowerEncodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkBase64Label);

        // Default - return array
        il.Emit(OpCodes.Br, returnArrayLabel);

        // Return hex string: Convert.ToHexString(hashBytes).ToLowerInvariant()
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, hashBytesLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToHexString", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // Return base64 string: Convert.ToBase64String(hashBytes)
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, hashBytesLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBase64String", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Ret);

        // Return Buffer: create $Buffer from bytes
        il.MarkLabel(returnArrayLabel);

        // Return new $Buffer(hashBytes)
        il.Emit(OpCodes.Ldloc, hashBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }
}
