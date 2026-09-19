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

    /// <summary>
    /// Phase 1: Define type, fields, constructor, and Update method.
    /// Called before EmitRuntimeClass. Shares the definition with $Verify via
    /// <see cref="EmitStreamingSignVerifyTypeDefinition"/>.
    /// </summary>
    private StreamingSignVerifyParts EmitTSSignTypeDefinition(ModuleBuilder moduleBuilder, EmittedCryptoRuntime crypto)
    {
        var parts = EmitStreamingSignVerifyTypeDefinition(moduleBuilder, "$Sign",
            unsupportedAlgorithmPrefix: "Unsupported signing algorithm: ",
            updateFinalizedMessage: "Cannot update Sign after sign() has been called");
        crypto.SignCtor = parts.Ctor;
        return parts;
    }

    /// <summary>
    /// Phase 2: Add Sign method and finalize type.
    /// Called after EmitRuntimeClass (needs runtime.RequireCrypto().SignDataBytes).
    /// </summary>
    private void EmitTSSignFinalize(StreamingSignVerifyParts construction, EmittedRuntime runtime)
    {
        // Sign method needs runtime.RequireCrypto().SignDataBytes
        EmitTSSignSign(construction, construction.Type, runtime);

        construction.Type.CreateType();
    }

    /// <summary>
    /// Emits: public object Sign(string privateKeyPem, string? encoding)
    /// </summary>
    private void EmitTSSignSign(StreamingSignVerifyParts construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Sign",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, construction.FinalizedField, "sign() has already been called");

        // _finalized = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, construction.FinalizedField);

        // var dataBytes = _data.ToArray()
        var dataBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.DataField);
        il.Emit(OpCodes.Callvirt, _types.ListByteToArray);
        il.Emit(OpCodes.Stloc, dataBytesLocal);

        // Call emitted helper method to get signature bytes (no SharpTS.dll dependency)
        var signatureBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_1);  // privateKeyPem
        il.Emit(OpCodes.Ldloc, dataBytesLocal);  // dataBytes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.HashAlgorithmField);  // hashAlgorithm
        il.Emit(OpCodes.Call, runtime.RequireCrypto().SignDataBytes);
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check for "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check for "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Default to Buffer
        il.Emit(OpCodes.Br, bufferLabel);

        // hex: Convert.ToHexString(bytes).ToLowerInvariant()
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Br, endLabel);

        // base64: Convert.ToBase64String(bytes)
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Br, endLabel);

        // buffer: new $Buffer(bytes)
        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldloc, signatureBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireBuffer().Ctor);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
