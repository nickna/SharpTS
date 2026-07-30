using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $ECDH class for standalone ECDH key exchange support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSECDH
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _tsECDHTypeBuilder = null!;
    private FieldBuilder _tsECDHEcdhField = null!;
    private FieldBuilder _tsECDHCurveNameField = null!;
    private FieldBuilder _tsECDHFieldLenField = null!;

    /// <summary>
    /// Phase 1: Define type, fields, and constructor.
    /// Called before EmitRuntimeClass.
    /// </summary>
    private void EmitTSECDHTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $ECDH
        _tsECDHTypeBuilder = moduleBuilder.DefineType(
            "$ECDH",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSECDHType = _tsECDHTypeBuilder;

        // Fields
        _tsECDHEcdhField = _tsECDHTypeBuilder.DefineField("_ecdh", typeof(ECDiffieHellman), FieldAttributes.Private);
        _tsECDHCurveNameField = _tsECDHTypeBuilder.DefineField("_curveName", _types.String, FieldAttributes.Private);
        _tsECDHFieldLenField = _tsECDHTypeBuilder.DefineField("_fieldLen", _types.Int32, FieldAttributes.Private);

        // Constructor only in Phase 1
        EmitTSECDHCtor(_tsECDHTypeBuilder, runtime);
        // All methods that use runtime helpers are added in Phase 2

        // Define GetMember signature in Phase 1 so GetProperty can reference it.
        // The IL body is emitted in Phase 2 (EmitTSECDHGetMember).
        var getMemberMethod = _tsECDHTypeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSECDHGetMember = getMemberMethod;
    }

    /// <summary>
    /// Phase 2: Add all methods and finalize type.
    /// Called after EmitRuntimeClass (needs runtime helpers DecodeInput/EncodeResult).
    /// </summary>
    private void EmitTSECDHFinalize(EmittedRuntime runtime)
    {
        // Methods - order matters due to dependencies
        // GetPublicKey must come before GenerateKeys (GenerateKeys calls GetPublicKey)
        EmitTSECDHGetPublicKey(_tsECDHTypeBuilder, runtime);
        EmitTSECDHGetPrivateKey(_tsECDHTypeBuilder, runtime);
        EmitTSECDHSetPrivateKey(_tsECDHTypeBuilder, runtime);
        EmitTSECDHGenerateKeys(_tsECDHTypeBuilder, runtime);
        EmitTSECDHComputeSecret(_tsECDHTypeBuilder, runtime);
        EmitTSECDHGetMember(_tsECDHTypeBuilder, runtime);
        _tsECDHTypeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public $ECDH(string curveName)
    /// </summary>
    private void EmitTSECDHCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSECDHCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _curveName = curveName
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsECDHCurveNameField);

        // Get normalized curve name
        var normalizedLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, normalizedLocal);

        // Local for ECCurve
        var curveLocal = il.DeclareLocal(typeof(ECCurve));

        // Check curve names
        var p256Label = il.DefineLabel();
        var p384Label = il.DefineLabel();
        var p521Label = il.DefineLabel();
        var createLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "prime256v1" or "secp256r1" or "p-256"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "prime256v1");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p256Label);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "secp256r1");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p256Label);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "p-256");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p256Label);

        // Check "secp384r1" or "p-384"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "secp384r1");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p384Label);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "p-384");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p384Label);

        // Check "secp521r1" or "p-521"
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "secp521r1");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p521Label);
        il.Emit(OpCodes.Ldloc, normalizedLocal);
        il.Emit(OpCodes.Ldstr, "p-521");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, p521Label);

        // Default - throw
        il.Emit(OpCodes.Br, defaultLabel);

        // P256
        il.MarkLabel(p256Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Stfld, _tsECDHFieldLenField);
        il.Emit(OpCodes.Br, createLabel);

        // P384
        il.MarkLabel(p384Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 48);
        il.Emit(OpCodes.Stfld, _tsECDHFieldLenField);
        il.Emit(OpCodes.Br, createLabel);

        // P521
        il.MarkLabel(p521Label);
        il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty("nistP521")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curveLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 66);
        il.Emit(OpCodes.Stfld, _tsECDHFieldLenField);
        il.Emit(OpCodes.Br, createLabel);

        // Default - throw
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported curve: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        // Create ECDH with curve
        il.MarkLabel(createLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, curveLocal);
        il.Emit(OpCodes.Call, typeof(ECDiffieHellman).GetMethod("Create", [typeof(ECCurve)])!);
        il.Emit(OpCodes.Stfld, _tsECDHEcdhField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GenerateKeys(string? encoding, string? format)
    /// </summary>
    private void EmitTSECDHGenerateKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GenerateKeys",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.String]
        );
        runtime.TSECDHGenerateKeys = method;

        var il = method.GetILGenerator();

        // Get the current curve parameters
        var curveLocal = il.DeclareLocal(typeof(ECCurve));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldc_I4_0);  // includePrivateParameters = false
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Ldfld, typeof(ECParameters).GetField("Curve")!);
        il.Emit(OpCodes.Stloc, curveLocal);

        // Regenerate the key pair
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldloc, curveLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("GenerateKey", [typeof(ECCurve)])!);

        // Return GetPublicKey(encoding, format)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSECDHGetPublicKey);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object ComputeSecret(object otherPublicKey, string? inputEncoding, string? outputEncoding)
    /// </summary>
    private void EmitTSECDHComputeSecret(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ComputeSecret",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.String, _types.String]
        );
        runtime.TSECDHComputeSecret = method;

        var il = method.GetILGenerator();

        // Decode the other party's public key
        var otherBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);  // otherPublicKey
        il.Emit(OpCodes.Ldarg_2);  // inputEncoding
        il.Emit(OpCodes.Call, runtime.TSECDHDecodeInput);
        il.Emit(OpCodes.Stloc, otherBytesLocal);

        // Create an ECDiffieHellman for the other party
        var otherEcdhLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        il.Emit(OpCodes.Call, typeof(ECDiffieHellman).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, otherEcdhLocal);

        // Call helper to compute the shared secret (handles raw points + SPKI, DeriveRawSecretAgreement)
        var secretLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldloc, otherBytesLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHFieldLenField);
        il.Emit(OpCodes.Call, runtime.TSECDHComputeSecretHelper);
        il.Emit(OpCodes.Stloc, secretLocal);

        // Encode and return the result
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ldarg_3);  // outputEncoding
        il.Emit(OpCodes.Call, runtime.TSECDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetPublicKey(string? encoding, string? format)
    /// </summary>
    private void EmitTSECDHGetPublicKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPublicKey",
            MethodAttributes.Public,
            _types.Object,
            [_types.String, _types.String]
        );
        runtime.TSECDHGetPublicKey = method;

        var il = method.GetILGenerator();

        // Export EC parameters (public), then build the raw point per the format (#1060).
        var paramsLocal = il.DeclareLocal(typeof(ECParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        var qField = typeof(ECParameters).GetField("Q")!;
        var xField = typeof(ECPoint).GetField("X")!;
        var yField = typeof(ECPoint).GetField("Y")!;

        // EcdhEncodePoint(Q.X, Q.Y, _fieldLen, format)
        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, qField);
        il.Emit(OpCodes.Ldfld, xField);
        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, qField);
        il.Emit(OpCodes.Ldfld, yField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHFieldLenField);
        il.Emit(OpCodes.Ldarg_2);  // format
        il.Emit(OpCodes.Call, runtime.EcdhEncodePoint);

        // Encode and return the result
        il.Emit(OpCodes.Ldarg_1);  // encoding
        il.Emit(OpCodes.Call, runtime.TSECDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetPrivateKey(string? encoding)
    /// </summary>
    private void EmitTSECDHGetPrivateKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrivateKey",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSECDHGetPrivateKey = method;

        var il = method.GetILGenerator();

        // Return the raw private scalar D (Node behavior) (#1060).
        var paramsLocal = il.DeclareLocal(typeof(ECParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldc_I4_1);  // includePrivateParameters = true
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldfld, typeof(ECParameters).GetField("D")!);
        il.Emit(OpCodes.Ldarg_1);  // encoding
        il.Emit(OpCodes.Call, runtime.TSECDHEncodeResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void SetPrivateKey(object key, string? encoding)
    /// </summary>
    private void EmitTSECDHSetPrivateKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPrivateKey",
            MethodAttributes.Public,
            null,
            [_types.Object, _types.String]
        );
        runtime.TSECDHSetPrivateKey = method;

        var il = method.GetILGenerator();

        // Decode the key bytes
        var keyBytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_1);  // key
        il.Emit(OpCodes.Ldarg_2);  // encoding
        il.Emit(OpCodes.Call, runtime.TSECDHDecodeInput);
        il.Emit(OpCodes.Stloc, keyBytesLocal);

        // Create a ReadOnlySpan<byte> from the byte array
        var spanLocal = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
        il.Emit(OpCodes.Ldloca, spanLocal);  // Address of span local
        il.Emit(OpCodes.Ldloc, keyBytesLocal);  // byte[]
        il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetConstructor([typeof(byte[])])!);

        // Import the private key
        var bytesReadLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsECDHEcdhField);
        il.Emit(OpCodes.Ldloc, spanLocal);  // ReadOnlySpan<byte>
        il.Emit(OpCodes.Ldloca, bytesReadLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportPkcs8PrivateKey", [typeof(ReadOnlySpan<byte>), typeof(int).MakeByRefType()])!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object? GetMember(string name)
    /// </summary>
    private void EmitTSECDHGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // MethodBuilder was already defined in EmitTSECDHTypeDefinition (Phase 1)
        var method = runtime.TSECDHGetMember;

        var il = method.GetILGenerator();

        // Switch on member name
        var generateKeysLabel = il.DefineLabel();
        var computeSecretLabel = il.DefineLabel();
        var getPublicKeyLabel = il.DefineLabel();
        var getPrivateKeyLabel = il.DefineLabel();
        var setPrivateKeyLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "generateKeys"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "generateKeys");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, generateKeysLabel);

        // Check "computeSecret"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "computeSecret");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, computeSecretLabel);

        // Check "getPublicKey"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "getPublicKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, getPublicKeyLabel);

        // Check "getPrivateKey"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "getPrivateKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, getPrivateKeyLabel);

        // Check "setPrivateKey"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "setPrivateKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, setPrivateKeyLabel);

        // Default - return null
        il.Emit(OpCodes.Br, defaultLabel);

        // generateKeys: Return bound method
        il.MarkLabel(generateKeysLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GenerateKeys");
        il.Emit(OpCodes.Ldc_I4_0);  // minArgs
        il.Emit(OpCodes.Ldc_I4_2);  // maxArgs
        il.Emit(OpCodes.Newobj, runtime.BoundECDHMethodCtor);
        il.Emit(OpCodes.Ret);

        // computeSecret: Return bound method
        il.MarkLabel(computeSecretLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "ComputeSecret");
        il.Emit(OpCodes.Ldc_I4_1);  // minArgs
        il.Emit(OpCodes.Ldc_I4_3);  // maxArgs
        il.Emit(OpCodes.Newobj, runtime.BoundECDHMethodCtor);
        il.Emit(OpCodes.Ret);

        // getPublicKey: Return bound method
        il.MarkLabel(getPublicKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetPublicKey");
        il.Emit(OpCodes.Ldc_I4_0);  // minArgs
        il.Emit(OpCodes.Ldc_I4_2);  // maxArgs
        il.Emit(OpCodes.Newobj, runtime.BoundECDHMethodCtor);
        il.Emit(OpCodes.Ret);

        // getPrivateKey: Return bound method
        il.MarkLabel(getPrivateKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "GetPrivateKey");
        il.Emit(OpCodes.Ldc_I4_0);  // minArgs
        il.Emit(OpCodes.Ldc_I4_1);  // maxArgs
        il.Emit(OpCodes.Newobj, runtime.BoundECDHMethodCtor);
        il.Emit(OpCodes.Ret);

        // setPrivateKey: Return bound method
        il.MarkLabel(setPrivateKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "SetPrivateKey");
        il.Emit(OpCodes.Ldc_I4_1);  // minArgs
        il.Emit(OpCodes.Ldc_I4_2);  // maxArgs
        il.Emit(OpCodes.Newobj, runtime.BoundECDHMethodCtor);
        il.Emit(OpCodes.Ret);

        // Default - return null
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits helper methods for encoding/decoding in $Runtime class.
    /// </summary>
    private void EmitTSECDHHelpers(TypeBuilder runtimeTypeBuilder, EmittedRuntime runtime)
    {
        EmitEcPointHelpers(runtimeTypeBuilder, runtime);
        EmitTSECDHEncodeResult(runtimeTypeBuilder, runtime);
        EmitTSECDHDecodeInput(runtimeTypeBuilder, runtime);
        EmitTSECDHComputeSecretHelper(runtimeTypeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static byte[] ComputeSecretHelper(ECDiffieHellman ecdh, byte[] otherPublicKeyBytes)
    /// This helper handles the Span conversion that can't be done directly in IL.
    /// </summary>
    private void EmitTSECDHComputeSecretHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // byte[] ECDHComputeSecretHelper(ECDiffieHellman self, byte[] otherBytes, int fieldLen)
        var method = typeBuilder.DefineMethod(
            "ECDHComputeSecretHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [typeof(ECDiffieHellman), _types.ByteArray, _types.Int32]
        );
        runtime.TSECDHComputeSecretHelper = method;

        var il = method.GetILGenerator();

        var otherEcdhLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        il.Emit(OpCodes.Call, typeof(ECDiffieHellman).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, otherEcdhLocal);

        var rawPointLabel = il.DefineLabel();
        var importedLabel = il.DefineLabel();

        // if (otherBytes[0] == 0x30) => SPKI path, else raw point path
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x30);
        il.Emit(OpCodes.Bne_Un, rawPointLabel);

        // SPKI: ImportSubjectPublicKeyInfo(span, out _)
        var spanLocal = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
        var bytesReadLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloca, spanLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetConstructor([typeof(byte[])])!);
        il.Emit(OpCodes.Ldloc, otherEcdhLocal);
        il.Emit(OpCodes.Ldloc, spanLocal);
        il.Emit(OpCodes.Ldloca, bytesReadLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportSubjectPublicKeyInfo",
            [typeof(ReadOnlySpan<byte>), typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Br, importedLabel);

        // Raw point: prefix 0x04/0x06/0x07 (uncompressed/hybrid) with length 1+2*fieldLen.
        il.MarkLabel(rawPointLabel);

        // Compressed (0x02/0x03) is a documented compiled ceiling: length == 1+fieldLen.
        var notCompressedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);      // fieldLen
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);          // 1 + fieldLen
        il.Emit(OpCodes.Bne_Un, notCompressedLabel);
        il.Emit(OpCodes.Ldstr, "ECDH.computeSecret: compressed-point input is not supported in compiled mode (interpreter only)");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notCompressedLabel);

        // Build ECParameters { Curve = self's curve, Q = { X, Y } } and import.
        var selfParamsLocal = il.DeclareLocal(typeof(ECParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, selfParamsLocal);

        // x = new byte[fieldLen]; Array.Copy(otherBytes, 1, x, 0, fieldLen)
        var xLocal = il.DeclareLocal(_types.ByteArray);
        var yLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, xLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        // y = new byte[fieldLen]; Array.Copy(otherBytes, 1+fieldLen, y, 0, fieldLen)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, yLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // otherParams = new ECParameters { Curve = selfParams.Curve, Q = new ECPoint { X = x, Y = y } }
        var otherParamsLocal = il.DeclareLocal(typeof(ECParameters));
        var qLocal = il.DeclareLocal(typeof(ECPoint));
        var curveField = typeof(ECParameters).GetField("Curve")!;
        var qField = typeof(ECParameters).GetField("Q")!;
        var xField = typeof(ECPoint).GetField("X")!;
        var yField = typeof(ECPoint).GetField("Y")!;

        il.Emit(OpCodes.Ldloca, qLocal);
        il.Emit(OpCodes.Initobj, typeof(ECPoint));
        il.Emit(OpCodes.Ldloca, qLocal);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Stfld, xField);
        il.Emit(OpCodes.Ldloca, qLocal);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Stfld, yField);

        il.Emit(OpCodes.Ldloca, otherParamsLocal);
        il.Emit(OpCodes.Initobj, typeof(ECParameters));
        il.Emit(OpCodes.Ldloca, otherParamsLocal);
        il.Emit(OpCodes.Ldloca, selfParamsLocal);
        il.Emit(OpCodes.Ldfld, curveField);
        il.Emit(OpCodes.Stfld, curveField);
        il.Emit(OpCodes.Ldloca, otherParamsLocal);
        il.Emit(OpCodes.Ldloc, qLocal);
        il.Emit(OpCodes.Stfld, qField);

        il.Emit(OpCodes.Ldloc, otherEcdhLocal);
        il.Emit(OpCodes.Ldloc, otherParamsLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportParameters", [typeof(ECParameters)])!);

        il.MarkLabel(importedLabel);

        // Derive raw secret (the X coordinate) — matches interp's DeriveRawSecretAgreement.
        var secretLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, otherEcdhLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetProperty("PublicKey")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("DeriveRawSecretAgreement", [typeof(ECDiffieHellmanPublicKey)])!);
        il.Emit(OpCodes.Stloc, secretLocal);

        il.Emit(OpCodes.Ldloc, otherEcdhLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object EncodeResult(byte[] bytes, string? encoding)
    /// </summary>
    private void EmitTSECDHEncodeResult(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ECDHEncodeResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ByteArray, _types.String]
        );
        runtime.TSECDHEncodeResult = method;

        var il = method.GetILGenerator();

        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var bufferLabel = il.DefineLabel();

        // Check encoding
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, bufferLabel);

        // Normalize encoding
        var encodingLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Default to buffer
        il.Emit(OpCodes.Br, bufferLabel);

        // hex: Convert.ToHexString(bytes).ToLowerInvariant()
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        // base64: Convert.ToBase64String(bytes)
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Ret);

        // buffer: new $Buffer(bytes)
        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] DecodeInput(object input, string? encoding)
    /// </summary>
    private void EmitTSECDHDecodeInput(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ECDHDecodeInput",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.Object, _types.String]
        );
        runtime.TSECDHDecodeInput = method;

        var il = method.GetILGenerator();

        var checkBytesLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // Check for $Buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, checkBytesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Ret);

        // Check for byte[]
        il.MarkLabel(checkBytesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Ret);

        // Check for string
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // String handling with encoding
        var strLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);

        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var utf8Label = il.DefineLabel();

        // Check encoding
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, utf8Label);

        var encodingLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, encodingLocal);

        // Check "hex"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "hex");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, hexLabel);

        // Check "base64"
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldstr, "base64");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, base64Label);

        // Default to UTF8
        il.Emit(OpCodes.Br, utf8Label);

        // hex: Convert.FromHexString(str)
        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromHexString", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // base64: Convert.FromBase64String(str)
        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("FromBase64String", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // utf8: Encoding.UTF8.GetBytes(str)
        il.MarkLabel(utf8Label);
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // Throw
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Input must be a Buffer, byte array, or string");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    // Instance fields for two-phase BoundECDHMethod emission
    private TypeBuilder _boundECDHMethodTypeBuilder = null!;
    private FieldBuilder _boundECDHMethodEcdhField = null!;
    private FieldBuilder _boundECDHMethodMethodNameField = null!;

    /// <summary>
    /// Phase 1: Define $BoundECDHMethod type, fields, and constructor.
    /// Called after $ECDH type definition (needs TSECDHType).
    /// </summary>
    private void EmitBoundECDHMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _boundECDHMethodTypeBuilder = moduleBuilder.DefineType(
            "$BoundECDHMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        _ = _boundECDHMethodTypeBuilder;

        // Fields
        _boundECDHMethodEcdhField = _boundECDHMethodTypeBuilder.DefineField("_ecdh", runtime.TSECDHType, FieldAttributes.Private);
        _boundECDHMethodMethodNameField = _boundECDHMethodTypeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Private);
        var minArgsField = _boundECDHMethodTypeBuilder.DefineField("_minArgs", _types.Int32, FieldAttributes.Private);
        var maxArgsField = _boundECDHMethodTypeBuilder.DefineField("_maxArgs", _types.Int32, FieldAttributes.Private);

        // Constructor: ($ECDH ecdh, string methodName, int minArgs, int maxArgs)
        var ctor = _boundECDHMethodTypeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSECDHType, _types.String, _types.Int32, _types.Int32]
        );
        runtime.BoundECDHMethodCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, _boundECDHMethodEcdhField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, _boundECDHMethodMethodNameField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_3);
        ctorIl.Emit(OpCodes.Stfld, minArgsField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg, 4);
        ctorIl.Emit(OpCodes.Stfld, maxArgsField);
        ctorIl.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Phase 2: Add Invoke method and finalize $BoundECDHMethod type.
    /// Called after ECDH methods are defined (Invoke calls them).
    /// </summary>
    private void EmitBoundECDHMethodFinalize(EmittedRuntime runtime)
    {
        // Invoke method: public object? Invoke(object[] args)
        var invoke = _boundECDHMethodTypeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        _ = invoke;

        var il = invoke.GetILGenerator();

        // Get method name
        var methodNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodMethodNameField);
        il.Emit(OpCodes.Stloc, methodNameLocal);

        // Switch on method name
        var generateKeysLabel = il.DefineLabel();
        var computeSecretLabel = il.DefineLabel();
        var getPublicKeyLabel = il.DefineLabel();
        var getPrivateKeyLabel = il.DefineLabel();
        var setPrivateKeyLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // Check "GenerateKeys"
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, "GenerateKeys");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, generateKeysLabel);

        // Check "ComputeSecret"
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, "ComputeSecret");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, computeSecretLabel);

        // Check "GetPublicKey"
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, "GetPublicKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, getPublicKeyLabel);

        // Check "GetPrivateKey"
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, "GetPrivateKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, getPrivateKeyLabel);

        // Check "SetPrivateKey"
        il.Emit(OpCodes.Ldloc, methodNameLocal);
        il.Emit(OpCodes.Ldstr, "SetPrivateKey");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, setPrivateKeyLabel);

        // Default - return null
        il.Emit(OpCodes.Br, defaultLabel);

        // GenerateKeys(encoding?, format?)
        il.MarkLabel(generateKeysLabel);
        EmitGetArgOrNull(il, 0);  // encoding
        EmitGetArgOrNull(il, 1);  // format
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodEcdhField);
        il.Emit(OpCodes.Ldloc_0);  // encoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc_1);  // format
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSECDHGenerateKeys);
        il.Emit(OpCodes.Ret);

        // ComputeSecret(otherPublicKey, inputEncoding?, outputEncoding?)
        il.MarkLabel(computeSecretLabel);
        EmitGetArgOrNull(il, 0);  // otherPublicKey
        EmitGetArgOrNull(il, 1);  // inputEncoding
        EmitGetArgOrNull(il, 2);  // outputEncoding
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodEcdhField);
        il.Emit(OpCodes.Ldloc_0);  // otherPublicKey
        il.Emit(OpCodes.Ldloc_1);  // inputEncoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc_2);  // outputEncoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSECDHComputeSecret);
        il.Emit(OpCodes.Ret);

        // GetPublicKey(encoding?, format?)
        il.MarkLabel(getPublicKeyLabel);
        EmitGetArgOrNull(il, 0);  // encoding
        EmitGetArgOrNull(il, 1);  // format
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodEcdhField);
        il.Emit(OpCodes.Ldloc_0);  // encoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc_1);  // format
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSECDHGetPublicKey);
        il.Emit(OpCodes.Ret);

        // GetPrivateKey(encoding?)
        il.MarkLabel(getPrivateKeyLabel);
        EmitGetArgOrNull(il, 0);  // encoding
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodEcdhField);
        il.Emit(OpCodes.Ldloc_0);  // encoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSECDHGetPrivateKey);
        il.Emit(OpCodes.Ret);

        // SetPrivateKey(key, encoding?)
        il.MarkLabel(setPrivateKeyLabel);
        EmitGetArgOrNull(il, 0);  // key
        EmitGetArgOrNull(il, 1);  // encoding
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _boundECDHMethodEcdhField);
        il.Emit(OpCodes.Ldloc_0);  // key
        il.Emit(OpCodes.Ldloc_1);  // encoding
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSECDHSetPrivateKey);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Default - return null
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        _boundECDHMethodTypeBuilder.CreateType();
    }

    /// <summary>
    /// Helper to emit code that gets an argument from args array or null if index is out of bounds.
    /// Stores result in a new local.
    /// </summary>
    private void EmitGetArgOrNull(ILGenerator il, int index)
    {
        var local = il.DeclareLocal(_types.Object);
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (args == null || args.Length <= index) goto null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ble, nullLabel);

        // Get args[index]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, index);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, local);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, local);

        il.MarkLabel(doneLabel);
    }
}
