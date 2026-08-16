using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static string ExtractKeyPem(object key)
    /// Extracts PEM string from various key input types (string, object with GetProperty, etc.)
    /// Used by RSA operations for standalone DLLs without SharpTS.dll dependency.
    /// </summary>
    private void EmitExtractKeyPem(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExtractKeyPem",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.ExtractKeyPem = method;

        var il = method.GetILGenerator();
        var keyLocal = il.DeclareLocal(_types.Object);
        var stringResultLabel = il.DefineLabel();
        var tryGetPropertyLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Store key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, keyLocal);

        // if (key is string) return (string)key
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, stringResultLabel);
        il.Emit(OpCodes.Pop);

        // Try to get "key" property from object
        // First try compiled $Object with GetProperty method
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // Standalone-only: require emitted $Object for object key extraction.
        var notTsObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTsObjectLabel);

        // var keyValue = (($Object)key).GetProperty("key");
        var keyValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, keyValueLocal);

        // if (keyValue is string keyStr) return keyStr;
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, returnLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(notTsObjectLabel);

        // throw new ArgumentException(...)
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Key must be a PEM string or object with 'key' property");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // return string result
        il.MarkLabel(stringResultLabel);
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] RsaEncryptRaw(string pem, byte[] data, bool useOaep)
    /// Encrypts data using RSA. If useOaep is true, uses OAEP-SHA1; otherwise PKCS#1 v1.5.
    /// </summary>
    private void EmitRsaEncryptRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RsaEncryptRaw",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte),
            [_types.String, _types.MakeArrayType(_types.Byte), _types.Boolean]);
        runtime.RsaEncryptRaw = method;

        var il = method.GetILGenerator();

        // using var rsa = RSA.Create();
        var rsaLocal = il.DeclareLocal(_types.RSA);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RSA, "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);

        // try { ... } finally { rsa?.Dispose(); }
        il.BeginExceptionBlock();

        // rsa.ImportFromPem(pem);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_0);  // pem
        // ImportFromPem is an extension method, need to use the actual method
        // It's on AsymmetricAlgorithm: public void ImportFromPem(ReadOnlySpan<char> input)
        // For simplicity, convert string to char span
        var importFromPemMethod = typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!;
        // Convert string to ReadOnlySpan<char>
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, importFromPemMethod);

        // var padding = useOaep ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.Pkcs1;
        var paddingLocal = il.DeclareLocal(_types.RSAEncryptionPadding);
        var usePkcs1Label = il.DefineLabel();
        var paddingDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);  // useOaep
        il.Emit(OpCodes.Brfalse, usePkcs1Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.RSAEncryptionPadding, "OaepSHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, paddingDoneLabel);
        il.MarkLabel(usePkcs1Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.RSAEncryptionPadding, "Pkcs1")!.GetGetMethod()!);
        il.MarkLabel(paddingDoneLabel);
        il.Emit(OpCodes.Stloc, paddingLocal);

        // return rsa.Encrypt(data, padding);
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_1);  // data
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.RSA, "Encrypt", [typeof(byte[]), typeof(RSAEncryptionPadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // finally block
        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.MarkLabel(skipDisposeLabel);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] RsaDecryptRaw(string pem, byte[] data, bool useOaep)
    /// Decrypts data using RSA. If useOaep is true, uses OAEP-SHA1; otherwise PKCS#1 v1.5.
    /// </summary>
    private void EmitRsaDecryptRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RsaDecryptRaw",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MakeArrayType(_types.Byte),
            [_types.String, _types.MakeArrayType(_types.Byte), _types.Boolean]);
        runtime.RsaDecryptRaw = method;

        var il = method.GetILGenerator();

        // using var rsa = RSA.Create();
        var rsaLocal = il.DeclareLocal(_types.RSA);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RSA, "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);

        // try { ... } finally { rsa?.Dispose(); }
        il.BeginExceptionBlock();

        // rsa.ImportFromPem(pem);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_0);  // pem
        var importFromPemMethod = typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!;
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        il.Emit(OpCodes.Callvirt, importFromPemMethod);

        // var padding = useOaep ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.Pkcs1;
        var paddingLocal = il.DeclareLocal(_types.RSAEncryptionPadding);
        var usePkcs1Label = il.DefineLabel();
        var paddingDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);  // useOaep
        il.Emit(OpCodes.Brfalse, usePkcs1Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.RSAEncryptionPadding, "OaepSHA1")!.GetGetMethod()!);
        il.Emit(OpCodes.Br, paddingDoneLabel);
        il.MarkLabel(usePkcs1Label);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.RSAEncryptionPadding, "Pkcs1")!.GetGetMethod()!);
        il.MarkLabel(paddingDoneLabel);
        il.Emit(OpCodes.Stloc, paddingLocal);

        // return rsa.Decrypt(data, padding);
        var resultLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_1);  // data
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.RSA, "Decrypt", [typeof(byte[]), typeof(RSAEncryptionPadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // finally block
        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.MarkLabel(skipDisposeLabel);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoPublicEncrypt(object key, byte[] buffer)
    /// Encrypts data using RSA-OAEP with SHA-1.
    /// Uses pure IL - no SharpTS.dll dependency.
    /// </summary>
    private void EmitCryptoPublicEncrypt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoPublicEncrypt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoPublicEncrypt = method;

        var il = method.GetILGenerator();

        // var pem = ExtractKeyPem(key);
        il.Emit(OpCodes.Ldarg_0);  // key
        il.Emit(OpCodes.Call, runtime.ExtractKeyPem);

        // var result = RsaEncryptRaw(pem, buffer, true);  // true = use OAEP
        il.Emit(OpCodes.Ldarg_1);  // buffer
        il.Emit(OpCodes.Ldc_I4_1);  // useOaep = true
        il.Emit(OpCodes.Call, runtime.RsaEncryptRaw);

        // Wrap result in $Buffer
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoPrivateDecrypt(object key, byte[] buffer)
    /// Decrypts data using RSA-OAEP with SHA-1.
    /// Uses pure IL - no SharpTS.dll dependency.
    /// </summary>
    private void EmitCryptoPrivateDecrypt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoPrivateDecrypt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoPrivateDecrypt = method;

        var il = method.GetILGenerator();

        // var pem = ExtractKeyPem(key);
        il.Emit(OpCodes.Ldarg_0);  // key
        il.Emit(OpCodes.Call, runtime.ExtractKeyPem);

        // var result = RsaDecryptRaw(pem, buffer, true);  // true = use OAEP
        il.Emit(OpCodes.Ldarg_1);  // buffer
        il.Emit(OpCodes.Ldc_I4_1);  // useOaep = true
        il.Emit(OpCodes.Call, runtime.RsaDecryptRaw);

        // Wrap result in $Buffer
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoPrivateEncrypt(object key, byte[] buffer)
    /// Encrypts data using private key (PKCS#1 v1.5 signing primitive).
    /// Note: privateEncrypt in Node.js actually uses Decrypt with PKCS#1 padding.
    /// Uses pure IL - no SharpTS.dll dependency.
    /// </summary>
    private void EmitCryptoPrivateEncrypt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoPrivateEncrypt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoPrivateEncrypt = method;

        var il = method.GetILGenerator();

        // var pem = ExtractKeyPem(key);
        il.Emit(OpCodes.Ldarg_0);  // key
        il.Emit(OpCodes.Call, runtime.ExtractKeyPem);

        // var result = RsaDecryptRaw(pem, buffer, false);  // false = use PKCS#1
        // Note: privateEncrypt uses RSA Decrypt with PKCS#1 padding
        il.Emit(OpCodes.Ldarg_1);  // buffer
        il.Emit(OpCodes.Ldc_I4_0);  // useOaep = false (PKCS#1)
        il.Emit(OpCodes.Call, runtime.RsaDecryptRaw);

        // Wrap result in $Buffer
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoPublicDecrypt(object key, byte[] buffer)
    /// Decrypts data using public key (PKCS#1 v1.5 verification primitive).
    /// Note: publicDecrypt in Node.js actually uses Encrypt with PKCS#1 padding.
    /// Uses pure IL - no SharpTS.dll dependency.
    /// </summary>
    private void EmitCryptoPublicDecrypt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoPublicDecrypt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoPublicDecrypt = method;

        var il = method.GetILGenerator();

        // var pem = ExtractKeyPem(key);
        il.Emit(OpCodes.Ldarg_0);  // key
        il.Emit(OpCodes.Call, runtime.ExtractKeyPem);

        // var result = RsaEncryptRaw(pem, buffer, false);  // false = use PKCS#1
        // Note: publicDecrypt uses RSA Encrypt with PKCS#1 padding
        il.Emit(OpCodes.Ldarg_1);  // buffer
        il.Emit(OpCodes.Ldc_I4_0);  // useOaep = false (PKCS#1)
        il.Emit(OpCodes.Call, runtime.RsaEncryptRaw);

        // Wrap result in $Buffer
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }
}
