using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Shared emitters for the streaming-crypto type pairs (#1132). $Sign/$Verify
/// share their entire type definition (fields, ctor, Update); $Cipher/$Decipher
/// share the ctor, GCM helper, input/output conversion, and the small
/// SetAutoPadding/SetAAD/Dispose members, parameterized by direction. $Hash and
/// $Hmac are structurally different (buffered one-shot vs IncrementalHash) and
/// only share the finalized-guard idiom.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits <c>if (_finalized) throw new InvalidOperationException(message);</c>.
    /// </summary>
    private void EmitThrowIfFinalized(ILGenerator il, FieldBuilder finalizedField, string message)
    {
        var notFinalizedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, finalizedField);
        il.Emit(OpCodes.Brfalse, notFinalizedLabel);

        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.InvalidOperationException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notFinalizedLabel);
    }

    /// <summary>
    /// Emits <c>if (stringLocal == value) goto targetLabel;</c>.
    /// </summary>
    private void EmitCryptoAlgoCompare(ILGenerator il, LocalBuilder stringLocal, string value, Label targetLabel)
    {
        il.Emit(OpCodes.Ldloc, stringLocal);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, targetLabel);
    }

    // ---------------------------------------------------------------------
    // $Sign / $Verify — shared type definition (fields + ctor + Update)
    // ---------------------------------------------------------------------

    private sealed record StreamingSignVerifyParts(
        TypeBuilder Type,
        FieldBuilder HashAlgorithmField,
        FieldBuilder DataField,
        FieldBuilder FinalizedField,
        ConstructorBuilder Ctor);

    /// <summary>
    /// Defines the $Sign/$Verify type shell: `_hashAlgorithm`/`_data`/`_finalized`
    /// fields, the algorithm-parsing constructor (strips the `rsa-`/`ecdsa-`
    /// prefix, resolves SHA1/256/384/512), and the UTF-8-accumulating
    /// <c>Update(string)</c>. The pair's final methods (Sign/Verify) stay with
    /// their emitters — they need runtime helpers only available in phase 2.
    /// </summary>
    private StreamingSignVerifyParts EmitStreamingSignVerifyTypeDefinition(
        ModuleBuilder moduleBuilder, string typeName,
        string unsupportedAlgorithmPrefix, string updateFinalizedMessage)
    {
        var typeBuilder = moduleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var hashAlgorithmField = typeBuilder.DefineField("_hashAlgorithm", _types.HashAlgorithmName, FieldAttributes.Private);
        var dataField = typeBuilder.DefineField("_data", typeof(List<byte>), FieldAttributes.Private);
        var finalizedField = typeBuilder.DefineField("_finalized", _types.Boolean, FieldAttributes.Private);

        var ctor = EmitStreamingSignVerifyCtor(typeBuilder, hashAlgorithmField, dataField, finalizedField,
            unsupportedAlgorithmPrefix);
        EmitStreamingSignVerifyUpdate(typeBuilder, dataField, finalizedField, updateFinalizedMessage);

        return new StreamingSignVerifyParts(typeBuilder, hashAlgorithmField, dataField, finalizedField, ctor);
    }

    /// <summary>
    /// Emits: public $Sign/$Verify(string algorithm)
    /// </summary>
    private ConstructorBuilder EmitStreamingSignVerifyCtor(TypeBuilder typeBuilder,
        FieldBuilder hashAlgorithmField, FieldBuilder dataField, FieldBuilder finalizedField,
        string unsupportedAlgorithmPrefix)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );

        var il = ctor.GetILGenerator();

        // Local for algorithm string (normalized)
        var normalizedLocal = il.DeclareLocal(_types.String);
        var hashNameLocal = il.DeclareLocal(_types.HashAlgorithmName);

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // normalized = algorithm.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, normalizedLocal);

        // Remove "rsa-" or "ecdsa-" prefix if present
        var checkEcdsaLabel = il.DefineLabel();
        var afterPrefixLabel = il.DefineLabel();

        // if (normalized.StartsWith("rsa-")) normalized = normalized.Substring(4)
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "rsa-");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, checkEcdsaLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, normalizedLocal);
        il.Emit(OpCodes.Br, afterPrefixLabel);

        // if (normalized.StartsWith("ecdsa-")) normalized = normalized.Substring(6)
        il.MarkLabel(checkEcdsaLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "ecdsa-");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, afterPrefixLabel);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, normalizedLocal);

        il.MarkLabel(afterPrefixLabel);

        // Switch on algorithm name
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var setHashLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitCryptoAlgoCompare(il, normalizedLocal, "sha1", sha1Label);
        EmitCryptoAlgoCompare(il, normalizedLocal, "sha256", sha256Label);
        EmitCryptoAlgoCompare(il, normalizedLocal, "sha384", sha384Label);
        EmitCryptoAlgoCompare(il, normalizedLocal, "sha512", sha512Label);

        // Default - throw exception
        il.Emit(OpCodes.Br, defaultLabel);

        // SHA1
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, "SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA256
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, "SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA384
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, "SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // SHA512
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, "SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashNameLocal);
        il.Emit(OpCodes.Br, setHashLabel);

        // Default - throw ArgumentException
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, unsupportedAlgorithmPrefix);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        // Set hash algorithm field
        il.MarkLabel(setHashLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hashNameLocal);
        il.Emit(OpCodes.Stfld, hashAlgorithmField);

        // _data = new List<byte>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ListByteDefaultCtor);
        il.Emit(OpCodes.Stfld, dataField);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, finalizedField);

        il.Emit(OpCodes.Ret);
        return ctor;
    }

    /// <summary>
    /// Emits: public $Sign/$Verify Update(string data) — UTF-8 encode + accumulate.
    /// </summary>
    private void EmitStreamingSignVerifyUpdate(TypeBuilder typeBuilder,
        FieldBuilder dataField, FieldBuilder finalizedField, string finalizedMessage)
    {
        var method = typeBuilder.DefineMethod(
            "Update",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();

        EmitThrowIfFinalized(il, finalizedField, finalizedMessage);

        // var bytes = Encoding.UTF8.GetBytes(data)
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // _data.AddRange(bytes)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dataField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Callvirt, _types.ListByteAddRange);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // ---------------------------------------------------------------------
    // $Cipher / $Decipher — shared ctor, GCM helper, conversions, small members
    // ---------------------------------------------------------------------

    private sealed record StreamingCipherFields(
        FieldBuilder Algorithm,
        FieldBuilder Key,
        FieldBuilder Iv,
        FieldBuilder IsGcm,
        FieldBuilder Aes,
        FieldBuilder Transform,
        FieldBuilder AesGcm,
        FieldBuilder Finalized,
        FieldBuilder AutoPadding,
        FieldBuilder AuthTag,
        FieldBuilder Aad);

    /// <summary>
    /// Emits: public $Cipher/$Decipher(string algorithm, byte[] key, byte[] iv).
    /// Parses aes-{128,192,256}-{cbc,gcm}, validates key/IV lengths, initializes
    /// the buffer list fields in <paramref name="byteBuffersToInit"/> order, and
    /// creates either the AesGcm instance or the CBC Aes +
    /// CreateEncryptor/CreateDecryptor transform per
    /// <paramref name="isEncrypt"/>.
    /// </summary>
    private ConstructorBuilder EmitStreamingCipherCtor(TypeBuilder typeBuilder,
        StreamingCipherFields f, bool isEncrypt, FieldBuilder[] byteBuffersToInit)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]
        );

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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, lowerAlgoLocal);
        il.Emit(OpCodes.Stfld, f.Algorithm);

        // Parse algorithm - determine isGcm and keySize
        var afterParseLabel = il.DefineLabel();
        var unsupportedLabel = il.DefineLabel();
        // (keySize, isGcm) per supported algorithm, in check order.
        (string Name, int KeySize, bool IsGcm)[] algorithms =
        [
            ("aes-128-cbc", 16, false),
            ("aes-192-cbc", 24, false),
            ("aes-256-cbc", 32, false),
            ("aes-128-gcm", 16, true),
            ("aes-192-gcm", 24, true),
            ("aes-256-gcm", 32, true),
        ];
        var algoLabels = new Label[algorithms.Length];
        for (int i = 0; i < algorithms.Length; i++)
        {
            algoLabels[i] = il.DefineLabel();
            EmitCryptoAlgoCompare(il, lowerAlgoLocal, algorithms[i].Name, algoLabels[i]);
        }
        il.Emit(OpCodes.Br, unsupportedLabel);

        for (int i = 0; i < algorithms.Length; i++)
        {
            il.MarkLabel(algoLabels[i]);
            il.Emit(OpCodes.Ldc_I4, algorithms[i].KeySize);
            il.Emit(OpCodes.Stloc, keySizeLocal);
            il.Emit(algorithms[i].IsGcm ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, isGcmLocal);
            il.Emit(OpCodes.Br, afterParseLabel);
        }

        // Unsupported algorithm - throw
        il.MarkLabel(unsupportedLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported cipher algorithm: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(afterParseLabel);

        // Store _isGcm
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, isGcmLocal);
        il.Emit(OpCodes.Stfld, f.IsGcm);

        // Validate key size
        var keySizeOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, keySizeLocal);
        il.Emit(OpCodes.Beq, keySizeOkLabel);

        // Key size mismatch - throw
        il.Emit(OpCodes.Ldstr, "Invalid key length for cipher algorithm");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(ivSizeOkLabel);

        // Store key and iv
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, f.Key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stfld, f.Iv);

        // Initialize buffer list fields
        foreach (var bufferField in byteBuffersToInit)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfByte));
            il.Emit(OpCodes.Stfld, bufferField);
        }

        // _autoPadding = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, f.AutoPadding);

        // _finalized = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, f.Finalized);

        // Initialize crypto objects based on mode
        var initCbcLabel = il.DefineLabel();
        var initDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, isGcmLocal);
        il.Emit(OpCodes.Brfalse, initCbcLabel);

        // GCM mode: _aesGcm = new AesGcm(_key, 16)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2); // key
        il.Emit(OpCodes.Ldc_I4, 16); // tag size
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.AesGcm, [_types.MakeArrayType(_types.Byte), _types.Int32])!);
        il.Emit(OpCodes.Stfld, f.AesGcm);
        il.Emit(OpCodes.Br, initDoneLabel);

        // CBC mode: create Aes and the encryptor/decryptor transform
        il.MarkLabel(initCbcLabel);

        // _aes = Aes.Create()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Aes, "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, f.Aes);

        // _aes.Key = key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, f.Aes);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Aes, "Key")!.SetMethod!);

        // _aes.IV = iv
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, f.Aes);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Aes, "IV")!.SetMethod!);

        // _aes.Mode = CipherMode.CBC
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, f.Aes);
        il.Emit(OpCodes.Ldc_I4_1); // CipherMode.CBC = 1
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Aes, "Mode")!.SetMethod!);

        // _aes.Padding = PaddingMode.PKCS7
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, f.Aes);
        il.Emit(OpCodes.Ldc_I4_2); // PaddingMode.PKCS7 = 2
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Aes, "Padding")!.SetMethod!);

        // _transform = _aes.CreateEncryptor() / CreateDecryptor()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, f.Aes);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Aes, isEncrypt ? "CreateEncryptor" : "CreateDecryptor", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, f.Transform);

        il.MarkLabel(initDoneLabel);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    /// <summary>
    /// Emits the private static GCM one-shot helper:
    /// encrypt: <c>GcmEncryptHelper(gcm, nonce, plaintext, ciphertext, tag, aad)</c>
    /// → <c>AesGcm.Encrypt(nonce, plaintext, ciphertext, tag, aad)</c>;
    /// decrypt: <c>GcmDecryptHelper(gcm, nonce, ciphertext, plaintext, tag, aad)</c>
    /// → <c>AesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad)</c>.
    /// A null aad is replaced with an empty array.
    /// </summary>
    private MethodBuilder EmitGcmTransformHelper(TypeBuilder typeBuilder, string helperName, bool isEncrypt)
    {
        var method = typeBuilder.DefineMethod(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.AesGcm, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);

        var il = method.GetILGenerator();

        var bclMethod = _types.GetMethod(_types.AesGcm, isEncrypt ? "Encrypt" : "Decrypt",
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)])!;

        // Check if aad is null - if so, use empty array
        var aadNotNullLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();
        var aadLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Brtrue, aadNotNullLabel);

        // aad is null - use empty byte array
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, aadLocal);
        il.Emit(OpCodes.Br, callLabel);

        il.MarkLabel(aadNotNullLabel);
        il.Emit(OpCodes.Ldarg_S, (byte)5);
        il.Emit(OpCodes.Stloc, aadLocal);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldarg_0);   // gcm
        il.Emit(OpCodes.Ldarg_1);   // nonce
        if (isEncrypt)
        {
            il.Emit(OpCodes.Ldarg_2);            // plaintext
            il.Emit(OpCodes.Ldarg_3);            // ciphertext
            il.Emit(OpCodes.Ldarg_S, (byte)4);   // tag
        }
        else
        {
            // AesGcm.Decrypt expects (nonce, ciphertext, tag, plaintext, aad)
            il.Emit(OpCodes.Ldarg_2);            // ciphertext
            il.Emit(OpCodes.Ldarg_S, (byte)4);   // tag
            il.Emit(OpCodes.Ldarg_3);            // plaintext
        }
        il.Emit(OpCodes.Ldloc, aadLocal);        // aad
        il.Emit(OpCodes.Callvirt, bclMethod);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits code converting a data argument (string with hex/base64/utf8
    /// encoding, or $Buffer) to a byte[] left on the stack. The encoding
    /// argument may be any object ($Undefined, null, or string).
    /// </summary>
    private void EmitCipherInputToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData, OpCode loadEncoding)
    {
        var dataLocal = il.DeclareLocal(_types.Object);
        var encodingLocal = il.DeclareLocal(_types.String);

        il.Emit(loadData);
        il.Emit(OpCodes.Stloc, dataLocal);

        // Encoding may be object ($Undefined, null, or string) — extract string or null
        var encodingIsStringLabel = il.DefineLabel();
        il.Emit(loadEncoding);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, encodingIsStringLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(encodingIsStringLabel);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkHexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkBase64Label);

        // Default UTF8
        il.Emit(OpCodes.Br, utf8DefaultLabel);

        // Hex decode
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.ConvertFromHexString);
        il.Emit(OpCodes.Br, doneLabel);

        // Base64 decode
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.ConvertFromBase64String);
        il.Emit(OpCodes.Br, doneLabel);

        // UTF8 default
        il.MarkLabel(utf8DefaultLabel);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits the output-formatting tail: hex / base64 / (optionally utf8) string,
    /// defaulting to a new $Buffer. Each string branch returns; the buffer branch
    /// leaves the $Buffer on the stack for the caller's Ret.
    /// <paramref name="supportUtf8"/> is the deliberate Cipher/Decipher
    /// divergence (#1054): encrypted output is arbitrary binary so $Cipher never
    /// decodes utf8; decrypted plaintext may be utf8 text so $Decipher does.
    /// </summary>
    private void EmitCipherFormatOutput(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder bytesLocal, OpCode loadEncoding, bool supportUtf8)
    {
        // Check encoding
        var checkHexLabel = il.DefineLabel();
        var checkBase64Label = il.DefineLabel();
        var checkUtf8Label = il.DefineLabel();
        var returnBufferLabel = il.DefineLabel();
        var encodingLocal = il.DeclareLocal(_types.String);

        // Encoding may be object ($Undefined, null, or string) — extract string or null
        var outEncIsStringLabel = il.DefineLabel();
        il.Emit(loadEncoding);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, outEncIsStringLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(outEncIsStringLabel);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // if (encoding == null) return Buffer
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Brfalse, returnBufferLabel);

        // lowerEncoding = encoding.ToLowerInvariant()
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkHexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, checkBase64Label);

        if (supportUtf8)
        {
            // Check "utf8" or "utf-8"
            il.Emit(OpCodes.Ldloc, encodingLocal);
            il.Emit(OpCodes.Ldstr, "utf8");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brtrue, checkUtf8Label);

            il.Emit(OpCodes.Ldloc, encodingLocal);
            il.Emit(OpCodes.Ldstr, "utf-8");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brtrue, checkUtf8Label);
        }

        // Default - return buffer
        il.Emit(OpCodes.Br, returnBufferLabel);

        // Return hex string
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // Return base64 string
        il.MarkLabel(checkBase64Label);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Ret);

        if (supportUtf8)
        {
            // Return UTF8 string
            il.MarkLabel(checkUtf8Label);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloc, bytesLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetString", [_types.MakeArrayType(_types.Byte)])!);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Label defined but unused on the no-utf8 path; mark it into the
            // buffer tail so the ILGenerator sees every label marked.
            il.MarkLabel(checkUtf8Label);
        }

        // Return Buffer
        il.MarkLabel(returnBufferLabel);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
    }

    /// <summary>
    /// Emits code converting a $Buffer or byte[] argument to byte[], left on the
    /// stack; anything else throws ArgumentException.
    /// </summary>
    private void EmitCipherBufferArgToBytes(ILGenerator il, EmittedRuntime runtime, OpCode loadData)
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
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

    /// <summary>
    /// Emits: public $Cipher/$Decipher SetAutoPadding(bool) — chaining no-op.
    /// </summary>
    private void EmitCipherSetAutoPadding(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "SetAutoPadding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Boolean]
        );
        _ = method;

        var il = method.GetILGenerator();

        // Return this for chaining (simplified - just return this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a chaining setter (SetAAD / SetAuthTag) that converts a $Buffer or
    /// byte[] argument and stores it in <paramref name="targetField"/>.
    /// </summary>
    private void EmitCipherStoreBytesArg(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string methodName, FieldBuilder targetField)
    {
        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();

        // Convert $Buffer to byte[]
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        EmitCipherBufferArgToBytes(il, runtime, OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Store
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Stfld, targetField);

        // Return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void Dispose() — disposes the CBC transform, the Aes, and
    /// the AesGcm, each when non-null.
    /// </summary>
    private void EmitCipherDispose(TypeBuilder typeBuilder,
        FieldBuilder transformField, FieldBuilder aesField, FieldBuilder aesGcmField)
    {
        var method = typeBuilder.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        foreach (var field in new[] { transformField, aesField, aesGcmField })
        {
            var nullLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Brfalse, nullLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDisposable, "Dispose")!);
            il.MarkLabel(nullLabel);
        }

        il.Emit(OpCodes.Ret);
    }
}
