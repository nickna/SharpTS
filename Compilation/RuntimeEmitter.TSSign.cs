using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Sign class for standalone crypto signing support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSSign
/// </summary>
public partial class RuntimeEmitter
{
    private FieldBuilder _tsSignHashAlgorithmField = null!;
    private FieldBuilder _tsSignDataField = null!;
    private FieldBuilder _tsSignFinalizedField = null!;

    private void EmitTSSignClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Sign
        var typeBuilder = moduleBuilder.DefineType(
            "$Sign",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSSignType = typeBuilder;

        // Fields
        _tsSignHashAlgorithmField = typeBuilder.DefineField("_hashAlgorithm", _types.HashAlgorithmName, FieldAttributes.Private);
        _tsSignDataField = typeBuilder.DefineField("_data", typeof(List<byte>), FieldAttributes.Private);
        _tsSignFinalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);

        // Constructor
        EmitTSSignCtor(typeBuilder, runtime);

        // Methods
        EmitTSSignUpdate(typeBuilder, runtime);
        EmitTSSignSign(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Sign(string algorithm)
    /// </summary>
    private void EmitTSSignCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSSignCtor = ctor;

        var il = ctor.GetILGenerator();

        // Local for algorithm string (normalized)
        var normalizedLocal = il.DeclareLocal(_types.String);
        var hashNameLocal = il.DeclareLocal(_types.HashAlgorithmName);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // normalized = algorithm.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, normalizedLocal);

        // Remove "rsa-" or "ecdsa-" prefix if present
        var checkEcdsaLabel = il.DefineLabel();
        var afterPrefixLabel = il.DefineLabel();

        // if (normalized.StartsWith("rsa-")) normalized = normalized.Substring(4)
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "rsa-");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, checkEcdsaLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, normalizedLocal);
        il.Emit(OpCodes.Br, afterPrefixLabel);

        // if (normalized.StartsWith("ecdsa-")) normalized = normalized.Substring(6)
        il.MarkLabel(checkEcdsaLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "ecdsa-");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, afterPrefixLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, normalizedLocal);

        il.MarkLabel(afterPrefixLabel);

        // Switch on algorithm name
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var setHashLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "sha1"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "sha1");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha1Label);

        // Check "sha256"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "sha256");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha256Label);

        // Check "sha384"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "sha384");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha384Label);

        // Check "sha512"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "sha512");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, sha512Label);

        // Default - throw exception
        il.Emit(OpCodes.Br, defaultLabel);

        // SHA1
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA256
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA384
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA512
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // Default - throw ArgumentException
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported signing algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Set hash algorithm field
        il.MarkLabel(setHashLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hashNameLocal);
        il.Emit(OpCodes.Stfld, _tsSignHashAlgorithmField);

        // _data = new List<byte>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<byte>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsSignDataField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsSignFinalizedField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Sign Update(string data)
    /// </summary>
    private void EmitTSSignUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        runtime.TSSignUpdateMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "Cannot update Sign after sign() has been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // var bytes = Encoding.UTF8.GetBytes(data)
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _data.AddRange(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignDataField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<byte>).GetMethod("AddRange", [typeof(IEnumerable<byte>)])!);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Sign(string privateKeyPem, string? encoding)
    /// </summary>
    private void EmitTSSignSign(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Sign",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.String]
        );
        runtime.TSSignSignMethod = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "sign() has already been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsSignFinalizedField);

        // var dataBytes = _data.ToArray()
        var dataBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignDataField);
        il.Emit(OpCodes.Callvirt, _types.ListByteToArray);
        il.Emit(OpCodes.Stloc, dataBytesLocal);

        // Call helper method to get signature bytes
        var signatureBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_1);  // privateKeyPem
        il.Emit(OpCodes.Ldloc, dataBytesLocal);  // dataBytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignHashAlgorithmField);  // hashAlgorithm
        il.Emit(OpCodes.Call, typeof(CryptoSignHelper).GetMethod("SignDataBytes")!);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);

        // Handle encoding
        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var bufferLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check encoding
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, bufferLabel);

        // Normalize encoding to lowercase
        var encodingLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check for "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check for "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Default to Buffer
        il.Emit(OpCodes.Br, bufferLabel);

        // hex: Convert.ToHexString(bytes).ToLowerInvariant()
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Br, endLabel);

        // base64: Convert.ToBase64String(bytes)
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Br, endLabel);

        // buffer: new $Buffer(bytes)
        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper for RSA/EC signing operations.
/// Used by compiled code to avoid complex IL emission for try/catch and key detection.
/// </summary>
public static class CryptoSignHelper
{
    /// <summary>
    /// Signs data using an RSA or EC private key and returns the raw signature bytes.
    /// </summary>
    public static byte[] SignDataBytes(string privateKeyPem, byte[] data, HashAlgorithmName hashAlgorithm)
    {
        // Detect key type from PEM header
        if (privateKeyPem.Contains("EC PRIVATE KEY") || privateKeyPem.Contains("-----BEGIN PRIVATE KEY-----"))
        {
            // Try EC first, fall back to RSA
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(privateKeyPem);
                return ecdsa.SignData(data, hashAlgorithm);
            }
            catch
            {
                // Fall back to RSA
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);
                return rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1);
            }
        }
        else
        {
            // Assume RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            return rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1);
        }
    }
}
