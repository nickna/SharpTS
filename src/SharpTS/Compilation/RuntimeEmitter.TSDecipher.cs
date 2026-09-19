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
    private sealed record DecipherConstruction(
        FieldBuilder Algorithm,
        FieldBuilder Key,
        FieldBuilder Iv,
        FieldBuilder IsGcm,
        FieldBuilder Aes,
        FieldBuilder Decryptor,
        FieldBuilder AesGcm,
        FieldBuilder CiphertextBuffer,
        FieldBuilder InputBuffer,
        FieldBuilder Finalized,
        FieldBuilder AutoPadding,
        FieldBuilder AuthTag,
        FieldBuilder Aad,
        MethodBuilder GcmDecryptHelper);

    private void EmitTSDecipherClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Decipher : IDisposable
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Decipher",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IDisposable]
        );
        _ = typeBuilder;

        // Fields
        var algorithm = typeBuilder.DefineField("_algorithm", _types.String, FieldAttributes.Private);
        var key = typeBuilder.DefineField("_key", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        var iv = typeBuilder.DefineField("_iv", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        var isGcm = typeBuilder.DefineField("_isGcm", _types.Boolean, FieldAttributes.Private);
        var aes = typeBuilder.DefineField("_aes", _types.Aes, FieldAttributes.Private);
        var decryptor = typeBuilder.DefineField("_decryptor", _types.ICryptoTransform, FieldAttributes.Private);
        var aesGcm = typeBuilder.DefineField("_aesGcm", _types.AesGcm, FieldAttributes.Private);
        var ciphertextBuffer = typeBuilder.DefineField("_ciphertextBuffer", _types.ListOfByte, FieldAttributes.Private);
        var inputBuffer = typeBuilder.DefineField("_inputBuffer", _types.ListOfByte, FieldAttributes.Private);
        var finalized = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);
        var autoPadding = typeBuilder.DefineField("_autoPadding", _types.Boolean, FieldAttributes.Private);
        var authTag = typeBuilder.DefineField("_authTag", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        var aad = typeBuilder.DefineField("_aad", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);

        // Emit GCM helper method first (needed by Final)
        var gcmDecryptHelper = EmitTSDecipherGcmDecryptHelper(typeBuilder, runtime);
        var construction = new DecipherConstruction(algorithm, key, iv, isGcm, aes, decryptor, aesGcm,
            ciphertextBuffer, inputBuffer, finalized, autoPadding, authTag, aad, gcmDecryptHelper);

        // Constructor
        EmitTSDecipherCtor(construction, typeBuilder, runtime.RequireCrypto());

        // Methods
        EmitTSDecipherUpdate(construction, typeBuilder, runtime);
        EmitTSDecipherFinal(construction, typeBuilder, runtime);
        EmitTSDecipherSetAutoPadding(typeBuilder, runtime);
        EmitTSDecipherSetAuthTag(construction, typeBuilder, runtime);
        EmitTSDecipherSetAAD(construction, typeBuilder, runtime);
        EmitTSDecipherDispose(construction, typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Decipher(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitTSDecipherCtor(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedCryptoRuntime crypto)
    {
        crypto.DecipherCtor = EmitStreamingCipherCtor(typeBuilder, DecipherFields(construction), isEncrypt: false,
            [construction.CiphertextBuffer, construction.InputBuffer]);
    }

    private StreamingCipherFields DecipherFields(DecipherConstruction construction) => new(
        construction.Algorithm, construction.Key, construction.Iv, construction.IsGcm,
        construction.Aes, construction.Decryptor, construction.AesGcm,
        construction.Finalized, construction.AutoPadding, construction.AuthTag, construction.Aad);

    /// <summary>
    /// Emits: private static void GcmDecryptHelper(AesGcm gcm, byte[] nonce, byte[] ciphertext, byte[] plaintext, byte[] tag, byte[] aad)
    /// via the shared <see cref="EmitGcmTransformHelper"/> (AesGcm.Decrypt takes
    /// (nonce, ciphertext, tag, plaintext, aad) — the helper reorders).
    /// </summary>
    private MethodBuilder EmitTSDecipherGcmDecryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        return EmitGcmTransformHelper(typeBuilder, "GcmDecryptHelper", isEncrypt: false);
    }

    /// <summary>
    /// Emits: public object Update(object data, string? inputEncoding, string? outputEncoding)
    /// </summary>
    private void EmitTSDecipherUpdate(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
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

        EmitThrowIfFinalized(il, construction.Finalized, "Decipher has already been finalized");

        // Convert input to bytes
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitCipherInputToBytes(il, runtime, OpCodes.Ldarg_1, OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, inputBytesLocal);

        // Check if GCM mode
        var gcmModeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.IsGcm);
        il.Emit(OpCodes.Brtrue, gcmModeLabel);

        // CBC mode: buffer the data
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // Add input to buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.InputBuffer);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "AddRange", [_types.IEnumerableOfByte])!);

        // Return empty buffer for now (decryption happens in final)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        EmitCipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3, supportUtf8: true);
        il.Emit(OpCodes.Ret);

        // GCM mode: accumulate ciphertext
        il.MarkLabel(gcmModeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.CiphertextBuffer);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "AddRange", [_types.IEnumerableOfByte])!);

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
    private void EmitTSDecipherFinal(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Final",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, construction.Finalized, "Decipher has already been finalized");

        // Set finalized
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, construction.Finalized);

        // Check if GCM mode
        var cbcModeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.IsGcm);
        il.Emit(OpCodes.Brfalse, cbcModeLabel);

        // GCM mode: Check that auth tag is set
        var authTagSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.AuthTag);
        il.Emit(OpCodes.Brtrue, authTagSetLabel);
        il.Emit(OpCodes.Ldstr, "setAuthTag must be called before final() for GCM mode");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(authTagSetLabel);

        // Get ciphertext from buffer
        var ciphertextLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.CiphertextBuffer);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "ToArray")!);
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
        il.Emit(OpCodes.Ldfld, construction.AesGcm);  // gcm
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Iv);       // nonce
        il.Emit(OpCodes.Ldloc, ciphertextLocal);          // ciphertext
        il.Emit(OpCodes.Ldloc, plaintextLocal);           // plaintext
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.AuthTag);  // tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Aad);      // aad (can be null)
        il.Emit(OpCodes.Call, construction.GcmDecryptHelper);

        // Return formatted plaintext
        EmitCipherFormatOutput(il, runtime, plaintextLocal, OpCodes.Ldarg_1, supportUtf8: true);
        il.Emit(OpCodes.Ret);

        // CBC mode: TransformFinalBlock with all buffered data
        il.MarkLabel(cbcModeLabel);
        var inputDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var finalBlockLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // inputData = _inputBuffer.ToArray()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.InputBuffer);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "ToArray")!);
        il.Emit(OpCodes.Stloc, inputDataLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, construction.Decryptor);
        il.Emit(OpCodes.Ldloc, inputDataLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, inputDataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ICryptoTransform, "TransformFinalBlock")!);
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
    private void EmitTSDecipherSetAuthTag(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherStoreBytesArg(typeBuilder, runtime, "SetAuthTag", construction.AuthTag);

    /// <summary>
    /// Emits: public $Decipher SetAAD(object aad) — accepts $Buffer or byte[].
    /// </summary>
    private void EmitTSDecipherSetAAD(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherStoreBytesArg(typeBuilder, runtime, "SetAAD", construction.Aad);

    /// <summary>
    /// Emits: public void Dispose()
    /// </summary>
    private void EmitTSDecipherDispose(DecipherConstruction construction, TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherDispose(typeBuilder, construction.Decryptor, construction.Aes, construction.AesGcm);
}
