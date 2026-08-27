using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static object CryptoCreateSecretKey(object key, object? encoding)
    /// Creates a secret (symmetric) KeyObject using pure IL (no SharpTS.dll dependency).
    /// </summary>
    private void EmitCryptoCreateSecretKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateSecretKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.CryptoCreateSecretKey = method;

        var il = method.GetILGenerator();

        var keyBytesLocal = il.DeclareLocal(_types.ByteArray);
        var encodingLocal = il.DeclareLocal(_types.String);

        // Check if key is string
        var notStringLabel = il.DefineLabel();
        var createKeyObjectLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // key is string - get encoding
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Dup);
        var useDefaultEncodingLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, useDefaultEncodingLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        var encodingSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, encodingSetLabel);
        il.MarkLabel(useDefaultEncodingLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.MarkLabel(encodingSetLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Convert string to bytes based on encoding
        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var latin1Label = il.DefineLabel();
        var stringConvertedLabel = il.DefineLabel();

        // Check for "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check for "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Check for "latin1" or "binary"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "latin1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "binary");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brtrue, latin1Label);

        // Check for "utf8" or "utf-8"
        var utf8Label = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "utf-8");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Brtrue, utf8Label);

        // Unknown encoding - throw
        il.Emit(OpCodes.Ldstr, "crypto.createSecretKey: unsupported encoding '");
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // UTF8
        il.MarkLabel(utf8Label);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, keyBytesLocal);
        il.Emit(OpCodes.Br, stringConvertedLabel);

        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Stloc, keyBytesLocal);
        il.Emit(OpCodes.Br, stringConvertedLabel);

        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Stloc, keyBytesLocal);
        il.Emit(OpCodes.Br, stringConvertedLabel);

        il.MarkLabel(latin1Label);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("Latin1")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, keyBytesLocal);

        il.MarkLabel(stringConvertedLabel);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        // Not a string - try to get bytes from Buffer or byte[]
        il.MarkLabel(notStringLabel);

        // Try byte[] first
        var tryBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, tryBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Stloc, keyBytesLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        // Try $Buffer - call GetData() method
        il.MarkLabel(tryBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        var trySharpTSBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, trySharpTSBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, keyBytesLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        // Standalone-only behavior: no interpreter SharpTSBuffer reflection fallback.
        il.MarkLabel(trySharpTSBufferLabel);
        il.Emit(OpCodes.Ldstr, "crypto.createSecretKey: key must be a Buffer or string");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create and return new $TSKeyObject(keyBytes)
        il.MarkLabel(createKeyObjectLabel);
        il.Emit(OpCodes.Ldloc, keyBytesLocal);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorSecret);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreatePublicKey(object key)
    /// Creates a public KeyObject from PEM using pure IL (no SharpTS.dll dependency).
    /// </summary>
    private void EmitCryptoCreatePublicKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreatePublicKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.CryptoCreatePublicKey = method;

        var il = method.GetILGenerator();

        var pemLocal = il.DeclareLocal(_types.String);
        var keyValueLocal = il.DeclareLocal(_types.Object);
        var formatLocal = il.DeclareLocal(_types.String);
        var typeLocal = il.DeclareLocal(_types.String);
        var optionValueLocal = il.DeclareLocal(_types.Object);
        var dictionaryTryGetValue = _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()]);

        // Extract PEM from key
        var notStringLabel = il.DefineLabel();
        var createKeyObjectLabel = il.DefineLabel();

        // createPublicKey(private/public KeyObject) derives/copies the public key.
        var notKeyObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSKeyObjectCtorAsym.DeclaringType!);
        il.Emit(OpCodes.Brfalse, notKeyObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSKeyObjectCtorAsym.DeclaringType!);
        il.Emit(OpCodes.Callvirt, runtime.TSKeyObjectToPublicKey);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notKeyObject);

        // Check if key is string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pemLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        // Not a string - try emitted $Object first, then Dictionary<string,object?>
        il.MarkLabel(notStringLabel);

        // Check for $Object (emitted type)
        var tryDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, tryDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, keyValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "format");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, formatLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);
        var objectOptionReady = il.DefineLabel();
        il.Emit(OpCodes.Br, objectOptionReady);

        // Check for Dictionary<string,object?> (compiled object literals)
        il.MarkLabel(tryDictLabel);
        var noGetPropertyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noGetPropertyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Stloc, keyValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "format");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, formatLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.MarkLabel(objectOptionReady);
        var optionIsDer = il.DefineLabel();
        var optionIsPem = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldstr, "jwk");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, optionIsDer);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.TSKeyObjectImportJwk);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(optionIsDer);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldstr, "der");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, optionIsPem);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.TSKeyObjectImportDer);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(optionIsPem);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pemLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        il.MarkLabel(noGetPropertyLabel);
        il.Emit(OpCodes.Ldstr, "crypto.createPublicKey: key must be a PEM string or object with 'key' property");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create and return new $TSKeyObject(pem, isPrivate: false)
        il.MarkLabel(createKeyObjectLabel);
        il.Emit(OpCodes.Ldloc, pemLocal);
        il.Emit(OpCodes.Ldc_I4_0); // isPrivate = false
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorAsym);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreatePrivateKey(object key)
    /// Creates a private KeyObject from PEM using pure IL (no SharpTS.dll dependency).
    /// </summary>
    private void EmitCryptoCreatePrivateKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreatePrivateKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.CryptoCreatePrivateKey = method;

        var il = method.GetILGenerator();

        var pemLocal = il.DeclareLocal(_types.String);
        var keyValueLocal = il.DeclareLocal(_types.Object);
        var formatLocal = il.DeclareLocal(_types.String);
        var typeLocal = il.DeclareLocal(_types.String);
        var optionValueLocal = il.DeclareLocal(_types.Object);
        var dictionaryTryGetValue = _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()]);

        // Extract PEM from key
        var notStringLabel = il.DefineLabel();
        var createKeyObjectLabel = il.DefineLabel();

        // Check if key is string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pemLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        // Not a string - require emitted $Object and read its "key" property.
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        var noGetPropertyLabel = il.DefineLabel();
        var tryDict = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tryDict);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, keyValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "format");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, formatLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);
        var optionReady = il.DefineLabel();
        il.Emit(OpCodes.Br, optionReady);

        il.MarkLabel(tryDict);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noGetPropertyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "key");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Stloc, keyValueLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "format");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, formatLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Ldloca, optionValueLocal);
        il.Emit(OpCodes.Callvirt, dictionaryTryGetValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, optionValueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.MarkLabel(optionReady);
        var optionIsDer = il.DefineLabel();
        var optionIsPem = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldstr, "jwk");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, optionIsDer);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.TSKeyObjectImportJwk);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(optionIsDer);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Ldstr, "der");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, optionIsPem);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.TSKeyObjectImportDer);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(optionIsPem);
        il.Emit(OpCodes.Ldloc, keyValueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pemLocal);
        il.Emit(OpCodes.Br, createKeyObjectLabel);

        il.MarkLabel(noGetPropertyLabel);
        il.Emit(OpCodes.Ldstr, "crypto.createPrivateKey: key must be a PEM string or object with 'key' property");
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create and return new $TSKeyObject(pem, isPrivate: true)
        il.MarkLabel(createKeyObjectLabel);
        il.Emit(OpCodes.Ldloc, pemLocal);
        il.Emit(OpCodes.Ldc_I4_1); // isPrivate = true
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorAsym);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>crypto.diffieHellman({ privateKey, publicKey }) for EC KeyObjects.</summary>
    private void EmitCryptoDiffieHellman(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_diffieHellman",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.CryptoDiffieHellman = method;
        var il = method.GetILGenerator();
        var privateKeyLocal = il.DeclareLocal(_types.Object);
        var publicKeyLocal = il.DeclareLocal(_types.Object);
        var keyObjectType = runtime.TSKeyObjectCtorAsym.DeclaringType!;
        var invalid = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "privateKey");
        il.Emit(OpCodes.Call, runtime.TSKeyObjectGetOption);
        il.Emit(OpCodes.Stloc, privateKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "publicKey");
        il.Emit(OpCodes.Call, runtime.TSKeyObjectGetOption);
        il.Emit(OpCodes.Stloc, publicKeyLocal);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Isinst, keyObjectType);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, publicKeyLocal);
        il.Emit(OpCodes.Isinst, keyObjectType);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Castclass, keyObjectType);
        il.Emit(OpCodes.Ldloc, publicKeyLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSKeyObjectDeriveSecret);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldstr,
            "crypto.diffieHellman requires an options object with privateKey and publicKey KeyObjects");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        runtime.RegisterBuiltInModuleMethod("crypto", "diffieHellman", method);
    }
}
