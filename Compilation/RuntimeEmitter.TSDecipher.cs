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
        runtime.TSDecipherType = typeBuilder;

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
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]
        );
        runtime.TSDecipherCtor = ctor;

        var il = ctor.GetILGenerator();

        // Locals
        var lowerAlgoLocal = il.DeclareLocal(_types.String);
        var isGcmLocal = il.DeclareLocal(_types.Boolean);
        var keySizeLocal = il.DeclareLocal(_types.Int32);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _algorithm = algorithm.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, lowerAlgoLocal);
        il.Emit(OpCodes.Stfld, _tsDecipherAlgorithmField);

        // Parse algorithm - determine isGcm and keySize
        var aes128CbcLabel = il.DefineLabel();
        var aes192CbcLabel = il.DefineLabel();
        var aes256CbcLabel = il.DefineLabel();
        var aes128GcmLabel = il.DefineLabel();
        var aes192GcmLabel = il.DefineLabel();
        var aes256GcmLabel = il.DefineLabel();
        var afterParseLabel = il.DefineLabel();
        var unsupportedLabel = il.DefineLabel();

        // Check algorithms
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-128-cbc", aes128CbcLabel);
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-192-cbc", aes192CbcLabel);
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-256-cbc", aes256CbcLabel);
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-128-gcm", aes128GcmLabel);
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-192-gcm", aes192GcmLabel);
        EmitDecipherStringCompare(il, lowerAlgoLocal, "aes-256-gcm", aes256GcmLabel);
        il.Emit(OpCodes.Br, unsupportedLabel);

        // aes-128-cbc: keySize=16, isGcm=false
        il.MarkLabel(aes128CbcLabel);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // aes-192-cbc: keySize=24, isGcm=false
        il.MarkLabel(aes192CbcLabel);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // aes-256-cbc: keySize=32, isGcm=false
        il.MarkLabel(aes256CbcLabel);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // aes-128-gcm: keySize=16, isGcm=true
        il.MarkLabel(aes128GcmLabel);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // aes-192-gcm: keySize=24, isGcm=true
        il.MarkLabel(aes192GcmLabel);
        il.Emit(OpCodes.Ldc_I4, 24);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // aes-256-gcm: keySize=32, isGcm=true
        il.MarkLabel(aes256GcmLabel);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Stloc, keySizeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isGcmLocal);
        il.Emit(OpCodes.Br, afterParseLabel);

        // Unsupported algorithm - throw
        il.MarkLabel(unsupportedLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported cipher algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(afterParseLabel);

        // Store _isGcm
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isGcmLocal);
        il.Emit(OpCodes.Stfld, _tsDecipherIsGcmField);

        // Validate key size
        var keySizeOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, keySizeLocal);
        il.Emit(OpCodes.Beq, keySizeOkLabel);

        // Key size mismatch - throw
        il.Emit(OpCodes.Ldstr, "Invalid key length for cipher algorithm");
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(keySizeOkLabel);

        // Validate IV size
        var ivSizeOkLabel = il.DefineLabel();
        var expectedIvSizeLocal = il.DeclareLocal(_types.Int32);

        // expectedIvSize = isGcm ? 12 : 16
        il.Emit(OpCodes.Ldloc, isGcmLocal);
        var notGcmIvLabel = il.DefineLabel();
        var storeExpectedIvLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notGcmIvLabel);
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Br, storeExpectedIvLabel);
        il.MarkLabel(notGcmIvLabel);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.MarkLabel(storeExpectedIvLabel);
        il.Emit(OpCodes.Stloc, expectedIvSizeLocal);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, expectedIvSizeLocal);
        il.Emit(OpCodes.Beq, ivSizeOkLabel);

        // IV size mismatch - throw
        il.Emit(OpCodes.Ldstr, "Invalid IV length for cipher algorithm");
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(ivSizeOkLabel);

        // Store key and iv
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _tsDecipherKeyField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _tsDecipherIvField);

        // Initialize buffers
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfByte));
        il.Emit(OpCodes.Stfld, _tsDecipherCiphertextBufferField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfByte));
        il.Emit(OpCodes.Stfld, _tsDecipherInputBufferField);

        // _autoPadding = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsDecipherAutoPaddingField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsDecipherFinalizedField);

        // Initialize crypto objects based on mode
        var initCbcLabel = il.DefineLabel();
        var initDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, isGcmLocal);
        il.Emit(OpCodes.Brfalse, initCbcLabel);

        // GCM mode: _aesGcm = new AesGcm(_key, 16)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2); // key
        il.Emit(OpCodes.Ldc_I4, 16); // tag size
        il.Emit(OpCodes.Newobj, _types.AesGcm.GetConstructor([_types.MakeArrayType(_types.Byte), _types.Int32])!);
        il.Emit(OpCodes.Stfld, _tsDecipherAesGcmField);
        il.Emit(OpCodes.Br, initDoneLabel);

        // CBC mode: create Aes and decryptor
        il.MarkLabel(initCbcLabel);

        // _aes = Aes.Create()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Aes.GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsDecipherAesField);

        // _aes.Key = key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Key")!.SetMethod!);

        // _aes.IV = iv
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("IV")!.SetMethod!);

        // _aes.Mode = CipherMode.CBC
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Ldc_I4_1); // CipherMode.CBC = 1
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Mode")!.SetMethod!);

        // _aes.Padding = PaddingMode.PKCS7
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Ldc_I4_2); // PaddingMode.PKCS7 = 2
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Padding")!.SetMethod!);

        // _decryptor = _aes.CreateDecryptor()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetMethod("CreateDecryptor", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsDecipherDecryptorField);

        il.MarkLabel(initDoneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDecipherStringCompare(ILGenerator il, LocalBuilder stringLocal, string value, Label targetLabel)
    {
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, targetLabel);
    }

    /// <summary>
    /// Emits: private static void GcmDecryptHelper(AesGcm gcm, byte[] nonce, byte[] ciphertext, byte[] plaintext, byte[] tag, byte[] aad)
    /// Uses the AesGcm.Decrypt byte[] overload.
    /// Note: AesGcm.Decrypt signature is (nonce, ciphertext, tag, plaintext, associatedData)
    /// </summary>
    private void EmitTSDecipherGcmDecryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GcmDecryptHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.AesGcm, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        _tsDecipherGcmDecryptHelper = method;

        var il = method.GetILGenerator();

        // Get the byte[] overload: Decrypt(byte[] nonce, byte[] ciphertext, byte[] tag, byte[] plaintext, byte[] associatedData)
        var decryptMethod = _types.AesGcm.GetMethod("Decrypt",
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)])!;

        // Check if aad is null - if so, use empty array
        var aadNotNullLabel = il.DefineLabel();
        var callDecryptLabel = il.DefineLabel();
        var aadLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Brtrue, aadNotNullLabel);

        // aad is null - use empty byte array
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, aadLocal);
        il.Emit(OpCodes.Br, callDecryptLabel);

        il.MarkLabel(aadNotNullLabel);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stloc, aadLocal);

        // Call gcm.Decrypt(nonce, ciphertext, tag, plaintext, aad)
        // Our args: arg0=gcm, arg1=nonce, arg2=ciphertext, arg3=plaintext, arg4=tag, arg5=aad
        // Method expects: nonce, ciphertext, tag, plaintext, associatedData
        il.MarkLabel(callDecryptLabel);
        il.Emit(OpCodes.Ldarg_0);            // gcm
        il.Emit(OpCodes.Ldarg_1);            // nonce
        il.Emit(OpCodes.Ldarg_2);            // ciphertext
        il.Emit(OpCodes.Ldarg_S, (byte)4);   // tag
        il.Emit(OpCodes.Ldarg_3);            // plaintext
        il.Emit(OpCodes.Ldloc, aadLocal);    // aad
        il.Emit(OpCodes.Callvirt, decryptMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Update(object data, string? inputEncoding, string? outputEncoding)
    /// </summary>
    private void EmitTSDecipherUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.String, _types.String]
        );
        runtime.TSDecipherUpdateMethod = method;

        var il = method.GetILGenerator();

        // Check if finalized
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "Decipher has already been finalized");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // Convert input to bytes
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitDecipherConvertToBytes(il, runtime, OpCodes.Ldarg_1, OpCodes.Ldarg_2);
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
        EmitDecipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3);
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
        EmitDecipherFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3);
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
        runtime.TSDecipherFinalMethod = method;

        var il = method.GetILGenerator();

        // Check if finalized
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "Decipher has already been finalized");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

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
        EmitDecipherFormatOutput(il, runtime, plaintextLocal, OpCodes.Ldarg_1);
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

        EmitDecipherFormatOutput(il, runtime, finalBlockLocal, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Decipher SetAutoPadding(bool autoPadding)
    /// </summary>
    private void EmitTSDecipherSetAutoPadding(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAutoPadding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Boolean]
        );
        runtime.TSDecipherSetAutoPaddingMethod = method;

        var il = method.GetILGenerator();

        // Return this for chaining (simplified)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Decipher SetAuthTag(object tag)
    /// Accepts $Buffer or byte[] and stores as byte[].
    /// </summary>
    private void EmitTSDecipherSetAuthTag(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAuthTag",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.TSDecipherSetAuthTagMethod = method;

        var il = method.GetILGenerator();

        // Convert $Buffer to byte[]
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitBufferToBytes(il, runtime, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Store auth tag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Stfld, _tsDecipherAuthTagField);

        // Return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Decipher SetAAD(object aad)
    /// Accepts $Buffer or byte[] and stores as byte[].
    /// </summary>
    private void EmitTSDecipherSetAAD(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAAD",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.TSDecipherSetAADMethod = method;

        var il = method.GetILGenerator();

        // Convert $Buffer to byte[]
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitBufferToBytes(il, runtime, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Store AAD
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Stfld, _tsDecipherAadField);

        // Return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void Dispose()
    /// </summary>
    private void EmitTSDecipherDispose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Dispose decryptor if not null
        var decryptorNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherDecryptorField);
        il.Emit(OpCodes.Brfalse, decryptorNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherDecryptorField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(decryptorNullLabel);

        // Dispose aes if not null
        var aesNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Brfalse, aesNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(aesNullLabel);

        // Dispose aesGcm if not null
        var aesGcmNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesGcmField);
        il.Emit(OpCodes.Brfalse, aesGcmNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsDecipherAesGcmField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(aesGcmNullLabel);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper to emit code that converts input data to byte array for decipher.
    /// </summary>
    private void EmitDecipherConvertToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData, OpCode loadEncoding)
    {
        var dataLocal = il.DeclareLocal(_types.Object);
        var encodingLocal = il.DeclareLocal(_types.String);

        il.Emit(loadData);
        il.Emit(OpCodes.Stloc, dataLocal);
        il.Emit(loadEncoding);
        il.Emit(OpCodes.Stloc, encodingLocal);

        var isBufferLabel = il.DefineLabel();
        var isStringLabel = il.DefineLabel();
        var checkHexLabel = il.DefineLabel();
        var checkBase64Label = il.DefineLabel();
        var utf8DefaultLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // Check if Buffer
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, isBufferLabel);

        // Check if string
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // Default: throw
        il.Emit(OpCodes.Ldstr, "Data must be a string or Buffer");
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Buffer path
        il.MarkLabel(isBufferLabel);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Br, doneLabel);

        // String path - check encoding
        il.MarkLabel(isStringLabel);

        // Check if encoding is null
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Brfalse, utf8DefaultLabel);

        // lowerEncoding = encoding.ToLowerInvariant()
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkHexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkBase64Label);

        // Default UTF8
        il.Emit(OpCodes.Br, utf8DefaultLabel);

        // Hex decode
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Br, doneLabel);

        // Base64 decode
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Br, doneLabel);

        // UTF8 default
        il.MarkLabel(utf8DefaultLabel);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Helper to emit code that formats output bytes for decipher.
    /// </summary>
    private void EmitDecipherFormatOutput(ILGenerator il, EmittedRuntime runtime, LocalBuilder bytesLocal, OpCode loadEncoding)
    {
        // Check encoding
        var checkHexLabel = il.DefineLabel();
        var checkBase64Label = il.DefineLabel();
        var checkUtf8Label = il.DefineLabel();
        var returnBufferLabel = il.DefineLabel();
        var encodingLocal = il.DeclareLocal(_types.String);

        il.Emit(loadEncoding);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // if (encoding == null) return Buffer
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Brfalse, returnBufferLabel);

        // lowerEncoding = encoding.ToLowerInvariant()
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkHexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkBase64Label);

        // Check "utf8" or "utf-8"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkUtf8Label);

        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "utf-8");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkUtf8Label);

        // Default - return buffer
        il.Emit(OpCodes.Br, returnBufferLabel);

        // Return hex string
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToHexString", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // Return base64 string
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBase64String", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Ret);

        // Return UTF8 string
        il.MarkLabel(checkUtf8Label);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetString", [_types.MakeArrayType(_types.Byte)])!);
        il.Emit(OpCodes.Ret);

        // Return Buffer
        il.MarkLabel(returnBufferLabel);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
    }

    /// <summary>
    /// Helper to emit code that converts a $Buffer or byte[] to byte[].
    /// Leaves byte[] on stack.
    /// </summary>
    private void EmitBufferToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData)
    {
        var objLocal = il.DeclareLocal(_types.Object);
        il.Emit(loadData);
        il.Emit(OpCodes.Stloc, objLocal);

        var isBufferLabel = il.DefineLabel();
        var isByteArrayLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // Check if $Buffer
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brtrue, isBufferLabel);

        // Check if byte[]
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, _types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Brtrue, isByteArrayLabel);

        // Fallback: throw
        il.Emit(OpCodes.Ldstr, "Expected Buffer or byte[]");
        il.Emit(OpCodes.Newobj, _types.ArgumentException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Buffer path: call GetData()
        il.MarkLabel(isBufferLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Br, doneLabel);

        // byte[] path: just cast
        il.MarkLabel(isByteArrayLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, _types.MakeArrayType(_types.Byte));

        il.MarkLabel(doneLabel);
    }
}
