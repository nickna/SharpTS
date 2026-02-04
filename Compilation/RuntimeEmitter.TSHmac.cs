using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Hmac class for standalone crypto HMAC support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSHmac
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsHmacField = null!;
    private FieldBuilder _tsHmacFinalizedField = null!;

    private void EmitTSHmacClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Hmac
        var typeBuilder = moduleBuilder.DefineType(
            "$Hmac",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSHmacType = typeBuilder;

        // Fields
        _tsHmacField = typeBuilder.DefineField("_hmac", _types.IncrementalHash, FieldAttributes.Private);
        _tsHmacFinalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);

        // Constructor: $Hmac(string algorithm, byte[] key)
        EmitTSHmacCtor(typeBuilder, runtime);

        // Methods
        EmitTSHmacUpdate(typeBuilder, runtime);
        EmitTSHmacDigest(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Hmac(string algorithm, byte[] key)
    /// </summary>
    private void EmitTSHmacCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.MakeArrayType(_types.Byte)]
        );
        runtime.TSHmacCtor = ctor;

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
        var createHmacLabel = il.DefineLabel();
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
        il.Emit(OpCodes.Br, createHmacLabel);

        // SHA1
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHmacLabel);

        // SHA256
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHmacLabel);

        // SHA384
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHmacLabel);

        // SHA512
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, createHmacLabel);

        // Default - throw ArgumentException
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported HMAC algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create HMAC
        il.MarkLabel(createHmacLabel);

        // _hmac = IncrementalHash.CreateHMAC(hashName, key)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hashNameLocal);
        il.Emit(OpCodes.Ldarg_2); // key (byte[])
        il.Emit(OpCodes.Call, _types.IncrementalHash.GetMethod("CreateHMAC", [_types.HashAlgorithmName, _types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Stfld, _tsHmacField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsHmacFinalizedField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Hmac Update(string data)
    /// </summary>
    private void EmitTSHmacUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        runtime.TSHmacUpdateMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHmacFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "Cannot update HMAC after digest() has been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // var bytes = Encoding.UTF8.GetBytes(data)
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _hmac.AppendData(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHmacField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, _types.IncrementalHash.GetMethod("AppendData", [_types.MakeArrayType(_types.Byte)])!);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Digest(string? encoding)
    /// </summary>
    private void EmitTSHmacDigest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Digest",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSHmacDigestMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHmacFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "digest() has already been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsHmacFinalizedField);

        // var hmacBytes = _hmac.GetHashAndReset()
        var hmacBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsHmacField);
        il.Emit(OpCodes.Callvirt, _types.IncrementalHash.GetMethod("GetHashAndReset", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, hmacBytesLocal);

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

        // Return hex string: Convert.ToHexString(hmacBytes).ToLowerInvariant()
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, hmacBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // Return base64 string: Convert.ToBase64String(hmacBytes)
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, hmacBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Ret);

        // Return Buffer: create $Buffer from bytes
        il.MarkLabel(returnArrayLabel);

        // Return new $Buffer(hmacBytes)
        il.Emit(OpCodes.Ldloc, hmacBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }
}
