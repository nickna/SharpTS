using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Cipher class for standalone crypto cipher support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSCipher
/// </summary>
public partial class RuntimeEmitter
{
    // Fields for $Cipher class
    private FieldBuilder _tsCipherAlgorithmField = null!;
    private FieldBuilder _tsCipherKeyField = null!;
    private FieldBuilder _tsCipherIvField = null!;
    private FieldBuilder _tsCipherIsGcmField = null!;
    private FieldBuilder _tsCipherAesField = null!;
    private FieldBuilder _tsCipherEncryptorField = null!;
    private FieldBuilder _tsCipherAesGcmField = null!;
    private FieldBuilder _tsCipherPlaintextBufferField = null!;
    private FieldBuilder _tsCipherFinalizedField = null!;
    private FieldBuilder _tsCipherAutoPaddingField = null!;
    private FieldBuilder _tsCipherAuthTagField = null!;
    private FieldBuilder _tsCipherAadField = null!;
    private MethodBuilder _tsCipherGcmEncryptHelper = null!;

    private void EmitTSCipherClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $Cipher : IDisposable
        var typeBuilder = moduleBuilder.DefineType(
            "$Cipher",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IDisposable]
        );
        _ = typeBuilder;

        // Fields
        _tsCipherAlgorithmField = typeBuilder.DefineField("_algorithm", _types.String, FieldAttributes.Private);
        _tsCipherKeyField = typeBuilder.DefineField("_key", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsCipherIvField = typeBuilder.DefineField("_iv", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsCipherIsGcmField = typeBuilder.DefineField("_isGcm", _types.Boolean, FieldAttributes.Private);
        _tsCipherAesField = typeBuilder.DefineField("_aes", _types.Aes, FieldAttributes.Private);
        _tsCipherEncryptorField = typeBuilder.DefineField("_encryptor", _types.ICryptoTransform, FieldAttributes.Private);
        _tsCipherAesGcmField = typeBuilder.DefineField("_aesGcm", _types.AesGcm, FieldAttributes.Private);
        _tsCipherPlaintextBufferField = typeBuilder.DefineField("_plaintextBuffer", _types.ListOfByte, FieldAttributes.Private);
        _tsCipherFinalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);
        _tsCipherAutoPaddingField = typeBuilder.DefineField("_autoPadding", _types.Boolean, FieldAttributes.Private);
        _tsCipherAuthTagField = typeBuilder.DefineField("_authTag", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);
        _tsCipherAadField = typeBuilder.DefineField("_aad", _types.MakeArrayType(_types.Byte), FieldAttributes.Private);

        // Emit GCM helper method first (needed by Final)
        EmitTSCipherGcmEncryptHelper(typeBuilder, runtime);

        // Constructor
        EmitTSCipherCtor(typeBuilder, runtime);

        // Methods
        EmitTSCipherUpdate(typeBuilder, runtime);
        EmitTSCipherFinal(typeBuilder, runtime);
        EmitTSCipherSetAutoPadding(typeBuilder, runtime);
        EmitTSCipherGetAuthTag(typeBuilder, runtime);
        EmitTSCipherSetAAD(typeBuilder, runtime);
        EmitTSCipherDispose(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $Cipher(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitTSCipherCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.TSCipherCtor = EmitStreamingCipherCtor(typeBuilder, CipherFields(), isEncrypt: true,
            [_tsCipherPlaintextBufferField]);
    }

    private StreamingCipherFields CipherFields() => new(
        _tsCipherAlgorithmField, _tsCipherKeyField, _tsCipherIvField, _tsCipherIsGcmField,
        _tsCipherAesField, _tsCipherEncryptorField, _tsCipherAesGcmField,
        _tsCipherFinalizedField, _tsCipherAutoPaddingField, _tsCipherAuthTagField, _tsCipherAadField);

    /// <summary>
    /// Emits: private static void GcmEncryptHelper(AesGcm gcm, byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] aad)
    /// via the shared <see cref="EmitGcmTransformHelper"/>.
    /// </summary>
    private void EmitTSCipherGcmEncryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        _tsCipherGcmEncryptHelper = EmitGcmTransformHelper(typeBuilder, "GcmEncryptHelper", isEncrypt: true);
    }

    /// <summary>
    /// Emits: public object Update(object data, string? inputEncoding, string? outputEncoding)
    /// Simplified: buffers all data and processes in Final()
    /// </summary>
    private void EmitTSCipherUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
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

        EmitThrowIfFinalized(il, _tsCipherFinalizedField, "Cipher has already been finalized");

        // Convert input to bytes
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitCipherInputToBytes(il, runtime, OpCodes.Ldarg_1, OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, inputBytesLocal);

        // Buffer data in _plaintextBuffer (used for both CBC and GCM)
        // This simplifies Update to just buffer, and Final does all the work
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherPlaintextBufferField);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "AddRange", [_types.IEnumerableOfByte])!);

        // Return empty buffer (all data processed in Final)
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        EmitCipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3, supportUtf8: false);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Final(string? outputEncoding)
    /// </summary>
    private void EmitTSCipherFinal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Final",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, _tsCipherFinalizedField, "Cipher has already been finalized");

        // Set finalized
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsCipherFinalizedField);

        // Check if GCM mode
        var cbcModeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherIsGcmField);
        il.Emit(OpCodes.Brfalse, cbcModeLabel);

        // GCM mode: Perform encryption using helper method
        // Get plaintext from buffer
        var plaintextLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherPlaintextBufferField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "ToArray")!);
        il.Emit(OpCodes.Stloc, plaintextLocal);

        // Create ciphertext array (same size as plaintext for GCM)
        var ciphertextLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldloc, plaintextLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, ciphertextLocal);

        // Create tag array (16 bytes for GCM)
        var tagLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, tagLocal);

        // Call GcmEncryptHelper(_aesGcm, _iv, plaintext, ciphertext, tag, _aad)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesGcmField);  // gcm
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherIvField);       // nonce
        il.Emit(OpCodes.Ldloc, plaintextLocal);         // plaintext
        il.Emit(OpCodes.Ldloc, ciphertextLocal);        // ciphertext
        il.Emit(OpCodes.Ldloc, tagLocal);               // tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAadField);      // aad (can be null)
        il.Emit(OpCodes.Call, _tsCipherGcmEncryptHelper);

        // Store tag in _authTag field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Stfld, _tsCipherAuthTagField);

        // Return formatted ciphertext
        EmitCipherFormatOutput(il, runtime, ciphertextLocal, OpCodes.Ldarg_1, supportUtf8: false);
        il.Emit(OpCodes.Ret);

        // CBC mode: Get all buffered data and TransformFinalBlock
        il.MarkLabel(cbcModeLabel);

        // bufferedData = _plaintextBuffer.ToArray()
        var bufferedDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherPlaintextBufferField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfByte, "ToArray")!);
        il.Emit(OpCodes.Stloc, bufferedDataLocal);

        // result = _encryptor.TransformFinalBlock(bufferedData, 0, bufferedData.Length)
        var finalBlockLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherEncryptorField);
        il.Emit(OpCodes.Ldloc, bufferedDataLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bufferedDataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ICryptoTransform, "TransformFinalBlock")!);
        il.Emit(OpCodes.Stloc, finalBlockLocal);

        EmitCipherFormatOutput(il, runtime, finalBlockLocal, OpCodes.Ldarg_1, supportUtf8: false);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Cipher SetAutoPadding(bool autoPadding)
    /// </summary>
    private void EmitTSCipherSetAutoPadding(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherSetAutoPadding(typeBuilder);

    /// <summary>
    /// Emits: public object GetAuthTag()
    /// </summary>
    private void EmitTSCipherGetAuthTag(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetAuthTag",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();

        // Check if GCM
        var isGcmLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherIsGcmField);
        il.Emit(OpCodes.Brtrue, isGcmLabel);
        il.Emit(OpCodes.Ldstr, "getAuthTag is only available for GCM mode ciphers");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(isGcmLabel);

        // Check if finalized
        var isFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherFinalizedField);
        il.Emit(OpCodes.Brtrue, isFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "getAuthTag must be called after final()");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(isFinalizedLabel);

        // Return new $Buffer(_authTag)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAuthTagField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Cipher SetAAD(object aad) — accepts $Buffer or byte[].
    /// </summary>
    private void EmitTSCipherSetAAD(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherStoreBytesArg(typeBuilder, runtime, "SetAAD", _tsCipherAadField);

    /// <summary>
    /// Emits: public void Dispose()
    /// </summary>
    private void EmitTSCipherDispose(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitCipherDispose(typeBuilder, _tsCipherEncryptorField, _tsCipherAesField, _tsCipherAesGcmField);

}
