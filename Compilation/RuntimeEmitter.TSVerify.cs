using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Verify class for standalone crypto verification support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSVerify
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _tsVerifyTypeBuilder = null!;
    private FieldBuilder _tsVerifyHashAlgorithmField = null!;
    private FieldBuilder _tsVerifyDataField = null!;
    private FieldBuilder _tsVerifyFinalizedField = null!;

    /// <summary>
    /// Phase 1: Define type, fields, constructor, and Update method.
    /// Called before EmitRuntimeClass.
    /// </summary>
    private void EmitTSVerifyTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Verify
        _tsVerifyTypeBuilder = moduleBuilder.DefineType(
            "$Verify",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        _ = _tsVerifyTypeBuilder;

        // Fields
        _tsVerifyHashAlgorithmField = _tsVerifyTypeBuilder.DefineField("_hashAlgorithm", _types.HashAlgorithmName, FieldAttributes.Private);
        _tsVerifyDataField = _tsVerifyTypeBuilder.DefineField("_data", typeof(List<byte>), FieldAttributes.Private);
        _tsVerifyFinalizedField = _tsVerifyTypeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);

        // Constructor
        EmitTSVerifyCtor(_tsVerifyTypeBuilder, runtime);

        // Update method (doesn't need runtime helpers)
        EmitTSVerifyUpdate(_tsVerifyTypeBuilder, runtime);
    }

    /// <summary>
    /// Phase 2: Add Verify method and finalize type.
    /// Called after EmitRuntimeClass (needs runtime.VerifyDataBytes).
    /// </summary>
    private void EmitTSVerifyFinalize(EmittedRuntime runtime)
    {
        // Verify method needs runtime.VerifyDataBytes
        EmitTSVerifyVerify(_tsVerifyTypeBuilder, runtime);

        _tsVerifyTypeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Verify(string algorithm)
    /// </summary>
    private void EmitTSVerifyCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSVerifyCtor = ctor;

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
        il.Emit(OpCodes.Ldstr, "Unsupported verification algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Set hash algorithm field
        il.MarkLabel(setHashLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hashNameLocal);
        il.Emit(OpCodes.Stfld, _tsVerifyHashAlgorithmField);

        // _data = new List<byte>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ListByteDefaultCtor);
        il.Emit(OpCodes.Stfld, _tsVerifyDataField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsVerifyFinalizedField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Verify Update(string data)
    /// </summary>
    private void EmitTSVerifyUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsVerifyFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "Cannot update Verify after verify() has been called");
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
        il.Emit(OpCodes.Ldfld, _tsVerifyDataField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListByteAddRange);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Verify(string publicKeyPem, object signature, string? signatureEncoding)
    /// </summary>
    private void EmitTSVerifyVerify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Verify",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.Object, _types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        // if (_finalized) throw InvalidOperationException
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsVerifyFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, "verify() has already been called");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsVerifyFinalizedField);

        // var dataBytes = _data.ToArray()
        var dataBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsVerifyDataField);
        il.Emit(OpCodes.Callvirt, _types.ListByteToArray);
        il.Emit(OpCodes.Stloc, dataBytesLocal);

        // Convert signature to bytes based on encoding
        var signatureBytesLocal = il.DeclareLocal(_types.ByteArray);
        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var bufferLabel = il.DefineLabel();
        var bytesLabel = il.DefineLabel();
        var signatureReadyLabel = il.DefineLabel();

        // Check if signature is string
        il.Emit(OpCodes.Ldarg_2);  // signature
        il.Emit(OpCodes.Isinst, _types.String);
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // String signature - check encoding
        il.Emit(OpCodes.Ldarg_3);  // signatureEncoding
        il.Emit(OpCodes.Brfalse, bufferLabel);  // null encoding -> UTF8

        var encodingLowerLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLowerLocal);

        // Check for "hex"
        il.Emit(OpCodes.Ldloc, encodingLowerLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check for "base64"
        il.Emit(OpCodes.Ldloc, encodingLowerLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Default to UTF8
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);
        il.Emit(OpCodes.Br, signatureReadyLabel);

        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);
        il.Emit(OpCodes.Br, signatureReadyLabel);

        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);
        il.Emit(OpCodes.Br, signatureReadyLabel);

        // Not a string - check for byte[] or Buffer
        il.MarkLabel(notStringLabel);

        // Check for byte[]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, bufferLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);
        il.Emit(OpCodes.Br, signatureReadyLabel);

        // Try $Buffer.GetData()
        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        var notBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, signatureBytesLocal);
        il.Emit(OpCodes.Br, signatureReadyLabel);

        // Standalone-only behavior: accept string, byte[], or emitted $Buffer.
        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Ldstr, "Signature must be a string, Buffer, or byte array");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Call emitted helper method (no SharpTS.dll dependency)
        il.MarkLabel(signatureReadyLabel);
        il.Emit(OpCodes.Ldarg_1);  // publicKeyPem
        il.Emit(OpCodes.Ldloc, dataBytesLocal);  // dataBytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsVerifyHashAlgorithmField);  // hashAlgorithm
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);  // signatureBytes
        il.Emit(OpCodes.Call, runtime.VerifyDataBytes);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper for RSA/EC signature verification operations.
/// Used by compiled code to avoid complex IL emission for try/catch and key detection.
/// </summary>
public static class CryptoVerifyHelper
{
    /// <summary>
    /// Verifies a signature using an RSA or EC public key.
    /// </summary>
    public static bool VerifyData(string publicKeyPem, byte[] data, HashAlgorithmName hashAlgorithm, object signature, string? signatureEncoding)
    {
        // Convert signature to bytes
        byte[] signatureBytes = signature switch
        {
            string sigStr when signatureEncoding?.ToLowerInvariant() == "hex" =>
                Convert.FromHexString(sigStr),
            string sigStr when signatureEncoding?.ToLowerInvariant() == "base64" =>
                Convert.FromBase64String(sigStr),
            string sigStr => Encoding.UTF8.GetBytes(sigStr),
            byte[] bytes => bytes,
            _ => GetBufferData(signature)
        };

        // Detect key type from PEM header
        if (publicKeyPem.Contains("EC PUBLIC KEY") || publicKeyPem.Contains("-----BEGIN PUBLIC KEY-----"))
        {
            // Try EC first, fall back to RSA
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(publicKeyPem);
                return ecdsa.VerifyData(data, signatureBytes, hashAlgorithm);
            }
            catch
            {
                // Fall back to RSA
                using var rsa = RSA.Create();
                rsa.ImportFromPem(publicKeyPem);
                return rsa.VerifyData(data, signatureBytes, hashAlgorithm, RSASignaturePadding.Pkcs1);
            }
        }
        else
        {
            // Assume RSA
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(data, signatureBytes, hashAlgorithm, RSASignaturePadding.Pkcs1);
        }
    }

    private static byte[] GetBufferData(object buffer)
    {
        // Get the Data property from the buffer object
        var dataProperty = buffer.GetType().GetProperty("Data");
        if (dataProperty != null)
        {
            return (byte[])dataProperty.GetValue(buffer)!;
        }
        throw new ArgumentException("Signature must be a string or Buffer");
    }
}
