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
        runtime.TSCipherType = typeBuilder;

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
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]
        );
        runtime.TSCipherCtor = ctor;

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
        il.Emit(OpCodes.Stfld, _tsCipherAlgorithmField);

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
        EmitStringCompare(il, lowerAlgoLocal, "aes-128-cbc", aes128CbcLabel);
        EmitStringCompare(il, lowerAlgoLocal, "aes-192-cbc", aes192CbcLabel);
        EmitStringCompare(il, lowerAlgoLocal, "aes-256-cbc", aes256CbcLabel);
        EmitStringCompare(il, lowerAlgoLocal, "aes-128-gcm", aes128GcmLabel);
        EmitStringCompare(il, lowerAlgoLocal, "aes-192-gcm", aes192GcmLabel);
        EmitStringCompare(il, lowerAlgoLocal, "aes-256-gcm", aes256GcmLabel);
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
        il.Emit(OpCodes.Stfld, _tsCipherIsGcmField);

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

        // Validate IV size: GCM=12, CBC=16
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
        il.Emit(OpCodes.Stfld, _tsCipherKeyField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, _tsCipherIvField);

        // Initialize _plaintextBuffer for GCM
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfByte));
        il.Emit(OpCodes.Stfld, _tsCipherPlaintextBufferField);

        // _autoPadding = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsCipherAutoPaddingField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsCipherFinalizedField);

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
        il.Emit(OpCodes.Stfld, _tsCipherAesGcmField);
        il.Emit(OpCodes.Br, initDoneLabel);

        // CBC mode: create Aes and encryptor
        il.MarkLabel(initCbcLabel);

        // _aes = Aes.Create()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Aes.GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsCipherAesField);

        // _aes.Key = key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Key")!.SetMethod!);

        // _aes.IV = iv
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("IV")!.SetMethod!);

        // _aes.Mode = CipherMode.CBC
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Ldc_I4_1); // CipherMode.CBC = 1
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Mode")!.SetMethod!);

        // _aes.Padding = PaddingMode.PKCS7
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Ldc_I4_2); // PaddingMode.PKCS7 = 2
        il.Emit(OpCodes.Callvirt, _types.Aes.GetProperty("Padding")!.SetMethod!);

        // _encryptor = _aes.CreateEncryptor()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Callvirt, _types.Aes.GetMethod("CreateEncryptor", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsCipherEncryptorField);

        il.MarkLabel(initDoneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringCompare(ILGenerator il, LocalBuilder stringLocal, string value, Label targetLabel)
    {
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, targetLabel);
    }

    /// <summary>
    /// Emits: private static void GcmEncryptHelper(AesGcm gcm, byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] aad)
    /// Uses the AesGcm.Encrypt byte[] overload.
    /// </summary>
    private void EmitTSCipherGcmEncryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GcmEncryptHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.AesGcm, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        _tsCipherGcmEncryptHelper = method;

        var il = method.GetILGenerator();

        // Get the byte[] overload: Encrypt(byte[] nonce, byte[] plaintext, byte[] ciphertext, byte[] tag, byte[] associatedData)
        var encryptMethod = _types.AesGcm.GetMethod("Encrypt",
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)])!;

        // Check if aad is null - if so, use empty array
        var aadNotNullLabel = il.DefineLabel();
        var callEncryptLabel = il.DefineLabel();
        var aadLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Brtrue, aadNotNullLabel);

        // aad is null - use empty byte array
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, aadLocal);
        il.Emit(OpCodes.Br, callEncryptLabel);

        il.MarkLabel(aadNotNullLabel);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stloc, aadLocal);

        // Call gcm.Encrypt(nonce, plaintext, ciphertext, tag, aad)
        il.MarkLabel(callEncryptLabel);
        il.Emit(OpCodes.Ldarg_0);   // gcm
        il.Emit(OpCodes.Ldarg_1);   // nonce
        il.Emit(OpCodes.Ldarg_2);   // plaintext
        il.Emit(OpCodes.Ldarg_3);   // ciphertext
        il.Emit(OpCodes.Ldarg_S, (byte)4);   // tag
        il.Emit(OpCodes.Ldloc, aadLocal);    // aad
        il.Emit(OpCodes.Callvirt, encryptMethod);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Update(object data, string? inputEncoding, string? outputEncoding)
    /// Simplified: buffers all data and processes in Final()
    /// </summary>
    private void EmitTSCipherUpdate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.String, _types.String]
        );
        runtime.TSCipherUpdateMethod = method;

        var il = method.GetILGenerator();

        // Check if finalized
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "Cipher has already been finalized");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

        // Convert input to bytes
        var inputBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitConvertToBytes(il, runtime, OpCodes.Ldarg_1, OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, inputBytesLocal);

        // Buffer data in _plaintextBuffer (used for both CBC and GCM)
        // This simplifies Update to just buffer, and Final does all the work
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherPlaintextBufferField);
        il.Emit(OpCodes.Ldloc, inputBytesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("AddRange", [_types.IEnumerableOfByte])!);

        // Return empty buffer (all data processed in Final)
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        EmitFormatOutput(il, runtime, resultLocal, OpCodes.Ldarg_3);
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
        runtime.TSCipherFinalMethod = method;

        var il = method.GetILGenerator();

        // Check if finalized
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherFinalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "Cipher has already been finalized");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);

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
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("ToArray")!);
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
        EmitFormatOutput(il, runtime, ciphertextLocal, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        // CBC mode: Get all buffered data and TransformFinalBlock
        il.MarkLabel(cbcModeLabel);

        // bufferedData = _plaintextBuffer.ToArray()
        var bufferedDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherPlaintextBufferField);
        il.Emit(OpCodes.Callvirt, _types.ListOfByte.GetMethod("ToArray")!);
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
        il.Emit(OpCodes.Callvirt, _types.ICryptoTransform.GetMethod("TransformFinalBlock")!);
        il.Emit(OpCodes.Stloc, finalBlockLocal);

        EmitFormatOutput(il, runtime, finalBlockLocal, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Cipher SetAutoPadding(bool autoPadding)
    /// </summary>
    private void EmitTSCipherSetAutoPadding(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAutoPadding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Boolean]
        );
        runtime.TSCipherSetAutoPaddingMethod = method;

        var il = method.GetILGenerator();

        // Return this for chaining (simplified - just return this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

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
        runtime.TSCipherGetAuthTagMethod = method;

        var il = method.GetILGenerator();

        // Check if GCM
        var isGcmLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherIsGcmField);
        il.Emit(OpCodes.Brtrue, isGcmLabel);
        il.Emit(OpCodes.Ldstr, "getAuthTag is only available for GCM mode ciphers");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(isGcmLabel);

        // Check if finalized
        var isFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherFinalizedField);
        il.Emit(OpCodes.Brtrue, isFinalizedLabel);
        il.Emit(OpCodes.Ldstr, "getAuthTag must be called after final()");
        il.Emit(OpCodes.Newobj, _types.InvalidOperationException.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(isFinalizedLabel);

        // Return new $Buffer(_authTag)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAuthTagField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public $Cipher SetAAD(object aad)
    /// Accepts $Buffer or byte[] and stores as byte[].
    /// </summary>
    private void EmitTSCipherSetAAD(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAAD",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.TSCipherSetAADMethod = method;

        var il = method.GetILGenerator();

        // Convert $Buffer to byte[]
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitCipherBufferToBytes(il, runtime, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Store AAD
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Stfld, _tsCipherAadField);

        // Return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void Dispose()
    /// </summary>
    private void EmitTSCipherDispose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // Dispose encryptor if not null
        var encryptorNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherEncryptorField);
        il.Emit(OpCodes.Brfalse, encryptorNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherEncryptorField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(encryptorNullLabel);

        // Dispose aes if not null
        var aesNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Brfalse, aesNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(aesNullLabel);

        // Dispose aesGcm if not null
        var aesGcmNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesGcmField);
        il.Emit(OpCodes.Brfalse, aesGcmNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsCipherAesGcmField);
        il.Emit(OpCodes.Callvirt, _types.IDisposable.GetMethod("Dispose")!);
        il.MarkLabel(aesGcmNullLabel);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper to emit code that converts input data to byte array.
    /// </summary>
    private void EmitConvertToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData, OpCode loadEncoding)
    {
        // For simplicity, we assume the data is already a $Buffer and get its bytes
        // A full implementation would check for string and convert based on encoding
        var dataLocal = il.DeclareLocal(_types.Object);
        il.Emit(loadData);
        il.Emit(OpCodes.Stloc, dataLocal);

        var isBufferLabel = il.DefineLabel();
        var isStringLabel = il.DefineLabel();
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

        // String path - convert using UTF8 (simplified)
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Helper to emit code that formats output bytes.
    /// </summary>
    private void EmitFormatOutput(ILGenerator il, EmittedRuntime runtime, LocalBuilder bytesLocal, OpCode loadEncoding)
    {
        // Check encoding
        var checkHexLabel = il.DefineLabel();
        var checkBase64Label = il.DefineLabel();
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

        // Return Buffer
        il.MarkLabel(returnBufferLabel);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
    }

    /// <summary>
    /// Helper to emit code that converts a $Buffer or byte[] to byte[].
    /// Leaves byte[] on stack.
    /// </summary>
    private void EmitCipherBufferToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData)
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
