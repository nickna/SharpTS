using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a tiny dedicated type <c>$ArgumentsContext</c> holding the thread-static
    /// <c>_currentArguments</c> slot used to surface JS <c>arguments</c> in compiled
    /// mode (#64). See call-site comment in <c>EmitAll</c> for why this isolation
    /// matters — placing the field on <c>$TSFunction</c> or <c>$Runtime</c> alongside
    /// other ThreadStatic slots regressed an Intl test in layout-dependent ways.
    /// </summary>
    private void EmitArgumentsContextClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ArgumentsContext",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        var field = typeBuilder.DefineField(
            "_currentArguments",
            _types.ObjectArray,
            FieldAttributes.Public | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        field.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.CurrentArgumentsField = field;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a minimal marker attribute <c>$CapturesArguments</c> (empty
    /// <see cref="System.Attribute"/> subclass). Applied to function-declaration
    /// methods whose body reads JS <c>arguments</c>; read back via
    /// <see cref="System.Reflection.MemberInfo.IsDefined(Type, bool)"/> at
    /// <c>$TSFunction</c> construction. Lives in the output assembly so the
    /// compiled DLL stays standalone.
    /// </summary>
    private void EmitCapturesArgumentsAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$CapturesArguments",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ret);
        runtime.CapturesArgumentsAttrCtor = ctor;
        runtime.CapturesArgumentsAttrType = typeBuilder;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a minimal marker attribute <c>$PadUndefined</c> (empty
    /// <see cref="System.Attribute"/> subclass). Applied to user TypeScript function
    /// methods (declarations, arrows/function expressions, methods, async stubs); read
    /// back via <see cref="System.Reflection.MemberInfo.IsDefined(Type, bool)"/> at
    /// <c>$TSFunction</c> construction to decide whether <c>AdjustArgs</c> pads omitted
    /// trailing arguments with the <c>undefined</c> sentinel rather than CLR null (#640).
    /// Lives in the output assembly so the compiled DLL stays standalone.
    /// </summary>
    private void EmitPadUndefinedAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$PadUndefined",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ret);
        runtime.PadUndefinedAttrCtor = ctor;
        runtime.PadUndefinedAttrType = typeBuilder;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits <c>$FunctionLength(int)</c>, which preserves an emitted method's ECMAScript
    /// <c>Function.length</c>. CLR parameter metadata does not retain where a JavaScript default
    /// initializer first appeared, so class methods surfaced through reflection need this value.
    /// </summary>
    private void EmitFunctionLengthAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$FunctionLength",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var valueField = typeBuilder.DefineField(
            "Length", typeof(int), FieldAttributes.Public | FieldAttributes.InitOnly);
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [typeof(int)]);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, valueField);
        ctorIl.Emit(OpCodes.Ret);
        runtime.FunctionLengthAttrType = typeBuilder;
        runtime.FunctionLengthAttrCtor = ctor;
        runtime.FunctionLengthAttrValueField = valueField;
        typeBuilder.CreateType();
    }

    private void EmitFunctionNameAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$FunctionName",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var valueField = typeBuilder.DefineField(
            "Name", typeof(string), FieldAttributes.Public | FieldAttributes.InitOnly);
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, valueField);
        ctorIl.Emit(OpCodes.Ret);
        runtime.FunctionNameAttrType = typeBuilder;
        runtime.FunctionNameAttrCtor = ctor;
        runtime.FunctionNameAttrValueField = valueField;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Marks callable methods which do not implement ECMAScript [[Construct]].
    /// Arrow, async, and generator functions therefore have no own `prototype`
    /// property and reject construction even though they share $TSFunction.
    /// </summary>
    private void EmitNonConstructibleAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$NonConstructible",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ret);
        runtime.NonConstructibleAttrCtor = ctor;
        runtime.NonConstructibleAttrType = typeBuilder;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a minimal marker attribute <c>$ExpectsThis</c> (empty <see cref="System.Attribute"/>
    /// subclass) applied to a user function-expression / <c>this</c>-bearing arrow method, whose
    /// first emitted parameter is the synthetic <c>__this</c> receiver slot. <c>$TSFunction</c>
    /// normally detects that slot by parameter name (<c>params[0].Name == "__this"</c>), but a
    /// reference-assembly rewrite (<c>--compile --ref-asm</c>) strips parameter names, so the name
    /// check fails and the receiver slot is miscounted as a real argument — shifting a value-call's
    /// arguments by one. Custom attributes survive the rewrite, so reading this marker back via
    /// <see cref="System.Reflection.MemberInfo.IsDefined(Type, bool)"/> restores correct detection.
    /// Lives in the output assembly so the compiled DLL stays standalone. (#738)
    /// </summary>
    private void EmitExpectsThisAttribute(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$ExpectsThis",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(System.Attribute));
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(System.Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!);
        ctorIl.Emit(OpCodes.Ret);
        runtime.ExpectsThisAttrCtor = ctor;
        runtime.ExpectsThisAttrType = typeBuilder;
        typeBuilder.CreateType();
    }

    private void EmitTSFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSFunction
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TSFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSFunctionType = typeBuilder;

        // Fields
        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);
        var methodField = typeBuilder.DefineField("_method", _types.MethodInfo, FieldAttributes.Private);
        _ = methodField;
        // Cached name and length for functions where reflection doesn't work (e.g., MethodBuilder tokens)
        var cachedNameField = typeBuilder.DefineField("_cachedName", _types.String, FieldAttributes.Private);
        var cachedLengthField = typeBuilder.DefineField("_cachedLength", _types.Int32, FieldAttributes.Private);
        // Cached "first parameter is the synthetic __this slot" flag. Computed
        // ONCE at ctor time from MethodInfo.GetParameters()[0].Name, then read
        // from the field on every InvokeWithThis call. Pre-cache: every iter
        // helper invocation of the callback paid GetParameters + string-compare
        // through reflection — N=1M Map = 1M reflection lookups. Now: 1 lookup
        // at construction, 1 ldfld per call.
        // Exposed Public so iterator helpers in $Runtime can read it directly
        // for the "unary-arrow fast path" without going through a method call.
        var expectsThisField = typeBuilder.DefineField("_expectsThis", _types.Boolean, FieldAttributes.Public);
        runtime.TSFunctionExpectsThisField = expectsThisField;
        // True when the wrapped method's body reads JS `arguments` (marked with
        // the $CapturesArguments attribute). Public so the iterator-helper
        // skip-index-box detection can read it without a method call.
        var capturesArgumentsField = typeBuilder.DefineField("_capturesArguments", _types.Boolean, FieldAttributes.Public);
        runtime.TSFunctionCapturesArgumentsField = capturesArgumentsField;
        // Bitmask of parameter positions that AdjustArgs pads with the `undefined`
        // sentinel (instead of CLR null) when the argument is omitted. Bit i is set iff
        // the wrapped method carries the $PadUndefined marker (i.e. is a user TS function)
        // AND parameter i has an `object` slot. Object slots can hold the sentinel, so
        // `typeof`/`=== undefined`/default-firing all work. An optional or *defaulted*
        // param is widened to an `object` slot upstream (ParameterTypeResolver), so it
        // lands here with its bit set and behaves correctly across the boundary (#925).
        // A still-typed slot (string/double) is a genuinely *required* typed parameter —
        // it can't coerce the sentinel, so it stays null-padded (an omitted required arg
        // is a type error anyway). Built-ins have an all-zero mask and keep pure null
        // padding (their bodies use null-checks for optional-arg absence). Positions >= 32
        // are not tracked (such arity is never reached in practice) and pad null. (#640, #925)
        var padUndefinedMaskField = typeBuilder.DefineField("_padUndefinedMask", _types.Int32, FieldAttributes.Private);
        // Cached MethodInvoker. .NET 8+'s MethodInvoker.Create() pre-builds
        // the JIT'd dispatch stub for a method, then Invoke(...) calls it
        // directly — measured ~10× faster than MethodInfo.Invoke per call.
        // Invoke() goes through this on every callback dispatch (N=1M = 1M
        // reflection-style invocations); replacing reflection with a cached
        // invoker is the single biggest improvement to callback dispatch.
        var invokerField = typeBuilder.DefineField("_invoker", _types.MethodInvoker, FieldAttributes.Private);
        // AdjustArgs cache: paramCount + rest-param shape. Pre-cache, every
        // Invoke paid GetParameters + ParameterType lookups to decide between
        // pad / trim / rest-list / rest-array branches. Computed once at
        // construction, read as field loads thereafter.
        var paramCountField = typeBuilder.DefineField("_paramCount", _types.Int32, FieldAttributes.Public);
        runtime.TSFunctionParamCountField = paramCountField;
        var hasListRestField = typeBuilder.DefineField("_hasListRest", _types.Boolean, FieldAttributes.Private);
        var hasArrayRestField = typeBuilder.DefineField("_hasArrayRest", _types.Boolean, FieldAttributes.Private);
        // Set true at construction iff ConvertArgsForUnionTypes or
        // CoercePrimitiveArgs might do something non-trivial — i.e., any param
        // is a Union_* value type or one of the coerce-target types
        // (string/double/int/List<object>). For typical arrow callbacks
        // (`x => x*2` etc.) all params are `object`, so this is false and Invoke
        // skips both helper calls entirely. Saves 2 reflection-driven loops per
        // callback dispatch.
        var needsArgConversionField = typeBuilder.DefineField("_needsArgConversion", _types.Boolean, FieldAttributes.Private);

        // Static cache for "this" fields: ConcurrentDictionary<Type, FieldInfo>
        // used to avoid reflection overhead in BindThis
        var fieldCacheType = _types.MakeGenericType(_types.ConcurrentDictionaryOpen, _types.Type, _types.FieldInfo);
        var fieldCacheField = typeBuilder.DefineField("_thisFieldCache", fieldCacheType, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);

        // Static instance cache keyed by MethodInfo plus JS metadata. Every reference to a
        // function declaration (`function f(){}` followed by `f` used as a value)
        // previously created a fresh $TSFunction wrapper — breaking
        // identity-keyed property storage in PropertyDescriptorStore. Result:
        // `fn.x = 42` wrote under wrapper A's identity, `fn.x` read under a new
        // wrapper B → returned null. Most visibly, the test262 harness does
        // `function assert(...){}; assert.sameValue = function(...){}` and
        // then silently failed to dispatch `assert.sameValue(a, b)` (typeof
        // returned "object"), turning real spec failures into false-positive
        // Passes. The JS name and length are part of the key because distinct
        // built-ins can share one runtime helper (for example String.prototype
        // toString/valueOf and toLowerCase/toLocaleLowerCase). Keying only by
        // MethodInfo collapsed those aliases onto the first wrapper populated
        // and leaked its metadata. Function declarations use a stable emitted
        // name/arity, so repeated references still return the same instance.
        // Only applies to target=null wrappers (static function declarations) —
        // bound/closure wrappers carry state and must stay fresh.
        var instanceCacheKeyType = _types.MakeGenericType(typeof(ValueTuple<,,>),
            _types.MethodInfo, _types.String, _types.Int32);
        var instanceCacheType = _types.MakeGenericType(
            _types.ConcurrentDictionaryOpen, instanceCacheKeyType, typeBuilder);
        var instanceCacheField = typeBuilder.DefineField("_instanceCache", instanceCacheType, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        _ = instanceCacheField;

        // Static MethodInvoker cache keyed by MethodInfo. MethodInvoker.Create()
        // builds an internal dispatch stub that's amortized only when the
        // invoker is reused — calling Create per TSFunction allocation paid
        // ~25-50μs of setup that crushed small-N benchmarks (Map/100 went 6.7→57μs).
        // Multiple TSFunctions wrapping the same MethodInfo can safely share an
        // invoker (the invoker is purely an implementation detail of dispatch,
        // observable identity stays on the TSFunction itself).
        var invokerCacheType = _types.MakeGenericType(_types.ConcurrentDictionaryOpen, _types.MethodInfo, _types.MethodInvoker);
        var invokerCacheField = typeBuilder.DefineField("_invokerCache", invokerCacheType, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);

        // Static prototype cache keyed by MethodInfo. Per JS spec every
        // function has a `.prototype` object whose identity persists across
        // references: `fn.prototype === fn.prototype` holds. Keying by
        // MethodInfo (rather than $TSFunction instance) means two wrappers
        // for the same declaration return the SAME prototype — even when
        // the instance-level cache isn't in use. This unblocks `fn.prototype.x = v`
        // and `this instanceof fn` correctness without requiring the wider
        // identity cache to be wired up at both emit sites. Value-type is
        // object rather than $TSObject so there's no TypeBuilder generic-arg
        // issue at cctor time — the runtime value is always a $TSObject.
        var prototypeCacheType = _types.MakeGenericType(_types.ConcurrentDictionaryOpen, _types.MethodInfo, _types.Object);
        var prototypeCacheField = typeBuilder.DefineField("_prototypeCache", prototypeCacheType, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.TSFunctionPrototypeCacheField = prototypeCacheField;

        // Thread-static "current function this" slot. Reads in compiled function bodies
        // (LocalVariableResolver.LoadThis's final fallback path) pick this up when the
        // method has no `__this` parameter. Set + restored around InvokeWithThis below
        // (so `Fn.call(target, ...)` routes properties to target) and around
        // $Runtime.NewOnFunction's body (so `new Fn(...)` routes to the fresh instance).
        // Lives on $TSFunction rather than $Runtime so InvokeWithThis can reference it
        // at TSFunction-emit time — $Runtime hasn't been built yet at this point.
        var currentThisField = typeBuilder.DefineField(
            "_currentFunctionThis",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        currentThisField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.CurrentFunctionThisField = currentThisField;

        // Note: the thread-static `_currentArguments` slot used for JS `arguments` is
        // defined on $ArgumentsContext (emitted before this type) — see
        // EmitArgumentsContextClass.

        // Static Constructor
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(fieldCacheType));
        cctorIL.Emit(OpCodes.Stsfld, fieldCacheField);
        // Generic ConcurrentDictionary whose value-type is the enclosing TypeBuilder —
        // member resolution on such a constructed type must go through
        // TypeBuilder.GetConstructor/GetMethod rather than .GetConstructor directly.
        var concurrentDictOpenCtor = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>).GetConstructor(Type.EmptyTypes)!;
        cctorIL.Emit(OpCodes.Newobj, EmitterTypeHelpers.ResolveConstructor(instanceCacheType, concurrentDictOpenCtor));
        cctorIL.Emit(OpCodes.Stsfld, instanceCacheField);
        // Prototype cache: ConcurrentDictionary<MethodInfo, object> — generic args
        // are both concrete CLR types, so the constructor resolves directly.
        cctorIL.Emit(OpCodes.Newobj, _types.GetConstructor(prototypeCacheType, Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Stsfld, prototypeCacheField);
        // Invoker cache: ConcurrentDictionary<MethodInfo, MethodInvoker>.
        cctorIL.Emit(OpCodes.Newobj, _types.GetConstructor(invokerCacheType, Type.EmptyTypes)!);
        cctorIL.Emit(OpCodes.Stsfld, invokerCacheField);
        // Bare calls start with ECMAScript undefined. Sloppy bodies coerce it
        // to globalThis when they read `this`; strict bodies preserve it.
        cctorIL.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        cctorIL.Emit(OpCodes.Stsfld, currentThisField);
        cctorIL.Emit(OpCodes.Ret);

        // Constructor: public $TSFunction(object target, MethodInfo method)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.MethodInfo]
        );
        runtime.TSFunctionCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._target = target
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        // this._method = method
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, methodField);
        // this._cachedLength = -1 (sentinel for "not cached")
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_M1);
        ctorIL.Emit(OpCodes.Stfld, cachedLengthField);
        // this._cachedName = null (will use reflection)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldnull);
        ctorIL.Emit(OpCodes.Stfld, cachedNameField);
        // The Function-family constructors are exposed as CLR Type tokens. A
        // reflective `new Function()` has no emitted method body to wrap, so
        // NewOnType supplies a null MethodInfo. Keep that callable shell valid
        // for object operations (seal/freeze/prototype inspection); all of the
        // metadata caches below require a real method.
        var noMethodLabel = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Brfalse, noMethodLabel);
        // User methods carry their ECMAScript arity explicitly because reflection cannot
        // recover the "stop at the first default initializer" rule.
        EmitComputeFunctionLength(ctorIL, cachedLengthField, runtime, methodArgIndex: 2);
        EmitComputeFunctionName(ctorIL, cachedNameField, runtime, methodArgIndex: 2);
        // this._expectsThis = (method.GetParameters().Length > 0 && params[0].Name == "__this")
        EmitComputeExpectsThis(ctorIL, expectsThisField, runtime, methodArgIndex: 2);
        // this._capturesArguments = method.IsDefined($CapturesArguments)
        EmitComputeCapturesArguments(ctorIL, capturesArgumentsField, runtime, methodArgIndex: 2);
        // this._padUndefinedMask = $PadUndefined ? (object-param bits) : 0
        EmitComputePadUndefinedMask(ctorIL, padUndefinedMaskField, runtime, methodArgIndex: 2);
        // this._paramCount, _hasListRest, _hasArrayRest: cached by AdjustArgs.
        EmitComputeAdjustArgsCache(ctorIL, paramCountField, hasListRestField, hasArrayRestField, methodArgIndex: 2);
        EmitComputeNeedsArgConversion(ctorIL, needsArgConversionField, methodArgIndex: 2);
        // this._invoker = LookupOrAdd(_invokerCache, method)  [pseudocode]
        EmitLookupOrCreateInvoker(ctorIL, invokerField, invokerCacheField, invokerCacheType, methodArgIndex: 2);
        ctorIL.MarkLabel(noMethodLabel);
        ctorIL.Emit(OpCodes.Ret);

        // Alternative constructor with cached name/length: public $TSFunction(object target, MethodInfo method, string name, int length)
        // Use this constructor when the MethodInfo might not support GetParameters() (e.g., MethodBuilder tokens in persisted assemblies)
        var ctorWithCacheBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.MethodInfo, _types.String, _types.Int32]
        );
        runtime.TSFunctionCtorWithCache = ctorWithCacheBuilder;

        var ctorCacheIL = ctorWithCacheBuilder.GetILGenerator();
        // Call base constructor
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._target = target
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_1);
        ctorCacheIL.Emit(OpCodes.Stfld, targetField);
        // this._method = method
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_2);
        ctorCacheIL.Emit(OpCodes.Stfld, methodField);
        // this._cachedName = name
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg_3);
        ctorCacheIL.Emit(OpCodes.Stfld, cachedNameField);
        // this._cachedLength = length
        ctorCacheIL.Emit(OpCodes.Ldarg_0);
        ctorCacheIL.Emit(OpCodes.Ldarg, 4);  // 4th argument (0-indexed: 0=this, 1=target, 2=method, 3=name, 4=length)
        ctorCacheIL.Emit(OpCodes.Stfld, cachedLengthField);
        var noCachedMethodLabel = ctorCacheIL.DefineLabel();
        ctorCacheIL.Emit(OpCodes.Ldarg_2);
        ctorCacheIL.Emit(OpCodes.Brfalse, noCachedMethodLabel);
        // this._expectsThis = (method.GetParameters().Length > 0 && params[0].Name == "__this")
        EmitComputeExpectsThis(ctorCacheIL, expectsThisField, runtime, methodArgIndex: 2);
        EmitComputeCapturesArguments(ctorCacheIL, capturesArgumentsField, runtime, methodArgIndex: 2);
        EmitComputePadUndefinedMask(ctorCacheIL, padUndefinedMaskField, runtime, methodArgIndex: 2);
        EmitComputeAdjustArgsCache(ctorCacheIL, paramCountField, hasListRestField, hasArrayRestField, methodArgIndex: 2);
        EmitComputeNeedsArgConversion(ctorCacheIL, needsArgConversionField, methodArgIndex: 2);
        // this._invoker = LookupOrAdd(_invokerCache, method)
        EmitLookupOrCreateInvoker(ctorCacheIL, invokerField, invokerCacheField, invokerCacheType, methodArgIndex: 2);
        ctorCacheIL.MarkLabel(noCachedMethodLabel);
        ctorCacheIL.Emit(OpCodes.Ret);

        // Static factory: public static $TSFunction GetOrCreate(MethodInfo method, string name, int length).
        // Returns a cached $TSFunction for the given method + JS metadata, creating one if
        // not present. Used only for function DECLARATIONS (target=null), where
        // identity must be stable across references for PropertyDescriptorStore
        // to work (`fn.x = v` → `fn.x` roundtrips). Cached values are required
        // because MethodBuilder tokens don't reflect at runtime.
        var getOrCreateBuilder = typeBuilder.DefineMethod(
            "GetOrCreate",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            [_types.MethodInfo, _types.String, _types.Int32]
        );
        runtime.TSFunctionGetOrCreate = getOrCreateBuilder;
        var gocIL = getOrCreateBuilder.GetILGenerator();

        var cacheKeyLocal = gocIL.DeclareLocal(instanceCacheKeyType);
        var existingLocal = gocIL.DeclareLocal(typeBuilder);
        var newInstLocal = gocIL.DeclareLocal(typeBuilder);

        var cacheKeyCtor = _types.GetConstructor(instanceCacheKeyType,
            _types.MethodInfo, _types.String, _types.Int32)!;

        // key = (method, name, length)
        gocIL.Emit(OpCodes.Ldarg_0);
        gocIL.Emit(OpCodes.Ldarg_1);
        gocIL.Emit(OpCodes.Ldarg_2);
        gocIL.Emit(OpCodes.Newobj, cacheKeyCtor);
        gocIL.Emit(OpCodes.Stloc, cacheKeyLocal);

        // Resolve methods via TypeBuilder.GetMethod because the value-type is
        // still an open TypeBuilder at emit time.
        var concurrentDictOpen = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>);
        var openTryGet = concurrentDictOpen.GetMethod("TryGetValue")!;
        var openGetOrAdd = concurrentDictOpen.GetMethods()
            .First(m => m.Name == "GetOrAdd" && m.GetParameters().Length == 2
                 && m.GetParameters()[1].ParameterType == m.DeclaringType!.GetGenericArguments()[1]);
        var tryGetValueM = EmitterTypeHelpers.ResolveMethod(instanceCacheType, openTryGet);
        var getOrAddM = EmitterTypeHelpers.ResolveMethod(instanceCacheType, openGetOrAdd);

        // if (_instanceCache.TryGetValue(key, out existing)) return existing;
        gocIL.Emit(OpCodes.Ldsfld, instanceCacheField);
        gocIL.Emit(OpCodes.Ldloc, cacheKeyLocal);
        gocIL.Emit(OpCodes.Ldloca, existingLocal);
        gocIL.Emit(OpCodes.Callvirt, tryGetValueM);
        var notFound = gocIL.DefineLabel();
        gocIL.Emit(OpCodes.Brfalse, notFound);
        gocIL.Emit(OpCodes.Ldloc, existingLocal);
        gocIL.Emit(OpCodes.Ret);

        // Not in cache: create new via the cached-name/length constructor
        // (MethodBuilder tokens don't reflect at runtime), try to add, return
        // cache value (handles races).
        gocIL.MarkLabel(notFound);
        gocIL.Emit(OpCodes.Ldnull);            // target = null (static method)
        gocIL.Emit(OpCodes.Ldarg_0);           // method
        gocIL.Emit(OpCodes.Ldarg_1);           // name
        gocIL.Emit(OpCodes.Ldarg_2);           // length
        gocIL.Emit(OpCodes.Newobj, ctorWithCacheBuilder);
        gocIL.Emit(OpCodes.Stloc, newInstLocal);

        // return _instanceCache.GetOrAdd(key, newInst)
        gocIL.Emit(OpCodes.Ldsfld, instanceCacheField);
        gocIL.Emit(OpCodes.Ldloc, cacheKeyLocal);
        gocIL.Emit(OpCodes.Ldloc, newInstLocal);
        gocIL.Emit(OpCodes.Callvirt, getOrAddM);
        gocIL.Emit(OpCodes.Ret);

        // Public getter for the internal _method field so sibling classes
        // (e.g., $Runtime.GetFunctionMethod's prototype cache) can read the
        // MethodInfo key without a FieldAccessException. A method is used
        // instead of making the field Public so SharpTSProxy.InvokeTrap's
        // reflection-based `_method` lookup (BindingFlags.NonPublic) keeps
        // working unchanged.
        var getMethodInfoBuilder = typeBuilder.DefineMethod(
            "GetMethodInfo",
            MethodAttributes.Public,
            _types.MethodInfo,
            Type.EmptyTypes
        );
        runtime.TSFunctionGetMethodInfo = getMethodInfoBuilder;
        var gmIL = getMethodInfoBuilder.GetILGenerator();
        gmIL.Emit(OpCodes.Ldarg_0);
        gmIL.Emit(OpCodes.Ldfld, methodField);
        gmIL.Emit(OpCodes.Ret);

        var getTargetBuilder = typeBuilder.DefineMethod(
            "GetTarget",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        runtime.TSFunctionGetTarget = getTargetBuilder;
        var gtIL = getTargetBuilder.GetILGenerator();
        gtIL.Emit(OpCodes.Ldarg_0);
        gtIL.Emit(OpCodes.Ldfld, targetField);
        gtIL.Emit(OpCodes.Ret);

        // Helper method: private static object[] AdjustArgs(MethodInfo method, object[] args)
        var adjustArgsMethod = EmitTSFunctionAdjustArgsHelper(typeBuilder, runtime, paramCountField, hasListRestField, hasArrayRestField, padUndefinedMaskField);

        // Helper method: private static void ConvertArgsForUnionTypes(MethodInfo method, object[] args)
        var convertArgsMethod = EmitTSFunctionConvertArgsHelper(typeBuilder, runtime);

        // Helper method: private static void CoercePrimitiveArgs(MethodInfo method, object[] args)
        // Coerces args whose target parameter type is `string` via $Runtime.ToJsString.
        // Used for borrowed prototype methods like
        // `Number.prototype.split = String.prototype.split; new Number(x).split(...)`
        // where the wrapped helper expects (string, ...) but receives a non-string `this`.
        var coercePrimitivesMethod = EmitTSFunctionCoercePrimitivesHelper(typeBuilder, runtime);

        // Invoke method: public object Invoke(object[] args)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.TSFunctionInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();

        // Local variables
        var effectiveArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var invokeTargetLocal = invokeIL.DeclareLocal(_types.Object);

        // Check if this is a static method with a bound target
        // if (_method.IsStatic && _target != null)
        var notStaticWithTarget = invokeIL.DefineLabel();
        var afterArgPrep = invokeIL.DefineLabel();

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsStatic")!.GetGetMethod()!);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Brfalse, notStaticWithTarget);

        // Static method with bound target: prepend target to args
        // effectiveArgs = new object[args.Length + 1];
        // effectiveArgs[0] = _target;
        // Array.Copy(args, 0, effectiveArgs, 1, args.Length);
        // invokeTarget = null;
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Add);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);

        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stelem_Ref);

        invokeIL.Emit(OpCodes.Ldarg_1);  // source
        invokeIL.Emit(OpCodes.Ldc_I4_0); // sourceIndex
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal); // dest
        invokeIL.Emit(OpCodes.Ldc_I4_1); // destIndex
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);  // length
        invokeIL.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);

        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Br, afterArgPrep);

        // Not a static with target: use args directly, target is _target
        invokeIL.MarkLabel(notStaticWithTarget);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Stloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Stloc, invokeTargetLocal);

        invokeIL.MarkLabel(afterArgPrep);

        // Adjust args for rest parameters and padding/trimming
        // adjustedArgs = this.AdjustArgs(effectiveArgs) — instance call, reads
        // cached fields instead of GetParameters reflection per invoke.
        var adjustedArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldloc, effectiveArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, adjustArgsMethod);
        invokeIL.Emit(OpCodes.Stloc, adjustedArgsLocal);

        // Skip both Convert and Coerce helpers when the cached
        // _needsArgConversion flag says no param needs union or primitive
        // conversion. For typical arrow callbacks (`x => x*2` etc., all
        // params typed `object`), this is the case and we save 2
        // reflection-driven loops per dispatch.
        var skipArgConversionLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, needsArgConversionField);
        invokeIL.Emit(OpCodes.Brfalse, skipArgConversionLabel);

        // Convert args for union types before invoking
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Call, convertArgsMethod);

        // Coerce string-typed params from non-string args via $Runtime.ToJsString.
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, methodField);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Call, coercePrimitivesMethod);

        invokeIL.MarkLabel(skipArgConversionLabel);

        // Publish the un-adjusted caller args to the thread-static $Runtime._currentArguments
        // slot so flagged function bodies (those that reference JS `arguments`) can see
        // values beyond their declared arity — lodash overRest pattern from #64. No
        // try/finally restore: the prologue on the callee side reads once at entry and
        // clears immediately, and each new Invoke re-sets for its own callee, so the
        // value never needs to be restored to a prior state here.
        if (runtime.CurrentArgumentsField != null)
        {
            invokeIL.Emit(OpCodes.Ldarg_1);
            invokeIL.Emit(OpCodes.Stsfld, runtime.CurrentArgumentsField);
        }

        // Use cached MethodInvoker for fast dispatch — its Span<object?>
        // overload skips reflection-Invoke's per-call setup and is ~10×
        // faster on hot paths. The Span<object?> wraps the existing
        // adjustedArgs array (no extra allocation).
        var spanOfObject = _types.MakeGenericType(typeof(Span<>), typeof(object));
        var spanCtorFromArray = _types.GetConstructor(spanOfObject, [typeof(object[])])!;
        var invokerInvokeSpan = _types.GetMethod(_types.MethodInvoker, "Invoke", [_types.Object, spanOfObject])!;

        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, invokerField);
        invokeIL.Emit(OpCodes.Ldloc, invokeTargetLocal);
        invokeIL.Emit(OpCodes.Ldloc, adjustedArgsLocal);
        invokeIL.Emit(OpCodes.Newobj, spanCtorFromArray);
        invokeIL.Emit(OpCodes.Callvirt, invokerInvokeSpan);
        invokeIL.Emit(OpCodes.Ret);

        // InvokeWithThis method: public object InvokeWithThis(object thisArg, object[] args)
        // This checks if the first parameter is named "__this" and prepends thisArg if so
        var invokeWithThisBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        runtime.TSFunctionInvokeWithThis = invokeWithThisBuilder;

        var iwt = invokeWithThisBuilder.GetILGenerator();

        // Local variables
        var expectsThisLocal = iwt.DeclareLocal(_types.Boolean);
        var effectiveArgsIWT = iwt.DeclareLocal(_types.ObjectArray);

        // expectsThis = this._expectsThis  (cached at construction time —
        // pre-cache this was a per-call MethodInfo.GetParameters() + string
        // compare, ~5-15% of the hot iterator dispatch budget)
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, expectsThisField);
        iwt.Emit(OpCodes.Stloc, expectsThisLocal);

        // if (!expectsThis) { route thisArg through the thread-local; call Invoke(args) }
        // The function body has no __this parameter (e.g. `function Fn() { this.x = ... }`),
        // so the only way the user's `this` reaches it is via LocalVariableResolver.LoadThis's
        // thread-local fallback. Set + restore around the Invoke so `Fn.call(target, ...)`
        // routes property writes to target. NewOnFunction does the same set/restore around
        // its own InvokeWithThis call — both layers of save/restore are no-ops at the inner
        // layer (same value), so they nest correctly.
        var expectsThisLabel = iwt.DefineLabel();
        iwt.Emit(OpCodes.Ldloc, expectsThisLocal);
        iwt.Emit(OpCodes.Brtrue, expectsThisLabel);

        // Save current thread-local this, set to thisArg, call Invoke, restore in finally.
        var prevThisIWT = iwt.DeclareLocal(_types.Object);
        var resultIWT = iwt.DeclareLocal(_types.Object);

        iwt.Emit(OpCodes.Ldsfld, currentThisField);
        iwt.Emit(OpCodes.Stloc, prevThisIWT);

        // Preserve the raw receiver. The emitted body owns OrdinaryCallBindThis:
        // strict functions keep null/undefined, while sloppy functions coerce
        // both to globalThis. Normalizing here erased that distinction.
        iwt.Emit(OpCodes.Ldarg_1);
        iwt.Emit(OpCodes.Stsfld, currentThisField);

        iwt.BeginExceptionBlock();
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Callvirt, invokeBuilder);
        iwt.Emit(OpCodes.Stloc, resultIWT);
        iwt.BeginFinallyBlock();
        iwt.Emit(OpCodes.Ldloc, prevThisIWT);
        iwt.Emit(OpCodes.Stsfld, currentThisField);
        iwt.EndExceptionBlock();

        iwt.Emit(OpCodes.Ldloc, resultIWT);
        iwt.Emit(OpCodes.Ret);

        // expectsThis is true - prepend thisArg to args, then dispatch
        // directly via the cached _invoker, choosing invokeTarget based on
        // method.IsStatic. This bypasses the regular Invoke() body so we
        // avoid its `IsStatic && _target != null` branch (which would
        // double-prepend the bound _target on top of the thisArg we just
        // injected — and trim the real argument off the tail in AdjustArgs).
        //
        // Two distinct call shapes converge here:
        //   • Static helper with `__this` first param + bound _target (e.g.
        //     EmitObjProtoMethodCheck's wrappers): _target is a host dict
        //     used by direct `dict.method(arg)` dispatch. For .call(other,...)
        //     dispatch we WANT to drop _target and use the explicit thisArg.
        //     IsStatic=true → invokeTarget=null.
        //   • Instance method on display class with `__this` first param +
        //     display-instance _target (anon function expressions with
        //     captures): _target IS the IL-level receiver of the method, NOT
        //     a JS-level "this". The JS this is the injected thisArg, the IL
        //     receiver MUST stay as _target. IsStatic=false → invokeTarget=_target.
        iwt.MarkLabel(expectsThisLabel);

        // effectiveArgs = new object[args.Length + 1]
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Ldlen);
        iwt.Emit(OpCodes.Conv_I4);
        iwt.Emit(OpCodes.Ldc_I4_1);
        iwt.Emit(OpCodes.Add);
        iwt.Emit(OpCodes.Newarr, _types.Object);
        iwt.Emit(OpCodes.Stloc, effectiveArgsIWT);

        // effectiveArgs[0] = thisArg, with a narrow fallback to _target when
        // thisArg is CLR null AND the method is static (a built-in
        // helper like the RegExp Symbol.* protocol methods, NOT a display-
        // class instance method). For static helpers, indexed-method dispatch
        // (`re[Symbol.X](arg)` compiled as InvokeMethodValue(receiver=null,
        // fn, [arg])) loses the receiver — the TSFunction was constructed
        // with `_target=re` and we want to recover that. For instance methods
        // (anon function expressions with captures), `_target` is the IL-level
        // display-class receiver, NOT the JS `this`, so the fallback must NOT
        // fire there — that path keeps `this` as the explicit thisArg, which
        // for non-strict bare calls is correctly globalThis (null in our
        // model).
        var fallbackProbeLabel = iwt.DefineLabel();
        var afterFallbackLabel = iwt.DefineLabel();

        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT);
        iwt.Emit(OpCodes.Ldc_I4_0);

        // Only the static-helper case considers _target as the JS this.
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, methodField);
        iwt.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsStatic")!.GetGetMethod()!);
        iwt.Emit(OpCodes.Brfalse, useThisArgDirectLabelDecl(out var useThisArgDirectLabel));

        // Static method: CLR null means the generic value-call path lost an
        // implicit method receiver, so probe the bound target. The explicit JS
        // undefined sentinel is a real thisArgument (Promise reaction jobs,
        // strict calls) and must never be replaced by _target.
        iwt.Emit(OpCodes.Ldarg_1);
        iwt.Emit(OpCodes.Brfalse, fallbackProbeLabel);
        // thisArg is a real Object — use it.
        iwt.Emit(OpCodes.Ldarg_1);
        iwt.Emit(OpCodes.Br, afterFallbackLabel);

        iwt.MarkLabel(fallbackProbeLabel);
        // _target != null → use _target; else keep the original thisArg.
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, targetField);
        iwt.Emit(OpCodes.Dup);
        iwt.Emit(OpCodes.Brtrue, afterFallbackLabel);
        iwt.Emit(OpCodes.Pop);
        iwt.Emit(OpCodes.Ldarg_1);
        iwt.Emit(OpCodes.Br, afterFallbackLabel);

        iwt.MarkLabel(useThisArgDirectLabel);
        // Instance method: thisArg is the JS `this` as-is.
        iwt.Emit(OpCodes.Ldarg_1);

        iwt.MarkLabel(afterFallbackLabel);
        iwt.Emit(OpCodes.Stelem_Ref);

        // Local helper: declare-and-return a label so the inline IsStatic
        // check above can short-circuit to a yet-to-be-marked target without
        // breaking the emit-flow expression.
        Label useThisArgDirectLabelDecl(out Label l)
        {
            l = iwt.DefineLabel();
            return l;
        }

        // Array.Copy(args, 0, effectiveArgs, 1, args.Length)
        iwt.Emit(OpCodes.Ldarg_2);  // source
        iwt.Emit(OpCodes.Ldc_I4_0); // sourceIndex
        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT); // dest
        iwt.Emit(OpCodes.Ldc_I4_1); // destIndex
        iwt.Emit(OpCodes.Ldarg_2);
        iwt.Emit(OpCodes.Ldlen);
        iwt.Emit(OpCodes.Conv_I4);  // length
        iwt.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);

        // adjustedArgs = this.AdjustArgs(effectiveArgs)
        var iwtAdjustedArgsLocal = iwt.DeclareLocal(_types.ObjectArray);
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT);
        iwt.Emit(OpCodes.Callvirt, adjustArgsMethod);
        iwt.Emit(OpCodes.Stloc, iwtAdjustedArgsLocal);

        // Optional Convert/Coerce gated on _needsArgConversion.
        var iwtSkipConvLabel = iwt.DefineLabel();
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, needsArgConversionField);
        iwt.Emit(OpCodes.Brfalse, iwtSkipConvLabel);
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, methodField);
        iwt.Emit(OpCodes.Ldloc, iwtAdjustedArgsLocal);
        iwt.Emit(OpCodes.Call, convertArgsMethod);
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, methodField);
        iwt.Emit(OpCodes.Ldloc, iwtAdjustedArgsLocal);
        iwt.Emit(OpCodes.Call, coercePrimitivesMethod);
        iwt.MarkLabel(iwtSkipConvLabel);

        // Publish effectiveArgs (thisArg + caller args) to thread-static.
        // Mirrors what Invoke() did pre-fix when called from this path:
        // function bodies that read JS `arguments` skip the leading __this
        // slot and expect the rest to be the user's actual arguments. Using
        // raw Ldarg_2 here would empty out `arguments` for any `arguments.length`
        // / `arguments[i]` access from inside a function-expression body.
        if (runtime.CurrentArgumentsField != null)
        {
            iwt.Emit(OpCodes.Ldloc, effectiveArgsIWT);
            iwt.Emit(OpCodes.Stsfld, runtime.CurrentArgumentsField);
        }

        // invokeTarget = method.IsStatic ? null : _target
        var iwtInvokeTargetLocal = iwt.DeclareLocal(_types.Object);
        var iwtIsInstanceLabel = iwt.DefineLabel();
        var iwtAfterTargetLabel = iwt.DefineLabel();
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, methodField);
        iwt.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "IsStatic")!.GetGetMethod()!);
        iwt.Emit(OpCodes.Brfalse, iwtIsInstanceLabel);
        // Static: invokeTarget = null
        iwt.Emit(OpCodes.Ldnull);
        iwt.Emit(OpCodes.Stloc, iwtInvokeTargetLocal);
        iwt.Emit(OpCodes.Br, iwtAfterTargetLabel);
        // Instance: invokeTarget = _target (display-class instance)
        iwt.MarkLabel(iwtIsInstanceLabel);
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, targetField);
        iwt.Emit(OpCodes.Stloc, iwtInvokeTargetLocal);
        iwt.MarkLabel(iwtAfterTargetLabel);

        // _invoker.Invoke(invokeTarget, new Span<object>(adjustedArgs))
        var iwtSpanOfObject = _types.MakeGenericType(typeof(Span<>), typeof(object));
        var iwtSpanCtor = _types.GetConstructor(iwtSpanOfObject, [typeof(object[])])!;
        var iwtInvokerInvokeSpan = _types.GetMethod(_types.MethodInvoker, "Invoke", [_types.Object, iwtSpanOfObject])!;
        iwt.Emit(OpCodes.Ldarg_0);
        iwt.Emit(OpCodes.Ldfld, invokerField);
        iwt.Emit(OpCodes.Ldloc, iwtInvokeTargetLocal);
        iwt.Emit(OpCodes.Ldloc, iwtAdjustedArgsLocal);
        iwt.Emit(OpCodes.Newobj, iwtSpanCtor);
        iwt.Emit(OpCodes.Callvirt, iwtInvokerInvokeSpan);
        iwt.Emit(OpCodes.Ret);

        // ToString method
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function]");
        toStringIL.Emit(OpCodes.Ret);

        // BindThis method: public void BindThis(object thisValue)
        // Sets the 'this' field in the display class to the given value
        var bindThisBuilder = typeBuilder.DefineMethod(
            "BindThis",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        runtime.TSFunctionBindThis = bindThisBuilder;

        var bindThisIL = bindThisBuilder.GetILGenerator();
        var noTargetLabel = bindThisIL.DefineLabel();
        var endLabel = bindThisIL.DefineLabel();
        var thisFieldLocal = bindThisIL.DeclareLocal(_types.FieldInfo);
        var targetTypeLocal = bindThisIL.DeclareLocal(_types.Type);

        // if (_target == null) return;
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // targetType = _target.GetType();
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType")!);
        bindThisIL.Emit(OpCodes.Stloc, targetTypeLocal);

        // Try get from cache
        // if (!_thisFieldCache.TryGetValue(targetType, out thisField))
        bindThisIL.Emit(OpCodes.Ldsfld, fieldCacheField);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldloca, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Callvirt, _types.GetMethod(fieldCacheType, "TryGetValue", [_types.Type, _types.FieldInfo.MakeByRefType()])!);
        var cacheHitLabel = bindThisIL.DefineLabel();
        bindThisIL.Emit(OpCodes.Brtrue, cacheHitLabel);

        // Cache miss: lookup field
        // thisField = targetType.GetField("this", BindingFlags.Public | BindingFlags.Instance);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldstr, "this");
        bindThisIL.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
        bindThisIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", [_types.String, _types.BindingFlags])!);
        bindThisIL.Emit(OpCodes.Stloc, thisFieldLocal);

        // Store in cache (even if null, but ConcurrentDictionary doesn't allow null values if we used that, but here we can just skip if null)
        // Actually, if null, we shouldn't cache null if we use TryGetValue. 
        // Let's simplify: if field is found, cache it. If not found, we don't cache (or cache a dummy? no need to overcomplicate).

        var fieldNullLabel = bindThisIL.DefineLabel();
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Brfalse, fieldNullLabel);

        // Cache it
        bindThisIL.Emit(OpCodes.Ldsfld, fieldCacheField);
        bindThisIL.Emit(OpCodes.Ldloc, targetTypeLocal);
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Callvirt, _types.GetMethod(fieldCacheType, "TryAdd", [_types.Type, _types.FieldInfo])!);
        bindThisIL.Emit(OpCodes.Pop); // discard bool result

        bindThisIL.MarkLabel(fieldNullLabel);
        bindThisIL.MarkLabel(cacheHitLabel);

        // if (thisField == null) return;
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Brfalse, noTargetLabel);

        // thisField.SetValue(_target, thisValue);
        bindThisIL.Emit(OpCodes.Ldloc, thisFieldLocal);
        bindThisIL.Emit(OpCodes.Ldarg_0);
        bindThisIL.Emit(OpCodes.Ldfld, targetField);
        bindThisIL.Emit(OpCodes.Ldarg_1);
        bindThisIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "SetValue", [_types.Object, _types.Object])!);
        bindThisIL.Emit(OpCodes.Br, endLabel);

        bindThisIL.MarkLabel(noTargetLabel);
        bindThisIL.MarkLabel(endLabel);
        bindThisIL.Emit(OpCodes.Ret);

        // Length property: returns the number of required parameters (excluding rest, optional, and those with defaults)
        // public int get_Length()
        var lengthGetterBuilder = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TSFunctionLengthGetter = lengthGetterBuilder;

        var lengthIL = lengthGetterBuilder.GetILGenerator();
        var paramsLocalLength = lengthIL.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var countLocal = lengthIL.DeclareLocal(_types.Int32);
        var indexLocalLength = lengthIL.DeclareLocal(_types.Int32);
        var paramLocalLength = lengthIL.DeclareLocal(_types.ParameterInfo);
        var lengthLoopStart = lengthIL.DefineLabel();
        var lengthLoopEnd = lengthIL.DefineLabel();
        var incrementCount = lengthIL.DefineLabel();
        var skipParam = lengthIL.DefineLabel();
        var returnZero = lengthIL.DefineLabel();
        var useCachedLength = lengthIL.DefineLabel();
        var computeLength = lengthIL.DefineLabel();

        // Check if _cachedLength >= 0 (cached value available)
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, cachedLengthField);
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Bge, useCachedLength);
        lengthIL.Emit(OpCodes.Br, computeLength);

        // Return cached length
        lengthIL.MarkLabel(useCachedLength);
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, cachedLengthField);
        lengthIL.Emit(OpCodes.Ret);

        // Compute length via reflection
        lengthIL.MarkLabel(computeLength);

        // Check if _method is null - if so, return 0
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, methodField);
        lengthIL.Emit(OpCodes.Brfalse, returnZero);

        // params = _method.GetParameters()
        lengthIL.Emit(OpCodes.Ldarg_0);
        lengthIL.Emit(OpCodes.Ldfld, methodField);
        lengthIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetParameters")!);
        lengthIL.Emit(OpCodes.Stloc, paramsLocalLength);

        // Check if params is null - if so, return 0
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Brfalse, returnZero);

        // count = 0
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Stloc, countLocal);

        // index = 0
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Stloc, indexLocalLength);

        // Loop through parameters
        lengthIL.MarkLabel(lengthLoopStart);
        // if (index >= params.Length) goto end
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Ldlen);
        lengthIL.Emit(OpCodes.Conv_I4);
        lengthIL.Emit(OpCodes.Bge, lengthLoopEnd);

        // param = params[index]
        lengthIL.Emit(OpCodes.Ldloc, paramsLocalLength);
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldelem_Ref);
        lengthIL.Emit(OpCodes.Stloc, paramLocalLength);

        // Skip if param.IsOptional (has default value or is optional)
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "IsOptional")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);

        // Skip if param type is List<object> (rest parameter)
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        lengthIL.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        lengthIL.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);

        // Skip if param name starts with "__" (internal parameters like __this).
        // ParameterInfo.Name can be null on emitted MethodBuilder methods that
        // didn't supply parameter names (e.g. MathFloorAdapter takes (object)
        // unnamed). Guard the StartsWith with a null check — null name means
        // "regular parameter, count it".
        var nameLocal = lengthIL.DeclareLocal(_types.String);
        lengthIL.Emit(OpCodes.Ldloc, paramLocalLength);
        lengthIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "Name")!.GetGetMethod()!);
        lengthIL.Emit(OpCodes.Stloc, nameLocal);
        lengthIL.Emit(OpCodes.Ldloc, nameLocal);
        var nameNotNullLabel = lengthIL.DefineLabel();
        lengthIL.Emit(OpCodes.Brtrue, nameNotNullLabel);
        lengthIL.Emit(OpCodes.Br, incrementCount);
        lengthIL.MarkLabel(nameNotNullLabel);
        lengthIL.Emit(OpCodes.Ldloc, nameLocal);
        lengthIL.Emit(OpCodes.Ldstr, "__");
        lengthIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
        lengthIL.Emit(OpCodes.Brtrue, skipParam);
        lengthIL.MarkLabel(incrementCount);

        // count++
        lengthIL.Emit(OpCodes.Ldloc, countLocal);
        lengthIL.Emit(OpCodes.Ldc_I4_1);
        lengthIL.Emit(OpCodes.Add);
        lengthIL.Emit(OpCodes.Stloc, countLocal);

        lengthIL.MarkLabel(skipParam);
        // index++
        lengthIL.Emit(OpCodes.Ldloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Ldc_I4_1);
        lengthIL.Emit(OpCodes.Add);
        lengthIL.Emit(OpCodes.Stloc, indexLocalLength);
        lengthIL.Emit(OpCodes.Br, lengthLoopStart);

        lengthIL.MarkLabel(lengthLoopEnd);
        lengthIL.Emit(OpCodes.Ldloc, countLocal);
        lengthIL.Emit(OpCodes.Ret);

        // Return 0 if _method or params was null
        lengthIL.MarkLabel(returnZero);
        lengthIL.Emit(OpCodes.Ldc_I4_0);
        lengthIL.Emit(OpCodes.Ret);

        // Define Length property
        var lengthProperty = typeBuilder.DefineProperty(
            "Length",
            PropertyAttributes.None,
            _types.Int32,
            Type.EmptyTypes
        );
        lengthProperty.SetGetMethod(lengthGetterBuilder);

        // Name property: returns the method name
        // public string get_Name()
        var nameGetterBuilder = typeBuilder.DefineMethod(
            "get_Name",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSFunctionNameGetter = nameGetterBuilder;

        var nameIL = nameGetterBuilder.GetILGenerator();
        var nameReturnEmpty = nameIL.DefineLabel();
        var useCachedName = nameIL.DefineLabel();
        var computeName = nameIL.DefineLabel();

        // Check if _cachedName is not null (cached value available)
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, cachedNameField);
        nameIL.Emit(OpCodes.Brtrue, useCachedName);
        nameIL.Emit(OpCodes.Br, computeName);

        // Return cached name
        nameIL.MarkLabel(useCachedName);
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, cachedNameField);
        nameIL.Emit(OpCodes.Ret);

        // Compute name via reflection
        nameIL.MarkLabel(computeName);

        // Check if _method is null - if so, return ""
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, methodField);
        nameIL.Emit(OpCodes.Brfalse, nameReturnEmpty);

        // return _method.Name
        nameIL.Emit(OpCodes.Ldarg_0);
        nameIL.Emit(OpCodes.Ldfld, methodField);
        nameIL.Emit(OpCodes.Callvirt, _types.GetProperty(_types.MethodInfo, "Name")!.GetGetMethod()!);
        nameIL.Emit(OpCodes.Ret);

        // Return "" if _method was null
        nameIL.MarkLabel(nameReturnEmpty);
        nameIL.Emit(OpCodes.Ldstr, "");
        nameIL.Emit(OpCodes.Ret);

        // Define Name property
        var nameProperty = typeBuilder.DefineProperty(
            "Name",
            PropertyAttributes.None,
            _types.String,
            Type.EmptyTypes
        );
        nameProperty.SetGetMethod(nameGetterBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits IL inside a constructor to populate <c>this._invoker</c> via the
    /// static cache: if the cache contains a <see cref="System.Reflection.MethodInvoker"/>
    /// for this <see cref="System.Reflection.MethodInfo"/>, reuse it; otherwise
    /// create one and add. Crucially avoids paying <c>MethodInvoker.Create</c>'s
    /// setup cost (~25-50μs) per TSFunction allocation when the same MethodInfo
    /// has already been seen.
    /// </summary>
    private void EmitLookupOrCreateInvoker(ILGenerator il, FieldBuilder invokerField,
        FieldBuilder invokerCacheField, Type invokerCacheType, int methodArgIndex)
    {
        var existingLocal = il.DeclareLocal(_types.MethodInvoker);
        var newInvokerLocal = il.DeclareLocal(_types.MethodInvoker);

        // ConcurrentDictionary<MethodInfo, MethodInvoker> — both generic args
        // are concrete CLR types, so methods resolve directly off the
        // constructed type (no TypeBuilder.GetMethod wrapping needed; that
        // path only applies when an arg is itself a TypeBuilder).
        var tryGetValueM = _types.GetMethod(invokerCacheType, "TryGetValue",
            [_types.MethodInfo, _types.MethodInvoker.MakeByRefType()])!;
        var getOrAddM = _types.GetMethod(invokerCacheType, "GetOrAdd",
            [_types.MethodInfo, _types.MethodInvoker])!;

        var foundLabel = il.DefineLabel();

        // if (_invokerCache.TryGetValue(method, out existing)) goto found
        il.Emit(OpCodes.Ldsfld, invokerCacheField);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueM);
        il.Emit(OpCodes.Brtrue, foundLabel);

        // Not in cache: create + GetOrAdd (handles race; either our or another
        // thread's invoker wins, both work the same)
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodInvoker, "Create", [_types.MethodBase])!);
        il.Emit(OpCodes.Stloc, newInvokerLocal);

        il.Emit(OpCodes.Ldsfld, invokerCacheField);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldloc, newInvokerLocal);
        il.Emit(OpCodes.Callvirt, getOrAddM);
        il.Emit(OpCodes.Stloc, existingLocal);

        il.MarkLabel(foundLabel);
        // this._invoker = existing
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Stfld, invokerField);
    }

    /// <summary>
    /// Emits IL inside a constructor to populate <c>_paramCount</c>,
    /// <c>_hasListRest</c>, <c>_hasArrayRest</c> from the method's parameters.
    /// AdjustArgs reads these instead of calling GetParameters/ParameterType
    /// per invocation. Reflection cost is paid once at construction time.
    /// </summary>
    private void EmitComputeAdjustArgsCache(ILGenerator il, FieldBuilder paramCountField,
        FieldBuilder hasListRestField, FieldBuilder hasArrayRestField, int methodArgIndex)
    {
        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var paramCountLocal = il.DeclareLocal(_types.Int32);
        var lastTypeLocal = il.DeclareLocal(_types.Type);
        var hasListRestLocal = il.DeclareLocal(_types.Boolean);

        // var ps = method.GetParameters();
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // var paramCount = ps.Length;  this._paramCount = paramCount;
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, paramCountLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Stfld, paramCountField);

        // if (paramCount == 0) goto done — no rest param possible
        var hasParamsLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Brtrue, hasParamsLabel);
        // Both bools default to false (zero-init by the runtime).
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(hasParamsLabel);
        // var lastType = ps[paramCount - 1].ParameterType;
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lastTypeLocal);

        // A final List<object> normally represents a rest parameter. The one
        // exception is a sole synthetic `__this` slot on Array.prototype
        // zero-argument helpers (entries/keys/values/toReversed): treating that
        // receiver as rest would replace a missing receiver with an empty list.
        il.Emit(OpCodes.Ldloc, lastTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Stloc, hasListRestLocal);
        var storeHasListRestLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hasListRestLocal);
        il.Emit(OpCodes.Brfalse, storeHasListRestLabel);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, storeHasListRestLabel);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__this");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, storeHasListRestLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasListRestLocal);
        il.MarkLabel(storeHasListRestLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hasListRestLocal);
        il.Emit(OpCodes.Stfld, hasListRestField);

        // this._hasArrayRest = (lastType == typeof(object[]))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, lastTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.ObjectArray);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Stfld, hasArrayRestField);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits IL inside a constructor to compute and store the cached
    /// <c>_needsArgConversion</c> bool. True iff any param is a
    /// <c>Union_*</c> value type (handled by <c>ConvertArgsForUnionTypes</c>)
    /// or one of the coerce-target types (handled by <c>CoercePrimitiveArgs</c>:
    /// string/double/int32/List&lt;object&gt;). For typical arrow callbacks
    /// with all-<c>object</c> params, this is false and Invoke skips both
    /// helpers entirely.
    /// </summary>
    private void EmitComputeNeedsArgConversion(ILGenerator il, FieldBuilder needsArgConversionField, int methodArgIndex)
    {
        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var iLocal = il.DeclareLocal(_types.Int32);
        var paramTypeLocal = il.DeclareLocal(_types.Type);

        var loopBody = il.DefineLabel();
        var loopCond = il.DefineLabel();
        var setTrueLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var notValueType = il.DefineLabel();

        // ps = method.GetParameters()
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopBody);
        // pt = ps[i].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paramTypeLocal);

        // Check coerce-target types: string, double, int32, List<object>
        EmitTypeEqAndBranchTrue(il, paramTypeLocal, _types.String, setTrueLabel);
        EmitTypeEqAndBranchTrue(il, paramTypeLocal, _types.Double, setTrueLabel);
        EmitTypeEqAndBranchTrue(il, paramTypeLocal, _types.Int32, setTrueLabel);
        EmitTypeEqAndBranchTrue(il, paramTypeLocal, _types.ListOfObject, setTrueLabel);

        // Check Union_* value type
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsValueType")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notValueType);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "Union_");
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, setTrueLabel);
        il.MarkLabel(notValueType);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopBody);

        // No match: _needsArgConversion = false (zero-init default — explicit for clarity)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, needsArgConversionField);
        il.Emit(OpCodes.Br, doneLabel);

        // Match: _needsArgConversion = true
        il.MarkLabel(setTrueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, needsArgConversionField);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits IL: <c>if (paramTypeLocal == typeof(targetType)) goto matchLabel;</c>
    /// Helper to tighten the Union_/coerce-target-type pattern in
    /// <see cref="EmitComputeNeedsArgConversion"/>.
    /// </summary>
    private void EmitTypeEqAndBranchTrue(ILGenerator il, LocalBuilder paramTypeLocal, Type targetType, Label matchLabel)
    {
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldtoken, targetType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, matchLabel);
    }

    /// <summary>
    /// Emits IL inside a constructor to compute and store the cached <c>_expectsThis</c> bool field:
    /// <c>this._expectsThis = (params.Length > 0 &amp;&amp; params[0].Name == "__this")
    /// || method.IsDefined(typeof($ExpectsThis), false)</c>. The name check is the primary path; the
    /// <c>$ExpectsThis</c> attribute backstops it because a <c>--ref-asm</c> rewrite strips parameter
    /// names (so the name check fails there, otherwise shifting a value-call's arguments). Caller
    /// provides the field and the constructor argument index of the <c>MethodInfo</c> param. (#738)
    /// </summary>
    private void EmitComputeExpectsThis(ILGenerator il, FieldBuilder expectsThisField, EmittedRuntime runtime, int methodArgIndex)
    {
        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var resultLocal = il.DeclareLocal(_types.Boolean);
        var checkAttr = il.DefineLabel();
        var store = il.DefineLabel();

        // params = method.GetParameters()
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // nameMatch = params.Length > 0 && params[0].Name == "__this"
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, checkAttr);   // 0 params → fall back to the attribute

        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__this");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brtrue, store);        // name matched → done

        // Backstop: resultLocal = method.IsDefined(typeof($ExpectsThis), false) — survives the
        // parameter-name strip the name check above does not.
        il.MarkLabel(checkAttr);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.ExpectsThisAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(store);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Stfld, expectsThisField);
    }

    /// <summary>
    /// Emits <c>this._capturesArguments = method.IsDefined(typeof($CapturesArguments), false)</c>.
    /// True for function-declaration methods whose body reads JS <c>arguments</c>;
    /// see <see cref="EmitCapturesArgumentsAttribute"/>.
    /// </summary>
    private void EmitComputeCapturesArguments(ILGenerator il, FieldBuilder capturesArgumentsField, EmittedRuntime runtime, int methodArgIndex)
    {
        // this._capturesArguments = method.IsDefined(attrType, false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.CapturesArgumentsAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        // MemberInfo.IsDefined(Type, bool) — looked up via MethodInfo (inherited).
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Stfld, capturesArgumentsField);
    }

    /// <summary>
    /// Replaces the <c>-1</c> reflection fallback in <c>_cachedLength</c> when the wrapped method
    /// carries <c>$FunctionLength</c>. Built-ins remain unmarked and retain reflection behavior.
    /// </summary>
    private void EmitComputeFunctionLength(ILGenerator il, FieldBuilder cachedLengthField, EmittedRuntime runtime, int methodArgIndex)
    {
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.FunctionLengthAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.FunctionLengthAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.MethodInfo, "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.FunctionLengthAttrType);
        il.Emit(OpCodes.Ldfld, runtime.FunctionLengthAttrValueField);
        il.Emit(OpCodes.Stfld, cachedLengthField);

        il.MarkLabel(done);
    }

    private void EmitComputeFunctionName(ILGenerator il, FieldBuilder cachedNameField, EmittedRuntime runtime, int methodArgIndex)
    {
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.FunctionNameAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.FunctionNameAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetCustomAttributes", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.FunctionNameAttrType);
        il.Emit(OpCodes.Ldfld, runtime.FunctionNameAttrValueField);
        il.Emit(OpCodes.Stfld, cachedNameField);
        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits computation of <c>this._padUndefinedMask</c>. Zero unless the method carries the
    /// <c>$PadUndefined</c> marker (i.e. is a user TS function), in which case bit <c>i</c> is set
    /// for every parameter <c>i &lt; 32</c> whose slot is <c>object</c>. <c>AdjustArgs</c> pads only
    /// those positions with the <c>undefined</c> sentinel; typed slots and built-ins keep null
    /// padding. See <see cref="EmitPadUndefinedAttribute"/>. (#640)
    /// </summary>
    private void EmitComputePadUndefinedMask(ILGenerator il, FieldBuilder padUndefinedMaskField, EmittedRuntime runtime, int methodArgIndex)
    {
        var done = il.DefineLabel();

        // if (!method.IsDefined($PadUndefined, false)) leave mask at its zero-init value.
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Ldtoken, runtime.PadUndefinedAttrType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
        il.Emit(OpCodes.Brfalse, done);

        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var iLocal = il.DeclareLocal(_types.Int32);
        var maskLocal = il.DeclareLocal(_types.Int32);
        var nLocal = il.DeclareLocal(_types.Int32);

        // ps = method.GetParameters(); n = ps.Length; mask = 0; i = 0;
        il.Emit(OpCodes.Ldarg, methodArgIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, maskLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopBody = il.DefineLabel();
        var loopCond = il.DefineLabel();
        var nextIter = il.DefineLabel();
        var storeMask = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopBody);
        // if (ps[i].ParameterType != typeof(object)) goto nextIter;
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, nextIter);
        // mask |= (1 << i);
        il.Emit(OpCodes.Ldloc, maskLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, maskLocal);

        il.MarkLabel(nextIter);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        // while (i < n && i < 32)
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Bge, storeMask);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Blt, loopBody);

        il.MarkLabel(storeMask);
        // this._padUndefinedMask = mask;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, maskLocal);
        il.Emit(OpCodes.Stfld, padUndefinedMaskField);

        il.MarkLabel(done);
    }

    /// <summary>
    /// Emits an instance helper method <c>$TSFunction.AdjustArgs(object[] args)</c>
    /// that adjusts argument arrays for rest parameters and padding/trimming
    /// using cached field reads (<c>_paramCount</c>, <c>_hasListRest</c>,
    /// <c>_hasArrayRest</c>) instead of per-call <c>MethodInfo.GetParameters</c>
    /// + <c>ParameterType</c> reflection.
    /// </summary>
    private MethodBuilder EmitTSFunctionAdjustArgsHelper(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder paramCountField, FieldBuilder hasListRestField, FieldBuilder hasArrayRestField,
        FieldBuilder padUndefinedMaskField)
    {
        // private object[] AdjustArgs(object[] args)
        // Instance method: arg0 = this, arg1 = args. paramCount and rest-shape
        // are read from cached fields populated at construction time.
        var method = typeBuilder.DefineMethod(
            "AdjustArgs",
            MethodAttributes.Private,
            _types.ObjectArray,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Local variables
        var paramCountLocal = il.DeclareLocal(_types.Int32);
        var argsLengthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ObjectArray);
        var restListLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var copyCountLocal = il.DeclareLocal(_types.Int32);

        // paramCount = this._paramCount (cached at construction)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, paramCountField);
        il.Emit(OpCodes.Stloc, paramCountLocal);

        // argsLength = args.Length
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLengthLocal);

        // Labels
        var notRestParam = il.DefineLabel();
        var needsPadding = il.DefineLabel();
        var needsTrimming = il.DefineLabel();
        var restLoopStart = il.DefineLabel();
        var restLoopEnd = il.DefineLabel();

        // if (paramCount == 0) goto notRestParam — no rest possible; standard path
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Brfalse, notRestParam);

        // if (!_hasListRest) goto notListRestParam
        var notListRestParam = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hasListRestField);
        il.Emit(OpCodes.Brfalse, notListRestParam);

        // === REST PARAMETER HANDLING (List<object>) ===
        // result = new object[paramCount]
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        // regularParamCount = paramCount - 1
        // copyCount = min(argsLength, regularParamCount)
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, copyCountLocal);

        // if (copyCount > 0) Array.Copy(args, result, copyCount)
        var skipCopy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, copyCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipCopy);
        il.Emit(OpCodes.Ldarg_1); // source
        il.Emit(OpCodes.Ldloc, resultLocal); // dest
        il.Emit(OpCodes.Ldloc, copyCountLocal); // length
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.MarkLabel(skipCopy);

        // restList = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, restListLocal);

        // for (i = paramCount - 1; i < argsLength; i++) restList.Add(args[i])
        // i = paramCount - 1 (start of rest args)
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(restLoopStart);
        // if (i >= argsLength) goto restLoopEnd
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Bge, restLoopEnd);

        // restList.Add(args[i])
        il.Emit(OpCodes.Ldloc, restListLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, restLoopStart);

        il.MarkLabel(restLoopEnd);

        // result[paramCount - 1] = restList
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, restListLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // === REST PARAMETER HANDLING (object[]) ===
        // Handles methods like $EventEmitter.Emit(string, object[]) called via runtime dispatch
        il.MarkLabel(notListRestParam);
        // if (!_hasArrayRest) goto notRestParam
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hasArrayRestField);
        il.Emit(OpCodes.Brfalse, notRestParam);

        // Collect trailing args into object[] for the last parameter
        // result = new object[paramCount]
        var arrayRestResult = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, arrayRestResult);

        // Copy regular params: Array.Copy(args, result, min(argsLength, paramCount - 1))
        var regularCount = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, regularCount);

        var skipRegularCopy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, regularCount);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipRegularCopy);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, regularCount);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, copyCountLocal);
        il.Emit(OpCodes.Ldloc, copyCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipRegularCopy);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, arrayRestResult);
        il.Emit(OpCodes.Ldloc, copyCountLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.MarkLabel(skipRegularCopy);

        // restCount = max(0, argsLength - regularCount)
        var restCountLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, regularCount);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, restCountLocal);

        // restArray = new object[restCount]
        var restArrayLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldloc, restCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, restArrayLocal);

        // if (restCount > 0) Array.Copy(args, regularCount, restArray, 0, restCount)
        var skipRestCopy = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, restCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipRestCopy);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, regularCount);
        il.Emit(OpCodes.Ldloc, restArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, restCountLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.MarkLabel(skipRestCopy);

        // result[paramCount - 1] = restArray
        il.Emit(OpCodes.Ldloc, arrayRestResult);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, restArrayLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // return result
        il.Emit(OpCodes.Ldloc, arrayRestResult);
        il.Emit(OpCodes.Ret);

        // === NOT A REST PARAMETER - STANDARD PADDING/TRIMMING ===
        il.MarkLabel(notRestParam);

        // if (argsLength == paramCount) return args
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Bne_Un, needsPadding);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(needsPadding);
        // if (argsLength >= paramCount) goto needsTrimming
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Bge, needsTrimming);

        // Pad missing args: result = new object[paramCount]; Array.Copy(args, result, argsLength)
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);

        // Pad value for the missing trailing slots, per the cached _padUndefinedMask.
        //
        // A slot whose mask bit is set (user TS function + `object`-typed parameter) is filled
        // with the `undefined` sentinel, matching JS semantics and the direct-call path — so
        // `typeof`, `=== undefined`, and `=== null` answer correctly for an omitted optional
        // parameter, and an object-slotted default-valued parameter's entry prologue fires.
        // Optional and value-type-defaulted params are widened to `object` upstream
        // (ParameterTypeResolver), so they get the sentinel here for every user-function kind —
        // declarations, arrows, methods, and async/generator/async-generator functions (#925).
        //
        // Every other slot is left as CLR null (the Newarr zero value). That covers built-ins
        // (mask 0 — their bodies use null-checks for optional-arg absence: stream write/end
        // callbacks, encodings, ...) and genuinely value-typed *required* user params (a `double`
        // can't hold the sentinel; an omitted required arg is a type error regardless). The
        // one built-in that must distinguish absent (skip) from explicit JS null (throw) —
        // value-form Object.create's props — gets a dedicated wrapper instead
        // (ObjectCreateValueForm in RuntimeEmitter.Objects.Prototype.cs). (#640, #925)
        var skipUndefinedPad = il.DefineLabel();
        // if (_padUndefinedMask == 0) skip — fast path for built-ins and all-typed signatures.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, padUndefinedMaskField);
        il.Emit(OpCodes.Brfalse, skipUndefinedPad);
        // for (int i = argsLength; i < paramCount; i++)
        //     if (((_padUndefinedMask >> i) & 1) != 0) result[i] = $Undefined.Instance;
        var padIdxLocal = il.DeclareLocal(_types.Int32);
        var padLoopBody = il.DefineLabel();
        var padLoopCond = il.DefineLabel();
        var padSkipSlot = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLengthLocal);
        il.Emit(OpCodes.Stloc, padIdxLocal);
        il.Emit(OpCodes.Br, padLoopCond);
        il.MarkLabel(padLoopBody);
        // if (((mask >> i) & 1) == 0) goto next;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, padUndefinedMaskField);
        il.Emit(OpCodes.Ldloc, padIdxLocal);
        il.Emit(OpCodes.Shr_Un);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, padSkipSlot);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, padIdxLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stelem_Ref);
        il.MarkLabel(padSkipSlot);
        il.Emit(OpCodes.Ldloc, padIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, padIdxLocal);
        il.MarkLabel(padLoopCond);
        il.Emit(OpCodes.Ldloc, padIdxLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Blt, padLoopBody);
        il.MarkLabel(skipUndefinedPad);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Trim: result = new object[paramCount]; Array.Copy(args, result, paramCount)
        il.MarkLabel(needsTrimming);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, paramCountLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.ArrayType, _types.Int32])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a private static helper method on $TSFunction to convert arguments for union type parameters.
    /// </summary>
    private MethodBuilder EmitTSFunctionConvertArgsHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static void ConvertArgsForUnionTypes(MethodInfo method, object[] args)
        // Wraps raw primitive values into union types using op_Implicit operators.
        var method = typeBuilder.DefineMethod(
            "ConvertArgsForUnionTypes",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.MethodInfo, _types.ObjectArray]
        );

        var il = method.GetILGenerator();

        // Locals: 0=ParameterInfo[], 1=int count, 2=int i, 3=Type paramType, 4=Type argType, 5=MethodInfo implicitOp
        var paramsLocal = il.DeclareLocal(typeof(ParameterInfo[]));
        var countLocal = il.DeclareLocal(typeof(int));
        var iLocal = il.DeclareLocal(typeof(int));
        var paramTypeLocal = il.DeclareLocal(_types.Type);
        var argTypeLocal = il.DeclareLocal(_types.Type);
        var implicitOpLocal = il.DeclareLocal(_types.MethodInfo);

        // params = method.GetParameters()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // count = Math.Min(args.Length, params.Length)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stloc, countLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopConditionLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

        il.Emit(OpCodes.Br, loopConditionLabel);

        il.MarkLabel(loopBodyLabel);

        // paramType = params[i].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(ParameterInfo).GetProperty("ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paramTypeLocal);

        // if (!paramType.IsValueType) continue
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsValueType")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // if (!paramType.Name.StartsWith("Union_")) continue
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "Union_");
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // if (args[i] == null) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // argType = args[i].GetType()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType")!);
        il.Emit(OpCodes.Stloc, argTypeLocal);

        // if (argType == paramType) continue
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, continueLabel);

        // implicitOp = paramType.GetMethod("op_Implicit", new Type[] { argType })
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldstr, "op_Implicit");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Type);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argTypeLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", [typeof(string), typeof(Type[])])!);
        il.Emit(OpCodes.Stloc, implicitOpLocal);

        // if (implicitOp == null) continue
        il.Emit(OpCodes.Ldloc, implicitOpLocal);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // args[i] = implicitOp.Invoke(null, new object[] { args[i] })
        il.Emit(OpCodes.Ldarg_1);   // args array (for stelem later)
        il.Emit(OpCodes.Ldloc, iLocal);  // index (for stelem later)
        il.Emit(OpCodes.Ldloc, implicitOpLocal);
        il.Emit(OpCodes.Ldnull);     // target (null for static)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Stelem_Ref); // args[i] = result

        il.MarkLabel(continueLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopConditionLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopBodyLabel);

        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a private static helper on $TSFunction that coerces primitive-
    /// typed arguments before reflection-Invoke. Without this, calling a
    /// helper that expects a <c>string</c> parameter via reflection with a
    /// non-string boxed argument throws ArgumentException at the CLR level.
    /// Pattern: <c>Number.prototype.split = String.prototype.split; new
    /// Number(100).split(1, 1)</c> — the wrapped StringSplit helper takes
    /// (string, string, ...) but receives a $Object boxed-Number receiver.
    /// Iterates parameters and calls <c>$Runtime.ToJsString</c> via late-bound
    /// reflection on args[i] when paramType == typeof(string) and args[i] is
    /// not already a string. Late-bound because $TSFunction is finalized
    /// before $Runtime, so the MethodBuilder reference isn't available at IL
    /// emit time. Caches the resolved MethodInfo in a static field.
    /// </summary>
    private MethodBuilder EmitTSFunctionCoercePrimitivesHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Static cache fields for resolved $Runtime helpers.
        var toJsStringCacheField = typeBuilder.DefineField(
            "_toJsStringCache",
            _types.MethodInfo,
            FieldAttributes.Private | FieldAttributes.Static);
        var toNumberCacheField = typeBuilder.DefineField(
            "_toNumberCache",
            _types.MethodInfo,
            FieldAttributes.Private | FieldAttributes.Static);
        var arrayLikeMaterializeCacheField = typeBuilder.DefineField(
            "_arrayLikeMaterializeCache",
            _types.MethodInfo,
            FieldAttributes.Private | FieldAttributes.Static);
        var requireObjectCoercibleThisCacheField = typeBuilder.DefineField(
            "_requireObjectCoercibleThisCache",
            _types.MethodInfo,
            FieldAttributes.Private | FieldAttributes.Static);

        var method = typeBuilder.DefineMethod(
            "CoercePrimitiveArgs",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.MethodInfo, _types.ObjectArray]
        );
        var il = method.GetILGenerator();

        var paramsLocal = il.DeclareLocal(_types.MakeArrayType(_types.ParameterInfo));
        var countLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var paramTypeLocal = il.DeclareLocal(_types.Type);
        var stringTypeLocal = il.DeclareLocal(_types.Type);
        var doubleTypeLocal = il.DeclareLocal(_types.Type);
        var int32TypeLocal = il.DeclareLocal(_types.Type);
        var listTypeLocal = il.DeclareLocal(_types.Type);
        var toJsStringLocal = il.DeclareLocal(_types.MethodInfo);
        var toNumberLocal = il.DeclareLocal(_types.MethodInfo);
        var arrayLikeMaterializeLocal = il.DeclareLocal(_types.MethodInfo);
        var requireObjectCoercibleThisLocal = il.DeclareLocal(_types.MethodInfo);
        var oneArgLocal = il.DeclareLocal(_types.ObjectArray);

        // params = method.GetParameters()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "GetParameters")!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        // count = Math.Min(args.Length, params.Length)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, countLocal);

        // stringType = typeof(string), doubleType = typeof(double),
        // listType = typeof(List<object>).
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Stloc, stringTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Stloc, doubleTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.ListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Stloc, listTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Stloc, int32TypeLocal);

        // Local helper: resolve a $Runtime static method by name into a cache field.
        // Stack-in/out: nothing; stores resolved MethodInfo in `localOut`.
        void ResolveRuntimeMethod(string methodName, FieldBuilder cacheField, LocalBuilder localOut)
        {
            il.Emit(OpCodes.Ldsfld, cacheField);
            il.Emit(OpCodes.Stloc, localOut);
            var have = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, localOut);
            il.Emit(OpCodes.Brtrue, have);
            il.Emit(OpCodes.Ldtoken, typeBuilder);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldstr, "$Runtime");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Assembly, "GetType", [_types.String])!);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", [_types.String])!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, cacheField);
            il.Emit(OpCodes.Stloc, localOut);
            il.MarkLabel(have);
        }
        ResolveRuntimeMethod("ToJsString", toJsStringCacheField, toJsStringLocal);
        ResolveRuntimeMethod("ToNumber", toNumberCacheField, toNumberLocal);
        ResolveRuntimeMethod("ArrayLikeMaterialize", arrayLikeMaterializeCacheField, arrayLikeMaterializeLocal);
        ResolveRuntimeMethod("RequireObjectCoercibleThis", requireObjectCoercibleThisCacheField, requireObjectCoercibleThisLocal);

        // If any resolution failed, bail. Each branch checks its own resolved
        // method again before invoking, so the bail-on-any approach is
        // conservative — a failed ArrayLikeMaterialize lookup shouldn't block
        // the string/double coercion paths. But all three are defined on $Runtime
        // unconditionally, so in practice all resolve.
        var bail = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toJsStringLocal);
        il.Emit(OpCodes.Brfalse, bail);
        il.Emit(OpCodes.Ldloc, toNumberLocal);
        il.Emit(OpCodes.Brfalse, bail);
        il.Emit(OpCodes.Ldloc, arrayLikeMaterializeLocal);
        il.Emit(OpCodes.Brfalse, bail);
        il.Emit(OpCodes.Ldloc, requireObjectCoercibleThisLocal);
        il.Emit(OpCodes.Brfalse, bail);

        // ECMA-262 RequireObjectCoercible(this) for String.prototype helpers
        // and Array.prototype helpers whose `this` slot the wire layer names
        // "__this". String helpers always pass through the guard because it
        // also rejects Symbol receivers before ToString. Array helpers invoke
        // it only for null/undefined because other primitives are valid
        // ToObject receivers. Late-bound to avoid forward-referencing
        // TSTypeErrorCtor from TSFunction's IL (TSError is emitted later).
        // Closes ~50 String.prototype/* and Array.prototype/* tests in the
        // `this-value-not-obj-coercible.js` / `this-is-{null,undefined}-throws`
        // / `return-abrupt-from-this.js` clusters.
        var skipNullishThisCheckLabel = il.DefineLabel();
        var invokeHelperLabel = il.DefineLabel();
        // Need params.Length > 0 and args.Length > 0
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, skipNullishThisCheckLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, skipNullishThisCheckLabel);
        // Check params[0].Name == "__this" first.
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__this");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, skipNullishThisCheckLabel);

        // String receiver slots always use the helper (Symbol must throw).
        var inspectArrayReceiverLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, stringTypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, inspectArrayReceiverLabel);
        il.Emit(OpCodes.Br, invokeHelperLabel);

        // List<object> receiver slots are Array.prototype helpers. Invoke the
        // guard only for null/$Undefined; numbers, booleans, strings and
        // Symbols are all object-coercible here.
        il.MarkLabel(inspectArrayReceiverLabel);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, listTypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brfalse, skipNullishThisCheckLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, invokeHelperLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, skipNullishThisCheckLabel);

        il.MarkLabel(invokeHelperLabel);
        // requireObjectCoercibleThis.Invoke(null, new object[] { args[0] }) — throws.
        // The reflection invoke wraps the helper's TypeError in a
        // TargetInvocationException; unwrap and rethrow the InnerException so
        // the JS-level catch sees the original $TypeError (with __tsValue
        // intact and `e.constructor === TypeError` holding correctly).
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, oneArgLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, requireObjectCoercibleThisLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, oneArgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Pop);
        il.BeginCatchBlock(_types.TargetInvocationException);
        // Rethrow inner exception so JS catch sees the original error shape.
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        il.Emit(OpCodes.Throw);
        il.EndExceptionBlock();
        il.MarkLabel(skipNullishThisCheckLabel);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        var continueLbl = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);

        il.MarkLabel(loopBody);

        // paramType = params[i].ParameterType
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ParameterInfo, "ParameterType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paramTypeLocal);

        // Branch on paramType: string → ToJsString; double → ToNumber;
        // int → ToNumber → Conv_I4; List<object> → ArrayLikeMaterialize; else continue.
        var stringBranch = il.DefineLabel();
        var doubleBranch = il.DefineLabel();
        var int32Branch = il.DefineLabel();
        var listBranch = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldloc, stringTypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, stringBranch);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldloc, doubleTypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, doubleBranch);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldloc, int32TypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, int32Branch);
        il.Emit(OpCodes.Ldloc, paramTypeLocal);
        il.Emit(OpCodes.Ldloc, listTypeLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "op_Equality", [_types.Type, _types.Type])!);
        il.Emit(OpCodes.Brtrue, listBranch);
        il.Emit(OpCodes.Br, continueLbl);

        // String branch
        il.MarkLabel(stringBranch);

        // arg = args[i]
        // if (arg == null) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, continueLbl);

        // if (arg is string) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, continueLbl);

        // oneArg = new object[1] { args[i] }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, oneArgLocal);

        // args[i] = (string) toJsString.Invoke(null, oneArg)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, toJsStringLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, oneArgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Br, continueLbl);

        // Double branch
        il.MarkLabel(doubleBranch);

        // if (arg == null) → coerce to 0.0 (ECMA-262 ToNumber(null) == 0).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        var doubleArgNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, doubleArgNotNull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Br, continueLbl);
        il.MarkLabel(doubleArgNotNull);

        // if (arg is double) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, continueLbl);

        // oneArg = new object[1] { args[i] }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, oneArgLocal);

        // args[i] = (double) toNumber.Invoke(null, oneArg)
        // toNumber.Invoke returns object boxing a double already, so just store it.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, toNumberLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, oneArgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Br, continueLbl);

        // Int32 branch — helpers like StringSlice take an int argCount param.
        // Coerce via ToNumber, then unbox + Conv_I4 + box Int32. Bool/double/
        // string args all flow through ECMA-262 ToInteger.
        il.MarkLabel(int32Branch);

        // if (arg == null) coerce to 0
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        var int32ArgNotNull = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, int32ArgNotNull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Br, continueLbl);
        il.MarkLabel(int32ArgNotNull);

        // if (arg is int) continue
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, continueLbl);

        // oneArg = new object[1] { args[i] }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, oneArgLocal);

        // d = (double) toNumber.Invoke(null, oneArg); args[i] = (Int32)(int)d (boxed).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, toNumberLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, oneArgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Br, continueLbl);

        // List<object> branch — for borrowed Array.prototype.X helpers whose
        // first param is List<object> (the receiver). Materialize non-list
        // receivers (Dictionary, $Object, string, object[]) into a new list
        // via $Runtime.ArrayLikeMaterialize so the helper's Castclass succeeds.
        il.MarkLabel(listBranch);

        // if (arg == null) continue — null receiver typically already failed
        // upstream; preserve current behavior.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Brfalse, continueLbl);

        // if (arg is List<object>) continue — already materialized.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, continueLbl);

        // oneArg = new object[1] { args[i] }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, oneArgLocal);

        // args[i] = (List<object>) arrayLikeMaterialize.Invoke(null, oneArg)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrayLikeMaterializeLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, oneArgLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "Invoke", [_types.Object, _types.ObjectArray])!);
        il.Emit(OpCodes.Stelem_Ref);

        il.MarkLabel(continueLbl);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Blt, loopBody);

        il.MarkLabel(bail);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
