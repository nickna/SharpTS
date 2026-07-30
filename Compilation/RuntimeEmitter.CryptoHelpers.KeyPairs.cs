using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static object CryptoGenerateKeyPairSync(string type, object? options)
    /// Generates an RSA or EC key pair.
    /// Uses pure IL - no SharpTS.dll dependency.
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

        var tupleLocal = il.DeclareLocal(typeof((string, string)));
        var rsaLabel = il.DefineLabel();
        var ecLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var createObjectLabel = il.DefineLabel();

        // Convert type to lowercase
        var typeLowerLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.StringToLowerInvariant);
        il.Emit(OpCodes.Stloc, typeLowerLocal);

        // Check for "rsa"
        il.Emit(OpCodes.Ldloc, typeLowerLocal);
        il.Emit(OpCodes.Ldstr, "rsa");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, rsaLabel);

        // Check for "ec"
        il.Emit(OpCodes.Ldloc, typeLowerLocal);
        il.Emit(OpCodes.Ldstr, "ec");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, ecLabel);

        // Unknown type - throw
        il.Emit(OpCodes.Br, throwLabel);

        // RSA key generation
        il.MarkLabel(rsaLabel);
        il.Emit(OpCodes.Ldarg_1);  // options
        il.Emit(OpCodes.Call, runtime.GenerateRsaKeyPairRaw);
        il.Emit(OpCodes.Stloc, tupleLocal);
        il.Emit(OpCodes.Br, createObjectLabel);

        // EC key generation
        il.MarkLabel(ecLabel);
        il.Emit(OpCodes.Ldarg_1);  // options
        il.Emit(OpCodes.Call, runtime.GenerateEcKeyPairRaw);
        il.Emit(OpCodes.Stloc, tupleLocal);
        il.Emit(OpCodes.Br, createObjectLabel);

        // Throw for unknown type
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.generateKeyPairSync: unsupported key type");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // Create result object
        il.MarkLabel(createObjectLabel);

        // Create Dictionary<string, object?> for $Object
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["publicKey"] = tuple.Item1
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "publicKey");
        il.Emit(OpCodes.Ldloca, tupleLocal);
        il.Emit(OpCodes.Ldfld, typeof((string, string)).GetField("Item1")!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        // dict["privateKey"] = tuple.Item2
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "privateKey");
        il.Emit(OpCodes.Ldloca, tupleLocal);
        il.Emit(OpCodes.Ldfld, typeof((string, string)).GetField("Item2")!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        // return new $Object(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateDiffieHellman(object primeOrLength, object? generator)
    /// Creates a DiffieHellman object using the emitted $DiffieHellman class.
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

        // Check if primeOrLength is a double (number) -> use prime length constructor
        var notNumberLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notNumberLabel);

        // Use prime length constructor: new $DiffieHellman((int)(double)primeOrLength)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.TSDiffieHellmanCtorPrimeLength);
        il.Emit(OpCodes.Ret);

        // Not a number - decode prime and generator bytes
        il.MarkLabel(notNumberLabel);

        // Decode prime bytes
        var primeLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);  // encoding
        il.Emit(OpCodes.Call, runtime.TSDHDecodeInput);
        il.Emit(OpCodes.Stloc, primeLocal);

        // Decode generator bytes (if not null)
        var generatorLocal = il.DeclareLocal(_types.ByteArray);
        var generatorNullLabel = il.DefineLabel();
        var afterGeneratorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, generatorNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.TSDHDecodeInput);
        il.Emit(OpCodes.Stloc, generatorLocal);
        il.Emit(OpCodes.Br, afterGeneratorLabel);
        il.MarkLabel(generatorNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, generatorLocal);
        il.MarkLabel(afterGeneratorLabel);

        // Use prime/generator constructor: new $DiffieHellman(prime, generator)
        il.Emit(OpCodes.Ldloc, primeLocal);
        il.Emit(OpCodes.Ldloc, generatorLocal);
        il.Emit(OpCodes.Newobj, runtime.TSDiffieHellmanCtorPrimeGenerator);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetDiffieHellman(string groupName)
    /// Gets a predefined DiffieHellman group using the emitted $DiffieHellman class.
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

        // Use group constructor: new $DiffieHellman(groupName)
        il.Emit(OpCodes.Ldarg_0);  // groupName
        il.Emit(OpCodes.Newobj, runtime.TSDiffieHellmanCtorGroup);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateECDH(string curveName)
    /// Creates an ECDH object using the emitted $ECDH class.
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

        // Create new $ECDH(curveName)
        il.Emit(OpCodes.Ldarg_0);  // curveName
        il.Emit(OpCodes.Newobj, runtime.TSECDHCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static int GetOptionInt(object options, string name, int defaultValue)
    /// Extracts an int option from an object using GetProperty or reflection.
    /// </summary>
    private void EmitGetOptionInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetOptionInt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object, _types.String, _types.Int32]);
        runtime.GetOptionInt = method;

        var il = method.GetILGenerator();
        var returnDefaultLabel = il.DefineLabel();
        var checkValueLabel = il.DefineLabel();

        // if (options == null) return defaultValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        // Read the option from an emitted $Object OR a Dictionary<string,object?>
        // literal (compiled object literals may be either) (#1060).
        var valueLocal = il.DeclareLocal(_types.Object);
        EmitReadObjectOption(il, runtime, valueLocal, returnDefaultLabel);

        // if (value is double d) return (int)d
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        // return defaultValue
        il.MarkLabel(returnDefaultLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string GetOptionString(object options, string name, string defaultValue)
    /// Extracts a string option from an object using GetProperty or reflection.
    /// </summary>
    private void EmitGetOptionString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetOptionString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.String, _types.String]);
        runtime.GetOptionString = method;

        var il = method.GetILGenerator();
        var returnDefaultLabel = il.DefineLabel();

        // if (options == null) return defaultValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnDefaultLabel);

        // Read the option from an emitted $Object OR a Dictionary<string,object?>
        // literal (compiled object literals may be either) (#1060).
        var valueLocal = il.DeclareLocal(_types.Object);
        EmitReadObjectOption(il, runtime, valueLocal, returnDefaultLabel);

        // if (value is string s) return s
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        var returnStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, returnStringLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, returnDefaultLabel);

        il.MarkLabel(returnStringLabel);
        il.Emit(OpCodes.Ret);

        // return defaultValue
        il.MarkLabel(returnDefaultLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static (string, string) GenerateRsaKeyPairRaw(object? options)
    /// Generates RSA key pair and returns (publicKeyPem, privateKeyPem).
    /// </summary>
    private void EmitGenerateRsaKeyPairRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var tupleType = typeof(ValueTuple<string, string>);
        var method = typeBuilder.DefineMethod(
            "GenerateRsaKeyPairRaw",
            MethodAttributes.Public | MethodAttributes.Static,
            tupleType,
            [_types.Object]);
        runtime.GenerateRsaKeyPairRaw = method;

        var il = method.GetILGenerator();

        // int modulusLength = GetOptionInt(options, "modulusLength", 2048)
        var modulusLengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);  // options
        il.Emit(OpCodes.Ldstr, "modulusLength");
        il.Emit(OpCodes.Ldc_I4, 2048);
        il.Emit(OpCodes.Call, runtime.GetOptionInt);
        il.Emit(OpCodes.Stloc, modulusLengthLocal);

        // using var rsa = RSA.Create(modulusLength)
        var rsaLocal = il.DeclareLocal(_types.RSA);
        il.Emit(OpCodes.Ldloc, modulusLengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RSA, "Create", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, rsaLocal);

        il.BeginExceptionBlock();

        // var publicKey = rsa.ExportSubjectPublicKeyInfoPem()
        var publicKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.RSA, "ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, publicKeyLocal);

        // var privateKey = rsa.ExportPkcs8PrivateKeyPem()
        var privateKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.RSA, "ExportPkcs8PrivateKeyPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, privateKeyLocal);

        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.MarkLabel(skipDisposeLabel);
        il.EndExceptionBlock();

        // return (publicKey, privateKey)
        il.Emit(OpCodes.Ldloc, publicKeyLocal);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Newobj, tupleType.GetConstructor([typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static (string, string) GenerateEcKeyPairRaw(object? options)
    /// Generates EC key pair and returns (publicKeyPem, privateKeyPem).
    /// </summary>
    private void EmitGenerateEcKeyPairRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var tupleType = typeof(ValueTuple<string, string>);
        var method = typeBuilder.DefineMethod(
            "GenerateEcKeyPairRaw",
            MethodAttributes.Public | MethodAttributes.Static,
            tupleType,
            [_types.Object]);
        runtime.GenerateEcKeyPairRaw = method;

        var il = method.GetILGenerator();

        // string curveName = GetOptionString(options, "namedCurve", "prime256v1")
        var curveNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);  // options
        il.Emit(OpCodes.Ldstr, "namedCurve");
        il.Emit(OpCodes.Ldstr, "prime256v1");
        il.Emit(OpCodes.Call, runtime.GetOptionString);
        il.Emit(OpCodes.Stloc, curveNameLocal);

        // Map curveName to ECCurve
        var curveLocal = il.DeclareLocal(typeof(ECCurve));
        var p256Label = il.DefineLabel();
        var p384Label = il.DefineLabel();
        var p521Label = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var createKeyLabel = il.DefineLabel();

        // Convert to lowercase
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Callvirt, _types.StringToLowerInvariant);
        il.Emit(OpCodes.Stloc, curveNameLocal);

        // Check for P-256 variants
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "prime256v1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p256Label);
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "secp256r1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p256Label);
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "p-256");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p256Label);

        // Check for P-384 variants
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "secp384r1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p384Label);
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "p-384");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p384Label);

        // Check for P-521 variants
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "secp521r1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p521Label);
        il.Emit(OpCodes.Ldloc, curveNameLocal);
        il.Emit(OpCodes.Ldstr, "p-521");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p521Label);

        // Unknown curve - throw
        il.Emit(OpCodes.Br, throwLabel);

        // P-256
        il.MarkLabel(p256Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Br, createKeyLabel);

        // P-384
        il.MarkLabel(p384Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Br, createKeyLabel);

        // P-521
        il.MarkLabel(p521Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP521")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Br, createKeyLabel);

        // Throw for unknown curve
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.generateKeyPairSync: unsupported curve");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        // Create ECDsa key
        il.MarkLabel(createKeyLabel);
        var ecdsaLocal = il.DeclareLocal(typeof(ECDsa));
        il.Emit(OpCodes.Ldloc, curveLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", [typeof(ECCurve)])!);
        il.Emit(OpCodes.Stloc, ecdsaLocal);

        il.BeginExceptionBlock();

        // var publicKey = ecdsa.ExportSubjectPublicKeyInfoPem()
        var publicKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, publicKeyLocal);

        // var privateKey = ecdsa.ExportPkcs8PrivateKeyPem()
        var privateKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportPkcs8PrivateKeyPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, privateKeyLocal);

        il.BeginFinallyBlock();
        var skipDisposeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Brfalse, skipDisposeLabel);
        il.Emit(OpCodes.Ldloc, ecdsaLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.MarkLabel(skipDisposeLabel);
        il.EndExceptionBlock();

        // return (publicKey, privateKey)
        il.Emit(OpCodes.Ldloc, publicKeyLocal);
        il.Emit(OpCodes.Ldloc, privateKeyLocal);
        il.Emit(OpCodes.Newobj, tupleType.GetConstructor([typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }
}
