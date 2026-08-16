using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static string CryptoKeyToPem(object key, bool isPrivate)
    /// Resolves a compiled key argument (PEM string / $Buffer PEM / $Object with a
    /// 'key' field) to a PEM string for the one-shot sign/verify helpers (#1055).
    /// $TSKeyObject keys are handled by the KeyObject completeness child (#1059).
    /// </summary>
    private void EmitCryptoKeyToPem(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoKeyToPem",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Boolean]);
        runtime.CryptoKeyToPem = method;

        var il = method.GetILGenerator();
        var notStringLabel = il.DefineLabel();
        var notBufferLabel = il.DefineLabel();
        var notObjLabel = il.DefineLabel();

        // string → itself
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        // $Buffer → UTF8 string of its data
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, notObjLabel);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetString", [typeof(byte[])])!);
        il.Emit(OpCodes.Ret);

        // $Object with a "key" field → recurse on that field
        il.MarkLabel(notObjLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method); // recurse
        il.Emit(OpCodes.Ret);

        // otherwise: throw
        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Ldstr, "crypto: key must be a PEM string, Buffer, KeyObject, or object with a 'key' property");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static object CryptoSignOneShot(string algorithm, object data, object key, string encoding)
    /// One-shot crypto.sign wrapping SignDataBytes with KeyObject/PEM key resolution (#1055).
    /// </summary>
    private void EmitCryptoSignOneShot(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoSignOneShot",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]);
        runtime.CryptoSignDataEx = method;

        var il = method.GetILGenerator();

        // SignDataBytes(CryptoKeyToPem(key, true), CryptoBytesFromAny(data), CryptoSignHashName(algorithm))
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.CryptoKeyToPem);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.CryptoBytesFromAny);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.CryptoSignHashName);
        il.Emit(OpCodes.Call, runtime.SignDataBytes);
        // → $Buffer
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoVerifyOneShot(string algorithm, object data, object key, object signature)
    /// One-shot crypto.verify → boxed bool (#1055).
    /// </summary>
    private void EmitCryptoVerifyOneShot(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoVerifyOneShot",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object, _types.Object]);
        runtime.CryptoVerifyDataEx = method;

        var il = method.GetILGenerator();

        // VerifyDataBytes(CryptoKeyToPem(key, false), CryptoBytesFromAny(data), CryptoSignHashName(algorithm), CryptoBytesFromAny(signature))
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.CryptoKeyToPem);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.CryptoBytesFromAny);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.CryptoSignHashName);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.CryptoBytesFromAny);
        il.Emit(OpCodes.Call, runtime.VerifyDataBytes);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoHashOneShot(string algorithm, object data, string encoding)
    /// One-shot crypto.hash — default encoding 'hex' unless 'buffer' (#1055).
    /// </summary>
    private void EmitCryptoHashOneShot(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoHashOneShot",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.String]);
        runtime.CryptoHashOneShot = method;

        var il = method.GetILGenerator();
        var digestLocal = il.DeclareLocal(_types.ByteArray);
        var encLocal = il.DeclareLocal(_types.String);

        // digest = CryptoHashData(algorithm, CryptoBytesFromAny(data), -1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.CryptoBytesFromAny);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Call, runtime.CryptoHashData);
        il.Emit(OpCodes.Stloc, digestLocal);

        // enc = encoding ?? "hex"
        var haveEncLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveEncLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "hex");
        il.MarkLabel(haveEncLabel);
        il.Emit(OpCodes.Stloc, encLocal);

        // if (enc == "buffer") return new $Buffer(digest)
        var notBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, encLocal);
        il.Emit(OpCodes.Ldstr, "buffer");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, notBufferLabel);
        il.Emit(OpCodes.Ldloc, digestLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Ldloc, digestLocal);
        il.Emit(OpCodes.Ldloc, encLocal);
        il.Emit(OpCodes.Call, runtime.CryptoEncodeBytes);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] SignDataBytes(string privateKeyPem, byte[] data, HashAlgorithmName hashAlgorithm)
    /// Signs data using RSA or EC private key. Uses try/catch to detect key type.
    /// </summary>
    private void EmitSignDataBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SignDataBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.String, _types.ByteArray, _types.HashAlgorithmName]);
        runtime.SignDataBytes = method;

        var il = method.GetILGenerator();

        // Result local used for both EC and RSA paths
        var resultLocal = il.DeclareLocal(_types.ByteArray);
        var exitLabel = il.DefineLabel();
        var rsaSignLabel = il.DefineLabel();

        // Check for explicit RSA header
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "RSA PRIVATE KEY");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        il.Emit(OpCodes.Brtrue, rsaSignLabel);

        // Generic format or EC - try EC first with try/catch
        var ecdsaLocal = il.DeclareLocal(typeof(ECDsa));

        // try { ECDsa sign }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecdsaLocal);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        // result = ecdsa.SignData(data, hashAlgorithm)
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("SignData", [typeof(byte[]), typeof(HashAlgorithmName)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // Dispose ecdsa
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Leave, exitLabel);

        // catch (CryptographicException) { fall back to RSA }
        il.BeginCatchBlock(typeof(CryptographicException));
        il.Emit(OpCodes.Pop);
        // Dispose the failed ECDsa if it was created
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        var ecdsaNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, ecdsaNullLabel);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.MarkLabel(ecdsaNullLabel);
        il.Emit(OpCodes.Leave, rsaSignLabel);
        il.EndExceptionBlock();

        // RSA signing path
        il.MarkLabel(rsaSignLabel);
        var rsaLocal = il.DeclareLocal(typeof(RSA));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        // result = rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1)
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(RSASignaturePadding).GetProperty("Pkcs1")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("SignData", [typeof(byte[]), typeof(HashAlgorithmName), typeof(RSASignaturePadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // Dispose rsa
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // Exit: return result
        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool VerifyDataBytes(string publicKeyPem, byte[] data, HashAlgorithmName hashAlgorithm, byte[] signature)
    /// Verifies a signature using RSA or EC public key. Uses try/catch to detect key type.
    /// </summary>
    private void EmitVerifyDataBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "VerifyDataBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.ByteArray, _types.HashAlgorithmName, _types.ByteArray]);
        runtime.VerifyDataBytes = method;

        var il = method.GetILGenerator();

        // Result local used for both EC and RSA paths
        var resultLocal = il.DeclareLocal(_types.Boolean);
        var exitLabel = il.DefineLabel();
        var rsaVerifyLabel = il.DefineLabel();

        // Check for explicit RSA header
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "RSA PUBLIC KEY");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        il.Emit(OpCodes.Brtrue, rsaVerifyLabel);

        // Generic format or EC - try EC first with try/catch
        var ecdsaLocal = il.DeclareLocal(typeof(ECDsa));

        // try { ECDsa verify }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecdsaLocal);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        // result = ecdsa.VerifyData(data, signature, hashAlgorithm)
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Ldarg_1);  // data
        il.Emit(OpCodes.Ldarg_3);  // signature
        il.Emit(OpCodes.Ldarg_2);  // hashAlgorithm
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // Dispose ecdsa
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Leave, exitLabel);

        // catch (CryptographicException) { fall back to RSA }
        il.BeginCatchBlock(typeof(CryptographicException));
        il.Emit(OpCodes.Pop);
        // Dispose the failed ECDsa if it was created
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        var ecdsaNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, ecdsaNullLabel);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.MarkLabel(ecdsaNullLabel);
        il.Emit(OpCodes.Leave, rsaVerifyLabel);
        il.EndExceptionBlock();

        // RSA verification path
        il.MarkLabel(rsaVerifyLabel);
        var rsaLocal = il.DeclareLocal(typeof(RSA));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        // result = rsa.VerifyData(data, signature, hashAlgorithm, RSASignaturePadding.Pkcs1)
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_1);  // data
        il.Emit(OpCodes.Ldarg_3);  // signature
        il.Emit(OpCodes.Ldarg_2);  // hashAlgorithm
        il.Emit(OpCodes.Call, typeof(RSASignaturePadding).GetProperty("Pkcs1")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName), typeof(RSASignaturePadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // Dispose rsa
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // Exit: return result
        il.MarkLabel(exitLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
