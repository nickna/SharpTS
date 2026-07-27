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
    private TypeBuilder _tsSignTypeBuilder = null!;
    private FieldBuilder _tsSignHashAlgorithmField = null!;
    private FieldBuilder _tsSignDataField = null!;
    private FieldBuilder _tsSignFinalizedField = null!;

    /// <summary>
    /// Phase 1: Define type, fields, constructor, and Update method.
    /// Called before EmitRuntimeClass. Shares the definition with $Verify via
    /// <see cref="EmitStreamingSignVerifyTypeDefinition"/>.
    /// </summary>
    private void EmitTSSignTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var parts = EmitStreamingSignVerifyTypeDefinition(moduleBuilder, "$Sign",
            unsupportedAlgorithmPrefix: "Unsupported signing algorithm: ",
            updateFinalizedMessage: "Cannot update Sign after sign() has been called");
        _tsSignTypeBuilder = parts.Type;
        _tsSignHashAlgorithmField = parts.HashAlgorithmField;
        _tsSignDataField = parts.DataField;
        _tsSignFinalizedField = parts.FinalizedField;
        runtime.TSSignCtor = parts.Ctor;
    }

    /// <summary>
    /// Phase 2: Add Sign method and finalize type.
    /// Called after EmitRuntimeClass (needs runtime.SignDataBytes).
    /// </summary>
    private void EmitTSSignFinalize(EmittedRuntime runtime)
    {
        // Sign method needs runtime.SignDataBytes
        EmitTSSignSign(_tsSignTypeBuilder, runtime);

        _tsSignTypeBuilder.CreateType();
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
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, _tsSignFinalizedField, "sign() has already been called");

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

        // Call emitted helper method to get signature bytes (no SharpTS.dll dependency)
        var signatureBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_1);  // privateKeyPem
        il.Emit(OpCodes.Ldloc, dataBytesLocal);  // dataBytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsSignHashAlgorithmField);  // hashAlgorithm
        il.Emit(OpCodes.Call, runtime.SignDataBytes);
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
