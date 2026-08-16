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
    /// Called before EmitRuntimeClass. Shares the definition with $Sign via
    /// <see cref="EmitStreamingSignVerifyTypeDefinition"/>.
    /// </summary>
    private void EmitTSVerifyTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var parts = EmitStreamingSignVerifyTypeDefinition(moduleBuilder, "$Verify",
            unsupportedAlgorithmPrefix: "Unsupported verification algorithm: ",
            updateFinalizedMessage: "Cannot update Verify after verify() has been called");
        _tsVerifyTypeBuilder = parts.Type;
        _tsVerifyHashAlgorithmField = parts.HashAlgorithmField;
        _tsVerifyDataField = parts.DataField;
        _tsVerifyFinalizedField = parts.FinalizedField;
        runtime.TSVerifyCtor = parts.Ctor;
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

        EmitThrowIfFinalized(il, _tsVerifyFinalizedField, "verify() has already been called");

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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLowerLocal);

        // Check for "hex"
        il.Emit(OpCodes.Ldloc, encodingLowerLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check for "base64"
        il.Emit(OpCodes.Ldloc, encodingLowerLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
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
