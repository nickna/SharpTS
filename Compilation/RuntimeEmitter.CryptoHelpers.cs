using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits crypto module helper methods.
    /// </summary>
    private void EmitCryptoMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCryptoCreateHash(typeBuilder, runtime);
        EmitCryptoCreateHmac(typeBuilder, runtime);
        EmitCryptoCreateCipheriv(typeBuilder, runtime);
        EmitCryptoCreateDecipheriv(typeBuilder, runtime);
        EmitCryptoRandomBytes(typeBuilder, runtime);
        EmitCryptoPbkdf2Sync(typeBuilder, runtime);
        EmitCryptoScryptSync(typeBuilder, runtime);
        EmitCryptoTimingSafeEqual(typeBuilder, runtime);
        EmitCryptoCreateSign(typeBuilder, runtime);
        EmitCryptoCreateVerify(typeBuilder, runtime);
        EmitCryptoGetHashes(typeBuilder, runtime);
        EmitCryptoGetCiphers(typeBuilder, runtime);
        EmitCryptoGenerateKeyPairSync(typeBuilder, runtime);
        EmitCryptoCreateDiffieHellman(typeBuilder, runtime);
        EmitCryptoGetDiffieHellman(typeBuilder, runtime);
        EmitCryptoCreateECDH(typeBuilder, runtime);

        // Emit wrapper methods for named imports
        EmitCryptoMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for crypto module functions to support named imports.
    /// Each wrapper takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitCryptoMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // createHash(algorithm) -> $Hash
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createHash", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
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
    /// Emits: public static object CryptoCreateHash(string algorithm)
    /// </summary>
    private void EmitCryptoCreateHash(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateHash",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateHash = method;

        var il = method.GetILGenerator();

        // new $Hash(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSHashCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateHmac(string algorithm, byte[] key)
    /// </summary>
    private void EmitCryptoCreateHmac(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateHmac",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateHmac = method;

        var il = method.GetILGenerator();

        // new $Hmac(algorithm, key) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSHmacCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateCipheriv(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitCryptoCreateCipheriv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateCipheriv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateCipheriv = method;

        var il = method.GetILGenerator();

        // new $Cipher(algorithm, key, iv)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, runtime.TSCipherCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateDecipheriv(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitCryptoCreateDecipheriv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateDecipheriv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateDecipheriv = method;

        var il = method.GetILGenerator();

        // new $Decipher(algorithm, key, iv)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, runtime.TSDecipherCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoRandomBytes(int size)
    /// Returns a $Buffer containing random bytes.
    /// </summary>
    private void EmitCryptoRandomBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoRandomBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Int32]);
        runtime.CryptoRandomBytes = method;

        var il = method.GetILGenerator();

        // var bytes = RandomNumberGenerator.GetBytes(size);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RandomNumberGenerator).GetMethod("GetBytes", [typeof(int)])!);

        // Return new $Buffer(bytes)
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoPbkdf2Sync(byte[] password, byte[] salt, int iterations, int keylen, string digest)
    /// Returns a $Buffer containing the derived key.
    /// </summary>
    private void EmitCryptoPbkdf2Sync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoPbkdf2Sync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.Int32, _types.Int32, _types.String]);
        runtime.CryptoPbkdf2Sync = method;

        var il = method.GetILGenerator();

        // Get HashAlgorithmName based on digest string
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var md5Label = il.DefineLabel();
        var callPbkdf2Label = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Convert digest to lowercase for comparison
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("ToLowerInvariant")!);
        var digestLower = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, digestLower);

        // Check for sha256 (most common)
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha256");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, sha256Label);

        // Check for sha1
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha1");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, sha1Label);

        // Check for sha384
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha384");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, sha384Label);

        // Check for sha512
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha512");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, sha512Label);

        // Check for md5
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "md5");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Brtrue, md5Label);

        // Unknown algorithm - throw
        il.Emit(OpCodes.Br, throwLabel);

        // sha256 case
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashLocal);
        il.Emit(OpCodes.Br, callPbkdf2Label);

        // sha1 case
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashLocal);
        il.Emit(OpCodes.Br, callPbkdf2Label);

        // sha384 case
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashLocal);
        il.Emit(OpCodes.Br, callPbkdf2Label);

        // sha512 case
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashLocal);
        il.Emit(OpCodes.Br, callPbkdf2Label);

        // md5 case
        il.MarkLabel(md5Label);
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("MD5")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashLocal);
        il.Emit(OpCodes.Br, callPbkdf2Label);

        // throw case
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported digest algorithm");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        // Call Rfc2898DeriveBytes.Pbkdf2
        il.MarkLabel(callPbkdf2Label);
        il.Emit(OpCodes.Ldarg_0);  // password
        il.Emit(OpCodes.Ldarg_1);  // salt
        il.Emit(OpCodes.Ldarg_2);  // iterations
        il.Emit(OpCodes.Ldloc, hashLocal);  // hashAlgorithm
        il.Emit(OpCodes.Ldarg_3);  // keylen
        il.Emit(OpCodes.Call, typeof(Rfc2898DeriveBytes).GetMethod("Pbkdf2",
            [typeof(byte[]), typeof(byte[]), typeof(int), typeof(HashAlgorithmName), typeof(int)])!);

        // Return new $Buffer(derivedKey)
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoScryptSync(byte[] password, byte[] salt, int keylen, object? options)
    /// Returns a $Buffer containing the derived key.
    /// </summary>
    private void EmitCryptoScryptSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoScryptSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte), _types.Int32, _types.Object]);
        runtime.CryptoScryptSync = method;

        // Define a helper method that does the actual scrypt computation
        EmitScryptHelper(typeBuilder, runtime);

        // Define a helper method to extract option value
        var getOptionMethod = EmitScryptGetOption(typeBuilder, runtime);

        var il = method.GetILGenerator();

        // Default parameters
        var NLocal = il.DeclareLocal(_types.Int32);
        var rLocal = il.DeclareLocal(_types.Int32);
        var pLocal = il.DeclareLocal(_types.Int32);

        // N = 16384 (default)
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Stloc, NLocal);

        // r = 8 (default)
        il.Emit(OpCodes.Ldc_I4, 8);
        il.Emit(OpCodes.Stloc, rLocal);

        // p = 1 (default)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, pLocal);

        // Check if options is not null
        var noOptionsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, noOptionsLabel);

        // Try to get N from options
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "N");
        il.Emit(OpCodes.Ldloc, NLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, NLocal);

        // Try to get cost (alias for N)
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "cost");
        il.Emit(OpCodes.Ldloc, NLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, NLocal);

        // Try to get r from options
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "r");
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, rLocal);

        // Try to get blockSize (alias for r)
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "blockSize");
        il.Emit(OpCodes.Ldloc, rLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, rLocal);

        // Try to get p from options
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "p");
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, pLocal);

        // Try to get parallelization (alias for p)
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "parallelization");
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Call, getOptionMethod);
        il.Emit(OpCodes.Stloc, pLocal);

        il.MarkLabel(noOptionsLabel);

        // Call scrypt helper: ScryptDeriveBytes(password, salt, N, r, p, keylen)
        il.Emit(OpCodes.Ldarg_0);  // password
        il.Emit(OpCodes.Ldarg_1);  // salt
        il.Emit(OpCodes.Ldloc, NLocal);  // N
        il.Emit(OpCodes.Ldloc, rLocal);  // r
        il.Emit(OpCodes.Ldloc, pLocal);  // p
        il.Emit(OpCodes.Ldarg_2);  // keylen
        il.Emit(OpCodes.Call, runtime.ScryptDeriveBytes);

        // Return new $Buffer(derivedKey)
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a helper method to extract an int option from an object.
    /// Signature: int GetScryptOption(object options, string name, int defaultValue)
    /// Handles both $Object and Dictionary&lt;string, object&gt; types.
    /// </summary>
    private MethodBuilder EmitScryptGetOption(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetScryptOption",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Int32,
            [_types.Object, _types.String, _types.Int32]);

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var returnDefaultLabel = il.DefineLabel();
        var tryDictionaryLabel = il.DefineLabel();
        var checkValueLabel = il.DefineLabel();

        // Check if options is $Object
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, tryDictionaryLabel);

        // It's $Object - call GetProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, checkValueLabel);

        // Try Dictionary<string, object>
        il.MarkLabel(tryDictionaryLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        // It's Dictionary - call TryGetValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        // Check if value is double
        il.MarkLabel(checkValueLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        // returnDefault:
        il.MarkLabel(returnDefaultLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits the scrypt key derivation helper method.
    /// This is a simplified implementation that delegates to a static helper class.
    /// </summary>
    private void EmitScryptHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptDeriveBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte),
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.Int32, _types.Int32, _types.Int32, _types.Int32]);
        runtime.ScryptDeriveBytes = method;

        var il = method.GetILGenerator();

        // Call the static ScryptImpl.DeriveBytes method from our runtime
        il.Emit(OpCodes.Ldarg_0);  // password
        il.Emit(OpCodes.Ldarg_1);  // salt
        il.Emit(OpCodes.Ldarg_2);  // N
        il.Emit(OpCodes.Ldarg_3);  // r
        il.Emit(OpCodes.Ldarg, 4); // p
        il.Emit(OpCodes.Ldarg, 5); // dkLen
        il.Emit(OpCodes.Call, typeof(ScryptImpl).GetMethod("DeriveBytes",
            [typeof(byte[]), typeof(byte[]), typeof(int), typeof(int), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoTimingSafeEqual(byte[] a, byte[] b)
    /// Returns a boxed boolean indicating whether the buffers are equal using constant-time comparison.
    /// Throws if the buffers have different lengths (Node.js behavior).
    /// </summary>
    private void EmitCryptoTimingSafeEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoTimingSafeEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoTimingSafeEqual = method;

        var il = method.GetILGenerator();

        // Check if lengths are equal
        var lengthsMatchLabel = il.DefineLabel();
        var aLenLocal = il.DeclareLocal(_types.Int32);
        var bLenLocal = il.DeclareLocal(_types.Int32);

        // Get length of a
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, aLenLocal);

        // Get length of b
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bLenLocal);

        // if (a.Length == b.Length) goto lengthsMatch
        il.Emit(OpCodes.Ldloc, aLenLocal);
        il.Emit(OpCodes.Ldloc, bLenLocal);
        il.Emit(OpCodes.Beq, lengthsMatchLabel);

        // Throw exception with Node.js-style message
        // "Input buffers must have the same byte length. Received {aLen} and {bLen}"
        il.Emit(OpCodes.Ldstr, "crypto.timingSafeEqual: Input buffers must have the same byte length. Received ");
        il.Emit(OpCodes.Ldloca, aLenLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, " and ");
        il.Emit(OpCodes.Ldloca, bLenLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(lengthsMatchLabel);

        // Call our static helper method that handles the Span conversion
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(CryptoTimingSafeEqualHelper).GetMethod("Compare",
            [typeof(byte[]), typeof(byte[])])!);

        // Box the result and return
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateSign(string algorithm)
    /// </summary>
    private void EmitCryptoCreateSign(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateSign",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateSign = method;

        var il = method.GetILGenerator();

        // new $Sign(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSSignCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateVerify(string algorithm)
    /// </summary>
    private void EmitCryptoCreateVerify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateVerify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateVerify = method;

        var il = method.GetILGenerator();

        // new $Verify(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSVerifyCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetHashes()
    /// Returns an array of supported hash algorithm names.
    /// </summary>
    private void EmitCryptoGetHashes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetHashes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetHashes = method;

        var il = method.GetILGenerator();

        // Create List<object?> with hash names
        string[] hashes = ["md5", "sha1", "sha256", "sha384", "sha512"];

        // new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Add each hash name to the list
        foreach (var hash in hashes)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, hash);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        }

        // return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetCiphers()
    /// Returns an array of supported cipher algorithm names.
    /// </summary>
    private void EmitCryptoGetCiphers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetCiphers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetCiphers = method;

        var il = method.GetILGenerator();

        // Create List<object?> with cipher names
        string[] ciphers = ["aes-128-cbc", "aes-192-cbc", "aes-256-cbc", "aes-128-gcm", "aes-192-gcm", "aes-256-gcm"];

        // new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Add each cipher name to the list
        foreach (var cipher in ciphers)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, cipher);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        }

        // return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGenerateKeyPairSync(string type, object? options)
    /// Generates an RSA or EC key pair.
    /// </summary>
    private void EmitCryptoGenerateKeyPairSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGenerateKeyPairSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]);
        runtime.CryptoGenerateKeyPairSync = method;

        var il = method.GetILGenerator();

        // Call static helper that returns (publicKey, privateKey) tuple
        il.Emit(OpCodes.Ldarg_0);  // type
        il.Emit(OpCodes.Ldarg_1);  // options
        il.Emit(OpCodes.Call, typeof(CryptoKeyPairHelper).GetMethod("GenerateKeyPairRaw")!);

        // Store tuple in local
        var tupleLocal = il.DeclareLocal(typeof((string, string)));
        il.Emit(OpCodes.Stloc, tupleLocal);

        // Create Dictionary<string, object?> for $Object
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObject.GetConstructor(Type.EmptyTypes)!);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["publicKey"] = tuple.Item1
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "publicKey");
        il.Emit(OpCodes.Ldloca, tupleLocal);
        il.Emit(OpCodes.Ldfld, typeof((string, string)).GetField("Item1")!);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // dict["privateKey"] = tuple.Item2
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "privateKey");
        il.Emit(OpCodes.Ldloca, tupleLocal);
        il.Emit(OpCodes.Ldfld, typeof((string, string)).GetField("Item2")!);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // return new $Object(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateDiffieHellman(object primeOrLength, object? generator)
    /// Creates a DiffieHellman object.
    /// </summary>
    private void EmitCryptoCreateDiffieHellman(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateDiffieHellman",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.CryptoCreateDiffieHellman = method;

        var il = method.GetILGenerator();

        // Call static helper
        il.Emit(OpCodes.Ldarg_0);  // primeOrLength
        il.Emit(OpCodes.Ldarg_1);  // generator
        il.Emit(OpCodes.Call, typeof(CryptoDHHelper).GetMethod("CreateDiffieHellman")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetDiffieHellman(string groupName)
    /// Gets a predefined DiffieHellman group.
    /// </summary>
    private void EmitCryptoGetDiffieHellman(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetDiffieHellman",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoGetDiffieHellman = method;

        var il = method.GetILGenerator();

        // Call static helper
        il.Emit(OpCodes.Ldarg_0);  // groupName
        il.Emit(OpCodes.Call, typeof(CryptoDHHelper).GetMethod("GetDiffieHellman")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateECDH(string curveName)
    /// Creates an ECDH object.
    /// </summary>
    private void EmitCryptoCreateECDH(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateECDH",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateECDH = method;

        var il = method.GetILGenerator();

        // Call static helper
        il.Emit(OpCodes.Ldarg_0);  // curveName
        il.Emit(OpCodes.Call, typeof(CryptoECDHHelper).GetMethod("CreateECDH")!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper for timing-safe comparison.
/// Used by compiled code to avoid Span emission complexities.
/// </summary>
public static class CryptoTimingSafeEqualHelper
{
    /// <summary>
    /// Performs constant-time comparison of two byte arrays.
    /// </summary>
    public static bool Compare(byte[] a, byte[] b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// Static implementation of scrypt key derivation (RFC 7914).
/// Used by both interpreter and compiled code.
/// </summary>
public static class ScryptImpl
{
    /// <summary>
    /// Derives a key with options parsing (for compiled mode).
    /// </summary>
    public static byte[] DeriveWithOptions(byte[] password, byte[] salt, int dkLen, object? options)
    {
        // Default scrypt parameters (Node.js defaults)
        int N = 16384;  // cost parameter (must be power of 2)
        int r = 8;      // block size
        int p = 1;      // parallelization

        // Parse options if provided
        if (options != null)
        {
            N = GetOptionInt(options, "N", N);
            N = GetOptionInt(options, "cost", N);
            r = GetOptionInt(options, "r", r);
            r = GetOptionInt(options, "blockSize", r);
            p = GetOptionInt(options, "p", p);
            p = GetOptionInt(options, "parallelization", p);
        }

        // Validate N is a power of 2
        if (N < 2 || (N & (N - 1)) != 0)
            throw new ArgumentException("scryptSync: N must be a power of 2 greater than 1");

        return DeriveBytes(password, salt, N, r, p, dkLen);
    }

    /// <summary>
    /// Gets an integer option from an object (supports both SharpTSObject and $Object).
    /// </summary>
    private static int GetOptionInt(object options, string name, int defaultValue)
    {
        var type = options.GetType();

        // Try GetProperty method first (for $Object)
        var getPropertyMethod = type.GetMethod("GetProperty", [typeof(string)]);
        if (getPropertyMethod != null)
        {
            var value = getPropertyMethod.Invoke(options, [name]);
            if (value is double d)
                return (int)d;
            return defaultValue;
        }

        // Try Fields property (for SharpTSObject)
        var fieldsProperty = type.GetProperty("Fields");
        if (fieldsProperty != null)
        {
            var fields = fieldsProperty.GetValue(options) as System.Collections.Generic.IReadOnlyDictionary<string, object?>;
            if (fields != null && fields.TryGetValue(name, out var val) && val is double dVal)
                return (int)dVal;
        }

        return defaultValue;
    }

    /// <summary>
    /// Derives a key using the scrypt key derivation function.
    /// </summary>
    public static byte[] DeriveBytes(byte[] password, byte[] salt, int N, int r, int p, int dkLen)
    {
        // Validate parameters
        if (N < 2 || (N & (N - 1)) != 0)
            throw new ArgumentException("N must be a power of 2 greater than 1", nameof(N));
        if (r < 1)
            throw new ArgumentException("r must be at least 1", nameof(r));
        if (p < 1)
            throw new ArgumentException("p must be at least 1", nameof(p));

        // Step 1: Generate initial data B using PBKDF2-HMAC-SHA256
        int blockSize = 128 * r;
        byte[] B = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * blockSize);

        // Step 2: Apply scryptROMix to each block
        for (int i = 0; i < p; i++)
        {
            byte[] block = new byte[blockSize];
            Array.Copy(B, i * blockSize, block, 0, blockSize);
            ScryptROMix(block, N, r);
            Array.Copy(block, 0, B, i * blockSize, blockSize);
        }

        // Step 3: Derive final key using PBKDF2-HMAC-SHA256
        return Rfc2898DeriveBytes.Pbkdf2(password, B, 1, HashAlgorithmName.SHA256, dkLen);
    }

    private static void ScryptROMix(byte[] B, int N, int r)
    {
        int blockSize = 128 * r;
        byte[][] V = new byte[N][];

        // Step 1: Store intermediate values in V
        for (int i = 0; i < N; i++)
        {
            V[i] = (byte[])B.Clone();
            ScryptBlockMix(B, r);
        }

        // Step 2: Mix with random lookups
        for (int i = 0; i < N; i++)
        {
            // Get last 64 bits as little-endian integer mod N
            long j = BitConverter.ToInt64(B, blockSize - 64) & (N - 1);
            if (j < 0) j += N;

            // XOR B with V[j]
            for (int k = 0; k < blockSize; k++)
                B[k] ^= V[j][k];

            ScryptBlockMix(B, r);
        }
    }

    private static void ScryptBlockMix(byte[] B, int r)
    {
        int blockSize = 128 * r;
        byte[] X = new byte[64];
        byte[] Y = new byte[blockSize];

        // Copy last 64-byte block to X
        Array.Copy(B, blockSize - 64, X, 0, 64);

        // Process 2r blocks
        for (int i = 0; i < 2 * r; i++)
        {
            // XOR X with current block
            for (int j = 0; j < 64; j++)
                X[j] ^= B[i * 64 + j];

            // Apply Salsa20/8 core
            Salsa20Core(X);

            // Copy to Y (even blocks first, then odd blocks)
            int destOffset = (i / 2) * 64 + (i % 2) * r * 64;
            Array.Copy(X, 0, Y, destOffset, 64);
        }

        Array.Copy(Y, 0, B, 0, blockSize);
    }

    private static void Salsa20Core(byte[] block)
    {
        // Convert bytes to uint32 array (little-endian)
        uint[] x = new uint[16];
        for (int i = 0; i < 16; i++)
            x[i] = BitConverter.ToUInt32(block, i * 4);

        uint[] original = (uint[])x.Clone();

        // 8 rounds (4 double-rounds)
        for (int i = 0; i < 4; i++)
        {
            // Column round
            x[4] ^= RotateLeft(x[0] + x[12], 7);
            x[8] ^= RotateLeft(x[4] + x[0], 9);
            x[12] ^= RotateLeft(x[8] + x[4], 13);
            x[0] ^= RotateLeft(x[12] + x[8], 18);

            x[9] ^= RotateLeft(x[5] + x[1], 7);
            x[13] ^= RotateLeft(x[9] + x[5], 9);
            x[1] ^= RotateLeft(x[13] + x[9], 13);
            x[5] ^= RotateLeft(x[1] + x[13], 18);

            x[14] ^= RotateLeft(x[10] + x[6], 7);
            x[2] ^= RotateLeft(x[14] + x[10], 9);
            x[6] ^= RotateLeft(x[2] + x[14], 13);
            x[10] ^= RotateLeft(x[6] + x[2], 18);

            x[3] ^= RotateLeft(x[15] + x[11], 7);
            x[7] ^= RotateLeft(x[3] + x[15], 9);
            x[11] ^= RotateLeft(x[7] + x[3], 13);
            x[15] ^= RotateLeft(x[11] + x[7], 18);

            // Row round
            x[1] ^= RotateLeft(x[0] + x[3], 7);
            x[2] ^= RotateLeft(x[1] + x[0], 9);
            x[3] ^= RotateLeft(x[2] + x[1], 13);
            x[0] ^= RotateLeft(x[3] + x[2], 18);

            x[6] ^= RotateLeft(x[5] + x[4], 7);
            x[7] ^= RotateLeft(x[6] + x[5], 9);
            x[4] ^= RotateLeft(x[7] + x[6], 13);
            x[5] ^= RotateLeft(x[4] + x[7], 18);

            x[11] ^= RotateLeft(x[10] + x[9], 7);
            x[8] ^= RotateLeft(x[11] + x[10], 9);
            x[9] ^= RotateLeft(x[8] + x[11], 13);
            x[10] ^= RotateLeft(x[9] + x[8], 18);

            x[12] ^= RotateLeft(x[15] + x[14], 7);
            x[13] ^= RotateLeft(x[12] + x[15], 9);
            x[14] ^= RotateLeft(x[13] + x[12], 13);
            x[15] ^= RotateLeft(x[14] + x[13], 18);
        }

        // Add original to result
        for (int i = 0; i < 16; i++)
            x[i] += original[i];

        // Convert back to bytes
        for (int i = 0; i < 16; i++)
        {
            byte[] bytes = BitConverter.GetBytes(x[i]);
            Array.Copy(bytes, 0, block, i * 4, 4);
        }
    }

    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }
}

/// <summary>
/// Static helper for getHashes() and getCiphers().
/// Used by compiled code.
/// </summary>
public static class CryptoInfoHelper
{
    private static readonly string[] _hashes = ["md5", "sha1", "sha256", "sha384", "sha512"];
    private static readonly string[] _ciphers = ["aes-128-cbc", "aes-192-cbc", "aes-256-cbc", "aes-128-gcm", "aes-192-gcm", "aes-256-gcm"];

    public static object GetHashes()
    {
        return new SharpTS.Runtime.Types.SharpTSArray(new List<object?>(_hashes));
    }

    public static object GetCiphers()
    {
        return new SharpTS.Runtime.Types.SharpTSArray(new List<object?>(_ciphers));
    }
}

/// <summary>
/// Static helper for generateKeyPairSync().
/// Used by compiled code.
/// </summary>
public static class CryptoKeyPairHelper
{
    public static object GenerateKeyPairSync(string type, object? options)
    {
        return type.ToLowerInvariant() switch
        {
            "rsa" => GenerateRsaKeyPair(options),
            "ec" => GenerateEcKeyPair(options),
            _ => throw new ArgumentException($"crypto.generateKeyPairSync: unsupported key type '{type}'")
        };
    }

    /// <summary>
    /// Returns raw (publicKey, privateKey) tuple for compiled mode to wrap in $Object.
    /// </summary>
    public static (string publicKey, string privateKey) GenerateKeyPairRaw(string type, object? options)
    {
        return type.ToLowerInvariant() switch
        {
            "rsa" => GenerateRsaKeyPairRaw(options),
            "ec" => GenerateEcKeyPairRaw(options),
            _ => throw new ArgumentException($"crypto.generateKeyPairSync: unsupported key type '{type}'")
        };
    }

    private static object GenerateRsaKeyPair(object? options)
    {
        var (publicKey, privateKey) = GenerateRsaKeyPairRaw(options);
        return new SharpTS.Runtime.Types.SharpTSObject(new Dictionary<string, object?>
        {
            ["publicKey"] = publicKey,
            ["privateKey"] = privateKey
        });
    }

    private static (string publicKey, string privateKey) GenerateRsaKeyPairRaw(object? options)
    {
        int modulusLength = 2048;
        if (options != null)
        {
            modulusLength = GetOptionInt(options, "modulusLength", modulusLength);
        }

        using var rsa = RSA.Create(modulusLength);
        return (rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    private static object GenerateEcKeyPair(object? options)
    {
        var (publicKey, privateKey) = GenerateEcKeyPairRaw(options);
        return new SharpTS.Runtime.Types.SharpTSObject(new Dictionary<string, object?>
        {
            ["publicKey"] = publicKey,
            ["privateKey"] = privateKey
        });
    }

    private static (string publicKey, string privateKey) GenerateEcKeyPairRaw(object? options)
    {
        var curveName = "prime256v1";
        if (options != null)
        {
            curveName = GetOptionString(options, "namedCurve", curveName);
        }

        var curve = curveName.ToLowerInvariant() switch
        {
            "prime256v1" or "secp256r1" or "p-256" => ECCurve.NamedCurves.nistP256,
            "secp384r1" or "p-384" => ECCurve.NamedCurves.nistP384,
            "secp521r1" or "p-521" => ECCurve.NamedCurves.nistP521,
            _ => throw new ArgumentException($"crypto.generateKeyPairSync: unsupported curve '{curveName}'")
        };

        using var ecdsa = ECDsa.Create(curve);
        return (ecdsa.ExportSubjectPublicKeyInfoPem(), ecdsa.ExportPkcs8PrivateKeyPem());
    }

    private static int GetOptionInt(object options, string name, int defaultValue)
    {
        var type = options.GetType();
        var getPropertyMethod = type.GetMethod("GetProperty", [typeof(string)]);
        if (getPropertyMethod != null)
        {
            var value = getPropertyMethod.Invoke(options, [name]);
            if (value is double d) return (int)d;
            return defaultValue;
        }

        var fieldsProperty = type.GetProperty("Fields");
        if (fieldsProperty != null)
        {
            var fields = fieldsProperty.GetValue(options) as IReadOnlyDictionary<string, object?>;
            if (fields != null && fields.TryGetValue(name, out var val) && val is double dVal)
                return (int)dVal;
        }

        return defaultValue;
    }

    private static string GetOptionString(object options, string name, string defaultValue)
    {
        var type = options.GetType();
        var getPropertyMethod = type.GetMethod("GetProperty", [typeof(string)]);
        if (getPropertyMethod != null)
        {
            var value = getPropertyMethod.Invoke(options, [name]);
            if (value is string s) return s;
            return defaultValue;
        }

        var fieldsProperty = type.GetProperty("Fields");
        if (fieldsProperty != null)
        {
            var fields = fieldsProperty.GetValue(options) as IReadOnlyDictionary<string, object?>;
            if (fields != null && fields.TryGetValue(name, out var val) && val is string sVal)
                return sVal;
        }

        return defaultValue;
    }
}

/// <summary>
/// Static helper for createDiffieHellman() and getDiffieHellman().
/// Used by compiled code.
/// </summary>
public static class CryptoDHHelper
{
    public static object CreateDiffieHellman(object primeOrLength, object? generator)
    {
        if (primeOrLength is double d)
        {
            return new SharpTS.Runtime.Types.SharpTSDiffieHellman((int)d);
        }

        var prime = ConvertToBytes(primeOrLength);
        byte[]? gen = generator != null ? ConvertToBytes(generator) : null;
        return new SharpTS.Runtime.Types.SharpTSDiffieHellman(prime, gen);
    }

    public static object GetDiffieHellman(string groupName)
    {
        return new SharpTS.Runtime.Types.SharpTSDiffieHellman(groupName, isGroup: true);
    }

    private static byte[] ConvertToBytes(object value)
    {
        if (value is SharpTS.Runtime.Types.SharpTSBuffer buffer)
            return buffer.Data;
        if (value is byte[] bytes)
            return bytes;
        if (value is string str)
            return System.Text.Encoding.UTF8.GetBytes(str);
        throw new ArgumentException("Value must be a Buffer, byte array, or string");
    }
}

/// <summary>
/// Static helper for createECDH().
/// Used by compiled code.
/// </summary>
public static class CryptoECDHHelper
{
    public static object CreateECDH(string curveName)
    {
        return new SharpTS.Runtime.Types.SharpTSECDH(curveName);
    }
}
