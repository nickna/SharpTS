using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Decipher class for standalone crypto decipher support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDecipher
/// </summary>
public partial class RuntimeEmitter
{
    // Fields for $Decipher class
    private FieldBuilder _tsDecipherAlgorithmField = null!;
    private FieldBuilder _tsDecipherKeyField = null!;
    private FieldBuilder _tsDecipherIvField = null!;
    private FieldBuilder _tsDecipherIsGcmField = null!;
    private FieldBuilder _tsDecipherAesField = null!;
    private FieldBuilder _tsDecipherDecryptorField = null!;
    private FieldBuilder _tsDecipherAesGcmField = null!;
    private FieldBuilder _tsDecipherCiphertextBufferField = null!;
    private FieldBuilder _tsDecipherInputBufferField = null!;
    private FieldBuilder _tsDecipherFinalizedField = null!;
    private FieldBuilder _tsDecipherAutoPaddingField = null!;
    private FieldBuilder _tsDecipherAuthTagField = null!;
    private FieldBuilder _tsDecipherAadField = null!;
    private MethodBuilder _tsDecipherGcmDecryptHelper = null!;

    private void EmitTSDecipherClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Decipher : IDisposable
        var typeBuilder = moduleBuilder.DefineType(
            "$Decipher",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IDisposable]
        );
        _ = typeBuilder;

        // Fields
        _tsDecipherAlgorithmField = typeBuilder.DefineField("_algorithm", _types.String, FieldAttributes.Private);
        _tsDecipherKeyField = typeBuilder.DefineField("_key", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsDecipherIvField = typeBuilder.DefineField("_iv", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsDecipherIsGcmField = typeBuilder.DefineField("_isGcm", _types.Boolean, FieldAttributes.Private);
        _tsDecipherAesField = typeBuilder.DefineField("_aes", _types.Aes, FieldAttributes.Private);
        _tsDecipherDecryptorField = typeBuilder.DefineField("_decryptor", _types.ICryptoTransform, FieldAttributes.Private);
        _tsDecipherAesGcmField = typeBuilder.DefineField("_aesGcm", _types.AesGcm, FieldAttributes.Private);
        _tsDecipherCiphertextBufferField = typeBuilder.DefineField("_ciphertextBuffer", _types.ListOfByte, FieldAttributes.Private);
        _tsDecipherInputBufferField = typeBuilder.DefineField("_inputBuffer", _types.ListOfByte, FieldAttributes.Private);
        _tsDecipherFinalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);
        _tsDecipherAutoPaddingField = typeBuilder.DefineField("_autoPadding", _types.Boolean, FieldAttributes.Private);
        _tsDecipherAuthTagField = typeBuilder.DefineField("_authTag", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsDecipherAadField = typeBuilder.DefineField("_aad", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);

        // Emit GCM helper method first (needed by Final)
        EmitTSDecipherGcmDecryptHelper(typeBuilder, runtime);

        // Constructor
        EmitTSDecipherCtor(typeBuilder, runtime);

        // Methods
        EmitTSDecipherUpdate(typeBuilder, runtime);
        EmitTSDecipherFinal(typeBuilder, runtime);
        EmitTSDecipherSetAutoPadding(typeBuilder, runtime);
        EmitTSDecipherSetAuthTag(typeBuilder, runtime);
        EmitTSDecipherSetAAD(typeBuilder, runtime);
        EmitTSDecipherDispose(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Decipher(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitTSDecipherCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.TSDecipherCtor = EmitStreamingCipherCtor(typeBuilder, DecipherFields(), isEncrypt: false,
            [_tsDecipherCiphertextBufferField, _tsDecipherInputBufferField]);
    }

    private StreamingCipherFields DecipherFields() => new(
        _tsDecipherAlgorithmField, _tsDecipherKeyField, _tsDecipherIvField, _tsDecipherIsGcmField,
        _tsDecipherAesField, _tsDecipherDecryptorField, _tsDecipherAesGcmField,
        _tsDecipherFinalizedField, _tsDecipherAutoPaddingField, _tsDecipherAuthTagField, _tsDecipherAadField);

    /// <summary>
    /// Emits: private static void GcmDecryptHelper(AesGcm gcm, byte[] nonce, byte[] ciphertext, byte[] plaintext, byte[] tag, byte[] aad)
    /// via the shared <see cref="EmitGcmTransformHelper"/> (AesGcm.Decrypt takes
    /// (nonce, ciphertext, tag, plaintext, aad) — the helper reorders).
    /// </summary>
    private void EmitTSDecipherGcmDecryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        _tsDecipherGcmDecryptHelper = EmitGcmTransformHelper(typeBuilder, "GcmDecryptHelper", isEncrypt: false);
    }

    /// <summary>
    /// Emits: public object Update(object data, string? inputEncoding, string? outputEncoding)
    /// </summary>
    private void EmitTSDecipherUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Parameter types are object to allow $Undefined to be passed for encoding
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, _tsDecipherFinalizedField, "Decipher has already been finalized");

        // Convert input to bytes
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitCipherInputToBytes(il, runtime, OpCodes.Ldarg_1, OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, inputBytesLocal);

        // Check if GCM mode
        var gcmModeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherIsGcmField);
        il.Emit(OpCodes.Brtrue, gcmModeLabel);

        // CBC mode: buffer the data
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // Add input to buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherInputBufferField);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("AddRange", [_types.IEnumerableOfByte])!);

        // Return empty buffer for now (decryption happens in final)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        EmitCipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3, supportUtf8: true);
        il.Emit(OpCodes.Ret);

        // GCM mode: accumulate ciphertext
        il.MarkLabel(gcmModeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherCiphertextBufferField);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("AddRange", [_types.IEnumerableOfByte])!);

        // Return empty buffer
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        EmitCipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3, supportUtf8: true);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Final(string? outputEncoding)
    /// </summary>
    private void EmitTSDecipherFinal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Final",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, _tsDecipherFinalizedField, "Decipher has already been finalized");

        // Set finalized
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDecipherFinalizedField);

        // Check if GCM mode
        var cbcModeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherIsGcmField);
        il.Emit(OpCodes.Brfalse, cbcModeLabel);

        // GCM mode: Check that auth tag is set
        var authTagSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAuthTagField);
        il.Emit(OpCodes.Brtrue, authTagSetLabel);
        il.Emit(OpCodes.Ldstr, "setAuthTag must be called before final() for GCM mode");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(authTagSetLabel);

        // Get ciphertext from buffer
        var ciphertextLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherCiphertextBufferField);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("ToArray")!);
        il.Emit(OpCodes.Stloc, ciphertextLocal);

        // Create plaintext array (same size as ciphertext for GCM)
        var plaintextLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldloc, ciphertextLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, plaintextLocal);

        // Call GcmDecryptHelper(_aesGcm, _iv, ciphertext, plaintext, _authTag, _aad)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesGcmField);  // gcm
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherIvField);       // nonce
        il.Emit(OpCodes.Ldloc, ciphertextLocal);          // ciphertext
        il.Emit(OpCodes.Ldloc, plaintextLocal);           // plaintext
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAuthTagField);  // tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAadField);      // aad (can be null)
        il.Emit(OpCodes.Call, _tsDecipherGcmDecryptHelper);

        // Return formatted plaintext
        EmitCipherFormatOutput(il, runtime, plaintextLocal, OpCodes.Ldarg_1, supportUtf8: true);
        il.Emit(OpCodes.Ret);

        // CBC mode: TransformFinalBlock with all buffered data
        il.MarkLabel(cbcModeLabel);
        var inputDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var finalBlockLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // inputData = _inputBuffer.ToArray()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherInputBufferField);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("ToArray")!);
        il.Emit(OpCodes.Stloc, inputDataLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherDecryptorField);
        il.Emit(OpCodes.Ldloc, inputDataLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, inputDataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.ICryptoTransform.GetMethod("TransformFinalBlock")!);
        il.Emit(OpCodes.Stloc, finalBlockLocal);

        EmitCipherFormatOutput(il, runtime, finalBlockLocal, OpCodes.Ldarg_1, supportUtf8: true);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Decipher SetAutoPadding(bool autoPadding)
    /// </summary>
    private void EmitTSDecipherSetAutoPadding(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherSetAutoPadding(typeBuilder);

    /// <summary>
    /// Emits: public $Decipher SetAuthTag(object tag) — accepts $Buffer or byte[].
    /// </summary>
    private void EmitTSDecipherSetAuthTag(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherStoreBytesArg(typeBuilder, runtime, "SetAuthTag", _tsDecipherAuthTagField);

    /// <summary>
    /// Emits: public $Decipher SetAAD(object aad) — accepts $Buffer or byte[].
    /// </summary>
    private void EmitTSDecipherSetAAD(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherStoreBytesArg(typeBuilder, runtime, "SetAAD", _tsDecipherAadField);

    /// <summary>
    /// Emits: public void Dispose()
    /// </summary>
    private void EmitTSDecipherDispose(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherDispose(typeBuilder, _tsDecipherDecryptorField, _tsDecipherAesField, _tsDecipherAesGcmField);
}
