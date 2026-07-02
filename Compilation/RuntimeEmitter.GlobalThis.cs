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
        runtime.CachedFetchFunction = typeBuilder.DefineField(
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

        // Static dictionary for user-assigned globalThis properties
        runtime.GlobalThisProperties = typeBuilder.DefineField(
            "_globalThisProperties",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static);

        EmitGlobalThisGetProperty(typeBuilder, runtime);
        EmitGlobalThisSetProperty(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object GlobalThisGetProperty(string name)
    /// Gets a property from globalThis, checking user-assigned properties first,
    /// then delegating to built-ins.
    /// </summary>
    private void EmitGlobalThisGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 (#271) so the
        // property/index dispatchers emitted earlier can call it.
        var method = (MethodBuilder)runtime.GlobalThisGetProperty;

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
        var returnLabel = il.DefineLabel();
        var checkBuiltInsLabel = il.DefineLabel();

        // --- Check user-assigned properties dictionary first ---
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisProperties);
        il.Emit(OpCodes.Brfalse, checkBuiltInsLabel); // dict not initialized yet
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisProperties);
        il.Emit(OpCodes.Ldarg_0); // name
        il.Emit(OpCodes.Ldloca, valueLocal);
        var dictTryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType());
        il.Emit(OpCodes.Callvirt, dictTryGetValue);
        il.Emit(OpCodes.Brfalse, checkBuiltInsLabel); // not found in dict
        il.Emit(OpCodes.Ldloc, valueLocal);
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
        // identifier). The `runtime.Fetch` MethodBuilder is null otherwise.
        if (_features.UsesFetch)
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
        if (_features.UsesDate)
            EmitTypeBranch("Date", runtime.TSDateType);
        if (_features.UsesRegExp)
            EmitTypeBranch("RegExp", runtime.TSRegExpType);
        EmitTypeBranch("Map", _types.DictionaryObjectObject);
        EmitTypeBranch("Set", _types.HashSetOfObject);
        EmitTypeBranch("WeakMap", _types.ConditionalWeakTableObjectObject);
        EmitTypeBranch("WeakSet", _types.ConditionalWeakTableObjectObject);
        EmitTypeBranch("Promise", _types.TaskOfObject);
        if (_features.UsesBuffer)
            EmitTypeBranch("Buffer", runtime.TSBufferType);
        EmitTypeBranch("Function", runtime.TSFunctionType);
        if (_features.UsesTextEncoding)
        {
            EmitTypeBranch("TextEncoder", runtime.TSTextEncoderType);
            EmitTypeBranch("TextDecoder", runtime.TSTextDecoderType);
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
        EmitTypeBranch("Symbol", runtime.TSSymbolType);

        // Error and the native-error subclasses are constructor functions; expose
        // their .NET Type tokens so value-form `root.Error` / `root.TypeError`
        // resolve to the real constructors (lodash's runInContext reads
        // `context.Error` and `context.TypeError`). #271.
        EmitTypeBranch("Error", runtime.TSErrorType);
        EmitTypeBranch("TypeError", runtime.TSTypeErrorType);
        EmitTypeBranch("RangeError", runtime.TSRangeErrorType);
        EmitTypeBranch("ReferenceError", runtime.TSReferenceErrorType);
        EmitTypeBranch("SyntaxError", runtime.TSSyntaxErrorType);
        EmitTypeBranch("URIError", runtime.TSURIErrorType);
        EmitTypeBranch("EvalError", runtime.TSEvalErrorType);
        EmitTypeBranch("AggregateError", runtime.TSAggregateErrorType);

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
        EmitSingletonBranch("Math", runtime.MathSingletonField);
        EmitSingletonBranch("JSON", runtime.JsonSingletonField);

        // globalThis.process → the live $Process singleton (epic #1078), same
        // object as the bare `process` identifier and the module facade's
        // default export.
        {
            var notProcess = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "process");
            il.Emit(OpCodes.Call, strEquals);
            il.Emit(OpCodes.Brfalse, notProcess);
            il.Emit(OpCodes.Call, runtime.GetProcessObject);
            il.Emit(OpCodes.Br, returnLabel);
            il.MarkLabel(notProcess);
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
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, returnLabel);

        // globalThis / global self-reference → the runtime sentinel (#271).
        il.MarkLabel(globalThisRefLabel);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Br, returnLabel);

        // Null marker for namespaces whose value-form access stays null (legacy).
        il.MarkLabel(nullMarkerLabel);
        il.MarkLabel(selfRefLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, returnLabel);

        // undefined property
        il.MarkLabel(undefinedPropLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
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
        if (_features.UsesFetch)
        {
            il.MarkLabel(fetchLabel);
            EmitCachedTSFunction(il, runtime.CachedFetchFunction, runtime.Fetch, runtime);
            il.Emit(OpCodes.Br, returnLabel);
        }

        // parseInt — wrap NumberParseInt via $TSFunction.GetOrCreate so the
        // result has identity (parseInt === parseInt) AND equals Number.parseInt
        // (also wraps NumberParseInt). Per ECMA-262 Number.parseInt is the same
        // function object as the global parseInt.
        void EmitGetOrCreateTSFn(MethodBuilder wrappedMethod, string jsName, int jsLength)
        {
            il.Emit(OpCodes.Ldtoken, wrappedMethod);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Call, runtime.TSFunctionGetOrCreate);
        }
        il.MarkLabel(parseIntLabel);
        EmitGetOrCreateTSFn(runtime.NumberParseInt, "parseInt", 2);
        il.Emit(OpCodes.Br, returnLabel);

        // parseFloat — same pattern.
        il.MarkLabel(parseFloatLabel);
        EmitGetOrCreateTSFn(runtime.NumberParseFloat, "parseFloat", 1);
        il.Emit(OpCodes.Br, returnLabel);

        // isNaN
        il.MarkLabel(isNaNLabel);
        EmitGetOrCreateTSFn(runtime.NumberIsNaN, "isNaN", 1);
        il.Emit(OpCodes.Br, returnLabel);

        // isFinite
        il.MarkLabel(isFiniteLabel);
        EmitGetOrCreateTSFn(runtime.NumberIsFinite, "isFinite", 1);
        il.Emit(OpCodes.Br, returnLabel);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to load a cached TSFunction, creating it lazily if null.
    /// Pattern: if (cachedField == null) { cachedField = new TSFunction(null, methodInfo); } push cachedField;
    /// </summary>
    private void EmitCachedTSFunction(ILGenerator il, FieldBuilder cachedField, MethodBuilder wrappedMethod, EmittedRuntime runtime)
    {
        var alreadyCachedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, cachedField);
        il.Emit(OpCodes.Brtrue, alreadyCachedLabel);
        // Create and cache the TSFunction
        il.Emit(OpCodes.Ldnull); // target (static method)
        il.Emit(OpCodes.Ldtoken, wrappedMethod);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Stsfld, cachedField);
        il.MarkLabel(alreadyCachedLabel);
        il.Emit(OpCodes.Ldsfld, cachedField);
    }

    /// <summary>
    /// Emits: public static void GlobalThisSetProperty(string name, object value)
    /// Sets a property on globalThis, storing in a static dictionary.
    /// </summary>
    private void EmitGlobalThisSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 (#271).
        var method = (MethodBuilder)runtime.GlobalThisSetProperty;

        var il = method.GetILGenerator();

        // Lazily initialize the dictionary: if (_globalThisProperties == null) _globalThisProperties = new();
        var dictReadyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisProperties);
        il.Emit(OpCodes.Brtrue, dictReadyLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, runtime.GlobalThisProperties);
        il.MarkLabel(dictReadyLabel);

        // _globalThisProperties[name] = value
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisProperties);
        il.Emit(OpCodes.Ldarg_0); // name
        il.Emit(OpCodes.Ldarg_1); // value
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        il.Emit(OpCodes.Ret);
    }
}
