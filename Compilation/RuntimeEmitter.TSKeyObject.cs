using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $TSKeyObject class for standalone DLLs.
/// This replaces SharpTSKeyObject for compiled mode without SharpTS.dll dependency.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $TSKeyObject class.
    /// Must be called before other crypto methods that use it.
    /// </summary>
    private void EmitTSKeyObjectClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $TSKeyObject
        var typeBuilder = moduleBuilder.DefineType(
            "$TSKeyObject",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        // private int _type; // 0=Secret, 1=Public, 2=Private
        var typeField = typeBuilder.DefineField("_type", _types.Int32, FieldAttributes.Private);
        // private int _asymKeyType; // 0=None, 1=Rsa, 2=Ec
        var asymKeyTypeField = typeBuilder.DefineField("_asymKeyType", _types.Int32, FieldAttributes.Private);
        // private byte[] _symmetricKey;
        var symmetricKeyField = typeBuilder.DefineField("_symmetricKey", _types.ByteArray, FieldAttributes.Private);
        // private RSA _rsaKey;
        var rsaKeyField = typeBuilder.DefineField("_rsaKey", typeof(RSA), FieldAttributes.Private);
        // private ECDsa _ecdsaKey;
        var ecdsaKeyField = typeBuilder.DefineField("_ecdsaKey", typeof(ECDsa), FieldAttributes.Private);
        // private string _originalPem;
        var originalPemField = typeBuilder.DefineField("_originalPem", _types.String, FieldAttributes.Private);

        // Constructor for secret keys: public $TSKeyObject(byte[] key)
        var secretCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ByteArray]);
        var secretCtorIL = secretCtor.GetILGenerator();
        // base()
        secretCtorIL.Emit(OpCodes.Ldarg_0);
        secretCtorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // _type = 0 (Secret)
        secretCtorIL.Emit(OpCodes.Ldarg_0);
        secretCtorIL.Emit(OpCodes.Ldc_I4_0);
        secretCtorIL.Emit(OpCodes.Stfld, typeField);
        // _asymKeyType = 0 (None)
        secretCtorIL.Emit(OpCodes.Ldarg_0);
        secretCtorIL.Emit(OpCodes.Ldc_I4_0);
        secretCtorIL.Emit(OpCodes.Stfld, asymKeyTypeField);
        // _symmetricKey = key
        secretCtorIL.Emit(OpCodes.Ldarg_0);
        secretCtorIL.Emit(OpCodes.Ldarg_1);
        secretCtorIL.Emit(OpCodes.Stfld, symmetricKeyField);
        secretCtorIL.Emit(OpCodes.Ret);

        runtime.TSKeyObjectCtorSecret = secretCtor;

        // Constructor for asymmetric keys: public $TSKeyObject(string pem, bool isPrivate)
        var asymCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.Boolean]);
        var asymCtorIL = asymCtor.GetILGenerator();

        // base()
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _type = isPrivate ? 2 : 1
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldarg_2);
        var publicLabel = asymCtorIL.DefineLabel();
        var typeSetLabel = asymCtorIL.DefineLabel();
        asymCtorIL.Emit(OpCodes.Brfalse, publicLabel);
        asymCtorIL.Emit(OpCodes.Ldc_I4_2); // Private
        asymCtorIL.Emit(OpCodes.Br, typeSetLabel);
        asymCtorIL.MarkLabel(publicLabel);
        asymCtorIL.Emit(OpCodes.Ldc_I4_1); // Public
        asymCtorIL.MarkLabel(typeSetLabel);
        asymCtorIL.Emit(OpCodes.Stfld, typeField);

        // _originalPem = pem
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Stfld, originalPemField);

        // Detect key type from PEM using explicit markers
        // Check for explicit RSA markers
        var isExplicitRsaLocal = asymCtorIL.DeclareLocal(_types.Boolean);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Ldstr, "RSA PRIVATE KEY");
        asymCtorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Ldstr, "RSA PUBLIC KEY");
        asymCtorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        asymCtorIL.Emit(OpCodes.Or);
        asymCtorIL.Emit(OpCodes.Stloc, isExplicitRsaLocal);

        // Check for explicit EC markers
        var isExplicitEcLocal = asymCtorIL.DeclareLocal(_types.Boolean);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Ldstr, "EC PRIVATE KEY");
        asymCtorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Ldstr, "EC PUBLIC KEY");
        asymCtorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", [_types.String])!);
        asymCtorIL.Emit(OpCodes.Or);
        asymCtorIL.Emit(OpCodes.Stloc, isExplicitEcLocal);

        var tryRsaExplicitLabel = asymCtorIL.DefineLabel();
        var tryEcExplicitLabel = asymCtorIL.DefineLabel();
        var tryGenericLabel = asymCtorIL.DefineLabel();
        var doneLabel = asymCtorIL.DefineLabel();

        // If explicit RSA → RSA path (no try/catch needed)
        asymCtorIL.Emit(OpCodes.Ldloc, isExplicitRsaLocal);
        asymCtorIL.Emit(OpCodes.Brtrue, tryRsaExplicitLabel);

        // If explicit EC → EC path (no try/catch needed)
        asymCtorIL.Emit(OpCodes.Ldloc, isExplicitEcLocal);
        asymCtorIL.Emit(OpCodes.Brtrue, tryEcExplicitLabel);

        // Generic format - try RSA first, then EC with try/catch
        asymCtorIL.Emit(OpCodes.Br, tryGenericLabel);

        // Explicit RSA key path
        asymCtorIL.MarkLabel(tryRsaExplicitLabel);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        asymCtorIL.Emit(OpCodes.Stfld, rsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldfld, rsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        asymCtorIL.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldc_I4_1);
        asymCtorIL.Emit(OpCodes.Stfld, asymKeyTypeField);
        asymCtorIL.Emit(OpCodes.Br, doneLabel);

        // Explicit EC key path
        asymCtorIL.MarkLabel(tryEcExplicitLabel);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        asymCtorIL.Emit(OpCodes.Stfld, ecdsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldfld, ecdsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        asymCtorIL.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldc_I4_2);
        asymCtorIL.Emit(OpCodes.Stfld, asymKeyTypeField);
        asymCtorIL.Emit(OpCodes.Br, doneLabel);

        // Generic format - try RSA first with try/catch
        asymCtorIL.MarkLabel(tryGenericLabel);
        var tryRsaEndLabel = asymCtorIL.DefineLabel();

        // try { RSA import }
        asymCtorIL.BeginExceptionBlock();
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        asymCtorIL.Emit(OpCodes.Stfld, rsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldfld, rsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        asymCtorIL.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldc_I4_1);
        asymCtorIL.Emit(OpCodes.Stfld, asymKeyTypeField);
        asymCtorIL.Emit(OpCodes.Leave, doneLabel);

        // catch (CryptographicException) { try EC }
        asymCtorIL.BeginCatchBlock(typeof(CryptographicException));
        asymCtorIL.Emit(OpCodes.Pop); // Discard exception
        // Clear the failed RSA key
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldnull);
        asymCtorIL.Emit(OpCodes.Stfld, rsaKeyField);
        // Try EC
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        asymCtorIL.Emit(OpCodes.Stfld, ecdsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldfld, ecdsaKeyField);
        asymCtorIL.Emit(OpCodes.Ldarg_1);
        asymCtorIL.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod("AsSpan", [typeof(string)])!);
        asymCtorIL.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportFromPem", [typeof(ReadOnlySpan<char>)])!);
        asymCtorIL.Emit(OpCodes.Ldarg_0);
        asymCtorIL.Emit(OpCodes.Ldc_I4_2);
        asymCtorIL.Emit(OpCodes.Stfld, asymKeyTypeField);
        asymCtorIL.Emit(OpCodes.Leave, doneLabel);
        asymCtorIL.EndExceptionBlock();

        asymCtorIL.MarkLabel(doneLabel);
        asymCtorIL.Emit(OpCodes.Ret);

        runtime.TSKeyObjectCtorAsym = asymCtor;

        // Property: public string type { get; }
        EmitTSKeyObjectTypeProperty(typeBuilder, typeField);

        // Property: public int symmetricKeySize { get; }
        EmitTSKeyObjectSymmetricKeySizeProperty(typeBuilder, symmetricKeyField);

        // Property: public string? asymmetricKeyType { get; }
        EmitTSKeyObjectAsymmetricKeyTypeProperty(typeBuilder, asymKeyTypeField);

        // Property: public object? asymmetricKeyDetails { get; }
        EmitTSKeyObjectAsymmetricKeyDetailsProperty(typeBuilder, asymKeyTypeField, rsaKeyField, ecdsaKeyField);

        // Method: public object export(object? options = null)
        EmitTSKeyObjectExportMethod(typeBuilder, typeField, symmetricKeyField, rsaKeyField, ecdsaKeyField, runtime);

        // Store the type but don't create it yet - will be done after methods are defined
        _ = typeBuilder.CreateType()!;
    }

    private void EmitTSKeyObjectTypeProperty(TypeBuilder typeBuilder, FieldBuilder typeField)
    {
        var prop = typeBuilder.DefineProperty("type", PropertyAttributes.None, _types.String, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_Type",  // PascalCase for reflection lookup in GetFieldsProperty
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();

        // switch (_type) { 0 => "secret", 1 => "public", 2 => "private" }
        var secretLabel = il.DefineLabel();
        var publicLabel = il.DefineLabel();
        var privateLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Switch, [secretLabel, publicLabel, privateLabel]);
        // default (shouldn't happen)
        il.Emit(OpCodes.Ldstr, "unknown");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(secretLabel);
        il.Emit(OpCodes.Ldstr, "secret");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(publicLabel);
        il.Emit(OpCodes.Ldstr, "public");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(privateLabel);
        il.Emit(OpCodes.Ldstr, "private");
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSKeyObjectSymmetricKeySizeProperty(TypeBuilder typeBuilder, FieldBuilder symmetricKeyField)
    {
        var prop = typeBuilder.DefineProperty("symmetricKeySize", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_SymmetricKeySize",  // PascalCase for reflection lookup in GetFieldsProperty
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();

        // if (_symmetricKey == null) return null;
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, symmetricKeyField);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // return (object)(double)_symmetricKey.Length;  // Box as double for JS number semantics
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, symmetricKeyField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);  // Convert to double for JavaScript number semantics
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSKeyObjectAsymmetricKeyTypeProperty(TypeBuilder typeBuilder, FieldBuilder asymKeyTypeField)
    {
        var prop = typeBuilder.DefineProperty("asymmetricKeyType", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_AsymmetricKeyType",  // PascalCase for reflection lookup in GetFieldsProperty
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();

        // switch (_asymKeyType) { 0 => null, 1 => "rsa", 2 => "ec" }
        var noneLabel = il.DefineLabel();
        var rsaLabel = il.DefineLabel();
        var ecLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, asymKeyTypeField);
        il.Emit(OpCodes.Switch, [noneLabel, rsaLabel, ecLabel]);
        // default
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rsaLabel);
        il.Emit(OpCodes.Ldstr, "rsa");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ecLabel);
        il.Emit(OpCodes.Ldstr, "ec");
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSKeyObjectAsymmetricKeyDetailsProperty(
        TypeBuilder typeBuilder,
        FieldBuilder asymKeyTypeField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField)
    {
        var prop = typeBuilder.DefineProperty("asymmetricKeyDetails", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = typeBuilder.DefineMethod(
            "get_AsymmetricKeyDetails",  // PascalCase for reflection lookup in GetFieldsProperty
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();

        // if (_asymKeyType == 0) return null;
        var notNoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, asymKeyTypeField);
        il.Emit(OpCodes.Brtrue, notNoneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNoneLabel);

        // Create dictionary for details
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Check if RSA
        var ecCheckLabel = il.DefineLabel();
        var createObjectLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, asymKeyTypeField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, ecCheckLabel);

        // RSA: Get modulusLength - box as double for JS number semantics
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "modulusLength");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetProperty("KeySize")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);  // Convert to double for JS number semantics
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        // RSA: Get publicExponent from ExportParameters(false).Exponent
        // Exponent is big-endian byte[]; convert to double via loop: result = result * 256 + byte[i]
        var rsaParamsLocal = il.DeclareLocal(typeof(RSAParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Ldc_I4_0); // includePrivateParameters = false
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ExportParameters", [typeof(bool)])!);
        il.Emit(OpCodes.Stloc, rsaParamsLocal);

        var exponentLocal = il.DeclareLocal(typeof(byte[]));
        il.Emit(OpCodes.Ldloca, rsaParamsLocal);
        il.Emit(OpCodes.Ldfld, typeof(RSAParameters).GetField("Exponent")!);
        il.Emit(OpCodes.Stloc, exponentLocal);

        var expResultLocal = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, expResultLocal);

        var expIdxLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, expIdxLocal);

        var expLoopBody = il.DefineLabel();
        var expLoopCheck = il.DefineLabel();
        il.Emit(OpCodes.Br, expLoopCheck);

        il.MarkLabel(expLoopBody);
        il.Emit(OpCodes.Ldloc, expResultLocal);
        il.Emit(OpCodes.Ldc_R8, 256.0);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, exponentLocal);
        il.Emit(OpCodes.Ldloc, expIdxLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, expResultLocal);
        il.Emit(OpCodes.Ldloc, expIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, expIdxLocal);

        il.MarkLabel(expLoopCheck);
        il.Emit(OpCodes.Ldloc, expIdxLocal);
        il.Emit(OpCodes.Ldloc, exponentLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, expLoopBody);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "publicExponent");
        il.Emit(OpCodes.Ldloc, expResultLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Br, createObjectLabel);

        il.MarkLabel(ecCheckLabel);
        // EC: namedCurve = NodeCurveName(ecdsaKey.ExportParameters(false).Curve.Oid.FriendlyName) (#1060)
        var ecParamsLocal = il.DeclareLocal(typeof(ECParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, ecParamsLocal);

        var friendlyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, ecParamsLocal);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField("Curve")!);
        il.Emit(OpCodes.Call, typeof(ECCurve).GetProperty("Oid")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(System.Security.Cryptography.Oid).GetProperty("FriendlyName")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, friendlyLocal);

        // namedLocal defaults to friendly; remap the three NIST spellings to Node names.
        var namedLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, friendlyLocal);
        il.Emit(OpCodes.Stloc, namedLocal);

        void RemapCurve(string a, string b, string nodeName)
        {
            var hitLabel = il.DefineLabel();
            var afterLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, friendlyLocal);
            il.Emit(OpCodes.Ldstr, a);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, hitLabel);
            il.Emit(OpCodes.Ldloc, friendlyLocal);
            il.Emit(OpCodes.Ldstr, b);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, afterLabel);
            il.MarkLabel(hitLabel);
            il.Emit(OpCodes.Ldstr, nodeName);
            il.Emit(OpCodes.Stloc, namedLocal);
            il.MarkLabel(afterLabel);
        }
        RemapCurve("nistP256", "ECDSA_P256", "prime256v1");
        RemapCurve("nistP384", "ECDSA_P384", "secp384r1");
        RemapCurve("nistP521", "ECDSA_P521", "secp521r1");

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "namedCurve");
        il.Emit(OpCodes.Ldloc, namedLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.MarkLabel(createObjectLabel);
        // Return the dictionary directly (will work with property access)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSKeyObjectExportMethod(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder symmetricKeyField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "export",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();

        // if (_type == 0) return new $Buffer(_symmetricKey)
        var notSecretLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Brtrue, notSecretLabel);

        // Return Buffer from symmetric key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, symmetricKeyField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSecretLabel);

        // For asymmetric keys, export as PEM
        // Check if RSA
        var ecExportLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Brfalse, ecExportLabel);

        // RSA export
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        var rsaPrivateLabel = il.DefineLabel();
        var rsaDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, rsaPrivateLabel);

        // Public key export
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rsaPrivateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ExportPkcs8PrivateKeyPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        // EC export
        il.MarkLabel(ecExportLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        var ecPrivateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, ecPrivateLabel);

        // Public key export
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ecPrivateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportPkcs8PrivateKeyPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }
}
