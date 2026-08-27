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
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
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
        asymCtorIL.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
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
        asymCtorIL.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
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
        asymCtorIL.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
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
        asymCtorIL.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
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

        // Constructors used by JWK/DER import and public-key derivation.
        EmitTSKeyObjectManagedConstructors(
            typeBuilder, typeField, asymKeyTypeField, rsaKeyField, ecdsaKeyField, runtime);

        var getOption = EmitTSKeyObjectGetOption(typeBuilder, runtime);
        runtime.TSKeyObjectGetOption = getOption;
        var base64UrlEncode = EmitTSKeyObjectBase64UrlEncode(typeBuilder);
        var base64UrlDecode = EmitTSKeyObjectBase64UrlDecode(typeBuilder);

        // Property: public string type { get; }
        EmitTSKeyObjectTypeProperty(typeBuilder, typeField);

        // Property: public int symmetricKeySize { get; }
        EmitTSKeyObjectSymmetricKeySizeProperty(typeBuilder, symmetricKeyField);

        // Property: public string? asymmetricKeyType { get; }
        EmitTSKeyObjectAsymmetricKeyTypeProperty(typeBuilder, asymKeyTypeField);

        // Property: public object? asymmetricKeyDetails { get; }
        EmitTSKeyObjectAsymmetricKeyDetailsProperty(typeBuilder, asymKeyTypeField, rsaKeyField, ecdsaKeyField);

        // Method: public object export(object? options = null)
        var exportJwk = EmitTSKeyObjectExportJwk(
            typeBuilder, typeField, symmetricKeyField, rsaKeyField,
            ecdsaKeyField, base64UrlEncode);
        EmitTSKeyObjectExportMethod(
            typeBuilder, typeField, symmetricKeyField, rsaKeyField,
            ecdsaKeyField, runtime, getOption, exportJwk);
        EmitTSKeyObjectEquals(
            typeBuilder, typeField, asymKeyTypeField, symmetricKeyField,
            rsaKeyField, ecdsaKeyField);
        EmitTSKeyObjectToPublicKey(
            typeBuilder, typeField, rsaKeyField, ecdsaKeyField, runtime);
        EmitTSKeyObjectDeriveSecret(
            typeBuilder, typeField, ecdsaKeyField, runtime);
        EmitTSKeyObjectImportJwk(
            typeBuilder, runtime, getOption, base64UrlDecode);
        EmitTSKeyObjectImportDer(typeBuilder, runtime);

        // Store the type but don't create it yet - will be done after methods are defined
        _ = typeBuilder.CreateType()!;
    }

    private void EmitTSKeyObjectManagedConstructors(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder asymKeyTypeField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField,
        EmittedRuntime runtime)
    {
        void EmitCtor(
            Type keyType,
            FieldBuilder keyField,
            int asymType,
            Action<ConstructorBuilder> assign)
        {
            var ctor = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                [keyType, _types.Boolean]);
            assign(ctor);
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            var publicKey = il.DefineLabel();
            var haveType = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, publicKey);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Br, haveType);
            il.MarkLabel(publicKey);
            il.Emit(OpCodes.Ldc_I4_1);
            il.MarkLabel(haveType);
            il.Emit(OpCodes.Stfld, typeField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, asymType);
            il.Emit(OpCodes.Stfld, asymKeyTypeField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, keyField);
            il.Emit(OpCodes.Ret);
        }

        EmitCtor(typeof(RSA), rsaKeyField, 1,
            ctor => runtime.TSKeyObjectCtorRsa = ctor);
        EmitCtor(typeof(ECDsa), ecdsaKeyField, 2,
            ctor => runtime.TSKeyObjectCtorEc = ctor);
    }

    /// <summary>Reads a string-keyed option from emitted objects/dictionaries.</summary>
    private MethodBuilder EmitTSKeyObjectGetOption(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetOption",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]);
        var il = method.GetILGenerator();
        var dict = il.DeclareLocal(_types.DictionaryStringObject);
        var value = il.DeclareLocal(_types.Object);
        var tryDict = il.DefineLabel();
        var missing = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, tryDict);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tryDict);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dict);
        il.Emit(OpCodes.Ldloc, dict);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, dict);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(missing);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitTSKeyObjectBase64UrlEncode(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "Base64UrlEncode",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.ByteArray]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod(
            nameof(Convert.ToBase64String), [_types.ByteArray])!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Ldc_I4, (int)'_');
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitTSKeyObjectBase64UrlDecode(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "Base64UrlDecode",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ByteArray,
            [_types.String]);
        var il = method.GetILGenerator();
        var text = il.DeclareLocal(_types.String);
        var remainder = il.DeclareLocal(_types.Int32);
        var decode = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ldc_I4, (int)'_');
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Stloc, text);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, remainder);
        il.Emit(OpCodes.Ldloc, remainder);
        il.Emit(OpCodes.Brfalse, decode);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ldloc, remainder);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "PadRight", [_types.Int32, typeof(char)])!);
        il.Emit(OpCodes.Stloc, text);
        il.MarkLabel(decode);
        il.Emit(OpCodes.Ldloc, text);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod(
            nameof(Convert.FromBase64String), [_types.String])!);
        il.Emit(OpCodes.Ret);
        return method;
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

    private MethodBuilder EmitTSKeyObjectExportJwk(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder symmetricKeyField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField,
        MethodBuilder base64UrlEncode)
    {
        var method = typeBuilder.DefineMethod(
            "ExportJwk",
            MethodAttributes.Private,
            _types.Object,
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        var fields = il.DeclareLocal(_types.DictionaryStringObject);
        var notSecret = il.DefineLabel();
        var ec = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Newobj,
            _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, fields);

        void AddString(string name, Action loadValue)
        {
            il.Emit(OpCodes.Ldloc, fields);
            il.Emit(OpCodes.Ldstr, name);
            loadValue();
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Brtrue, notSecret);
        AddString("kty", () => il.Emit(OpCodes.Ldstr, "oct"));
        AddString("k", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, symmetricKeyField);
            il.Emit(OpCodes.Call, base64UrlEncode);
        });
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notSecret);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Brfalse, ec);

        var rsa = il.DeclareLocal(typeof(RSAParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, rsa);
        AddString("kty", () => il.Emit(OpCodes.Ldstr, "RSA"));
        void AddRsa(string name, string fieldName)
        {
            AddString(name, () =>
            {
                il.Emit(OpCodes.Ldloca, rsa);
                il.Emit(OpCodes.Ldfld, typeof(RSAParameters).GetField(fieldName)!);
                il.Emit(OpCodes.Call, base64UrlEncode);
            });
        }
        AddRsa("n", nameof(RSAParameters.Modulus));
        AddRsa("e", nameof(RSAParameters.Exponent));
        var rsaPublic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, rsaPublic);
        AddRsa("d", nameof(RSAParameters.D));
        AddRsa("p", nameof(RSAParameters.P));
        AddRsa("q", nameof(RSAParameters.Q));
        AddRsa("dp", nameof(RSAParameters.DP));
        AddRsa("dq", nameof(RSAParameters.DQ));
        AddRsa("qi", nameof(RSAParameters.InverseQ));
        il.MarkLabel(rsaPublic);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(ec);
        var ecParams = il.DeclareLocal(typeof(ECParameters));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Stloc, ecParams);
        AddString("kty", () => il.Emit(OpCodes.Ldstr, "EC"));

        // Map the three supported NIST curves to their JWK names.
        var friendly = il.DeclareLocal(_types.String);
        var curveName = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, ecParams);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField(nameof(ECParameters.Curve))!);
        il.Emit(OpCodes.Call, typeof(ECCurve).GetProperty(nameof(ECCurve.Oid))!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt,
            typeof(Oid).GetProperty(nameof(Oid.FriendlyName))!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, friendly);
        il.Emit(OpCodes.Ldstr, "P-521");
        il.Emit(OpCodes.Stloc, curveName);
        foreach (var (friendlyName, jwkName) in new[]
        {
            ("nistP256", "P-256"), ("ECDSA_P256", "P-256"),
            ("nistP384", "P-384"), ("ECDSA_P384", "P-384")
        })
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, friendly);
            il.Emit(OpCodes.Ldstr, friendlyName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, jwkName);
            il.Emit(OpCodes.Stloc, curveName);
            il.MarkLabel(next);
        }
        AddString("crv", () => il.Emit(OpCodes.Ldloc, curveName));
        void AddEcPoint(string name, string fieldName)
        {
            AddString(name, () =>
            {
                il.Emit(OpCodes.Ldloca, ecParams);
                il.Emit(OpCodes.Ldflda,
                    typeof(ECParameters).GetField(nameof(ECParameters.Q))!);
                il.Emit(OpCodes.Ldfld, typeof(ECPoint).GetField(fieldName)!);
                il.Emit(OpCodes.Call, base64UrlEncode);
            });
        }
        AddEcPoint("x", nameof(ECPoint.X));
        AddEcPoint("y", nameof(ECPoint.Y));
        var ecPublic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, ecPublic);
        AddString("d", () =>
        {
            il.Emit(OpCodes.Ldloca, ecParams);
            il.Emit(OpCodes.Ldfld,
                typeof(ECParameters).GetField(nameof(ECParameters.D))!);
            il.Emit(OpCodes.Call, base64UrlEncode);
        });
        il.MarkLabel(ecPublic);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, fields);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitTSKeyObjectEquals(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder asymKeyTypeField,
        FieldBuilder symmetricKeyField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            [_types.Object]);
        var il = method.GetILGenerator();
        var other = il.DeclareLocal(typeBuilder);
        var compare = il.DefineLabel();
        var secret = il.DefineLabel();
        var ec = il.DefineLabel();
        var returnFalse = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, other);
        il.Emit(OpCodes.Ldloc, other);
        il.Emit(OpCodes.Brfalse, returnFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldloc, other);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Bne_Un, returnFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, asymKeyTypeField);
        il.Emit(OpCodes.Ldloc, other);
        il.Emit(OpCodes.Ldfld, asymKeyTypeField);
        il.Emit(OpCodes.Bne_Un, returnFalse);

        var left = il.DeclareLocal(_types.ByteArray);
        var right = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Brfalse, secret);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Brfalse, ec);
        EmitKeyMaterial(il, typeField, rsaKeyField, typeof(RSA), left, loadOther: false);
        EmitKeyMaterial(il, typeField, rsaKeyField, typeof(RSA), right, loadOther: true, other);
        il.Emit(OpCodes.Br, compare);
        il.MarkLabel(ec);
        EmitKeyMaterial(il, typeField, ecdsaKeyField, typeof(ECDsa), left, loadOther: false);
        EmitKeyMaterial(il, typeField, ecdsaKeyField, typeof(ECDsa), right, loadOther: true, other);
        il.Emit(OpCodes.Br, compare);
        il.MarkLabel(secret);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, symmetricKeyField);
        il.Emit(OpCodes.Stloc, left);
        il.Emit(OpCodes.Ldloc, other);
        il.Emit(OpCodes.Ldfld, symmetricKeyField);
        il.Emit(OpCodes.Stloc, right);

        il.MarkLabel(compare);
        il.Emit(OpCodes.Call,
            typeof(System.Collections.StructuralComparisons).GetProperty(
                nameof(System.Collections.StructuralComparisons.StructuralEqualityComparer))!
                .GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, left);
        il.Emit(OpCodes.Ldloc, right);
        il.Emit(OpCodes.Callvirt,
            typeof(System.Collections.IEqualityComparer).GetMethod(
                nameof(System.Collections.IEqualityComparer.Equals))!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            method, _types.GetMethod(_types.Object, "Equals", [_types.Object])!);

        void EmitKeyMaterial(
            ILGenerator body,
            FieldBuilder keyType,
            FieldBuilder keyField,
            Type algorithmType,
            LocalBuilder target,
            bool loadOther,
            LocalBuilder? otherLocal = null)
        {
            var publicKey = body.DefineLabel();
            var done = body.DefineLabel();
            if (loadOther)
                body.Emit(OpCodes.Ldloc, otherLocal!);
            else
                body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, keyType);
            body.Emit(OpCodes.Ldc_I4_2);
            body.Emit(OpCodes.Bne_Un, publicKey);
            if (loadOther)
                body.Emit(OpCodes.Ldloc, otherLocal!);
            else
                body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, keyField);
            body.Emit(OpCodes.Callvirt,
                _types.GetMethod(algorithmType, "ExportPkcs8PrivateKey", Type.EmptyTypes));
            body.Emit(OpCodes.Br, done);
            body.MarkLabel(publicKey);
            if (loadOther)
                body.Emit(OpCodes.Ldloc, otherLocal!);
            else
                body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, keyField);
            body.Emit(OpCodes.Callvirt,
                _types.GetMethod(algorithmType, "ExportSubjectPublicKeyInfo", Type.EmptyTypes));
            body.MarkLabel(done);
            body.Emit(OpCodes.Stloc, target);
        }
    }

    private void EmitTSKeyObjectToPublicKey(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToPublicKey",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        runtime.TSKeyObjectToPublicKey = method;
        var il = method.GetILGenerator();
        var asymmetric = il.DefineLabel();
        var ec = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Brtrue, asymmetric);
        il.Emit(OpCodes.Ldstr,
            "crypto.createPublicKey: cannot derive a public key from a secret KeyObject");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(asymmetric);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Brfalse, ec);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorAsym);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(ec);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfoPem", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorAsym);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSKeyObjectDeriveSecret(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder ecdsaKeyField,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DeriveSecret",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]);
        runtime.TSKeyObjectDeriveSecret = method;
        var il = method.GetILGenerator();
        var otherLocal = il.DeclareLocal(typeBuilder);
        var privateEcdhLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var publicEcdhLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var secretLocal = il.DeclareLocal(_types.ByteArray);
        var invalid = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeBuilder);
        il.Emit(OpCodes.Stloc, otherLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Brfalse, invalid);

        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(ECDiffieHellman), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, privateEcdhLocal);
        il.Emit(OpCodes.Ldloc, privateEcdhLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod(
            "ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod(
            "ImportParameters", [typeof(ECParameters)])!);

        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(ECDiffieHellman), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, publicEcdhLocal);
        il.Emit(OpCodes.Ldloc, publicEcdhLocal);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod(
            "ExportParameters", [_types.Boolean])!);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod(
            "ImportParameters", [typeof(ECParameters)])!);

        il.Emit(OpCodes.Ldloc, privateEcdhLocal);
        il.Emit(OpCodes.Ldloc, publicEcdhLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetProperty(
            "PublicKey")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod(
            "DeriveRawSecretAgreement", [typeof(ECDiffieHellmanPublicKey)])!);
        il.Emit(OpCodes.Stloc, secretLocal);
        il.Emit(OpCodes.Ldloc, privateEcdhLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, publicEcdhLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldstr,
            "crypto.diffieHellman: privateKey and publicKey must be EC KeyObjects");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitTSKeyObjectImportJwk(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        MethodBuilder getOption,
        MethodBuilder base64UrlDecode)
    {
        var method = typeBuilder.DefineMethod(
            "ImportJwk",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Boolean]);
        runtime.TSKeyObjectImportJwk = method;
        var il = method.GetILGenerator();
        var kty = il.DeclareLocal(_types.String);
        var rsaLabel = il.DefineLabel();
        var ecLabel = il.DefineLabel();
        var octLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "kty");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, kty);

        void BranchKty(string value, Label label)
        {
            il.Emit(OpCodes.Ldloc, kty);
            il.Emit(OpCodes.Ldstr, value);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brtrue, label);
        }
        BranchKty("RSA", rsaLabel);
        BranchKty("EC", ecLabel);
        BranchKty("oct", octLabel);
        il.Emit(OpCodes.Ldstr, "Unsupported JWK key type");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(octLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "k");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, base64UrlDecode);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorSecret);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rsaLabel);
        var rsaParams = il.DeclareLocal(typeof(RSAParameters));
        il.Emit(OpCodes.Ldloca, rsaParams);
        il.Emit(OpCodes.Initobj, typeof(RSAParameters));
        void SetRsa(string jwkName, string fieldName)
        {
            il.Emit(OpCodes.Ldloca, rsaParams);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, jwkName);
            il.Emit(OpCodes.Call, getOption);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, base64UrlDecode);
            il.Emit(OpCodes.Stfld, typeof(RSAParameters).GetField(fieldName)!);
        }
        SetRsa("n", nameof(RSAParameters.Modulus));
        SetRsa("e", nameof(RSAParameters.Exponent));
        var rsaPublic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, rsaPublic);
        SetRsa("d", nameof(RSAParameters.D));
        SetRsa("p", nameof(RSAParameters.P));
        SetRsa("q", nameof(RSAParameters.Q));
        SetRsa("dp", nameof(RSAParameters.DP));
        SetRsa("dq", nameof(RSAParameters.DQ));
        SetRsa("qi", nameof(RSAParameters.InverseQ));
        il.MarkLabel(rsaPublic);
        var rsa = il.DeclareLocal(typeof(RSA));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, rsa);
        il.Emit(OpCodes.Ldloc, rsa);
        il.Emit(OpCodes.Ldloc, rsaParams);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ImportParameters", [typeof(RSAParameters)])!);
        il.Emit(OpCodes.Ldloc, rsa);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorRsa);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ecLabel);
        var ecParams = il.DeclareLocal(typeof(ECParameters));
        var curve = il.DeclareLocal(typeof(ECCurve));
        var crv = il.DeclareLocal(_types.String);
        var p384 = il.DefineLabel();
        var p521 = il.DefineLabel();
        var haveCurve = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "crv");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, crv);
        il.Emit(OpCodes.Ldloc, crv);
        il.Emit(OpCodes.Ldstr, "P-384");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p384);
        il.Emit(OpCodes.Ldloc, crv);
        il.Emit(OpCodes.Ldstr, "P-521");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, p521);
        il.Emit(OpCodes.Call,
            typeof(ECCurve.NamedCurves).GetProperty("nistP256")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curve);
        il.Emit(OpCodes.Br, haveCurve);
        il.MarkLabel(p384);
        il.Emit(OpCodes.Call,
            typeof(ECCurve.NamedCurves).GetProperty("nistP384")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curve);
        il.Emit(OpCodes.Br, haveCurve);
        il.MarkLabel(p521);
        il.Emit(OpCodes.Call,
            typeof(ECCurve.NamedCurves).GetProperty("nistP521")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, curve);
        il.MarkLabel(haveCurve);
        il.Emit(OpCodes.Ldloca, ecParams);
        il.Emit(OpCodes.Initobj, typeof(ECParameters));
        il.Emit(OpCodes.Ldloca, ecParams);
        il.Emit(OpCodes.Ldloc, curve);
        il.Emit(OpCodes.Stfld,
            typeof(ECParameters).GetField(nameof(ECParameters.Curve))!);
        void SetEcPoint(string jwkName, string fieldName)
        {
            il.Emit(OpCodes.Ldloca, ecParams);
            il.Emit(OpCodes.Ldflda,
                typeof(ECParameters).GetField(nameof(ECParameters.Q))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, jwkName);
            il.Emit(OpCodes.Call, getOption);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, base64UrlDecode);
            il.Emit(OpCodes.Stfld, typeof(ECPoint).GetField(fieldName)!);
        }
        SetEcPoint("x", nameof(ECPoint.X));
        SetEcPoint("y", nameof(ECPoint.Y));
        var ecPublic = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, ecPublic);
        il.Emit(OpCodes.Ldloca, ecParams);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "d");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, base64UrlDecode);
        il.Emit(OpCodes.Stfld,
            typeof(ECParameters).GetField(nameof(ECParameters.D))!);
        il.MarkLabel(ecPublic);
        var ecdsa = il.DeclareLocal(typeof(ECDsa));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, ecdsa);
        il.Emit(OpCodes.Ldloc, ecdsa);
        il.Emit(OpCodes.Ldloc, ecParams);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ImportParameters", [typeof(ECParameters)])!);
        il.Emit(OpCodes.Ldloc, ecdsa);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorEc);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSKeyObjectImportDer(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ImportDer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String, _types.Boolean]);
        runtime.TSKeyObjectImportDer = method;
        var il = method.GetILGenerator();
        var bytes = il.DeclareLocal(_types.ByteArray);
        var span = il.DeclareLocal(typeof(ReadOnlySpan<byte>));
        var read = il.DeclareLocal(_types.Int32);
        var result = il.DeclareLocal(_types.Object);
        var notBytes = il.DefineLabel();
        var haveBytes = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, notBytes);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Br, haveBytes);
        il.MarkLabel(notBytes);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        var invalid = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Br, haveBytes);
        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldstr, "DER key must be a Buffer");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(haveBytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Newobj,
            typeof(ReadOnlySpan<byte>).GetConstructor([_types.ByteArray])!);
        il.Emit(OpCodes.Stloc, span);

        var privateKey = il.DefineLabel();
        var publicSpki = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, privateKey);

        // Public pkcs1 is unambiguously RSA.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pkcs1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, publicSpki);
        EmitImportRsa("ImportRSAPublicKey", isPrivate: false);

        // SPKI/default: try RSA, then EC.
        il.MarkLabel(publicSpki);
        var publicDone = il.DefineLabel();
        il.BeginExceptionBlock();
        EmitImportRsa("ImportSubjectPublicKeyInfo", isPrivate: false,
            leaveTarget: publicDone);
        il.BeginCatchBlock(typeof(CryptographicException));
        il.Emit(OpCodes.Pop);
        EmitImportEc("ImportSubjectPublicKeyInfo", isPrivate: false,
            leaveTarget: publicDone);
        il.EndExceptionBlock();
        il.MarkLabel(publicDone);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(privateKey);
        var privatePkcs8 = il.DefineLabel();
        var privateSec1 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "pkcs1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, privateSec1);
        EmitImportRsa("ImportRSAPrivateKey", isPrivate: true);
        il.MarkLabel(privateSec1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "sec1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, privatePkcs8);
        EmitImportEc("ImportECPrivateKey", isPrivate: true);

        il.MarkLabel(privatePkcs8);
        var privateDone = il.DefineLabel();
        il.BeginExceptionBlock();
        EmitImportRsa("ImportPkcs8PrivateKey", isPrivate: true,
            leaveTarget: privateDone);
        il.BeginCatchBlock(typeof(CryptographicException));
        il.Emit(OpCodes.Pop);
        EmitImportEc("ImportPkcs8PrivateKey", isPrivate: true,
            leaveTarget: privateDone);
        il.EndExceptionBlock();
        il.MarkLabel(privateDone);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);

        void EmitImportRsa(
            string importName,
            bool isPrivate,
            Label? leaveTarget = null)
        {
            var rsa = il.DeclareLocal(typeof(RSA));
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes));
            il.Emit(OpCodes.Stloc, rsa);
            il.Emit(OpCodes.Ldloc, rsa);
            il.Emit(OpCodes.Ldloc, span);
            il.Emit(OpCodes.Ldloca, read);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(RSA),
                importName,
                [typeof(ReadOnlySpan<byte>), _types.Int32.MakeByRefType()]));
            il.Emit(OpCodes.Ldloc, rsa);
            il.Emit(isPrivate ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorRsa);
            if (leaveTarget.HasValue)
            {
                il.Emit(OpCodes.Stloc, result);
                il.Emit(OpCodes.Leave, leaveTarget.Value);
            }
            else
                il.Emit(OpCodes.Ret);
        }

        void EmitImportEc(
            string importName,
            bool isPrivate,
            Label? leaveTarget = null)
        {
            var ec = il.DeclareLocal(typeof(ECDsa));
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes));
            il.Emit(OpCodes.Stloc, ec);
            il.Emit(OpCodes.Ldloc, ec);
            il.Emit(OpCodes.Ldloc, span);
            il.Emit(OpCodes.Ldloca, read);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(ECDsa),
                importName,
                [typeof(ReadOnlySpan<byte>), _types.Int32.MakeByRefType()]));
            il.Emit(OpCodes.Ldloc, ec);
            il.Emit(isPrivate ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorEc);
            if (leaveTarget.HasValue)
            {
                il.Emit(OpCodes.Stloc, result);
                il.Emit(OpCodes.Leave, leaveTarget.Value);
            }
            else
                il.Emit(OpCodes.Ret);
        }
    }

    private void EmitTSKeyObjectExportMethod(
        TypeBuilder typeBuilder,
        FieldBuilder typeField,
        FieldBuilder symmetricKeyField,
        FieldBuilder rsaKeyField,
        FieldBuilder ecdsaKeyField,
        EmittedRuntime runtime,
        MethodBuilder getOption,
        MethodBuilder exportJwk)
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

        var format = il.DeclareLocal(_types.String);
        var exportType = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "format");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, format);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "type");
        il.Emit(OpCodes.Call, getOption);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, exportType);

        // format:'jwk' returns a plain object rather than PEM text.
        var derCheck = il.DefineLabel();
        var pemExport = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, format);
        il.Emit(OpCodes.Ldstr, "jwk");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, derCheck);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, exportJwk);
        il.Emit(OpCodes.Ret);

        // format:'der' returns a Buffer and honors pkcs1/spki/pkcs8/sec1.
        il.MarkLabel(derCheck);
        il.Emit(OpCodes.Ldloc, format);
        il.Emit(OpCodes.Ldstr, "der");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, pemExport);
        var derBytes = il.DeclareLocal(_types.ByteArray);
        var derEc = il.DefineLabel();
        var derRsaPrivate = il.DefineLabel();
        var derEcPrivate = il.DefineLabel();
        var derRsaSpki = il.DefineLabel();
        var derEcPkcs8 = il.DefineLabel();
        var derDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Brfalse, derEc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, derRsaPrivate);
        il.Emit(OpCodes.Ldloc, exportType);
        il.Emit(OpCodes.Ldstr, "pkcs1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, derRsaSpki);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportRSAPublicKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);
        il.MarkLabel(derRsaSpki);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);
        il.MarkLabel(derRsaPrivate);
        il.Emit(OpCodes.Ldloc, exportType);
        il.Emit(OpCodes.Ldstr, "pkcs1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, derEcPkcs8);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportRSAPrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);
        il.MarkLabel(derEcPkcs8);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, rsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(RSA).GetMethod("ExportPkcs8PrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);

        il.MarkLabel(derEc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, derEcPrivate);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);
        il.MarkLabel(derEcPrivate);
        var derEcSec1 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, exportType);
        il.Emit(OpCodes.Ldstr, "sec1");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, derEcSec1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ExportPkcs8PrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.Emit(OpCodes.Br, derDone);
        il.MarkLabel(derEcSec1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, ecdsaKeyField);
        il.Emit(OpCodes.Callvirt,
            typeof(ECDsa).GetMethod("ExportECPrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, derBytes);
        il.MarkLabel(derDone);
        il.Emit(OpCodes.Ldloc, derBytes);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(pemExport);

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
