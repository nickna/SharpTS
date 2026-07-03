using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits crypto module helper methods.
    /// </summary>
    private void EmitCryptoMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Scrypt helpers (must be emitted first - scrypt methods are used by EmitCryptoScryptSync)
        EmitScryptMethods(typeBuilder, runtime);

        // Option readers first — CryptoCreateHash and the RSA/cipher option paths use them (#1054)
        EmitGetOptionInt(typeBuilder, runtime);
        EmitGetOptionString(typeBuilder, runtime);

        EmitCryptoCreateHash(typeBuilder, runtime);
        EmitCryptoCreateHmac(typeBuilder, runtime);
        EmitCryptoCreateCipheriv(typeBuilder, runtime);
        EmitCryptoCreateDecipheriv(typeBuilder, runtime);
        EmitCryptoRandomBytes(typeBuilder, runtime);
        EmitCryptoRandomFillSync(typeBuilder, runtime);
        EmitCryptoPbkdf2Sync(typeBuilder, runtime);
        EmitCryptoScryptSync(typeBuilder, runtime);
        EmitCryptoTimingSafeEqual(typeBuilder, runtime);
        EmitCryptoCreateSign(typeBuilder, runtime);
        EmitCryptoCreateVerify(typeBuilder, runtime);
        EmitCryptoGetHashes(typeBuilder, runtime);
        EmitCryptoGetCiphers(typeBuilder, runtime);

        // Standalone helpers (must be emitted before methods that use them)
        // RSA helpers
        EmitExtractKeyPem(typeBuilder, runtime);
        EmitRsaEncryptRaw(typeBuilder, runtime);
        EmitRsaDecryptRaw(typeBuilder, runtime);
        // Key pair generation helpers
        EmitGenerateRsaKeyPairRaw(typeBuilder, runtime);
        EmitGenerateEcKeyPairRaw(typeBuilder, runtime);

        // Methods that use the standalone helpers
        EmitCryptoGenerateKeyPairSync(typeBuilder, runtime);
        EmitCryptoCreateDiffieHellman(typeBuilder, runtime);
        EmitCryptoGetDiffieHellman(typeBuilder, runtime);
        EmitCryptoCreateECDH(typeBuilder, runtime);

        // RSA encryption/decryption (uses ExtractKeyPem, RsaEncryptRaw, RsaDecryptRaw)
        EmitCryptoPublicEncrypt(typeBuilder, runtime);
        EmitCryptoPrivateDecrypt(typeBuilder, runtime);
        EmitCryptoPrivateEncrypt(typeBuilder, runtime);
        EmitCryptoPublicDecrypt(typeBuilder, runtime);

        // HKDF key derivation
        EmitCryptoHkdfSync(typeBuilder, runtime);

        // Sign/Verify helpers (standalone)
        EmitSignDataBytes(typeBuilder, runtime);
        EmitVerifyDataBytes(typeBuilder, runtime);

        // ECDH helpers (standalone)
        EmitTSECDHHelpers(typeBuilder, runtime);

        // KeyObject API
        EmitCryptoCreateSecretKey(typeBuilder, runtime);
        EmitCryptoCreatePublicKey(typeBuilder, runtime);
        EmitCryptoCreatePrivateKey(typeBuilder, runtime);

        // Epic #1054 additions (one-shot sign/verify/hash, constants, getCipherInfo,
        // getCurves, primes). $CryptoPrimitives is already emitted (RuntimeEmitter.cs);
        // these helpers live on the $Runtime type.
        EmitCryptoKeyToPem(typeBuilder, runtime);
        EmitCryptoSignOneShot(typeBuilder, runtime);
        EmitCryptoVerifyOneShot(typeBuilder, runtime);
        EmitCryptoHashOneShot(typeBuilder, runtime);
        EmitCryptoGetConstants(typeBuilder, runtime);
        EmitCryptoGetCipherInfo(typeBuilder, runtime);
        EmitCryptoGetCurves(typeBuilder, runtime);
        EmitCryptoPrimeHelpers(typeBuilder, runtime);

        // Emit wrapper methods for named imports
        EmitCryptoMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for crypto module functions to support named imports.
    /// Each wrapper takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitCryptoMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // createHash(algorithm, options?) -> $Hash
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createHash", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);  // options (can be null)
            il.Emit(OpCodes.Call, runtime.CryptoCreateHash);
        });

        // createHmac(algorithm, key) -> $Hmac
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createHmac", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateHmac);
        });

        // randomBytes(size) -> $Array
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomBytes", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Call, runtime.CryptoRandomBytes);
        });

        // randomUUID() -> string
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomUUID", 0, il =>
        {
            var guidLocal = il.DeclareLocal(_types.Guid);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Guid, "NewGuid"));
            il.Emit(OpCodes.Stloc, guidLocal);
            il.Emit(OpCodes.Ldloca, guidLocal);
            il.Emit(OpCodes.Constrained, _types.Guid);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        });

        // randomInt(min?, max?) -> number
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomInt", 2, il =>
        {
            // If arg0 is null, return 0
            var hasArg0Label = il.DefineLabel();
            var hasArg1Label = il.DefineLabel();
            var doRandomLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, hasArg0Label);

            // No args - return 0
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(hasArg0Label);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue, hasArg1Label);

            // One arg - randomInt(max): range [0, max)
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Br, doRandomLabel);

            // Two args - randomInt(min, max): range [min, max)
            il.MarkLabel(hasArg1Label);
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToInt32(il);

            il.MarkLabel(doRandomLabel);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.RandomNumberGenerator,
                "GetInt32",
                _types.Int32, _types.Int32));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
        });

        // createCipheriv(algorithm, key, iv) -> $Cipher
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createCipheriv", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateCipheriv);
        });

        // createDecipheriv(algorithm, key, iv) -> $Decipher
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createDecipheriv", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateDecipheriv);
        });

        // pbkdf2Sync(password, salt, iterations, keylen, digest) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "pbkdf2Sync", 5, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_3);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg, 4);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoPbkdf2Sync);
        });

        // scryptSync(password, salt, keylen, options?) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "scryptSync", 4, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_3);  // options (can be null)
            il.Emit(OpCodes.Call, runtime.CryptoScryptSync);
        });

        // timingSafeEqual(a, b) -> boolean
        EmitCryptoMethodWrapper(typeBuilder, runtime, "timingSafeEqual", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Call, runtime.CryptoTimingSafeEqual);
        });

        // createSign(algorithm) -> $Sign
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createSign", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateSign);
        });

        // createVerify(algorithm) -> $Verify
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createVerify", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateVerify);
        });

        // getHashes() -> $Array
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getHashes", 0, il =>
        {
            il.Emit(OpCodes.Call, runtime.CryptoGetHashes);
        });

        // getCiphers() -> $Array
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getCiphers", 0, il =>
        {
            il.Emit(OpCodes.Call, runtime.CryptoGetCiphers);
        });

        // generateKeyPairSync(type, options?) -> $Object
        EmitCryptoMethodWrapper(typeBuilder, runtime, "generateKeyPairSync", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);  // options (can be null)
            il.Emit(OpCodes.Call, runtime.CryptoGenerateKeyPairSync);
        });

        // createDiffieHellman(primeOrLength, generator?) -> $DiffieHellman
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createDiffieHellman", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // prime or length
            il.Emit(OpCodes.Ldarg_1);  // generator (can be null)
            il.Emit(OpCodes.Call, runtime.CryptoCreateDiffieHellman);
        });

        // getDiffieHellman(groupName) -> $DiffieHellman
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getDiffieHellman", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoGetDiffieHellman);
        });

        // createECDH(curveName) -> $ECDH
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createECDH", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateECDH);
        });

        // publicEncrypt(key, buffer) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "publicEncrypt", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);  // buffer as byte[]
            il.Emit(OpCodes.Call, runtime.CryptoPublicEncrypt);
        });

        // privateDecrypt(key, buffer) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "privateDecrypt", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);  // buffer as byte[]
            il.Emit(OpCodes.Call, runtime.CryptoPrivateDecrypt);
        });

        // privateEncrypt(key, buffer) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "privateEncrypt", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);  // buffer as byte[]
            il.Emit(OpCodes.Call, runtime.CryptoPrivateEncrypt);
        });

        // publicDecrypt(key, buffer) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "publicDecrypt", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);  // buffer as byte[]
            il.Emit(OpCodes.Call, runtime.CryptoPublicDecrypt);
        });

        // hkdfSync(digest, ikm, salt, info, keylen) -> Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "hkdfSync", 5, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);  // digest
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);  // ikm
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToKeyBytes(il);  // salt
            il.Emit(OpCodes.Ldarg_3);
            EmitObjectToKeyBytes(il);  // info
            il.Emit(OpCodes.Ldarg, 4);
            EmitObjectToInt32(il);  // keylen
            il.Emit(OpCodes.Call, runtime.CryptoHkdfSync);
        });

        // createSecretKey(key, encoding?) -> KeyObject
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createSecretKey", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Ldarg_1);  // encoding (can be null)
            il.Emit(OpCodes.Call, runtime.CryptoCreateSecretKey);
        });

        // createPublicKey(key) -> KeyObject
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createPublicKey", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Call, runtime.CryptoCreatePublicKey);
        });

        // createPrivateKey(key) -> KeyObject
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createPrivateKey", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);  // key (as object)
            il.Emit(OpCodes.Call, runtime.CryptoCreatePrivateKey);
        });

        // === Async (callback-based) wrappers ===
        // These call the sync versions and invoke the callback with (null, result) or (errorMsg, null)

        // pbkdf2(password, salt, iterations, keylen, digest, callback) -> null
        EmitCryptoAsyncWrapper(typeBuilder, runtime, "pbkdf2", 6, (il, runtime_) =>
        {
            // Call sync version: CryptoPbkdf2Sync(password_bytes, salt_bytes, iterations, keylen, digest)
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_3);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg, 4);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime_.CryptoPbkdf2Sync);
        });

        // scrypt(password, salt, keylen, options_or_callback, callback?) -> null
        // Special: callback can be arg3 (no options) or arg4 (with options)
        EmitCryptoScryptAsyncWrapper(typeBuilder, runtime);

        // hkdf(digest, ikm, salt, info, keylen, callback) -> null
        EmitCryptoAsyncWrapper(typeBuilder, runtime, "hkdf", 6, (il, runtime_) =>
        {
            // Call sync version: CryptoHkdfSync(digest, ikm_bytes, salt_bytes, info_bytes, keylen)
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg_3);
            EmitObjectToKeyBytes(il);
            il.Emit(OpCodes.Ldarg, 4);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Call, runtime_.CryptoHkdfSync);
        });

        // generateKeyPair(type, options, callback) -> null
        // Special: callback is (err, publicKey, privateKey) not (err, result)
        EmitCryptoGenerateKeyPairAsyncWrapper(typeBuilder, runtime);

        // === Epic #1054 module-level wrappers (named-import support) ===

        // hash(algorithm, data, outputEncoding?) -> string|Buffer
        EmitCryptoMethodWrapper(typeBuilder, runtime, "hash", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            EmitObjectToStringOrNull(il);
            il.Emit(OpCodes.Call, runtime.CryptoHashOneShot);
        });

        // sign(algorithm, data, key, callback?) -> Buffer (sync form; callback form handled by module emitter)
        EmitCryptoMethodWrapper(typeBuilder, runtime, "sign", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToStringOrNull(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.CryptoSignDataEx);
        });

        // verify(algorithm, data, key, signature, callback?) -> bool
        EmitCryptoMethodWrapper(typeBuilder, runtime, "verify", 4, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToStringOrNull(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, runtime.CryptoVerifyDataEx);
        });

        // getCipherInfo(nameOrNid, options?) -> object|undefined
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getCipherInfo", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.CryptoGetCipherInfo);
        });

        // getCurves() -> string[]
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getCurves", 0, il =>
        {
            il.Emit(OpCodes.Call, runtime.CryptoGetCurves);
        });

        // generatePrimeSync(size, options?) -> Buffer|bigint
        EmitCryptoMethodWrapper(typeBuilder, runtime, "generatePrimeSync", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.CryptoGeneratePrimeSyncObj);
        });

        // checkPrimeSync(candidate, options?) -> bool
        EmitCryptoMethodWrapper(typeBuilder, runtime, "checkPrimeSync", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.CryptoCheckPrimeSyncObj);
        });

        // Callback-based async wrappers owned by this slice: randomFill, generatePrime,
        // checkPrime. (generateKey/getFips/setFips/diffieHellman/ECDH live in #1059/#1060.)
        EmitCryptoRandomFillAsyncWrapper(typeBuilder, runtime);
        EmitCryptoGeneratePrimeAsyncWrapper(typeBuilder, runtime);
        EmitCryptoCheckPrimeAsyncWrapper(typeBuilder, runtime);
    }

    /// <summary>obj?.ToString() or null (unlike EmitObjectToString which yields "").</summary>
    private void EmitObjectToStringOrNull(ILGenerator il)
    {
        var nullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits a wrapper method for a crypto module function.
    /// Takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitCryptoMethodWrapper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitBody)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_" + methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes);

        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);

        // Register the wrapper for named imports
        runtime.RegisterBuiltInModuleMethod("crypto", methodName, method);
    }

    /// <summary>
    /// Emits code to convert an object to string (handles null).
    /// </summary>
    private void EmitObjectToString(ILGenerator il)
    {
        // obj?.ToString() ?? ""
        var isNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, isNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(isNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits code to convert an object to int32 (handles null and boxed doubles).
    /// </summary>
    private void EmitObjectToInt32(ILGenerator il)
    {
        // Check for null
        var notNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notNullLabel);

        // Unbox as double first (TypeScript numbers are doubles)
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits code to convert an object to byte[] for HMAC key.
    /// Handles string (UTF-8) and $Array (Buffer-like).
    /// </summary>
    private void EmitObjectToKeyBytes(ILGenerator il)
    {
        // Check if string or $Array
        var isStringLabel = il.DefineLabel();
        var convertLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        var objLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, objLocal);

        // if (obj is string) -> UTF8.GetBytes
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // Otherwise, try to convert from $Array to byte[]
        // For now, just encode as UTF-8 string (fallback)
        il.Emit(OpCodes.Br, convertLabel);

        // String path
        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Br, endLabel);

        // Fallback - convert to string and then UTF-8
        il.MarkLabel(convertLabel);
        il.Emit(OpCodes.Call, _types.Encoding.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.Encoding.GetMethod("GetBytes", [_types.String])!);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits an async wrapper that calls a sync crypto method, then invokes the callback with (null, result) or (errorMsg, null).
    /// The last argument is always the callback (a $TSFunction).
    /// </summary>
    private void EmitCryptoAsyncWrapper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator, EmittedRuntime> emitSyncCall)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_" + methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes);

        var il = method.GetILGenerator();
        var callbackArgIndex = paramCount - 1;
        var resultLocal = il.DeclareLocal(_types.Object);
        var catchLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // try { result = syncMethod(...); callback(null, result); } catch { callback(error, null); }
        il.BeginExceptionBlock();

        // Call the sync method (emits args 0..N-2, calls runtime method)
        emitSyncCall(il, runtime);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback.Invoke([null, result])
        il.Emit(OpCodes.Ldarg, callbackArgIndex);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // err = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal); // result
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        // catch (Exception ex)
        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        // callback.Invoke([errorMessage, null])
        il.Emit(OpCodes.Ldarg, callbackArgIndex);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull); // result = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("crypto", methodName, method);
    }

    /// <summary>
    /// Special async wrapper for scrypt: callback can be arg3 (no options) or arg4 (with options).
    /// scrypt(password, salt, keylen, callback) OR scrypt(password, salt, keylen, options, callback)
    /// </summary>
    private void EmitCryptoScryptAsyncWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_scrypt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var callbackLocal = il.DeclareLocal(_types.Object);
        var optionsLocal = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        // Determine callback: if arg4 != null, callback=arg4, options=arg3
        //                     if arg4 == null, callback=arg3, options=null
        var arg4IsCallbackLabel = il.DefineLabel();
        var afterCallbackLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Brtrue, arg4IsCallbackLabel);

        // arg4 is null → callback=arg3, options=null
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.Emit(OpCodes.Br, afterCallbackLabel);

        // arg4 is not null → callback=arg4, options=arg3
        il.MarkLabel(arg4IsCallbackLabel);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, optionsLocal);

        il.MarkLabel(afterCallbackLabel);

        il.BeginExceptionBlock();

        // Call sync: CryptoScryptSync(password_bytes, salt_bytes, keylen, options)
        il.Emit(OpCodes.Ldarg_0);
        EmitObjectToKeyBytes(il);
        il.Emit(OpCodes.Ldarg_1);
        EmitObjectToKeyBytes(il);
        il.Emit(OpCodes.Ldarg_2);
        EmitObjectToInt32(il);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, runtime.CryptoScryptSync);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback.Invoke([null, result])
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("crypto", "scrypt", method);
    }

    /// <summary>
    /// Special async wrapper for generateKeyPair which has (err, publicKey, privateKey) callback signature.
    /// </summary>
    private void EmitCryptoGenerateKeyPairAsyncWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // generateKeyPair(type, options, callback)
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_generateKeyPair",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // Call sync version: CryptoGenerateKeyPairSync(type, options)
        il.Emit(OpCodes.Ldarg_0); // type
        EmitObjectToString(il);
        il.Emit(OpCodes.Ldarg_1); // options
        il.Emit(OpCodes.Call, runtime.CryptoGenerateKeyPairSync);
        il.Emit(OpCodes.Stloc, resultLocal);

        // callback.Invoke([null, publicKey, privateKey])
        // Get publicKey from result dict
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // err = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        // Get "publicKey" from result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "publicKey");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        // Get "privateKey" from result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldstr, "privateKey");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        // catch
        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        // slots 1 and 2 remain null
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endLabel);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("crypto", "generateKeyPair", method);
    }
}
