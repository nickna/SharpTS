using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits globalThis helper methods (GetProperty, SetProperty).
    /// </summary>
    private void EmitGlobalThisMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Static field to cache the fetch TSFunction for reference equality
        runtime.Fetch.CachedFunction = typeBuilder.DefineField(
            "_cachedFetchFunction",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static);

        // Static fields for cached global function TSFunction objects
        _ = typeBuilder.DefineField(
            "_cachedParseIntFunction",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static);

        _ = typeBuilder.DefineField(
            "_cachedParseFloatFunction",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static);

        _ = typeBuilder.DefineField(
            "_cachedIsNaNFunction",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static);

        _ = typeBuilder.DefineField(
            "_cachedIsFiniteFunction",
            _types.Object,
            FieldAttributes.Private | FieldAttributes.Static);

        EmitIndirectEval(typeBuilder, runtime.GlobalObject, runtime.Sentinels.UndefinedInstance);
        EmitUriComponentFunctions(typeBuilder, runtime.UriComponents,
            new UriComponentInputs(runtime.FunctionAttributes.PadUndefinedCtor, runtime.StringCoercion.ToJsString));
        runtime.UriComponents.CompleteEmission();
        var optional = new GlobalPropertyOptionalInputs(
            runtime.Dates.Implementation is not null ? runtime.Dates.RequireImplementation().Type : null,
            runtime.RegExps.Implementation is not null ? runtime.RegExps.RequireImplementation().Type : null,
            runtime.Reflect.Namespace is not null ? runtime.Reflect.RequireNamespace().SingletonField : null,
            _features.UsesBuffer ? runtime.RequireBuffer().Type : null,
            _features.UsesTextEncoding ? runtime.RequireTextEncoding().EncoderType : null,
            _features.UsesTextEncoding ? runtime.RequireTextEncoding().DecoderType : null,
            _features.UsesCrypto ? runtime.WebCrypto.GetObject : null,
            _features.UsesFetch ? runtime.Fetch.RequireImplementation().Invoke : null);
        EmitGlobalThisGetProperty(runtime.GlobalObject,
            new GlobalPropertyReadInputs(runtime.DescriptorStorage, runtime.Invocation.Method,
                runtime.ObjectState.IsBuiltinDeleted, runtime.Sentinels.UndefinedInstance,
                runtime.FunctionValues.Type, runtime.Numbers, runtime.Errors,
                runtime.Math.SingletonField, runtime.Json.SingletonField, runtime.Process.GetObject,
                runtime.Symbols.Type, runtime.UriComponents, runtime.FunctionConstruction.GetOrCreate,
                runtime.FunctionConstruction.Constructor, runtime.Fetch.CachedFunction, optional));
        EmitGlobalThisSetProperty(runtime.GlobalObject,
            new GlobalPropertyWriteInputs(runtime.DescriptorStorage.DescriptorType,
                runtime.DescriptorStorage.GetPropertyDescriptor,
                runtime.DescriptorStorage.DescriptorWritable.GetGetMethod()!,
                runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!));
        runtime.GlobalObject.CompleteEmission();
    }

    private readonly record struct GlobalPropertyOptionalInputs(
        Type? DateType, Type? RegExpType, FieldBuilder? ReflectSingleton,
        Type? BufferType, Type? EncoderType, Type? DecoderType,
        MethodBuilder? GetCryptoObject, MethodBuilder? FetchInvoke);

    private readonly record struct GlobalPropertyReadInputs(
        EmittedDescriptorStorageRuntime Descriptors, MethodBuilder InvokeMethod,
        MethodBuilder IsBuiltinDeleted, FieldInfo UndefinedInstance,
        Type FunctionType, EmittedNumberRuntime Numbers, EmittedErrorRuntime Errors,
        FieldBuilder MathSingleton, FieldBuilder JsonSingleton, MethodBuilder GetProcess,
        Type SymbolType, EmittedUriComponentRuntime UriComponents, MethodBuilder GetOrCreateFunction,
        ConstructorBuilder FunctionConstructor, FieldBuilder CachedFetchFunction,
        GlobalPropertyOptionalInputs Optional);

    private readonly record struct GlobalPropertyWriteInputs(
        Type DescriptorType, MethodBuilder GetPropertyDescriptor, MethodInfo GetWritable, MethodInfo SetValue);

    private readonly record struct UriComponentInputs(ConstructorBuilder? PadUndefinedCtor, MethodBuilder ToJsString);

    private void EmitUriComponentFunctions(TypeBuilder typeBuilder, EmittedUriComponentRuntime uriComponents, UriComponentInputs inputs)
    {
        MethodBuilder Emit(string clrName, MethodInfo uriMethod)
        {
            var method = typeBuilder.DefineMethod(
                clrName,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.String,
                [_types.Object]);

            // Value calls must turn an omitted argument into JS undefined, not
            // CLR null, before applying the URI function's ToString coercion.
            if (inputs.PadUndefinedCtor is not null)
                method.SetCustomAttribute(
                    inputs.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, inputs.ToJsString);
            il.Emit(OpCodes.Call, uriMethod);
            il.Emit(OpCodes.Ret);
            return method;
        }

        uriComponents.Encode = Emit(
            "GlobalEncodeURIComponent", _types.UriEscapeDataString);
        uriComponents.Decode = Emit(
            "GlobalDecodeURIComponent", _types.UriUnescapeDataString);
    }

    /// <summary>
    /// Emits: public static object GlobalThisGetProperty(string name)
    /// Gets a property from globalThis, checking user-assigned properties first,
    /// then delegating to built-ins.
    /// </summary>
    private void EmitGlobalThisGetProperty(EmittedGlobalObjectRuntime globalObject, GlobalPropertyReadInputs inputs)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 (#271) so the
        // property/index dispatchers emitted earlier can call it.
        var method = (MethodBuilder)globalObject.GetProperty;

        var il = method.GetILGenerator();

        var selfRefLabel = il.DefineLabel();
        var globalThisRefLabel = il.DefineLabel();
        var nullMarkerLabel = il.DefineLabel();
        var undefinedPropLabel = il.DefineLabel();
        var nanLabel = il.DefineLabel();
        var infinityLabel = il.DefineLabel();
        var fetchLabel = il.DefineLabel();
        var parseIntLabel = il.DefineLabel();
        var parseFloatLabel = il.DefineLabel();
        var isNaNLabel = il.DefineLabel();
        var isFiniteLabel = il.DefineLabel();
        var encodeURIComponentLabel = il.DefineLabel();
        var decodeURIComponentLabel = il.DefineLabel();
        var evalLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var checkBuiltInsLabel = il.DefineLabel();
        var checkDeletedLabel = il.DefineLabel();

        // Object.defineProperty can install data or accessor properties on the
        // global sentinel. Consult that canonical descriptor carrier before
        // the assignment dictionary and synthesized intrinsic table.
        var globalGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, globalGetterLocal);
        il.Emit(OpCodes.Call, inputs.Descriptors.TryGetGetter);
        var noGlobalGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noGlobalGetterLabel);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Ldloc, globalGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, inputs.InvokeMethod);
        il.Emit(OpCodes.Br, returnLabel);
        il.MarkLabel(noGlobalGetterLabel);

        var globalReadDescriptorLocal = il.DeclareLocal(inputs.Descriptors.DescriptorType);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.Descriptors.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, globalReadDescriptorLocal);
        var noGlobalReadDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, globalReadDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noGlobalReadDescriptorLabel);
        var globalAccessorUndefinedLabel = il.DefineLabel();
        var globalDataDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, globalReadDescriptorLocal);
        il.Emit(OpCodes.Callvirt, inputs.Descriptors.DescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, globalAccessorUndefinedLabel);
        il.Emit(OpCodes.Ldloc, globalReadDescriptorLocal);
        il.Emit(OpCodes.Callvirt, inputs.Descriptors.DescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, globalDataDescriptorLabel);
        il.MarkLabel(globalAccessorUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);
        il.MarkLabel(globalDataDescriptorLabel);
        il.Emit(OpCodes.Ldloc, globalReadDescriptorLocal);
        il.Emit(OpCodes.Callvirt, inputs.Descriptors.DescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Br, returnLabel);
        il.MarkLabel(noGlobalReadDescriptorLabel);

        // --- Check user-assigned properties dictionary first ---
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, globalObject.Properties);
        il.Emit(OpCodes.Brfalse, checkDeletedLabel); // dict not initialized yet
        il.Emit(OpCodes.Ldsfld, globalObject.Properties);
        il.Emit(OpCodes.Ldarg_0); // name
        il.Emit(OpCodes.Ldloca, valueLocal);
        var dictTryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType());
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, checkDeletedLabel); // not found in dict
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Br, returnLabel);

        // Deleting a configurable synthesized global records a tombstone.
        // User assignment is checked first above so a later assignment revives
        // the property, as ordinary JavaScript assignment does.
        il.MarkLabel(checkDeletedLabel);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.IsBuiltinDeleted);
        il.Emit(OpCodes.Brfalse, checkBuiltInsLabel);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(checkBuiltInsLabel);

        // Check for "globalThis" / "global" (self-reference and Node alias) —
        // value-form `globalThis.globalThis` / `globalThis.global` must return the
        // sentinel so the identity `globalThis.globalThis === globalThis` holds and
        // `freeSelf`-style probes keep a real object (#271).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "globalThis");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, globalThisRefLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "global");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, globalThisRefLabel);

        // Check for "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, undefinedPropLabel);

        // Check for "NaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nanLabel);

        // Check for "Infinity"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, infinityLabel);

        // Check for "fetch" — only when the program references fetch (or any fetch-family
        // identifier). HTTP-only programs also emit Web API helpers but do not expose this global.
        if (inputs.Optional.FetchInvoke is not null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "fetch");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, fetchLabel);
        }

        // Check for "parseInt"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "parseInt");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, parseIntLabel);

        // Check for "parseFloat"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "parseFloat");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, parseFloatLabel);

        // Check for "isNaN"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "isNaN");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, isNaNLabel);

        // Check for "isFinite"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "isFinite");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, isFiniteLabel);

        // URI functions are ordinary first-class globals as well as recognized
        // direct-call intrinsics.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "encodeURIComponent");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, encodeURIComponentLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "decodeURIComponent");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, decodeURIComponentLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "eval");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, evalLabel);

        // Built-in class constructors — return the actual .NET Type (Ldtoken +
        // GetTypeFromHandle) so `typeof globalThis.Array === "function"` and
        // `globalThis.Array === Array` hold. Previously these all returned null
        // as a "namespace marker," which broke lodash-style feature detection:
        // `typeof root.Object === "object" && root.Object === Object` was false
        // because root.Object was null.
        var strEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);

        void EmitTypeBranch(string name, Type t)
        {
            var notThisName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, notThisName);
            il.Emit(OpCodes.Ldtoken, t);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Br, returnLabel);
            il.MarkLabel(notThisName);
        }

        EmitTypeBranch("Array", _types.IListOfObject);
        if (inputs.Optional.DateType is not null)
            EmitTypeBranch("Date", inputs.Optional.DateType);
        if (inputs.Optional.RegExpType is not null)
            EmitTypeBranch("RegExp", inputs.Optional.RegExpType);
        EmitTypeBranch("Map", _types.DictionaryObjectObject);
        EmitTypeBranch("Set", _types.HashSetOfObject);
        EmitTypeBranch("WeakMap", _types.ConditionalWeakTableObjectObject);
        EmitTypeBranch("WeakSet", _types.ConditionalWeakTableObjectObject);
        EmitTypeBranch("Promise", _types.TaskOfObject);
        if (inputs.Optional.BufferType is not null)
            EmitTypeBranch("Buffer", inputs.Optional.BufferType!);
        EmitTypeBranch("Function", inputs.FunctionType);
        if (inputs.Optional.EncoderType is not null)
        {
            EmitTypeBranch("TextEncoder", inputs.Optional.EncoderType!);
            EmitTypeBranch("TextDecoder", inputs.Optional.DecoderType!);
        }
        // `Object` — return System.Object's Type token so `globalThis.Object === Object`
        // holds (bare `Object` lowers to this same helper via ILEmitter.Expressions.cs,
        // so both sides produce the canonical Type instance). The compile-time static
        // dispatch for `Object.keys(obj)` etc. runs through ObjectStaticEmitter before
        // the receiver is evaluated as a value, so this change doesn't affect it.
        EmitTypeBranch("Object", _types.Object);
        // Number / String / Boolean (issue #62) — expose the underlying
        // primitive .NET types so `typeof Number === "function"` and
        // `globalThis.Number === Number` hold. Compile-time static dispatch
        // for `Number.isInteger(x)` etc. routes through the dedicated
        // NumberStaticEmitter/StringStaticEmitter before the receiver is
        // evaluated, so these branches only matter for value-form access.
        EmitTypeBranch("Number", _types.Double);
        EmitTypeBranch("String", _types.String);
        EmitTypeBranch("Boolean", _types.Boolean);
        // Symbol (#234) — the $TSSymbol Type token, so `typeof Symbol` is
        // "function", `globalThis.Symbol === Symbol` holds, and aliased
        // member access resolves the well-known-symbol static fields via
        // GetProperty's Type branch.
        EmitTypeBranch("Symbol", inputs.SymbolType);

        // Error and the native-error subclasses are constructor functions; expose
        // their .NET Type tokens so value-form `root.Error` / `root.TypeError`
        // resolve to the real constructors (lodash's runInContext reads
        // `context.Error` and `context.TypeError`). #271.
        EmitTypeBranch("Error", inputs.Errors.Type);
        EmitTypeBranch("TypeError", inputs.Errors.TypeErrorType);
        EmitTypeBranch("RangeError", inputs.Errors.RangeErrorType);
        EmitTypeBranch("ReferenceError", inputs.Errors.ReferenceErrorType);
        EmitTypeBranch("SyntaxError", inputs.Errors.SyntaxErrorType);
        EmitTypeBranch("URIError", inputs.Errors.URIErrorType);
        EmitTypeBranch("EvalError", inputs.Errors.EvalErrorType);
        EmitTypeBranch("AggregateError", inputs.Errors.AggregateErrorType);

        // Math / JSON are extensible singleton objects in the runtime — return the
        // real Dictionary singletons so `root.Math`/`root.JSON` are usable values
        // (un-degrades lodash's native Math bindings inside runInContext). #271.
        void EmitSingletonBranch(string name, FieldBuilder field)
        {
            var notThisName = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, notThisName);
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Br, returnLabel);
            il.MarkLabel(notThisName);
        }
        EmitSingletonBranch("Math", inputs.MathSingleton);
        EmitSingletonBranch("JSON", inputs.JsonSingleton);
        if (inputs.Optional.ReflectSingleton is not null)
            EmitSingletonBranch("Reflect", inputs.Optional.ReflectSingleton);

        // globalThis.process → the live $Process singleton (epic #1078), same
        // object as the bare `process` identifier and the module facade's
        // default export.
        {
            var notProcess = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "process");
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, notProcess);
            il.Emit(OpCodes.Call, inputs.GetProcess);
            il.Emit(OpCodes.Br, returnLabel);
            il.MarkLabel(notProcess);
        }

        // globalThis.crypto → the $WebCrypto singleton (#1063), same object as
        // crypto.webcrypto. Gated: without crypto the reserved accessor's stub
        // returns null, so keep the name resolving to undefined instead.
        if (inputs.Optional.GetCryptoObject is not null)
        {
            var notCrypto = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "crypto");
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, notCrypto);
            il.Emit(OpCodes.Call, inputs.Optional.GetCryptoObject!);
            il.Emit(OpCodes.Br, returnLabel);
            il.MarkLabel(notCrypto);
        }

        // console / Reflect have no value-form singleton representation; keep
        // the historical null marker so syntactic dispatch (which fires before
        // this value-form path) stays authoritative for them.
        string[] nullMarkerNamespaces = ["console", "Reflect"];
        foreach (var ns in nullMarkerNamespaces)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, ns);
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brtrue, nullMarkerLabel);
        }

        // Default: return undefined
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        // globalThis / global self-reference → the runtime sentinel (#271).
        il.MarkLabel(globalThisRefLabel);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Br, returnLabel);

        // Null marker for namespaces whose value-form access stays null (legacy).
        il.MarkLabel(nullMarkerLabel);
        il.MarkLabel(selfRefLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, returnLabel);

        // undefined property
        il.MarkLabel(undefinedPropLabel);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        // NaN property
        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, returnLabel);

        // Infinity property
        il.MarkLabel(infinityLabel);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, returnLabel);

        // fetch property - return cached fetch TSFunction (only emitted when UsesFetch)
        if (inputs.Optional.FetchInvoke is not null)
        {
            il.MarkLabel(fetchLabel);
            EmitCachedTSFunction(il, inputs.CachedFetchFunction, inputs.Optional.FetchInvoke!, inputs.FunctionConstructor);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // parseInt — wrap NumberParseInt via $TSFunction.GetOrCreate so the
        // result has identity (parseInt === parseInt) AND equals Number.parseInt
        // (also wraps NumberParseInt). Per ECMA-262 Number.parseInt is the same
        // function object as the global parseInt.
        void EmitGetOrCreateTSFn(MethodBuilder wrappedMethod, string jsName, int jsLength)
        {
            il.Emit(OpCodes.Ldtoken, wrappedMethod);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Call, inputs.GetOrCreateFunction);
        }
        il.MarkLabel(parseIntLabel);
        EmitGetOrCreateTSFn(inputs.Numbers.ParseInt, "parseInt", 2);
        il.Emit(OpCodes.Br, returnLabel);

        // parseFloat — same pattern.
        il.MarkLabel(parseFloatLabel);
        EmitGetOrCreateTSFn(inputs.Numbers.ParseFloat, "parseFloat", 1);
        il.Emit(OpCodes.Br, returnLabel);

        // isNaN
        il.MarkLabel(isNaNLabel);
        EmitGetOrCreateTSFn(inputs.Numbers.IsNaN, "isNaN", 1);
        il.Emit(OpCodes.Br, returnLabel);

        // isFinite
        il.MarkLabel(isFiniteLabel);
        EmitGetOrCreateTSFn(inputs.Numbers.IsFinite, "isFinite", 1);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(encodeURIComponentLabel);
        EmitGetOrCreateTSFn(inputs.UriComponents.Encode, "encodeURIComponent", 1);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(decodeURIComponentLabel);
        EmitGetOrCreateTSFn(inputs.UriComponents.Decode, "decodeURIComponent", 1);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(evalLabel);
        EmitGetOrCreateTSFn(globalObject.IndirectEval, "eval", 1);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Indirect/value-form eval bridge. Non-string input is returned unchanged;
    /// string input uses the optional SharpTS interpreter bridge without adding
    /// a hard assembly reference to standalone output.
    /// </summary>
    private void EmitIndirectEval(TypeBuilder typeBuilder, EmittedGlobalObjectRuntime globalObject, FieldInfo undefinedInstance)
    {
        var method = typeBuilder.DefineMethod(
            "EvalIndirect", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        globalObject.IndirectEval = method;
        var il = method.GetILGenerator();

        var stringInput = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringInput);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(stringInput);

        var bridgeType = il.DeclareLocal(_types.Type);
        // Build the optional bridge name at runtime so standalone late-binding
        // audits keep the single established call site in GlobalFunctionHandler.
        il.Emit(OpCodes.Ldstr, "SharpTS.Execution.EvalBridge");
        il.Emit(OpCodes.Ldstr, ", SharpTS");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetType", _types.String));
        il.Emit(OpCodes.Stloc, bridgeType);
        var bridgePresent = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bridgeType);
        il.Emit(OpCodes.Brtrue, bridgePresent);
        il.Emit(OpCodes.Ldstr, "eval is not supported in standalone compiled output (SharpTS runtime not present).");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(bridgePresent);
        il.Emit(OpCodes.Ldloc, bridgeType);
        il.Emit(OpCodes.Ldstr, "Eval");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldsfld, undefinedInstance);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to load a cached TSFunction, creating it lazily if null.
    /// Pattern: if (cachedField == null) { cachedField = new TSFunction(null, methodInfo); } push cachedField;
    /// </summary>
    private void EmitCachedTSFunction(ILGenerator il, FieldBuilder cachedField, MethodBuilder wrappedMethod, ConstructorBuilder functionConstructor)
    {
        var alreadyCachedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cachedField);
        il.Emit(OpCodes.Brtrue, alreadyCachedLabel);
        // Create and cache the TSFunction
        il.Emit(OpCodes.Ldnull); // target (static method)
        il.Emit(OpCodes.Ldtoken, wrappedMethod);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, functionConstructor);
        il.Emit(OpCodes.Stsfld, cachedField);
        il.MarkLabel(alreadyCachedLabel);
        il.Emit(OpCodes.Ldsfld, cachedField);
    }

    /// <summary>
    /// Emits: public static void GlobalThisSetProperty(string name, object value)
    /// Sets a property on globalThis, storing in a static dictionary.
    /// </summary>
    private void EmitGlobalThisSetProperty(EmittedGlobalObjectRuntime globalObject, GlobalPropertyWriteInputs inputs)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 (#271).
        var method = (MethodBuilder)globalObject.SetProperty;

        var il = method.GetILGenerator();

        // Respect Object.defineProperty metadata installed on the global
        // sentinel. A non-writable data property silently rejects sloppy-mode
        // assignment. For a writable descriptor, update its live [[Value]] so
        // subsequent descriptor reflection and ordinary reads agree.
        var globalDescriptorLocal = il.DeclareLocal(inputs.DescriptorType);
        il.Emit(OpCodes.Ldsfld, globalObject.SingletonField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, globalDescriptorLocal);
        var noGlobalDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, globalDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noGlobalDescriptorLabel);
        il.Emit(OpCodes.Ldloc, globalDescriptorLocal);
        il.Emit(OpCodes.Callvirt, inputs.GetWritable);
        var globalDescriptorWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, globalDescriptorWritableLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(globalDescriptorWritableLabel);
        il.Emit(OpCodes.Ldloc, globalDescriptorLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, inputs.SetValue);
        il.MarkLabel(noGlobalDescriptorLabel);

        // Lazily initialize the dictionary: if (_globalThisProperties == null) _globalThisProperties = new();
        var dictReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, globalObject.Properties);
        il.Emit(OpCodes.Brtrue, dictReadyLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, globalObject.Properties);
        il.MarkLabel(dictReadyLabel);

        // _globalThisProperties[name] = value
        il.Emit(OpCodes.Ldsfld, globalObject.Properties);
        il.Emit(OpCodes.Ldarg_0); // name
        il.Emit(OpCodes.Ldarg_1); // value
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        il.Emit(OpCodes.Ret);
    }
}
