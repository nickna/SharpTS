using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
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
        il.Emit(OpCodes.Callvirt, _types.StringToLowerInvariant);
        var digestLower = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, digestLower);

        // Check for sha256 (most common)
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha256");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sha256Label);

        // Check for sha1
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sha1Label);

        // Check for sha384
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha384");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sha384Label);

        // Check for sha512
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "sha512");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sha512Label);

        // Check for md5
        il.Emit(OpCodes.Ldloc, digestLower);
        il.Emit(OpCodes.Ldstr, "md5");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
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
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
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

        // Note: ScryptDeriveBytes is already emitted by EmitScryptMethods (called at start of EmitCryptoMethods)

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

    // Note: EmitScryptHelper removed - scrypt is now emitted by EmitScryptMethods in RuntimeEmitter.Scrypt.cs

    /// <summary>
    /// Emits: public static object CryptoHkdfSync(string digest, byte[] ikm, byte[] salt, byte[] info, int keylen)
    /// HKDF key derivation (RFC 5869).
    /// Pure IL emission - no SharpTS.dll dependency.
    /// </summary>
    private void EmitCryptoHkdfSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoHkdfSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte),
             _types.MakeArrayType(_types.Byte), _types.Int32]);
        runtime.CryptoHkdfSync = method;

        var il = method.GetILGenerator();

        var hashAlgLocal = il.DeclareLocal(_types.HashAlgorithmName);
        var lowerDigestLocal = il.DeclareLocal(_types.String);

        // Labels for hash algorithm selection
        var sha1Label = il.DefineLabel();
        var sha256Label = il.DefineLabel();
        var sha384Label = il.DefineLabel();
        var sha512Label = il.DefineLabel();
        var unsupportedLabel = il.DefineLabel();
        var deriveKeyLabel = il.DefineLabel();
        var keylenNegativeLabel = il.DefineLabel();
        var keylenZeroLabel = il.DefineLabel();
        var afterKeylenCheckLabel = il.DefineLabel();

        // if (keylen < 0) throw ArgumentException
        il.Emit(OpCodes.Ldarg, 4);  // keylen
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, keylenNegativeLabel);

        // if (keylen == 0) return new $Buffer(Array.Empty<byte>())
        il.Emit(OpCodes.Ldarg, 4);  // keylen
        il.Emit(OpCodes.Brfalse, keylenZeroLabel);
        il.Emit(OpCodes.Br, afterKeylenCheckLabel);

        il.MarkLabel(keylenNegativeLabel);
        il.Emit(OpCodes.Ldstr, "crypto.hkdfSync: keylen must be non-negative");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(keylenZeroLabel);
        // Return new $Buffer(Array.Empty<byte>())
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Array).GetMethod("Empty")!, _types.Byte));
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(afterKeylenCheckLabel);

        // lowerDigest = digest.ToLowerInvariant()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerDigestLocal);

        // Switch on digest name
        // if (lowerDigest == "sha1") goto sha1Label
        il.Emit(OpCodes.Ldloc, lowerDigestLocal);
        il.Emit(OpCodes.Ldstr, "sha1");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, sha1Label);

        // if (lowerDigest == "sha256") goto sha256Label
        il.Emit(OpCodes.Ldloc, lowerDigestLocal);
        il.Emit(OpCodes.Ldstr, "sha256");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, sha256Label);

        // if (lowerDigest == "sha384") goto sha384Label
        il.Emit(OpCodes.Ldloc, lowerDigestLocal);
        il.Emit(OpCodes.Ldstr, "sha384");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, sha384Label);

        // if (lowerDigest == "sha512") goto sha512Label
        il.Emit(OpCodes.Ldloc, lowerDigestLocal);
        il.Emit(OpCodes.Ldstr, "sha512");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, sha512Label);

        // else goto unsupported
        il.Emit(OpCodes.Br, unsupportedLabel);

        // sha1: hashAlg = HashAlgorithmName.SHA1
        il.MarkLabel(sha1Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashAlgLocal);
        il.Emit(OpCodes.Br, deriveKeyLabel);

        // sha256: hashAlg = HashAlgorithmName.SHA256
        il.MarkLabel(sha256Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashAlgLocal);
        il.Emit(OpCodes.Br, deriveKeyLabel);

        // sha384: hashAlg = HashAlgorithmName.SHA384
        il.MarkLabel(sha384Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashAlgLocal);
        il.Emit(OpCodes.Br, deriveKeyLabel);

        // sha512: hashAlg = HashAlgorithmName.SHA512
        il.MarkLabel(sha512Label);
        il.Emit(OpCodes.Call, _types.HashAlgorithmName.GetProperty("SHA512")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, hashAlgLocal);
        il.Emit(OpCodes.Br, deriveKeyLabel);

        // unsupported: throw ArgumentException
        il.MarkLabel(unsupportedLabel);
        il.Emit(OpCodes.Ldstr, "crypto.hkdfSync: unsupported digest algorithm '");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "'. Supported: sha1, sha256, sha384, sha512");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // deriveKey: return new $Buffer(HKDF.DeriveKey(hashAlg, ikm, keylen, salt, info))
        il.MarkLabel(deriveKeyLabel);
        il.Emit(OpCodes.Ldloc, hashAlgLocal);   // hashAlgorithm
        il.Emit(OpCodes.Ldarg_1);               // ikm
        il.Emit(OpCodes.Ldarg, 4);              // keylen (outputLength)
        il.Emit(OpCodes.Ldarg_2);               // salt
        il.Emit(OpCodes.Ldarg_3);               // info
        il.Emit(OpCodes.Call, _types.HKDF.GetMethod("DeriveKey",
            [_types.HashAlgorithmName, typeof(byte[]), typeof(int), typeof(byte[]), typeof(byte[])])!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }
}
