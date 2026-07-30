using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// WebCrypto (#1063) emitted types: $CryptoKey, $SubtleCrypto, $WebCrypto, and the
/// GetWebCryptoObject singleton accessor. Emitted at the tail of EmitCryptoMethods
/// so the Wc* $Runtime helpers (RuntimeEmitter.WebCrypto.cs) and the $Promise /
/// $ArrayBuffer / $TypedArray / $Buffer types already exist. Dynamic dispatch on
/// these instances rides GetFieldsProperty's reflection fallback (public
/// properties + methods), so no per-callsite emitters are needed.
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _cryptoKeyType = null!;
    private ConstructorBuilder _cryptoKeyCtor = null!;
    private FieldBuilder _ckKind = null!;       // "secret" | "public" | "private"
    private FieldBuilder _ckExtractable = null!;
    private FieldBuilder _ckAlgorithm = null!;  // Dictionary<string, object>
    private FieldBuilder _ckUsages = null!;     // caller-provided usages value
    private FieldBuilder _ckMaterial = null!;   // raw secret / PKCS#8 / SPKI bytes
    private FieldBuilder _ckAlgoName = null!;   // UPPER WebCrypto name
    private FieldBuilder _ckHash = null!;       // lowercase digest or null
    private FieldBuilder _ckCurve = null!;      // canonical curve or null

    private ConstructorBuilder _subtleCtor = null!;
    private MethodBuilder _subtleGenerateKeyCore = null!;
    private MethodBuilder _subtleImportKeyCore = null!;
    private MethodBuilder _subtleExportKeyCore = null!;
    private MethodBuilder _subtleEncDecCore = null!;
    private MethodBuilder _subtleSignVerifyCore = null!;
    private MethodBuilder _subtleDeriveBitsCore = null!;

    /// <summary>Entry point: emits the three WebCrypto types + the singleton accessor body.</summary>
    private void EmitWebCryptoTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitCryptoKeyType(moduleBuilder, runtime);
        EmitSubtleCryptoType(moduleBuilder, runtime);
        EmitWebCryptoType(moduleBuilder, runtime);

        // crypto.getRandomValues(x) / named import — uniform module wrapper on $Runtime.
        var wrapper = _runtimeTypeBuilder!.DefineMethod(
            "CryptoWrapper_getRandomValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var wil = wrapper.GetILGenerator();
        wil.Emit(OpCodes.Call, runtime.GetWebCryptoObject);
        wil.Emit(OpCodes.Castclass, _webCryptoType);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Callvirt, _webCryptoGetRandomValues);
        wil.Emit(OpCodes.Ret);
        runtime.RegisterBuiltInModuleMethod("crypto", "getRandomValues", wrapper);
    }

    private TypeBuilder _webCryptoType = null!;
    private MethodBuilder _webCryptoGetRandomValues = null!;

    // ───────────────────────────── $CryptoKey ─────────────────────────────

    private void EmitCryptoKeyType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$CryptoKey",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object);
        _cryptoKeyType = tb;

        _ckKind = tb.DefineField("Kind", _types.String, FieldAttributes.Public);
        _ckExtractable = tb.DefineField("Ext", _types.Boolean, FieldAttributes.Public);
        _ckAlgorithm = tb.DefineField("Algo", _types.Object, FieldAttributes.Public);
        _ckUsages = tb.DefineField("UsagesValue", _types.Object, FieldAttributes.Public);
        _ckMaterial = tb.DefineField("Material", _types.ByteArray, FieldAttributes.Public);
        _ckAlgoName = tb.DefineField("AlgoName", _types.String, FieldAttributes.Public);
        _ckHash = tb.DefineField("HashName", _types.String, FieldAttributes.Public);
        _ckCurve = tb.DefineField("Curve", _types.String, FieldAttributes.Public);

        // ctor(kind, extractable, algorithm, usages, material, algoName, hash, curve)
        _cryptoKeyCtor = tb.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.String, _types.Boolean, _types.Object, _types.Object, _types.ByteArray, _types.String, _types.String, _types.String]);
        var il = _cryptoKeyCtor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        (FieldBuilder Field, int Arg)[] inits =
            [(_ckKind, 1), (_ckExtractable, 2), (_ckAlgorithm, 3), (_ckUsages, 4), (_ckMaterial, 5), (_ckAlgoName, 6), (_ckHash, 7), (_ckCurve, 8)];
        foreach (var (field, arg) in inits)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg, arg);
            il.Emit(OpCodes.Stfld, field);
        }
        il.Emit(OpCodes.Ret);

        // Properties: type, extractable, algorithm, usages
        EmitSimpleGetter(tb, "type", _types.String, gil =>
        {
            gil.Emit(OpCodes.Ldarg_0);
            gil.Emit(OpCodes.Ldfld, _ckKind);
        });
        EmitSimpleGetter(tb, "extractable", _types.Object, gil =>
        {
            gil.Emit(OpCodes.Ldarg_0);
            gil.Emit(OpCodes.Ldfld, _ckExtractable);
            gil.Emit(OpCodes.Box, _types.Boolean);
        });
        EmitSimpleGetter(tb, "algorithm", _types.Object, gil =>
        {
            gil.Emit(OpCodes.Ldarg_0);
            gil.Emit(OpCodes.Ldfld, _ckAlgorithm);
        });
        EmitSimpleGetter(tb, "usages", _types.Object, gil =>
        {
            gil.Emit(OpCodes.Ldarg_0);
            gil.Emit(OpCodes.Ldfld, _ckUsages);
        });

        tb.CreateType();
    }

    /// <summary>Defines a read-only lowercase property with a get_PascalCase getter.</summary>
    private void EmitSimpleGetter(TypeBuilder tb, string name, Type type, Action<ILGenerator> loadValue)
    {
        var prop = tb.DefineProperty(name, PropertyAttributes.None, type, Type.EmptyTypes);
        var getter = tb.DefineMethod(
            "get_" + char.ToUpperInvariant(name[0]) + name[1..],
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            type, Type.EmptyTypes);
        var il = getter.GetILGenerator();
        loadValue(il);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    // ──────────────────────────── $SubtleCrypto ────────────────────────────

    private void EmitSubtleCryptoType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$SubtleCrypto",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object);

        _subtleCtor = tb.DefineDefaultConstructor(MethodAttributes.Public);

        // Static cores first (instance wrappers call them).
        _subtleImportKeyCore = EmitSubtleImportKeyCore(tb, runtime);
        _subtleExportKeyCore = EmitSubtleExportKeyCore(tb, runtime);
        _subtleEncDecCore = EmitSubtleEncDecCore(tb, runtime);
        _subtleSignVerifyCore = EmitSubtleSignVerifyCore(tb, runtime);
        _subtleDeriveBitsCore = EmitSubtleDeriveBitsCore(tb, runtime);
        _subtleGenerateKeyCore = EmitSubtleGenerateKeyCore(tb, runtime);

        EmitSubtlePromiseWrappers(tb, runtime);

        tb.CreateType();
    }

    /// <summary>
    /// Emits a public instance method that computes a RAW result (via emitBody, which
    /// must leave one object on the stack with a single fall-through exit) and returns
    /// it as a resolved $Promise; any exception becomes a REJECTED $Promise (WebCrypto
    /// methods reject rather than throw — this also keeps guest try/catch-around-await
    /// working in compiled async bodies).
    /// </summary>
    private void EmitPromiseMethod(TypeBuilder tb, string name, int paramCount, Action<ILGenerator> emitBody)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++) paramTypes[i] = _types.Object;

        var m = tb.DefineMethod(name, MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, paramTypes);
        var il = m.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var end = il.DefineLabel();

        il.BeginExceptionBlock();
        emitBody(il);
        il.Emit(OpCodes.Call, _wcResolved);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, end);

        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Call, _wcRejected);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, end);
        il.EndExceptionBlock();

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits an if-chain arm: if (local == value) { body }.</summary>
    private void EmitIfEquals(ILGenerator il, LocalBuilder local, string value, Action body)
    {
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, next);
        body();
        il.MarkLabel(next);
    }

    private void EmitThrowMessage(ILGenerator il, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Loads a $CryptoKey field after castclass-ing the object argument.</summary>
    private void EmitLoadKeyField(ILGenerator il, int argIndex, FieldBuilder field)
    {
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Castclass, _cryptoKeyType);
        il.Emit(OpCodes.Ldfld, field);
    }

    private void EmitEnsureCryptoKey(ILGenerator il, int argIndex, string op)
    {
        var ok = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _cryptoKeyType);
        il.Emit(OpCodes.Brtrue, ok);
        EmitThrowMessage(il, $"crypto.subtle.{op}: a CryptoKey is required");
        il.MarkLabel(ok);
    }

    /// <summary>Builds a Dictionary&lt;string,object&gt; local from (name, loadValue) pairs.</summary>
    private LocalBuilder EmitNewDict(ILGenerator il)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);
        return dictLocal;
    }

    private void EmitDictAdd(ILGenerator il, LocalBuilder dict, string key, Action loadValue)
    {
        il.Emit(OpCodes.Ldloc, dict);
        il.Emit(OpCodes.Ldstr, key);
        loadValue();
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
    }

    private void EmitBoxedDouble(ILGenerator il, Action loadInt)
    {
        loadInt();
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
    }

    /// <summary>
    /// The public subtle methods: thin resolved/rejected-$Promise wrappers over the cores.
    /// </summary>
    private void EmitSubtlePromiseWrappers(TypeBuilder tb, EmittedRuntime runtime)
    {
        // digest(algorithm, data)
        EmitPromiseMethod(tb, "digest", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Call, _wcDigest);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
        });

        // generateKey(algorithm, extractable, usages)
        EmitPromiseMethod(tb, "generateKey", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _subtleGenerateKeyCore);
        });

        // importKey(format, keyData, algorithm, extractable, usages)
        EmitPromiseMethod(tb, "importKey", 5, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldarg, 5);
            il.Emit(OpCodes.Call, _subtleImportKeyCore);
        });

        // exportKey(format, key)
        EmitPromiseMethod(tb, "exportKey", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _subtleExportKeyCore);
        });

        // encrypt/decrypt(algorithm, key, data)
        foreach (var (name, encrypt) in new[] { ("encrypt", true), ("decrypt", false) })
        {
            EmitPromiseMethod(tb, name, 3, il =>
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, _wcToBytes);
                il.Emit(encrypt ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _subtleEncDecCore);
                il.Emit(OpCodes.Call, _wcToArrayBuffer);
            });
        }

        // sign(algorithm, key, data)
        EmitPromiseMethod(tb, "sign", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, _subtleSignVerifyCore);
            il.Emit(OpCodes.Castclass, _types.ByteArray);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
        });

        // verify(algorithm, key, signature, data)
        EmitPromiseMethod(tb, "verify", 4, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Call, _subtleSignVerifyCore);
        });

        // deriveBits(algorithm, baseKey, length)
        EmitPromiseMethod(tb, "deriveBits", 3, il =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Call, _wcIntParam);
            il.Emit(OpCodes.Call, _subtleDeriveBitsCore);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
        });

        // deriveKey(algorithm, baseKey, derivedKeyType, extractable, usages)
        EmitPromiseMethod(tb, "deriveKey", 5, il => EmitDeriveKeyBody(il));

        // wrapKey(format, key, wrappingKey, wrapAlgo)
        EmitPromiseMethod(tb, "wrapKey", 4, il =>
        {
            il.Emit(OpCodes.Ldarg, 4); // wrapAlgo
            il.Emit(OpCodes.Ldarg_3);  // wrappingKey
            il.Emit(OpCodes.Ldarg_1);  // format
            il.Emit(OpCodes.Ldarg_2);  // key
            il.Emit(OpCodes.Call, _subtleExportKeyCore);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Ldc_I4_1); // encrypt
            il.Emit(OpCodes.Call, _subtleEncDecCore);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
        });

        // unwrapKey(format, wrapped, unwrappingKey, unwrapAlgo, unwrappedKeyAlgo, extractable, usages)
        EmitPromiseMethod(tb, "unwrapKey", 7, il =>
        {
            var ptLocal = il.DeclareLocal(_types.ByteArray);
            il.Emit(OpCodes.Ldarg, 4);  // unwrapAlgo
            il.Emit(OpCodes.Ldarg_3);   // unwrappingKey
            il.Emit(OpCodes.Ldarg_2);   // wrappedKey
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Ldc_I4_0);  // decrypt
            il.Emit(OpCodes.Call, _subtleEncDecCore);
            il.Emit(OpCodes.Stloc, ptLocal);

            il.Emit(OpCodes.Ldarg_1);   // format
            il.Emit(OpCodes.Ldloc, ptLocal);
            il.Emit(OpCodes.Ldarg, 5);  // unwrappedKeyAlgo
            il.Emit(OpCodes.Ldarg, 6);  // extractable
            il.Emit(OpCodes.Ldarg, 7);  // usages
            il.Emit(OpCodes.Call, _subtleImportKeyCore);
        });
    }

    // static object GenerateKeyCore(object algorithm, object extractable, object usages)
    private MethodBuilder EmitSubtleGenerateKeyCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("GenerateKeyCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object]);
        var il = m.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        var extLocal = il.DeclareLocal(_types.Boolean);
        var hashLocal = il.DeclareLocal(_types.String);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var materialLocal = il.DeclareLocal(_types.ByteArray);
        var pairLocal = il.DeclareLocal(typeof(object[]));
        var curveLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, nameLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, extLocal);

        // AES-GCM / AES-CBC
        foreach (var aes in new[] { "AES-GCM", "AES-CBC" })
        {
            EmitIfEquals(il, nameLocal, aes, () =>
            {
                var lenOk = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, "length");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Call, _wcIntParam);
                il.Emit(OpCodes.Stloc, lenLocal);

                foreach (var valid in new[] { 128, 192, 256 })
                {
                    il.Emit(OpCodes.Ldloc, lenLocal);
                    il.Emit(OpCodes.Ldc_I4, valid);
                    il.Emit(OpCodes.Beq, lenOk);
                }
                EmitThrowMessage(il, "crypto.subtle.generateKey: AES key length must be 128, 192, or 256");
                il.MarkLabel(lenOk);

                il.Emit(OpCodes.Ldloc, lenLocal);
                il.Emit(OpCodes.Ldc_I4_8);
                il.Emit(OpCodes.Div);
                il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
                il.Emit(OpCodes.Stloc, materialLocal);

                var algDict = EmitNewDict(il);
                EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, aes));
                EmitDictAdd(il, algDict, "length", () => EmitBoxedDouble(il, () => il.Emit(OpCodes.Ldloc, lenLocal)));

                // new $CryptoKey("secret", ext, alg, usages, material, name, null, null)
                il.Emit(OpCodes.Ldstr, "secret");
                il.Emit(OpCodes.Ldloc, extLocal);
                il.Emit(OpCodes.Ldloc, algDict);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloc, materialLocal);
                il.Emit(OpCodes.Ldstr, aes);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
                il.Emit(OpCodes.Ret);
            });
        }

        // HMAC
        EmitIfEquals(il, nameLocal, "HMAC", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "hash");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Stloc, hashLocal);

            // default length: sha384/sha512 → 1024, else 512
            var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
            var use1024 = il.DefineLabel();
            var haveDefault = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldstr, "sha384");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, use1024);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldstr, "sha512");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, use1024);
            il.Emit(OpCodes.Ldc_I4, 512);
            il.Emit(OpCodes.Br, haveDefault);
            il.MarkLabel(use1024);
            il.Emit(OpCodes.Ldc_I4, 1024);
            il.MarkLabel(haveDefault);
            il.Emit(OpCodes.Stloc, lenLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _wcIntParam);
            il.Emit(OpCodes.Stloc, lenLocal);

            var lenOk = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Blt, DefineThrowTag(il, out var badLen));
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Rem);
            il.Emit(OpCodes.Brtrue, badLen);
            il.Emit(OpCodes.Br, lenOk);
            il.MarkLabel(badLen);
            EmitThrowMessage(il, "crypto.subtle.generateKey: HMAC length must be a positive multiple of 8");
            il.MarkLabel(lenOk);

            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Ldc_I4_8);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
            il.Emit(OpCodes.Stloc, materialLocal);

            var hashDict = EmitNewDict(il);
            EmitDictAdd(il, hashDict, "name", () => EmitWebHashName(il, hashLocal));
            var algDict = EmitNewDict(il);
            EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, "HMAC"));
            EmitDictAdd(il, algDict, "hash", () => il.Emit(OpCodes.Ldloc, hashDict));
            EmitDictAdd(il, algDict, "length", () => EmitBoxedDouble(il, () => il.Emit(OpCodes.Ldloc, lenLocal)));

            il.Emit(OpCodes.Ldstr, "secret");
            il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Ldloc, algDict);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, materialLocal);
            il.Emit(OpCodes.Ldstr, "HMAC");
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
            il.Emit(OpCodes.Ret);
        });

        // RSA-OAEP / RSASSA-PKCS1-V1_5 / RSA-PSS
        foreach (var rsa in new[] { "RSA-OAEP", "RSASSA-PKCS1-V1_5", "RSA-PSS" })
        {
            EmitIfEquals(il, nameLocal, rsa, () =>
            {
                var canonical = rsa == "RSASSA-PKCS1-V1_5" ? "RSASSA-PKCS1-v1_5" : rsa;

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, "modulusLength");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Ldc_I4_M1);
                il.Emit(OpCodes.Call, _wcIntParam);
                il.Emit(OpCodes.Stloc, lenLocal);

                var modOk = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, lenLocal);
                il.Emit(OpCodes.Ldc_I4, 256);
                il.Emit(OpCodes.Bge, modOk);
                EmitThrowMessage(il, "crypto.subtle.generateKey: RSA requires modulusLength");
                il.MarkLabel(modOk);

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, "hash");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Call, _wcMapHash);
                il.Emit(OpCodes.Stloc, hashLocal);

                il.Emit(OpCodes.Ldloc, lenLocal);
                il.Emit(OpCodes.Call, _wcGenRsa);
                il.Emit(OpCodes.Stloc, pairLocal);

                var hashDict = EmitNewDict(il);
                EmitDictAdd(il, hashDict, "name", () => EmitWebHashName(il, hashLocal));
                var algDict = EmitNewDict(il);
                EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, canonical));
                EmitDictAdd(il, algDict, "modulusLength", () => EmitBoxedDouble(il, () => il.Emit(OpCodes.Ldloc, lenLocal)));
                EmitDictAdd(il, algDict, "publicExponent", () =>
                {
                    il.Emit(OpCodes.Ldc_R8, 65537d);
                    il.Emit(OpCodes.Box, _types.Double);
                });
                EmitDictAdd(il, algDict, "hash", () => il.Emit(OpCodes.Ldloc, hashDict));

                EmitKeyPairResult(il, runtime, rsa, algDict, pairLocal, extLocal, hashLocal, curveNull: true);
            });
        }

        // ECDSA / ECDH
        foreach (var ec in new[] { "ECDSA", "ECDH" })
        {
            EmitIfEquals(il, nameLocal, ec, () =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, "namedCurve");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Call, _wcCanonicalCurve);
                il.Emit(OpCodes.Stloc, curveLocal);

                il.Emit(OpCodes.Ldloc, curveLocal);
                il.Emit(OpCodes.Call, _wcGenEc);
                il.Emit(OpCodes.Stloc, pairLocal);

                var algDict = EmitNewDict(il);
                EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, ec));
                EmitDictAdd(il, algDict, "namedCurve", () => il.Emit(OpCodes.Ldloc, curveLocal));

                EmitKeyPairResult(il, runtime, ec, algDict, pairLocal, extLocal, hashLocal: null, curveNull: false, curveLocal);
            });
        }

        // AES-CTR / Ed25519 / X25519 / everything else
        il.Emit(OpCodes.Ldstr, "crypto.subtle.generateKey: unsupported algorithm '");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' on this runtime");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        return m;
    }

    /// <summary>Pushes the WebCrypto ("SHA-256") form of the lowercase hash local.</summary>
    private void EmitWebHashName(ILGenerator il, LocalBuilder hashLocal)
    {
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var done = il.DefineLabel();
        var resultLocal = il.DeclareLocal(_types.String);
        foreach (var (lower, web) in _wcHashes)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, web);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>
    /// Builds { publicKey, privateKey } from a WcGen* pair array and returns it resolved.
    /// pair[0] = SPKI (public), pair[1] = PKCS#8 (private).
    /// </summary>
    private void EmitKeyPairResult(ILGenerator il, EmittedRuntime runtime, string algoName,
        LocalBuilder algDict, LocalBuilder pairLocal, LocalBuilder extLocal, LocalBuilder? hashLocal,
        bool curveNull, LocalBuilder? curveLocal = null)
    {
        var pubLocal = il.DeclareLocal(_types.Object);
        var privLocal = il.DeclareLocal(_types.Object);

        void EmitOne(int index, string kind, LocalBuilder target, bool alwaysExtractable)
        {
            il.Emit(OpCodes.Ldstr, kind);
            if (alwaysExtractable)
                il.Emit(OpCodes.Ldc_I4_1);
            else
                il.Emit(OpCodes.Ldloc, extLocal);
            il.Emit(OpCodes.Ldloc, algDict);
            il.Emit(OpCodes.Ldarg_2); // usages (GenerateKeyCore is static: arg2)
            il.Emit(OpCodes.Ldloc, pairLocal);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, _types.ByteArray);
            il.Emit(OpCodes.Ldstr, algoName);
            if (hashLocal != null)
                il.Emit(OpCodes.Ldloc, hashLocal);
            else
                il.Emit(OpCodes.Ldnull);
            if (curveNull)
                il.Emit(OpCodes.Ldnull);
            else
                il.Emit(OpCodes.Ldloc, curveLocal!);
            il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
            il.Emit(OpCodes.Stloc, target);
        }

        EmitOne(0, "public", pubLocal, alwaysExtractable: true);
        EmitOne(1, "private", privLocal, alwaysExtractable: false);

        var resultDict = EmitNewDict(il);
        EmitDictAdd(il, resultDict, "publicKey", () => il.Emit(OpCodes.Ldloc, pubLocal));
        EmitDictAdd(il, resultDict, "privateKey", () => il.Emit(OpCodes.Ldloc, privLocal));

        il.Emit(OpCodes.Ldloc, resultDict);
        il.Emit(OpCodes.Ret);
    }

    // static object ImportKeyCore(object format, object keyData, object algorithm, object extractable, object usages)
    private MethodBuilder EmitSubtleImportKeyCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("ImportKeyCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object]);
        var il = m.GetILGenerator();

        var fmtLocal = il.DeclareLocal(_types.String);
        var nameLocal = il.DeclareLocal(_types.String);
        var extLocal = il.DeclareLocal(_types.Boolean);
        var materialLocal = il.DeclareLocal(_types.ByteArray);
        var hashLocal = il.DeclareLocal(_types.String);
        var curveLocal = il.DeclareLocal(_types.String);
        var sizeLocal = il.DeclareLocal(_types.Int32);

        // fmt = (string)format
        var fmtOk = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, fmtLocal);
        il.Emit(OpCodes.Ldloc, fmtLocal);
        il.Emit(OpCodes.Brtrue, fmtOk);
        EmitThrowMessage(il, "crypto.subtle.importKey: format must be a string");
        il.MarkLabel(fmtOk);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, nameLocal);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Stloc, extLocal);

        // Secret-key family
        foreach (var secret in new[] { "AES-GCM", "AES-CBC", "HMAC", "PBKDF2", "HKDF" })
        {
            EmitIfEquals(il, nameLocal, secret, () =>
            {
                var rawOk = il.DefineLabel();
                var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
                il.Emit(OpCodes.Ldloc, fmtLocal);
                il.Emit(OpCodes.Ldstr, "raw");
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brtrue, rawOk);
                EmitThrowMessage(il, $"crypto.subtle.importKey: only the 'raw' format is supported for {secret} in compiled mode");
                il.MarkLabel(rawOk);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _wcToBytes);
                il.Emit(OpCodes.Stloc, materialLocal);

                if (secret is "AES-GCM" or "AES-CBC")
                {
                    var lenOk = il.DefineLabel();
                    foreach (var valid in new[] { 16, 24, 32 })
                    {
                        il.Emit(OpCodes.Ldloc, materialLocal);
                        il.Emit(OpCodes.Ldlen);
                        il.Emit(OpCodes.Conv_I4);
                        il.Emit(OpCodes.Ldc_I4, valid);
                        il.Emit(OpCodes.Beq, lenOk);
                    }
                    EmitThrowMessage(il, "crypto.subtle.importKey: AES key must be 128, 192, or 256 bits");
                    il.MarkLabel(lenOk);
                }

                LocalBuilder algDict;
                if (secret == "HMAC")
                {
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldstr, "hash");
                    il.Emit(OpCodes.Call, _wcParam);
                    il.Emit(OpCodes.Call, _wcMapHash);
                    il.Emit(OpCodes.Stloc, hashLocal);

                    var hashDict = EmitNewDict(il);
                    EmitDictAdd(il, hashDict, "name", () => EmitWebHashName(il, hashLocal));
                    algDict = EmitNewDict(il);
                    EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, "HMAC"));
                    EmitDictAdd(il, algDict, "hash", () => il.Emit(OpCodes.Ldloc, hashDict));
                }
                else
                {
                    algDict = EmitNewDict(il);
                    EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, secret));
                    if (secret is "AES-GCM" or "AES-CBC")
                    {
                        EmitDictAdd(il, algDict, "length", () =>
                        {
                            il.Emit(OpCodes.Ldloc, materialLocal);
                            il.Emit(OpCodes.Ldlen);
                            il.Emit(OpCodes.Conv_I4);
                            il.Emit(OpCodes.Ldc_I4_8);
                            il.Emit(OpCodes.Mul);
                            il.Emit(OpCodes.Conv_R8);
                            il.Emit(OpCodes.Box, _types.Double);
                        });
                    }
                }

                il.Emit(OpCodes.Ldstr, "secret");
                il.Emit(OpCodes.Ldloc, extLocal);
                il.Emit(OpCodes.Ldloc, algDict);
                il.Emit(OpCodes.Ldarg, 4);
                il.Emit(OpCodes.Ldloc, materialLocal);
                il.Emit(OpCodes.Ldstr, secret);
                if (secret == "HMAC")
                    il.Emit(OpCodes.Ldloc, hashLocal);
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
                il.Emit(OpCodes.Ret);
            });
        }

        // RSA family
        foreach (var rsa in new[] { "RSA-OAEP", "RSASSA-PKCS1-V1_5", "RSA-PSS" })
        {
            EmitIfEquals(il, nameLocal, rsa, () =>
            {
                var canonical = rsa == "RSASSA-PKCS1-V1_5" ? "RSASSA-PKCS1-v1_5" : rsa;
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldstr, "hash");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Call, _wcMapHash);
                il.Emit(OpCodes.Stloc, hashLocal);

                var kindLocal = il.DeclareLocal(_types.String);
                EmitDerImportKind(il, fmtLocal, materialLocal, kindLocal, isRsa: true, sizeLocal);

                var hashDict = EmitNewDict(il);
                EmitDictAdd(il, hashDict, "name", () => EmitWebHashName(il, hashLocal));
                var algDict = EmitNewDict(il);
                EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, canonical));
                EmitDictAdd(il, algDict, "modulusLength", () => EmitBoxedDouble(il, () => il.Emit(OpCodes.Ldloc, sizeLocal)));
                EmitDictAdd(il, algDict, "publicExponent", () =>
                {
                    il.Emit(OpCodes.Ldc_R8, 65537d);
                    il.Emit(OpCodes.Box, _types.Double);
                });
                EmitDictAdd(il, algDict, "hash", () => il.Emit(OpCodes.Ldloc, hashDict));

                il.Emit(OpCodes.Ldloc, kindLocal);
                il.Emit(OpCodes.Ldloc, extLocal);
                il.Emit(OpCodes.Ldloc, algDict);
                il.Emit(OpCodes.Ldarg, 4);
                il.Emit(OpCodes.Ldloc, materialLocal);
                il.Emit(OpCodes.Ldstr, rsa);
                il.Emit(OpCodes.Ldloc, hashLocal);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
                il.Emit(OpCodes.Ret);
            });
        }

        // EC family
        foreach (var ec in new[] { "ECDSA", "ECDH" })
        {
            EmitIfEquals(il, nameLocal, ec, () =>
            {
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldstr, "namedCurve");
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Call, _wcCanonicalCurve);
                il.Emit(OpCodes.Stloc, curveLocal);

                var kindLocal = il.DeclareLocal(_types.String);
                var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
                var afterImport = il.DefineLabel();

                // raw → uncompressed point → SPKI (public)
                var notRaw = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, fmtLocal);
                il.Emit(OpCodes.Ldstr, "raw");
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brfalse, notRaw);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, _wcToBytes);
                il.Emit(OpCodes.Ldloc, curveLocal);
                il.Emit(OpCodes.Call, _wcEcRawToSpki);
                il.Emit(OpCodes.Stloc, materialLocal);
                il.Emit(OpCodes.Ldstr, "public");
                il.Emit(OpCodes.Stloc, kindLocal);
                il.Emit(OpCodes.Br, afterImport);
                il.MarkLabel(notRaw);

                EmitDerImportKind(il, fmtLocal, materialLocal, kindLocal, isRsa: false, sizeLocal);
                il.MarkLabel(afterImport);

                var algDict = EmitNewDict(il);
                EmitDictAdd(il, algDict, "name", () => il.Emit(OpCodes.Ldstr, ec));
                EmitDictAdd(il, algDict, "namedCurve", () => il.Emit(OpCodes.Ldloc, curveLocal));

                il.Emit(OpCodes.Ldloc, kindLocal);
                il.Emit(OpCodes.Ldloc, extLocal);
                il.Emit(OpCodes.Ldloc, algDict);
                il.Emit(OpCodes.Ldarg, 4);
                il.Emit(OpCodes.Ldloc, materialLocal);
                il.Emit(OpCodes.Ldstr, ec);
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldloc, curveLocal);
                il.Emit(OpCodes.Newobj, _cryptoKeyCtor);
                il.Emit(OpCodes.Ret);
            });
        }

        il.Emit(OpCodes.Ldstr, "crypto.subtle.importKey: unsupported algorithm '");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' on this runtime");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        return m;
    }

    /// <summary>
    /// Shared spki/pkcs8 import: sets material, kind, and (for RSA) sizeLocal; throws on
    /// other formats (jwk is a compiled-mode ceiling).
    /// </summary>
    private void EmitDerImportKind(ILGenerator il, LocalBuilder fmtLocal, LocalBuilder materialLocal,
        LocalBuilder kindLocal, bool isRsa, LocalBuilder sizeLocal)
    {
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var done = il.DefineLabel();

        void EmitBranch(string fmt, string kind, bool isPrivate)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, fmtLocal);
            il.Emit(OpCodes.Ldstr, fmt);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Stloc, materialLocal);

            il.Emit(OpCodes.Ldloc, materialLocal);
            il.Emit(isPrivate ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            if (isRsa)
            {
                il.Emit(OpCodes.Call, _wcImportRsaCheck);
                il.Emit(OpCodes.Stloc, sizeLocal);
            }
            else
            {
                il.Emit(OpCodes.Call, _wcImportEcCheck);
            }

            il.Emit(OpCodes.Ldstr, kind);
            il.Emit(OpCodes.Stloc, kindLocal);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(next);
        }

        EmitBranch("spki", "public", isPrivate: false);
        EmitBranch("pkcs8", "private", isPrivate: true);
        EmitThrowMessage(il, "crypto.subtle.importKey: unsupported format for this key type in compiled mode (use raw/spki/pkcs8)");
        il.MarkLabel(done);
    }

    // static object ExportKeyCore(object format, object key)
    private MethodBuilder EmitSubtleExportKeyCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("ExportKeyCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        var il = m.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        EmitEnsureCryptoKey(il, 1, "exportKey");

        var fmtLocal = il.DeclareLocal(_types.String);
        var kindLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, fmtLocal);

        EmitLoadKeyField(il, 1, _ckKind);
        il.Emit(OpCodes.Stloc, kindLocal);

        // extractable check
        var extOk = il.DefineLabel();
        EmitLoadKeyField(il, 1, _ckExtractable);
        il.Emit(OpCodes.Brtrue, extOk);
        EmitThrowMessage(il, "crypto.subtle.exportKey: key is not extractable");
        il.MarkLabel(extOk);

        // raw
        EmitIfEquals(il, fmtLocal, "raw", () =>
        {
            // secret → material
            EmitIfEquals(il, kindLocal, "secret", () =>
            {
                EmitLoadKeyField(il, 1, _ckMaterial);
                il.Emit(OpCodes.Call, _wcToArrayBuffer);
                il.Emit(OpCodes.Ret);
            });
            // EC public → uncompressed point
            EmitIfEquals(il, kindLocal, "public", () =>
            {
                var noCurve = il.DefineLabel();
                EmitLoadKeyField(il, 1, _ckCurve);
                il.Emit(OpCodes.Brfalse, noCurve);
                EmitLoadKeyField(il, 1, _ckMaterial);
                il.Emit(OpCodes.Call, _wcEcSpkiToRaw);
                il.Emit(OpCodes.Call, _wcToArrayBuffer);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(noCurve);
            });
            EmitThrowMessage(il, "crypto.subtle.exportKey: 'raw' is only valid for secret and EC public keys");
        });

        // spki
        EmitIfEquals(il, fmtLocal, "spki", () =>
        {
            var bad = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, kindLocal);
            il.Emit(OpCodes.Ldstr, "public");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, bad);
            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(bad);
            EmitThrowMessage(il, "crypto.subtle.exportKey: 'spki' is only valid for public keys");
        });

        // pkcs8
        EmitIfEquals(il, fmtLocal, "pkcs8", () =>
        {
            var bad = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, kindLocal);
            il.Emit(OpCodes.Ldstr, "private");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, bad);
            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Call, _wcToArrayBuffer);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(bad);
            EmitThrowMessage(il, "crypto.subtle.exportKey: 'pkcs8' is only valid for private keys");
        });

        // jwk — secret (oct) only in compiled mode
        EmitIfEquals(il, fmtLocal, "jwk", () =>
        {
            var bad = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, kindLocal);
            il.Emit(OpCodes.Ldstr, "secret");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, bad);

            var jwkDict = EmitNewDict(il);
            EmitDictAdd(il, jwkDict, "kty", () => il.Emit(OpCodes.Ldstr, "oct"));
            EmitDictAdd(il, jwkDict, "k", () =>
            {
                EmitLoadKeyField(il, 1, _ckMaterial);
                il.Emit(OpCodes.Call, _wcBase64Url);
            });
            EmitDictAdd(il, jwkDict, "ext", () =>
            {
                EmitLoadKeyField(il, 1, _ckExtractable);
                il.Emit(OpCodes.Box, _types.Boolean);
            });
            il.Emit(OpCodes.Ldloc, jwkDict);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(bad);
            EmitThrowMessage(il, "crypto.subtle.exportKey: asymmetric 'jwk' export is not supported in compiled mode (use spki/pkcs8)");
        });

        EmitThrowMessage(il, "crypto.subtle.exportKey: unsupported format");
        return m;
    }

    // static byte[] EncDecCore(object algorithm, object key, byte[] input, bool encrypt)
    private MethodBuilder EmitSubtleEncDecCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("EncDecCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray, [_types.Object, _types.Object, _types.ByteArray, _types.Boolean]);
        var il = m.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        EmitEnsureCryptoKey(il, 1, "encrypt/decrypt");

        var nameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, nameLocal);

        EmitIfEquals(il, nameLocal, "AES-GCM", () =>
        {
            var ivLocal = il.DeclareLocal(_types.ByteArray);
            var aadLocal = il.DeclareLocal(_types.ByteArray);
            var aadObjLocal = il.DeclareLocal(_types.Object);
            var tagLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "iv");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Stloc, ivLocal);

            var noAad = il.DefineLabel();
            var aadDone = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "additionalData");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Stloc, aadObjLocal);
            il.Emit(OpCodes.Ldloc, aadObjLocal);
            il.Emit(OpCodes.Brfalse, noAad);
            il.Emit(OpCodes.Ldloc, aadObjLocal);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Stloc, aadLocal);
            il.Emit(OpCodes.Br, aadDone);
            il.MarkLabel(noAad);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, aadLocal);
            il.MarkLabel(aadDone);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "tagLength");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Ldc_I4, 128);
            il.Emit(OpCodes.Call, _wcIntParam);
            il.Emit(OpCodes.Stloc, tagLocal);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, ivLocal);
            il.Emit(OpCodes.Ldloc, aadLocal);
            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcAesGcm);
            il.Emit(OpCodes.Ret);
        });

        EmitIfEquals(il, nameLocal, "AES-CBC", () =>
        {
            var ivLocal = il.DeclareLocal(_types.ByteArray);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "iv");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Stloc, ivLocal);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, ivLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcAesCbc);
            il.Emit(OpCodes.Ret);
        });

        EmitIfEquals(il, nameLocal, "RSA-OAEP", () =>
        {
            // label → ceiling
            var noLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "label");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Brfalse, noLabel);
            EmitThrowMessage(il, "crypto.subtle: RSA-OAEP labels are not supported on this runtime (.NET BCL OAEP has no label parameter)");
            il.MarkLabel(noLabel);

            var isPrivLocal = il.DeclareLocal(_types.Boolean);
            EmitLoadKeyField(il, 1, _ckKind);
            il.Emit(OpCodes.Ldstr, "private");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Stloc, isPrivLocal);

            var hashLocal = il.DeclareLocal(_types.String);
            var haveHash = il.DefineLabel();
            EmitLoadKeyField(il, 1, _ckHash);
            il.Emit(OpCodes.Stloc, hashLocal);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Brtrue, haveHash);
            il.Emit(OpCodes.Ldstr, "sha1");
            il.Emit(OpCodes.Stloc, hashLocal);
            il.MarkLabel(haveHash);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, isPrivLocal);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcRsaOaep);
            il.Emit(OpCodes.Ret);
        });

        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported encrypt/decrypt algorithm '");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' on this runtime");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        return m;
    }

    // static object SignVerifyCore(object algorithm, object key, byte[] data, byte[]? sig)
    private MethodBuilder EmitSubtleSignVerifyCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("SignVerifyCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.ByteArray, _types.ByteArray]);
        var il = m.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        EmitEnsureCryptoKey(il, 1, "sign/verify");

        var nameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, nameLocal);

        EmitIfEquals(il, nameLocal, "HMAC", () =>
        {
            var computedLocal = il.DeclareLocal(_types.ByteArray);
            EmitLoadKeyField(il, 1, _ckHash);
            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _wcHmac);
            il.Emit(OpCodes.Stloc, computedLocal);

            var verifyLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brtrue, verifyLabel);
            il.Emit(OpCodes.Ldloc, computedLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(verifyLabel);
            var opImplicit = _types.GetMethod(_types.ReadOnlySpanOfByte, "op_Implicit", [typeof(byte[])])!;
            il.Emit(OpCodes.Ldloc, computedLocal);
            il.Emit(OpCodes.Call, opImplicit);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, opImplicit);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.CryptographicOperations, "FixedTimeEquals",
                [_types.ReadOnlySpanOfByte, _types.ReadOnlySpanOfByte])!);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
        });

        foreach (var (algo, pss) in new[] { ("RSASSA-PKCS1-V1_5", false), ("RSA-PSS", true) })
        {
            EmitIfEquals(il, nameLocal, algo, () =>
            {
                if (pss)
                {
                    // explicit saltLength must equal the digest length (BCL fixes it there)
                    var saltLocal = il.DeclareLocal(_types.Int32);
                    var saltOk = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldstr, "saltLength");
                    il.Emit(OpCodes.Call, _wcParam);
                    il.Emit(OpCodes.Ldc_I4_M1);
                    il.Emit(OpCodes.Call, _wcIntParam);
                    il.Emit(OpCodes.Stloc, saltLocal);
                    il.Emit(OpCodes.Ldloc, saltLocal);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Blt, saltOk); // unspecified
                    il.Emit(OpCodes.Ldloc, saltLocal);
                    EmitLoadKeyField(il, 1, _ckHash);
                    il.Emit(OpCodes.Call, _wcDigestLen);
                    il.Emit(OpCodes.Beq, saltOk);
                    EmitThrowMessage(il, "crypto.subtle: RSA-PSS saltLength is not supported on this runtime (.NET always uses the digest length)");
                    il.MarkLabel(saltOk);
                }

                var isPrivLocal = il.DeclareLocal(_types.Boolean);
                EmitLoadKeyField(il, 1, _ckKind);
                il.Emit(OpCodes.Ldstr, "private");
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Stloc, isPrivLocal);

                EmitLoadKeyField(il, 1, _ckMaterial);
                il.Emit(OpCodes.Ldloc, isPrivLocal);
                EmitLoadKeyField(il, 1, _ckHash);
                il.Emit(pss ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Call, _wcRsaSignVerify);
                il.Emit(OpCodes.Ret);
            });
        }

        EmitIfEquals(il, nameLocal, "ECDSA", () =>
        {
            var hashLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "hash");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Stloc, hashLocal);

            var isPrivLocal = il.DeclareLocal(_types.Boolean);
            EmitLoadKeyField(il, 1, _ckKind);
            il.Emit(OpCodes.Ldstr, "private");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Stloc, isPrivLocal);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, isPrivLocal);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, _wcEcdsaSignVerify);
            il.Emit(OpCodes.Ret);
        });

        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported sign/verify algorithm '");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' on this runtime");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        return m;
    }

    // static byte[] DeriveBitsCore(object algorithm, object baseKey, int lengthBits)
    private MethodBuilder EmitSubtleDeriveBitsCore(TypeBuilder tb, EmittedRuntime runtime)
    {
        var m = tb.DefineMethod("DeriveBitsCore", MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray, [_types.Object, _types.Object, _types.Int32]);
        var il = m.GetILGenerator();

        EmitEnsureCryptoKey(il, 1, "deriveBits");

        // length must be a positive multiple of 8
        var lenOk = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Blt, DefineThrowTag(il, out var badLen));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brtrue, badLen);
        il.Emit(OpCodes.Br, lenOk);
        il.MarkLabel(badLen);
        EmitThrowMessage(il, "crypto.subtle.deriveBits: length must be a positive multiple of 8 on this runtime");
        il.MarkLabel(lenOk);

        var nameLocal = il.DeclareLocal(_types.String);
        var lenBytesLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, lenBytesLocal);

        EmitIfEquals(il, nameLocal, "PBKDF2", () =>
        {
            var hashLocal = il.DeclareLocal(_types.String);
            var saltLocal = il.DeclareLocal(_types.ByteArray);
            var iterLocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "hash");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Stloc, hashLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "salt");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcToBytes);
            il.Emit(OpCodes.Stloc, saltLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "iterations");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Call, _wcIntParam);
            il.Emit(OpCodes.Stloc, iterLocal);

            var iterOk = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, iterLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Bge, iterOk);
            EmitThrowMessage(il, "crypto.subtle.deriveBits: PBKDF2 requires iterations");
            il.MarkLabel(iterOk);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, saltLocal);
            il.Emit(OpCodes.Ldloc, iterLocal);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldloc, lenBytesLocal);
            il.Emit(OpCodes.Call, _wcPbkdf2);
            il.Emit(OpCodes.Ret);
        });

        EmitIfEquals(il, nameLocal, "HKDF", () =>
        {
            var hashLocal = il.DeclareLocal(_types.String);
            var saltLocal = il.DeclareLocal(_types.ByteArray);
            var infoLocal = il.DeclareLocal(_types.ByteArray);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "hash");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Stloc, hashLocal);

            void EmitOptionalBytes(string param, LocalBuilder target)
            {
                var missing = il.DefineLabel();
                var done = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, param);
                il.Emit(OpCodes.Call, _wcParam);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, missing);
                il.Emit(OpCodes.Call, _wcToBytes);
                il.Emit(OpCodes.Stloc, target);
                il.Emit(OpCodes.Br, done);
                il.MarkLabel(missing);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, _types.Byte);
                il.Emit(OpCodes.Stloc, target);
                il.MarkLabel(done);
            }
            EmitOptionalBytes("salt", saltLocal);
            EmitOptionalBytes("info", infoLocal);

            il.Emit(OpCodes.Ldloc, hashLocal);
            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, lenBytesLocal);
            il.Emit(OpCodes.Ldloc, saltLocal);
            il.Emit(OpCodes.Ldloc, infoLocal);
            il.Emit(OpCodes.Call, _wcHkdf);
            il.Emit(OpCodes.Ret);
        });

        EmitIfEquals(il, nameLocal, "ECDH", () =>
        {
            var pubOk = il.DefineLabel();
            var pubLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "public");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Stloc, pubLocal);
            il.Emit(OpCodes.Ldloc, pubLocal);
            il.Emit(OpCodes.Isinst, _cryptoKeyType);
            il.Emit(OpCodes.Brtrue, pubOk);
            EmitThrowMessage(il, "crypto.subtle.deriveBits: ECDH requires { public: CryptoKey }");
            il.MarkLabel(pubOk);

            EmitLoadKeyField(il, 1, _ckMaterial);
            il.Emit(OpCodes.Ldloc, pubLocal);
            il.Emit(OpCodes.Castclass, _cryptoKeyType);
            il.Emit(OpCodes.Ldfld, _ckMaterial);
            il.Emit(OpCodes.Ldloc, lenBytesLocal);
            il.Emit(OpCodes.Call, _wcEcdhDerive);
            il.Emit(OpCodes.Ret);
        });

        il.Emit(OpCodes.Ldstr, "crypto.subtle.deriveBits: unsupported algorithm '");
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "' on this runtime");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        return m;
    }

    /// <summary>
    /// deriveKey(algorithm, baseKey, derivedKeyType, extractable, usages) body: computes
    /// the target length, derives, and leaves the imported $CryptoKey (raw) on the stack.
    /// Emitted inside EmitPromiseMethod's try block (instance method — args start at 1).
    /// </summary>
    private void EmitDeriveKeyBody(ILGenerator il)
    {
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        var targetLocal = il.DeclareLocal(_types.String);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var bitsLocal = il.DeclareLocal(_types.ByteArray);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _wcAlgoName);
        il.Emit(OpCodes.Stloc, targetLocal);

        // AES targets: length from derivedKeyType
        var isHmac = il.DefineLabel();
        var haveLen = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldstr, "HMAC");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, isHmac);

        // AES-GCM / AES-CBC
        var isAes = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldstr, "AES-GCM");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, isAes);
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Ldstr, "AES-CBC");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, isAes);
        EmitThrowMessage(il, "crypto.subtle.deriveKey: unsupported derived key type on this runtime");

        il.MarkLabel(isAes);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _wcParam);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Call, _wcIntParam);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, haveLen);

        il.MarkLabel(isHmac);
        {
            // default 512/1024 by hash, overridable by length
            var hashLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldstr, "hash");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Call, _wcMapHash);
            il.Emit(OpCodes.Stloc, hashLocal);

            var use1024 = il.DefineLabel();
            var haveDefault = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldstr, "sha384");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, use1024);
            il.Emit(OpCodes.Ldloc, hashLocal);
            il.Emit(OpCodes.Ldstr, "sha512");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brtrue, use1024);
            il.Emit(OpCodes.Ldc_I4, 512);
            il.Emit(OpCodes.Br, haveDefault);
            il.MarkLabel(use1024);
            il.Emit(OpCodes.Ldc_I4, 1024);
            il.MarkLabel(haveDefault);
            il.Emit(OpCodes.Stloc, lenLocal);

            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _wcParam);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _wcIntParam);
            il.Emit(OpCodes.Stloc, lenLocal);
        }

        il.MarkLabel(haveLen);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _subtleDeriveBitsCore);
        il.Emit(OpCodes.Stloc, bitsLocal);

        // ImportKeyCore("raw", bits, derivedKeyType, extractable, usages)
        il.Emit(OpCodes.Ldstr, "raw");
        il.Emit(OpCodes.Ldloc, bitsLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Call, _subtleImportKeyCore);
    }

    // ───────────────────────────── $WebCrypto ─────────────────────────────

    private void EmitWebCryptoType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var tb = moduleBuilder.DefineType(
            "$WebCrypto",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object);
        _webCryptoType = tb;

        // static singleton fields
        var instanceField = tb.DefineField("_instance", _types.Object, FieldAttributes.Public | FieldAttributes.Static);
        var subtleField = tb.DefineField("_subtle", _types.Object, FieldAttributes.Public | FieldAttributes.Static);

        var ctor = tb.DefineDefaultConstructor(MethodAttributes.Public);
        _ = ctor;

        // property subtle → lazily-created shared $SubtleCrypto
        EmitSimpleGetter(tb, "subtle", _types.Object, il =>
        {
            var have = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, subtleField);
            il.Emit(OpCodes.Brtrue, have);
            il.Emit(OpCodes.Newobj, _subtleCtor);
            il.Emit(OpCodes.Stsfld, subtleField);
            il.MarkLabel(have);
            il.Emit(OpCodes.Ldsfld, subtleField);
        });

        // getRandomValues(object) → object (fills in place, returns the argument)
        var grv = tb.DefineMethod("getRandomValues", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Object, [_types.Object]);
        _webCryptoGetRandomValues = grv;
        {
            var il = grv.GetILGenerator();
            var fill = typeof(System.Security.Cryptography.RandomNumberGenerator)
                .GetMethod("Fill", [typeof(Span<byte>)])!;
            var spanCtor3 = typeof(Span<byte>).GetConstructor([typeof(byte[]), typeof(int), typeof(int)])!;

            // $TypedArray
            if (runtime.TypedArrayBaseType != null)
            {
                var notTyped = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
                il.Emit(OpCodes.Brfalse, notTyped);

                var typedLocal = il.DeclareLocal(runtime.TypedArrayBaseType);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
                il.Emit(OpCodes.Stloc, typedLocal);

                // quota check
                var quotaOk = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, typedLocal);
                il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteLengthGetter);
                il.Emit(OpCodes.Ldc_I4, 65536);
                il.Emit(OpCodes.Ble, quotaOk);
                EmitThrowMessage(il, "QuotaExceededError: crypto.getRandomValues requested more than 65536 bytes");
                il.MarkLabel(quotaOk);

                // RandomNumberGenerator.Fill(new Span<byte>(buffer, offset, len))
                il.Emit(OpCodes.Ldloc, typedLocal);
                il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
                il.Emit(OpCodes.Ldloc, typedLocal);
                il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteOffsetGetter);
                il.Emit(OpCodes.Ldloc, typedLocal);
                il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteLengthGetter);
                il.Emit(OpCodes.Newobj, spanCtor3);
                il.Emit(OpCodes.Call, fill);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notTyped);
            }

            // $Buffer
            if (runtime.TSBufferType != null)
            {
                var notBuffer = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Isinst, runtime.TSBufferType);
                il.Emit(OpCodes.Brfalse, notBuffer);

                var dataLocal = il.DeclareLocal(_types.ByteArray);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, runtime.TSBufferType);
                il.Emit(OpCodes.Call, runtime.TSBufferGetData);
                il.Emit(OpCodes.Stloc, dataLocal);

                var quotaOk = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, dataLocal);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ldc_I4, 65536);
                il.Emit(OpCodes.Ble, quotaOk);
                EmitThrowMessage(il, "QuotaExceededError: crypto.getRandomValues requested more than 65536 bytes");
                il.MarkLabel(quotaOk);

                il.Emit(OpCodes.Ldloc, dataLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, dataLocal);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Newobj, spanCtor3);
                il.Emit(OpCodes.Call, fill);

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notBuffer);
            }

            EmitThrowMessage(il, "crypto.getRandomValues: argument must be an integer typed array");
        }

        // randomUUID() → string
        var uuid = tb.DefineMethod("randomUUID", MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.String, Type.EmptyTypes);
        {
            var il = uuid.GetILGenerator();
            var guidLocal = il.DeclareLocal(_types.Guid);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Guid, "NewGuid"));
            il.Emit(OpCodes.Stloc, guidLocal);
            il.Emit(OpCodes.Ldloca, guidLocal);
            il.Emit(OpCodes.Constrained, _types.Guid);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            il.Emit(OpCodes.Ret);
        }

        var created = tb.CreateType()!;

        // Fill the Phase1-reserved GetWebCryptoObject body: lazily-created singleton.
        var body = (MethodBuilder)runtime.GetWebCryptoObject;
        var bil = body.GetILGenerator();
        var haveInstance = bil.DefineLabel();
        bil.Emit(OpCodes.Ldsfld, instanceField);
        bil.Emit(OpCodes.Brtrue, haveInstance);
        bil.Emit(OpCodes.Newobj, created.GetConstructor(Type.EmptyTypes)!);
        bil.Emit(OpCodes.Stsfld, instanceField);
        bil.MarkLabel(haveInstance);
        bil.Emit(OpCodes.Ldsfld, instanceField);
        bil.Emit(OpCodes.Ret);
    }

    /// <summary>Stub GetWebCryptoObject body for programs compiled without crypto.</summary>
    private void EmitGetWebCryptoObjectStub(EmittedRuntime runtime)
    {
        var body = (MethodBuilder)runtime.GetWebCryptoObject;
        var il = body.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
