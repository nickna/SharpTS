using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Defines util inspect method signatures early so ConsoleDir can reference them.
    /// Method bodies are emitted later in EmitUtilStandaloneMethods.
    /// </summary>
    private void DefineUtilInspectSignatures(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // InspectValue(object value, int depth, int currentDepth) -> string
        runtime.UtilInspectValue = typeBuilder.DefineMethod(
            "UtilInspectValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectArray(object arr, int depth, int currentDepth) -> string
        runtime.UtilInspectArray = typeBuilder.DefineMethod(
            "UtilInspectArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectObject(object obj, int depth, int currentDepth) -> string
        runtime.UtilInspectObject = typeBuilder.DefineMethod(
            "UtilInspectObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);
    }

    private void EmitRuntimeClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // $Runtime TypeBuilder + early helper-method signatures (Stringify,
        // CreateException) were forward-declared by DefineRuntimeClassPhase1
        // so types that emit before us — $RegExp's Symbol.* helpers in
        // particular — could refer to them. Re-use the existing TypeBuilder
        // here; everything below adds fields/methods to the same type.
        var typeBuilder = (TypeBuilder)runtime.RuntimeType;
        _runtimeTypeBuilder = typeBuilder;

        // Cooperative cancellation flag — tripped by the Test262 runner
        // (or any embedder) via reflection to unwind compiled IL on timeout.
        // See issue #74. Public so the runner can SetValue via reflection;
        // polled by loop-backedge emissions via CheckCancellation below.
        var cancelRequestedField = typeBuilder.DefineField(
            "_cancelRequested",
            _types.Boolean,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.CancelRequestedField = cancelRequestedField;

        // Thread-static "original array-like receiver" — see EmittedRuntime for
        // full rationale. Set by the Array.prototype.X.call(receiver, ...) pattern
        // matcher; read by EmitCallbackArgsAndInvoke when populating the callback's
        // 4th argument.
        var currentArrayLikeReceiverField = typeBuilder.DefineField(
            "_currentArrayLikeReceiver",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        currentArrayLikeReceiverField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.CurrentArrayLikeReceiverField = currentArrayLikeReceiverField;
        // Reuse `_currentArrayLikeReceiver` for the lazy iteration signal too.
        // (Historically forced by the layout-sensitive .NET 10 tier-0 JIT bug
        // behind issue #39 — fixed upstream in 10.0.x servicing, so adding
        // fields here is safe again.) The reuse is kept because it is also
        // semantically clean: the dispatch site already sets the field to the
        // original receiver, and LoadArrayLikeElement can decide eager vs lazy
        // by inspecting the receiver's type.
        runtime.LazyArrayLikeReceiverField = currentArrayLikeReceiverField;

        // Thread-static "callback thisArg" for `arr.forEach(cb, thisArg)` and
        // similar Array prototype methods. Set by ArrayEmitter / $BoundArrayMethod
        // when the user passes a thisArg; read by EmitCallbackArgsAndInvoke as
        // the receiver passed to InvokeMethodValue.
        var currentCallbackThisArgField = typeBuilder.DefineField(
            "_currentCallbackThisArg",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        currentCallbackThisArgField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.CurrentCallbackThisArgField = currentCallbackThisArgField;

        // Math singleton — a shared Dictionary<string, object> that user code
        // can mutate (`Math.length = 1`). `Math.PI` etc. still go through
        // MathStaticEmitter's compile-time interception, which fires *before*
        // this bare-reference path — so this field only surfaces when Math is
        // used as a receiver (e.g., `Array.prototype.every.call(Math, cb)`).
        var mathSingletonField = typeBuilder.DefineField(
            "_mathSingleton",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.MathSingletonField = mathSingletonField;

        // globalThis/global sentinel (#271) — a plain object whose identity lets
        // the dynamic property paths recognize a value-position globalThis and
        // route reads/writes through GlobalThisGetProperty/GlobalThisSetProperty.
        // The field itself is forward-declared in DefineRuntimeClassPhase1 (so
        // $TSFunction.InvokeWithThis, emitted before this method, can coerce a
        // null sloppy-this thisArg to it — #735/#733). Only the .cctor init below
        // runs here; re-DefineField would throw a duplicate.
        var globalThisSingletonField = runtime.GlobalThisSingletonField;

        // Boolean / Number / String prototype singletons. Test262 patterns like
        //   Boolean.prototype[0] = true; Boolean.prototype.length = 1;
        //   Array.prototype.every.call(false, cb)
        // require these to be addressable Dictionary objects whose property
        // writes round-trip and whose values surface to the array-like
        // materializer when the receiver is a bare bool/double/string primitive.
        // Bare-reference resolution for `Boolean`/`Number`/`String.prototype`
        // routes through GetProperty's Type branch, which checks these fields.
        var booleanPrototypeField = typeBuilder.DefineField(
            "_booleanPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.BooleanPrototypeField = booleanPrototypeField;
        var numberPrototypeField = typeBuilder.DefineField(
            "_numberPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.NumberPrototypeField = numberPrototypeField;
        // Date.prototype — addressable as a value so reflection over it works
        // (`Object.getOwnPropertyDescriptor(Date.prototype, "getTime")`).
        // Instance calls are emitted inline by DateEmitter and never read this.
        runtime.DatePrototypeField = typeBuilder.DefineField(
            "_datePrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        var stringPrototypeField = typeBuilder.DefineField(
            "_stringPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.StringPrototypeField = stringPrototypeField;

        // JSON / console / Error / Reflect singletons. Mirror of MathSingleton —
        // bare `var o = JSON` must yield an addressable object so
        // `typeof JSON === "object"` holds (ECMA-262: JSON is an ordinary
        // built-in object). Compile-time static dispatch (JSONStaticEmitter,
        // etc.) still fires *before* the bare-reference path, so JSON.parse(x)
        // continues routing to the inline impl unchanged.
        var jsonSingletonField = typeBuilder.DefineField(
            "_jsonSingleton",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.JsonSingletonField = jsonSingletonField;

        // Array.prototype singleton — populated lazily after $TSFunction and the
        // Array* helper MethodBuilders are defined. Read by ArrayStaticEmitter
        // for `Array.prototype` value access. Required so `Array.prototype.sort`
        // is `typeof === "function"` (Test262 isConstructor harness probes this).
        var arrayPrototypeField = typeBuilder.DefineField(
            "_arrayPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.ArrayPrototypeField = arrayPrototypeField;

        // Object.prototype singleton — populated lazily with hasOwnProperty/
        // isPrototypeOf/toString/valueOf/etc. wrappers.
        var objectPrototypeField = typeBuilder.DefineField(
            "_objectPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.ObjectPrototypeField = objectPrototypeField;

        // Error.prototype singleton — populated with toString/constructor.
        // Returned by GetProperty's Type-receiver branch when receiver is
        // typeof($Error), so `Error.prototype.toString.call(non-error)` hits
        // the brand-checking helper instead of generic class reflection.
        var errorPrototypeField = typeBuilder.DefineField(
            "_errorPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.ErrorPrototypeField = errorPrototypeField;

        // Native-error subclass prototype singletons (TypeError/RangeError/...).
        // Per ECMA-262 §20.5.6.4 each NativeError prototype is a *distinct*
        // object whose [[Prototype]] is Error.prototype, with own constructor/
        // name/message slots. Previously all subclass instances pointed to the
        // shared Error.prototype, which made `TypeError.prototype` resolve to
        // undefined and `Object.getPrototypeOf(new TypeError()) === TypeError.prototype`
        // fail (test262 Promise/any/iter-* checks this identity).
        FieldBuilder DefineSubclassProto(string fieldName) =>
            typeBuilder.DefineField(fieldName, _types.DictionaryStringObject,
                FieldAttributes.Public | FieldAttributes.Static);
        var typeErrorPrototypeField = DefineSubclassProto("_typeErrorPrototype");
        var rangeErrorPrototypeField = DefineSubclassProto("_rangeErrorPrototype");
        var referenceErrorPrototypeField = DefineSubclassProto("_referenceErrorPrototype");
        var syntaxErrorPrototypeField = DefineSubclassProto("_syntaxErrorPrototype");
        var uriErrorPrototypeField = DefineSubclassProto("_uriErrorPrototype");
        var evalErrorPrototypeField = DefineSubclassProto("_evalErrorPrototype");
        var aggregateErrorPrototypeField = DefineSubclassProto("_aggregateErrorPrototype");
        runtime.TypeErrorPrototypeField = typeErrorPrototypeField;
        runtime.RangeErrorPrototypeField = rangeErrorPrototypeField;
        runtime.ReferenceErrorPrototypeField = referenceErrorPrototypeField;
        runtime.SyntaxErrorPrototypeField = syntaxErrorPrototypeField;
        runtime.URIErrorPrototypeField = uriErrorPrototypeField;
        runtime.EvalErrorPrototypeField = evalErrorPrototypeField;
        runtime.AggregateErrorPrototypeField = aggregateErrorPrototypeField;

        // Function.prototype singleton — populated lazily with $TSFunction
        // wrappers for call/apply/bind/toString/constructor. test262's
        // propertyHelper.js opens with
        //   `Function.prototype.call.bind(Object.prototype.hasOwnProperty)`,
        // so without this every test that includes propertyHelper bails at
        // harness load. Returned by GetFieldsProperty's Type-receiver branch
        // when receiver is typeof($TSFunction).
        var functionPrototypeField = typeBuilder.DefineField(
            "_functionPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.FunctionPrototypeField = functionPrototypeField;

        // RegExp.prototype field forward-declared by DefineRuntimeClassPhase1 —
        // $RegExp's emission depends on the field token so the prototype's
        // proto-accessor helpers can compare against it. Reuse here.
        var regexpPrototypeField = runtime.RegExpPrototypeField;

        // Promise.prototype field — Dict<string,object> populated lazily on
        // first read by EmitPromisePrototypePopulate. Forward-declared so
        // EmitGetProperty's Type branch can emit the Ldsfld + Call sequence
        // when `Promise.prototype` is read as a value.
        runtime.PromisePrototypeField = typeBuilder.DefineField(
            "_promisePrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);

        // CheckCancellation(): if (_cancelRequested) throw new
        //   OperationCanceledException("Compiled execution cancelled.");
        // Called by loop emitters at each backedge. Method body is emitted
        // here so the token is available as soon as EmitRuntimeClass starts;
        // later emitters can reference runtime.CheckCancellationMethod.
        var checkCancellation = typeBuilder.DefineMethod(
            "CheckCancellation",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
        runtime.CheckCancellationMethod = checkCancellation;
        {
            var il = checkCancellation.GetILGenerator();
            var returnLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, cancelRequestedField);
            il.Emit(OpCodes.Brfalse, returnLabel);
            il.Emit(OpCodes.Ldstr, "Compiled execution cancelled.");
            il.Emit(OpCodes.Newobj,
                typeof(OperationCanceledException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(returnLabel);
            il.Emit(OpCodes.Ret);
        }

        // BuildCancellationException(): constructs and RETURNS (does not throw)
        // the OperationCanceledException used at loop backedges. Loop emitters
        // emit `call BuildCancellationException(); throw` so the cancel path is a
        // non-returning `throw` rather than a returning `call CheckCancellation()`
        // — keeping the hot loop body free of a call that would otherwise force
        // loop-carried doubles onto the stack on SysV x64 (~1.8× on tight numeric
        // loops, #856). See EmittedRuntime.BuildCancellationExceptionMethod.
        var buildCancelEx = typeBuilder.DefineMethod(
            "BuildCancellationException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Exception),
            Type.EmptyTypes);
        runtime.BuildCancellationExceptionMethod = buildCancelEx;
        {
            var il = buildCancelEx.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "Compiled execution cancelled.");
            il.Emit(OpCodes.Newobj,
                typeof(OperationCanceledException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Ret);
        }

        // Static field for Random
        var randomField = typeBuilder.DefineField("_random", _types.Random, FieldAttributes.Private | FieldAttributes.Static);

        // Static field for symbol storage: ConditionalWeakTable<object, Dictionary<object, object?>>
        var symbolDictType = _types.DictionaryObjectObject;
        var symbolStorageType = _types.MakeGenericType(_types.ConditionalWeakTableOpen, _types.Object, symbolDictType);
        var symbolStorageField = typeBuilder.DefineField(
            "_symbolStorage",
            symbolStorageType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        _ = symbolStorageField;

        // Static fields for Object.freeze/seal tracking: ConditionalWeakTable<object, object>
        // Made public so other types (like class constructors) can access for freeze checks
        var frozenObjectsField = typeBuilder.DefineField(
            "_frozenObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.FrozenObjectsField = frozenObjectsField;
        var sealedObjectsField = typeBuilder.DefineField(
            "_sealedObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.SealedObjectsField = sealedObjectsField;

        // Static field for non-extensible objects tracking: ConditionalWeakTable<object, object>
        var nonExtensibleObjectsField = typeBuilder.DefineField(
            "_nonExtensibleObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.NonExtensibleObjectsField = nonExtensibleObjectsField;

        // Static field for prototype tracking: ConditionalWeakTable<object, object>
        var prototypeStoreField = typeBuilder.DefineField(
            "_prototypeStore",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        _ = prototypeStoreField;

        // ECMA-262 §17 declares `name`/`length` on built-in functions as
        // configurable. test262's verifyProperty exercises that by deleting
        // them and re-checking; without per-instance tracking, the synthetic
        // values returned by GetFunctionMethod / HasOwnPropertyHelper /
        // ObjectGetOwnPropertyDescriptor stay visible after delete and the
        // configurability check fails. Track deletions per object via a
        // ConditionalWeakTable<object, HashSet<string>>; helpers consult it
        // before reporting the spec defaults.
        var deletedBuiltinsField = typeBuilder.DefineField(
            "_deletedBuiltins",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.DeletedBuiltinsField = deletedBuiltinsField;

        // Static sentinel field for null/undefined Map keys
        var mapNullSentinelField = typeBuilder.DefineField(
            "_mapNullSentinel",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.MapNullSentinel = mapNullSentinelField;

        // Static field for FinalizationRegistry poke table
        runtime.FinRegPokeTableField = typeBuilder.DefineField(
            "_finRegPokeTable",
            _types.ConditionalWeakTableObjectObject,
            FieldAttributes.Private | FieldAttributes.Static
        );

        // Static field for console group indentation level (needed early for ConsoleLog)
        var consoleGroupLevelField = typeBuilder.DefineField(
            "_consoleGroupLevel",
            _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.ConsoleGroupLevelField = consoleGroupLevelField;

        // Pre-define populate-method shells before the cctor IL is generated
        // so the cctor can `Call` each populate to eagerly fill the
        // singletons. Without eager-population, `delete Number.prototype.toString;
        // n.toString` walks the chain and finds an empty Object.prototype dict
        // because populate is only invoked when Object.prototype is explicitly
        // referenced. Idempotent — populate methods early-return if Count > 0.
        DefineObjectPrototypePopulateShell(typeBuilder, runtime);
        DefineArrayPrototypePopulateShell(typeBuilder, runtime);
        DefineMathSingletonPopulateShell(typeBuilder, runtime);
        DefineJsonSingletonPopulateShell(typeBuilder, runtime);
        DefineStringPrototypePopulateShell(typeBuilder, runtime);
        DefineNumberPrototypePopulateShell(typeBuilder, runtime);
        DefineBooleanPrototypePopulateShell(typeBuilder, runtime);
        DefineDatePrototypePopulateShell(typeBuilder, runtime);
        DefineErrorPrototypePopulateShell(typeBuilder, runtime);
        DefineNativeErrorPrototypePopulateShells(typeBuilder, runtime);
        DefineFunctionPrototypePopulateShell(typeBuilder, runtime);
        DefineRegExpPrototypePopulateShell(typeBuilder, runtime);
        DefinePromisePrototypePopulateShell(typeBuilder, runtime);

        // Static constructor to initialize Random and symbol storage
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

        // Initialize _random = new Random()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Random));
        cctorIL.Emit(OpCodes.Stsfld, randomField);

        // Initialize _mathSingleton = new Dictionary<string, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, mathSingletonField);

        // Initialize _globalThisSingleton = new object() (#271 sentinel identity)
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        cctorIL.Emit(OpCodes.Stsfld, globalThisSingletonField);

        // Initialize the symbol-keyed accessor registry (#266).
        InitSymbolAccessorRegistry(cctorIL, runtime);

        // Boolean/Number/String prototype singletons (lazy-feeling but eagerly
        // initialized so Type→prototype lookups never hit a null).
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, booleanPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, numberPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, stringPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, jsonSingletonField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, runtime.DatePrototypeField);

        // Array.prototype starts empty; populated lazily by
        // EmitArrayPrototypePopulate-emitted helper on first read.
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, arrayPrototypeField);

        // Object.prototype starts empty; populated lazily by
        // EmitObjectPrototypePopulate-emitted helper on first read.
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, objectPrototypeField);

        // Error.prototype starts empty; populated by EmitErrorPrototypePopulate
        // (eagerly invoked from cctor tail below).
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, errorPrototypeField);

        // Native-error subclass prototypes — empty dicts; populated lazily on
        // first `<X>Error.prototype` read or `Object.getPrototypeOf(<x>err)`.
        foreach (var f in new[] {
            typeErrorPrototypeField, rangeErrorPrototypeField, referenceErrorPrototypeField,
            syntaxErrorPrototypeField, uriErrorPrototypeField, evalErrorPrototypeField,
            aggregateErrorPrototypeField })
        {
            cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            cctorIL.Emit(OpCodes.Stsfld, f);
        }

        // Function.prototype starts empty; populated by
        // EmitFunctionPrototypePopulate (eagerly invoked from cctor tail).
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, functionPrototypeField);

        // RegExp.prototype starts empty; populated by
        // EmitRegExpPrototypePopulate (eagerly invoked from cctor tail).
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, regexpPrototypeField);

        // Promise.prototype starts empty; populated lazily by
        // EmitPromisePrototypePopulate on first `Promise.prototype` read.
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, runtime.PromisePrototypeField);

        // Initialize _symbolStorage = new ConditionalWeakTable<object, Dictionary<object, object?>>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(symbolStorageType));
        cctorIL.Emit(OpCodes.Stsfld, symbolStorageField);

        // Initialize _frozenObjects = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, frozenObjectsField);

        // Initialize _sealedObjects = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, sealedObjectsField);

        // Initialize _nonExtensibleObjects = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, nonExtensibleObjectsField);

        // Initialize _prototypeStore = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, prototypeStoreField);

        // Initialize _deletedBuiltins = new ConditionalWeakTable<object, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
        cctorIL.Emit(OpCodes.Stsfld, deletedBuiltinsField);

        // Link the built-in singletons (Math, JSON, Boolean/Number/String/Array
        // prototypes) to Object.prototype via PDS. ECMA-262 declares each of
        // these intrinsic objects has [[Prototype]] = %Object.prototype% — tests
        // probe `Object.getPrototypeOf(Math) === Object.prototype` etc. PDS
        // entries are keyed by reference, so this only fires for the singleton
        // dictionaries themselves; user-built objects that mutate Math (e.g.
        // `Math.length = 1`) still see the linked prototype.
        void EmitLinkProto(FieldBuilder child)
        {
            cctorIL.Emit(OpCodes.Ldsfld, child);
            cctorIL.Emit(OpCodes.Ldsfld, objectPrototypeField);
            cctorIL.Emit(OpCodes.Call, runtime.PDSSetPrototype);
        }
        EmitLinkProto(mathSingletonField);
        EmitLinkProto(jsonSingletonField);
        EmitLinkProto(booleanPrototypeField);
        EmitLinkProto(numberPrototypeField);
        EmitLinkProto(stringPrototypeField);
        EmitLinkProto(arrayPrototypeField);
        EmitLinkProto(errorPrototypeField);
        EmitLinkProto(functionPrototypeField);
        EmitLinkProto(regexpPrototypeField);

        // Initialize _mapNullSentinel = new object()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        cctorIL.Emit(OpCodes.Stsfld, mapNullSentinelField);

        // Capture a monotonic process-start baseline for process.uptime(). Stopwatch
        // is monotonic (QueryPerformanceCounter / CLOCK_MONOTONIC); reading wall-clock
        // DateTime at each uptime() call could run backwards on an NTP slew. The .cctor
        // runs once at type load (≈ process start), so ProcessUptime() reports a
        // non-decreasing "seconds since process start". Read by EmitProcessUptime.
        var uptimeBaselineField = typeBuilder.DefineField(
            "_uptimeStartTimestamp",
            _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static);
        runtime.ProcessUptimeBaselineField = uptimeBaselineField;
        cctorIL.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        cctorIL.Emit(OpCodes.Stsfld, uptimeBaselineField);

        // Initialize perf_hooks timing fields (must be called after fields are defined)
        // Note: Fields will be defined by EmitPerfHooksMethods, so we defer this initialization
        // The initialization is done inline in EmitPerfHooksMethods instead

        // Initialize _finRegPokeTable = new ConditionalWeakTable<object, object>()
        EmitFinRegPokeTableInit(cctorIL, runtime);

        // Define the event-subscription registry (field + two helper methods). Must be
        // emitted while we still hold the cctor IL generator so the field gets initialized.
        EmitEventSubscriptionHelpers(typeBuilder, runtime, cctorIL);

        // Eagerly populate the prototype singletons so cross-prototype-chain
        // walks (e.g. `delete Number.prototype.toString; n.toString` should
        // fall through to Object.prototype.toString) hit populated dicts.
        // Each populate is idempotent (early-returns if Count > 0).
        cctorIL.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.ArrayPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.NumberPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.BooleanPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.DatePrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.ErrorPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.FunctionPrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.RegExpPrototypePopulateMethod);
        // Math / JSON value-form singletons (`const m = Math; m.max(...)`). Each
        // populate is idempotent and skips null backings, so calling the JSON one
        // unconditionally is safe even when the program doesn't use JSON (#276).
        cctorIL.Emit(OpCodes.Call, runtime.MathSingletonPopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.JsonSingletonPopulateMethod);

        cctorIL.Emit(OpCodes.Ret);

        // Emit all methods - these are now in partial class files
        // Core utilities

        // UnwrapIfBoxed's MethodBuilder must exist before Add/Equals/Stringify
        // so those helpers can reference its token to ToPrimitive boxed-primitive
        // wrappers before the type-based dispatch ($Object → __primitiveValue per
        // ECMA-262 §7.2.14 and §13.10.1). Its BODY is filled later
        // (EmitUnwrapIfBoxedBody, after EmitGetProperty) because the #574 own-
        // conversion dispatch calls GetProperty/InvokeMethodValue/HasOwnPropertyHelper.
        DeclareUnwrapIfBoxed(typeBuilder, runtime);

        EmitFormatNumberMethod(typeBuilder, runtime);
        EmitStringify(typeBuilder, runtime);
        // EmitStringRaw is moved later in this method (after ToJsString/
        // ToNumber/GetProperty are emitted) so the spec-form String.raw can
        // resolve template.raw properties + ToString-coerce substitutions.
        // Format specifier helpers (must be emitted before ConsoleLog/ConsoleLogMultiple which call them)
        EmitHasFormatSpecifiers(typeBuilder, runtime);
        EmitFormatSingleArg(typeBuilder, runtime);
        EmitFormatAsInteger(typeBuilder, runtime);
        EmitFormatAsFloat(typeBuilder, runtime);
        EmitFormatAsJson(typeBuilder, runtime);
        EmitFormatConsoleArgs(typeBuilder, runtime);
        // GetConsoleIndent must be emitted before ConsoleLog/ConsoleLogMultiple which call it
        EmitGetConsoleIndent(typeBuilder, runtime);
        EmitConsoleLog(typeBuilder, runtime);
        // JoinWithStringify must be emitted before ConsoleLogMultiple which uses it
        EmitJoinWithStringify(typeBuilder, runtime);
        EmitConsoleLogMultiple(typeBuilder, runtime);
        // Exception helpers must come before ToNumber, since ToNumber emits a
        // CreateException + TSTypeErrorCtor throw on Symbol receivers per
        // ECMA-262 7.1.4 step 2. Without this earlier emit, runtime.CreateException
        // would be null at IL emission time → "Value cannot be null. (Parameter 'meth')"
        // bubbles up to every compiled program. The original line-378 EmitCreateException
        // is left in place; calling here just reuses the same MethodBuilder slot.
        EmitCreateException(typeBuilder, runtime);
        EmitWrapException(typeBuilder, runtime);
        // ToNumber slot is forward-declared in DefineRuntimeClassPhase1 so
        // $RegExp's Symbol.split limit-coercion (which runs before $Runtime
        // body emit) can bind to it. Skip the duplicate-define here.
        DeclareConvertToNumber(typeBuilder, runtime);
        EmitJsToInt32(typeBuilder, runtime);
        EmitJsLessThan(typeBuilder, runtime);
        EmitJsLessOrEqual(typeBuilder, runtime);
        EmitIsTruthy(typeBuilder, runtime);
        EmitTypeOf(typeBuilder, runtime);
        EmitAdd(typeBuilder, runtime);
        // Equals body needs runtime.ToJsString for the ECMA-262 7.2.14
        // Object-vs-String branch (`new String(s) == s` requires
        // ToPrimitive(wrapper) → string, then string compare). ToJsString is
        // emitted later (it depends on GetProperty + InvokeMethodValue which
        // depend on this same chain). Declare the Equals MethodBuilder shell
        // here so any caller that references it before EmitEquals runs gets
        // a non-null token; body fills in after EmitToJsString below.
        DeclareEquals(typeBuilder, runtime);
        EmitStrictEquals(typeBuilder, runtime);
        // Object methods - must come BEFORE iterator methods since GetProperty, InvokeMethodValue are needed
        EmitCreateObject(typeBuilder, runtime);
        EmitGetArrayMethod(typeBuilder, runtime);
        // Deleted-builtins tracking — used by HasOwnPropertyHelper /
        // GetFunctionMethod / ObjectGetOwnPropertyDescriptor / DeleteIndex to
        // hide name/length on a $TSFunction after `delete fn.name`. Emitted
        // before its consumers.
        EmitDeletedBuiltinsHelpers(typeBuilder, runtime);
        // Symbol helpers — moved before HasOwnPropertyHelper so its
        // Symbol-key arm can call IsSymbolMethod / GetSymbolDictMethod
        // (Object.prototype.hasOwnProperty must honor Symbol keys per
        // ECMA-262 §20.1.3.2 step 1's ToPropertyKey).
        EmitGetSymbolDict(typeBuilder, runtime, symbolStorageField);
        EmitIsSymbol(typeBuilder, runtime);
        // hasOwnProperty + isPrototypeOf helpers — must come before
        // GetFunctionMethod so the corresponding arms can return $TSFunction
        // wrappers.
        EmitHasOwnPropertyHelper(typeBuilder, runtime);
        // propertyIsEnumerable shares HasOwn's plumbing (PDS lookup + dict
        // fallback) so emit it immediately after.
        EmitPropertyIsEnumerableHelper(typeBuilder, runtime);
        // Shell ObjectGetPrototypeOf early so IsPrototypeOfHelper can call it
        // and pick up the default-fallback to Object.prototype / Array.prototype
        // for plain Dict/List receivers without explicit PDS entries.
        DefineObjectGetPrototypeOfShell(typeBuilder, runtime);
        EmitIsPrototypeOfHelper(typeBuilder, runtime);
        // ObjectPrototypePopulate / ArrayPrototypePopulate shells already
        // defined above (before cctor) so the cctor can call them eagerly.
        EmitGetFunctionMethod(typeBuilder, runtime);  // For bind/call/apply on functions
        // Pre-define IsBoxedPrimitiveOfType shell so InstanceOf can reference
        // it. Body emitted later (after the prototype singletons are defined).
        DefineIsBoxedPrimitiveOfTypeShell(typeBuilder, runtime);
        // Pre-define the AbortSignal/Intl namespace singleton fields so
        // InstanceOf can brand-check the AbortSignal singleton (#246).
        // Populate bodies are emitted later (EmitNamespaceSingletons).
        DefineNamespaceSingletonFields(typeBuilder, runtime);
        // InstanceOf walks the prototype chain via GetFunctionMethod (for the
        // `F.prototype` fetch) — must be emitted AFTER GetFunctionMethod so
        // `runtime.GetFunctionMethod` is populated when InstanceOf references it.
        EmitInstanceOf(typeBuilder, runtime);
        EmitToPascalCase(typeBuilder, runtime);  // Must be emitted before GetFieldsProperty/SetFieldsProperty
        EmitSafeGetMethod(typeBuilder, runtime); // Must be emitted before GetFieldsProperty/SetFieldsProperty
        // Promise callback types must be created before InvokeValue (which dispatches to them)
        EmitPromiseCallbackTypes(moduleBuilder, runtime);
        // ArrayConstructor (#61) must come before InvokeValue since InvokeValue's
        // Type-callee dispatch branch emits a direct call to it for `Array(n)`
        // patterns where Array was stored as a value.
        EmitArrayConstructor(typeBuilder, runtime);
        // LookupBuiltInStaticMember (#63): MethodBuilder defined here so
        // GetProperty's Type branch can reference it; body emitted later
        // after all backing static methods are in place.
        DefineLookupBuiltInStaticMember(typeBuilder, runtime);
        // InvokeValue/InvokeMethodValue must come before GetFieldsProperty (needs InvokeMethodValue for getters)
        // and before Promise methods (needed by InvokeCallback)
        // Pre-declare ArrayLikeMaterialize's MethodBuilder so InvokeMethodValue
        // can reference it for the $BoundArrayMethod receiver-rebind path.
        // The body is filled in later (EmitArrayLikeMaterialize, line 544).
        DeclareArrayLikeMaterialize(typeBuilder, runtime);
        // Companion lazy-aware materializer + element reader for iterator
        // helpers (issue #90). Pre-declared so the iterator emitters can
        // reference them; bodies emitted after EmitGetProperty (which they
        // call).
        DeclareArrayLikeMaterializeForIteration(typeBuilder, runtime);
        DeclareLoadArrayLikeElement(typeBuilder, runtime);
        DeclareHasArrayLikeProperty(typeBuilder, runtime);
        EmitInvokeValue(typeBuilder, runtime);
        EmitInvokeMethodValue(typeBuilder, runtime);
        EmitGetFieldsProperty(typeBuilder, runtime);
        EmitGetListProperty(typeBuilder, runtime);
        // GetMapProperty / GetSetProperty are the duck-typed property dispatchers
        // for Dictionary<object,object> / HashSet<object> receivers. Each calls
        // its respective Map/Set method MethodBuilders (and BoundMapMethodCtor
        // / BoundSetMethodCtor wrappers), so they fold up under UsesMap / UsesSet.
        if (_features.UsesMap)
            EmitGetMapProperty(typeBuilder, runtime);
        if (_features.UsesSet)
            EmitGetSetProperty(typeBuilder, runtime);
        // Exception helpers were moved earlier (above EmitToNumber) since
        // ToNumber's Symbol-throw branch emits a CreateException call that
        // must resolve to a non-null MethodBuilder.
        EmitSetFieldsProperty(typeBuilder, runtime);
        EmitSetFieldsPropertyStrict(typeBuilder, runtime);
        // Promise methods must come before GetProperty (which needs PromiseThen for typeof p.then)
        EmitPromiseMethods(typeBuilder, runtime);
        // TypedArray detection helpers must come before GetProperty (which uses IsTypedArrayMethod)
        EmitTypedArrayDetectionHelpers(typeBuilder, runtime);
        // AbortController/AbortSignal methods must come before GetProperty,
        // whose dict-receiver branch dispatches "aborted"/"reason"/"onabort"
        // to the signal getters (#224). FireAbortEvent must precede
        // EmitAbortControllerMethods (which references it). Gated on
        // UsesAbortController, also implied by UsesWebStreams/fetch/http.
        if (_features.UsesAbortController)
        {
            EmitFireAbortEvent(typeBuilder, runtime);
            EmitAbortControllerMethods(typeBuilder, runtime);
            // stream.addAbortSignal destroy-on-abort wiring (#1027) — needs the AbortSignal
            // helpers above + the $StreamAbortCallback closure emitted in the stream block.
            if (_features.UsesNodeStreams)
                EmitStreamAddAbortSignalMethod(typeBuilder, runtime);
        }
        // String/Number/Boolean populate shells already defined above
        // (before cctor) so the cctor can call them eagerly.
        EmitGetProperty(typeBuilder, runtime);
        // Fill UnwrapIfBoxed's body now that GetProperty / InvokeMethodValue /
        // HasOwnPropertyHelper are all emitted — its #574 own-conversion dispatch
        // (own valueOf/toString before __primitiveValue) calls them.
        EmitUnwrapIfBoxedBody(runtime);
        // Dynamic iterator-protocol bridge — must come after GetProperty +
        // InvokeMethodValue since its non-enumerator fallback calls both.
        EmitIteratorProtocolCall(typeBuilder, runtime);
        // GetSymbolDict / IsSymbol already emitted above (moved earlier so
        // HasOwnPropertyHelper's Symbol-key arm can call them).
        // ToJsString depends on GetProperty + InvokeMethodValue + Stringify; emit after those.
        EmitToJsString(typeBuilder, runtime);
        // StringFromValue (String(x) call form) wraps ToJsString with the
        // §22.1.1.1 Symbol exemption; emit right after it.
        EmitStringFromValue(typeBuilder, runtime);
        // StringifyCoerce (implicit-coercion sites) wraps Stringify with the
        // §7.1.17 Symbol TypeError; the body needs TSSymbolType/TSTypeErrorCtor,
        // both bound by this point (ToJsString's Symbol arm uses them too).
        EmitStringifyCoerce(runtime);
        // Equals body — must come after ToJsString since the Object-vs-String
        // branch calls runtime.ToJsString.
        EmitEquals(typeBuilder, runtime);
        // ToNumber/ConvertToNumber bodies: emit AFTER GetProperty/InvokeMethodValue
        // so their ToPrimitive(value, "number") on Dictionary/$Object args can
        // call those helpers.
        EmitToNumber(typeBuilder, runtime);
        EmitConvertToNumber(typeBuilder, runtime);
        // String.raw lives here so its body can read `template.raw` via
        // GetProperty and ToString-coerce substitutions via ToJsString.
        EmitStringRaw(typeBuilder, runtime);
        EmitSetProperty(typeBuilder, runtime);
        EmitSetPropertyStrict(typeBuilder, runtime);
        EmitDeleteProperty(typeBuilder, runtime);
        EmitDeletePropertyStrict(typeBuilder, runtime);
        EmitMergeIntoObject(typeBuilder, runtime);
        EmitMergeIntoTSObject(typeBuilder, runtime);
        // (Symbol helpers EmitGetSymbolDict + EmitIsSymbol now emitted earlier
        // — before EmitToJsString — so the @@toPrimitive lookup can use them.)
        // DisposeResource depends on GetSymbolDict and InvokeMethodValue
        EmitDisposeResource(typeBuilder, runtime);
        // HasIn operator depends on IsSymbol and GetSymbolDict
        EmitHasIn(typeBuilder, runtime);
        // Array SetElement helpers - must come BEFORE GetIndex/SetIndex which reference them.
        // Previously only Typed variants were emitted here; the Object variant was deferred to
        // the Arrays section below. That left SetIndex's object-list branch unable to call it,
        // so we emit all of them up front now (including Object) for JS-spec auto-extend semantics.
        foreach (var desc in ArrayElements.All)
            EmitSetArrayElementFor(typeBuilder, runtime, desc);
        // Note: TypedArray detection helpers are emitted earlier (before GetProperty)
        EmitGetIndex(typeBuilder, runtime);
        EmitSetIndex(typeBuilder, runtime);
        EmitSetIndexStrict(typeBuilder, runtime);
        EmitDeleteIndex(typeBuilder, runtime);
        EmitDeleteIndexStrict(typeBuilder, runtime);
        EmitStrictModeHelpers(typeBuilder, runtime);
        // Basic iterator protocol methods - must come AFTER object methods (need GetProperty, InvokeMethodValue)
        EmitIteratorMethodsBasic(typeBuilder, runtime);
        // Emit $IteratorWrapper AFTER basic iterator methods (needs InvokeIteratorNext etc.)
        // but BEFORE IterateToList (which needs IteratorWrapperCtor)
        EmitIteratorWrapperType(moduleBuilder, runtime);
        // Advanced iterator methods (IterateToList) - needs IteratorWrapperCtor
        EmitIteratorMethodsAdvanced(typeBuilder, runtime);
        // ES2025 Iterator Helper methods and lazy wrapper types
        EmitIteratorHelperMethods(typeBuilder, moduleBuilder, runtime);
        // Arrays - must come AFTER iterator methods since ConcatArrays/ExpandCallArgs use IterateToList.
        // SetArrayElement* helpers (including Object variant) are emitted earlier, BEFORE SetIndex,
        // since SetIndex's object-list branch also calls SetArrayElement for auto-extend semantics.
        EmitCreateArray(typeBuilder, runtime);
        EmitGetLength(typeBuilder, runtime);
        EmitGetElement(typeBuilder, runtime);
        EmitGetKeys(typeBuilder, runtime);
        EmitGetOwnPropertyNames(typeBuilder, runtime);
        EmitGetValues(typeBuilder, runtime);
        EmitGetEntries(typeBuilder, runtime);
        EmitObjectFromEntries(typeBuilder, runtime);
        EmitObjectHasOwn(typeBuilder, runtime);
        EmitObjectIs(typeBuilder, runtime);
        EmitObjectAssign(typeBuilder, runtime);
        EmitObjectFreeze(typeBuilder, runtime, frozenObjectsField, sealedObjectsField);
        EmitObjectSeal(typeBuilder, runtime, sealedObjectsField);
        EmitObjectIsFrozen(typeBuilder, runtime, frozenObjectsField);
        EmitObjectIsSealed(typeBuilder, runtime, sealedObjectsField);
        EmitObjectDefineProperty(typeBuilder, runtime);
        // Math.* adapters must precede gOPD so its Math singleton synth can
        // reach the adapter MethodBuilders to produce identity-stable
        // `desc.value === Math.X` for built-in methods. Moved up from the
        // original site at the end of the runtime emit. Dep: runtime.ToNumber
        // (emitted at line 580, before this site).
        EmitMathAdapters(typeBuilder, runtime);
        // EmitRandom moved here from the original late-site so gOPD's Math
        // singleton synth can reach runtime.Random and produce an identity-
        // stable `desc.value === Math.random` descriptor. The Random method
        // builder only needs randomField (defined at line 215) — both
        // available now.
        EmitRandom(typeBuilder, runtime, randomField);
        EmitObjectGetOwnPropertyDescriptor(typeBuilder, runtime);
        EmitObjectDefineProperties(typeBuilder, runtime);
        // GetOwnPropertySymbols must precede gOPDs (gOPDs now also iterates
        // symbol keys to populate symbol-keyed descriptors in the result).
        EmitGetOwnPropertySymbols(typeBuilder, runtime);
        EmitObjectGetOwnPropertyDescriptors(typeBuilder, runtime);
        EmitObjectCreate(typeBuilder, runtime, prototypeStoreField);
        EmitObjectPreventExtensions(typeBuilder, runtime, nonExtensibleObjectsField, frozenObjectsField, sealedObjectsField);
        EmitObjectIsExtensible(typeBuilder, runtime, nonExtensibleObjectsField, frozenObjectsField, sealedObjectsField);
        EmitObjectGetPrototypeOf(typeBuilder, runtime, prototypeStoreField);
        EmitObjectSetPrototypeOf(typeBuilder, runtime, prototypeStoreField, nonExtensibleObjectsField);
        // __lookupGetter__ / __lookupSetter__ helpers (ECMA-262 §B.2.2.4/5).
        // Depends on PDSGetPropertyDescriptor, HasOwnPropertyHelperMethod,
        // ObjectGetPrototypeOf, ToJsString — all emitted earlier.
        EmitLookupAccessorHelpers(typeBuilder, runtime);
        EmitObjectGroupBy(typeBuilder, runtime);
        // Reflect.set / setPrototypeOf / defineProperty / ownKeys / apply /
        // construct — gated on UsesReflect. (Reflect.metadata uses
        // UsesReflectMetadata, gated separately at line 848.)
        // EmitIsConstructor is shared infrastructure so stays unconditional.
        EmitIsConstructor(typeBuilder, runtime);
        if (_features.UsesReflect)
        {
            EmitReflectSet(typeBuilder, runtime);
            EmitReflectSetPrototypeOf(typeBuilder, runtime, prototypeStoreField, nonExtensibleObjectsField);
            EmitReflectDefineProperty(typeBuilder, runtime);
            EmitReflectOwnKeys(typeBuilder, runtime);
            EmitReflectApply(typeBuilder, runtime);
            EmitReflectConstruct(typeBuilder, runtime);
        }
        EmitIsArray(typeBuilder, runtime);
        EmitConcatArrays(typeBuilder, runtime);
        EmitExpandCallArgs(typeBuilder, runtime);
        EmitArrayPop(typeBuilder, runtime);
        EmitArrayPopProto(typeBuilder, runtime);
        EmitArrayShift(typeBuilder, runtime);
        EmitArrayShiftProto(typeBuilder, runtime);
        EmitArrayUnshift(typeBuilder, runtime);
        EmitArrayUnshiftProto(typeBuilder, runtime);
        EmitArraySlice(typeBuilder, runtime);
        // Array callback methods must come after InvokeValue and IsTruthy
        EmitArrayMap(typeBuilder, runtime);
        EmitArrayMapDirect(typeBuilder, runtime);
        EmitArrayMapDouble(typeBuilder, runtime);
        EmitArrayFilterDouble(typeBuilder, runtime);
        EmitArrayFilter(typeBuilder, runtime);
        EmitArrayFilterDirect(typeBuilder, runtime);
        EmitArrayFilterDirectBool(typeBuilder, runtime);
        EmitArrayForEach(typeBuilder, runtime);
        EmitArrayForEachDirect(typeBuilder, runtime);
        EmitArrayPush(typeBuilder, runtime);
        EmitArrayPushTyped(typeBuilder, runtime, ArrayElements.Double);
        EmitArrayPushTyped(typeBuilder, runtime, ArrayElements.Bool);
        EmitArrayPushProto(typeBuilder, runtime);
        EmitArrayFind(typeBuilder, runtime);
        EmitArrayFindDirect(typeBuilder, runtime);
        EmitArrayFindDirectBool(typeBuilder, runtime);
        EmitArrayFindIndex(typeBuilder, runtime);
        EmitArrayFindIndexDirect(typeBuilder, runtime);
        EmitArrayFindIndexDirectBool(typeBuilder, runtime);
        EmitArrayFindLast(typeBuilder, runtime);
        EmitArrayFindLastIndex(typeBuilder, runtime);
        EmitArraySome(typeBuilder, runtime);
        EmitArraySomeDirect(typeBuilder, runtime);
        EmitArraySomeDirectBool(typeBuilder, runtime);
        EmitArrayEvery(typeBuilder, runtime);
        EmitArrayEveryDirect(typeBuilder, runtime);
        EmitArrayEveryDirectBool(typeBuilder, runtime);
        EmitArrayReduce(typeBuilder, runtime);
        EmitArrayReduceDirect(typeBuilder, runtime);
        EmitArrayReduceDouble(typeBuilder, runtime);
        EmitArrayReduceRight(typeBuilder, runtime);
        // Search helpers use ToIntegerOrInfinity for spec-compliant fromIndex clamping.
        EmitToIntegerOrInfinityHelper(typeBuilder, runtime);
        EmitArrayIncludes(typeBuilder, runtime);
        EmitArrayIndexOf(typeBuilder, runtime);
        EmitArrayLastIndexOf(typeBuilder, runtime);
        // ECMA-262 Array.prototype.* accepts any array-like (length + indexed
        // props) as receiver — materializer unpacks objects/strings/TSArrays.
        // Not currently called from any emit path (see ILEmitter.Calls.cs
        // Stage 3 deferral note). Kept in the runtime class so a future
        // full-prototype-surface implementation can wire it up.
        EmitArrayLikeMaterialize(typeBuilder, runtime);
        EmitArrayLikeMaterializeForIteration(typeBuilder, runtime);
        EmitHasArrayLikeProperty(typeBuilder, runtime);
        EmitLoadArrayLikeElement(typeBuilder, runtime);
        // RequireObjectCoercible(this) — emitted after TSError so it can
        // construct $TypeError directly. Called from $TSFunction.CoercePrimitiveArgs
        // via late-bound reflection.
        EmitRequireObjectCoercibleThis(typeBuilder, runtime);
        EmitArrayJoin(typeBuilder, runtime);
        EmitArrayConcat(typeBuilder, runtime);
        EmitArrayReverse(typeBuilder, runtime);
        EmitArrayReverseProto(typeBuilder, runtime);
        EmitArrayFlatHelper(typeBuilder, runtime); // Must be before EmitArrayFlat
        EmitArrayFlat(typeBuilder, runtime);
        EmitArrayFlatMap(typeBuilder, runtime);
        EmitArrayFrom(typeBuilder, runtime);
        EmitArrayOf(typeBuilder, runtime);
        EmitArrayFromAdapter(typeBuilder, runtime);
        // EmitArrayConstructor is emitted earlier (before InvokeValue) so its
        // MethodBuilder is available to InvokeValue's Type-callee dispatch.
        EmitArraySort(typeBuilder, runtime);
        EmitArrayToSorted(typeBuilder, runtime);
        // ToIntegerOrInfinity now emitted earlier (before EmitArrayIndexOf).
        EmitArraySplice(typeBuilder, runtime);
        EmitArrayToSpliced(typeBuilder, runtime);
        EmitArrayToReversed(typeBuilder, runtime);
        EmitArrayWith(typeBuilder, runtime);
        EmitArrayAt(typeBuilder, runtime);
        EmitArrayFill(typeBuilder, runtime);
        EmitArrayFillProto(typeBuilder, runtime);
        EmitArrayCopyWithin(typeBuilder, runtime);
        EmitArrayCopyWithinProto(typeBuilder, runtime);
        EmitArrayEntries(typeBuilder, runtime);
        EmitArrayKeys(typeBuilder, runtime);
        EmitArrayValues(typeBuilder, runtime);
        // Stubs used as MethodInfo backing for prototype $TSFunction wrappers
        // when no dedicated $Runtime helper exists (toString/toLocaleString/
        // match/search/etc.). Must be emitted before any prototype populate.
        EmitStringPrototypeStubs(typeBuilder, runtime);
        // Populate Array.prototype dict with $TSFunction wrappers for the
        // helpers above. Must come AFTER all the Array* MethodBuilders are
        // defined.
        EmitArrayPrototypePopulate(typeBuilder, runtime);
        // Object.prototype populate body — uses HasOwnPropertyHelper +
        // IsPrototypeOfHelper which are emitted before GetFunctionMethod above.
        EmitObjectPrototypePopulate(typeBuilder, runtime);
        // Boxed primitive helpers — must come AFTER prototype populates so
        // BooleanPrototypePopulateMethod / Number / String / Object are non-null.
        EmitNewBoxedPrimitive(typeBuilder, runtime);
        EmitNormalizeForeignBoxedPrimitive(typeBuilder, runtime);
        EmitToObject(typeBuilder, runtime);
        EmitIsBoxedPrimitiveOfType(typeBuilder, runtime);
        EmitUnwrapStringReceiver(typeBuilder, runtime);
        // EmitUnwrapIfBoxed moved earlier — see comment above EmitStringify.
        // String methods
        EmitStringCharAt(typeBuilder, runtime);
        EmitStringSubstring(typeBuilder, runtime);
        EmitStringSubstr(typeBuilder, runtime);
        EmitStringIndexOf(typeBuilder, runtime);
        EmitStringIndexOfFrom(typeBuilder, runtime);
        EmitStringReplace(typeBuilder, runtime);
        EmitStringIncludes(typeBuilder, runtime);
        EmitStringStartsWith(typeBuilder, runtime);
        EmitStringEndsWith(typeBuilder, runtime);
        EmitStringSlice(typeBuilder, runtime);
        EmitStringRepeat(typeBuilder, runtime);
        EmitStringPadStart(typeBuilder, runtime);
        EmitStringPadEnd(typeBuilder, runtime);
        EmitStringCharCodeAt(typeBuilder, runtime);
        EmitStringConcat(typeBuilder, runtime);
        EmitStringLastIndexOf(typeBuilder, runtime);
        EmitStringReplaceAll(typeBuilder, runtime);
        EmitStringAt(typeBuilder, runtime);
        EmitStringFromCharCode(typeBuilder, runtime);
        EmitStringCodePointAt(typeBuilder, runtime);
        EmitStringFromCodePoint(typeBuilder, runtime);
        EmitStringNormalize(typeBuilder, runtime);
        EmitStringLocaleCompare(typeBuilder, runtime);
        // RegExp methods emitted before String.prototype populate so the
        // spec-correct match/matchAll/search/split slots can reference the
        // regex-aware helpers (Stage 1 of issue #91 follow-up). Moved up
        // from after EmitDateMethods; no downstream consumer needs the old
        // ordering. Without this, the populate wires those slots to a
        // null-returning stub that drops args and breaks `new String(...)
        // .search(...)` (45 Test262 regressions, root-caused 2026-05-01).
        if (_features.UsesRegExp)
            EmitRegExpMethods(typeBuilder, runtime);
        // String.prototype dict populate — must come AFTER all the String* helpers,
        // the stubs (emitted earlier), AND the RegExp methods above.
        EmitStringPrototypePopulate(typeBuilder, runtime);
        // Boolean.prototype populate — uses the StringPrototypeGenericStub
        // for both toString and valueOf (no dedicated Boolean helpers).
        EmitBooleanPrototypePopulate(typeBuilder, runtime);
        // Number.prototype populate is wired after EmitNumberMethods below.
        // Object utilities
        EmitGetSuperMethod(typeBuilder, runtime);
        // EmitCreateException and EmitWrapException moved earlier (before Promise methods)
        EmitThrowUndefinedVariable(typeBuilder, runtime);
        // EmitRandom moved to before gOPD (see line ~660). The original site
        // here is now empty.
        EmitMathSumPrecise(typeBuilder, runtime);
        EmitDefineSymbolAccessor(typeBuilder, runtime);
        EmitTSObjectMergeEnumerable(typeBuilder, runtime);
        // Math.* adapters moved earlier (before EmitObjectGetOwnPropertyDescriptor)
        // so gOPD's Math singleton synth can produce identity-stable
        // `desc.value === Math.X` descriptors.
        EmitGetEnumMemberName(typeBuilder, runtime);
        EmitConcatTemplate(typeBuilder, runtime);
        EmitInvokeTaggedTemplate(typeBuilder, runtime);
        EmitInvokeTaggedTemplateWithThis(typeBuilder, runtime);
        EmitObjectRest(typeBuilder, runtime);
        // #685: array binding-pattern source normalizer — depends on IterateToList /
        // GetIteratorFunction (emitted above via EmitIteratorMethodsAdvanced).
        EmitArrayDestructureSource(typeBuilder, runtime);
        // JSON methods — gated on UsesJSON (also implied by UsesHttp).
        if (_features.UsesJSON)
        {
            EmitJsonParse(typeBuilder, runtime);
            EmitJsonParseWithReviver(typeBuilder, runtime);
            EmitJsonStringify(typeBuilder, runtime);
            EmitJsonStringifyFull(typeBuilder, runtime);
        }
        // Math / JSON value-form singleton populate bodies. Emitted here — after
        // EmitMathAdapters (Math.*Adapter) and the JSON methods above — so their
        // backing MethodBuilders are resolved. JSON helpers are null when JSON is
        // unused; EmitBuiltinSingletonPopulate skips null backings. (#276)
        EmitMathSingletonPopulate(runtime);
        EmitJsonSingletonPopulate(runtime);
        // BigInt methods — gated on UsesBigInt. Detector flips it on for any
        // `123n` literal, bare `BigInt` identifier, or BigInt64Array/BigUint64Array
        // typed-array reference. EmitBigIntBinary in ILEmitter.Operators.cs only
        // runs when the type-checker has marked an operand as TypeInfo.BigInt,
        // which itself requires a BigInt source — so the call sites are
        // naturally aligned with this gate.
        if (_features.UsesBigInt)
        {
            EmitCreateBigInt(typeBuilder, runtime);
            EmitBigIntArithmetic(typeBuilder, runtime);
            EmitBigIntComparison(typeBuilder, runtime);
            EmitBigIntBitwise(typeBuilder, runtime);
        }
        // Promise methods moved earlier (before GetProperty, which needs PromiseThen for typeof p.then)
        // Number methods
        EmitNumberMethods(typeBuilder, runtime);
        // Number.prototype populate body — must come AFTER EmitNumberMethods so
        // NumberToFixed/etc. MethodBuilders are non-null.
        EmitNumberPrototypePopulate(typeBuilder, runtime);
        // Fill in the symbol-keyed accessor registry helper bodies (#266).
        EmitSymbolAccessorRegistryBodies(runtime);
        // Microtask method (queueMicrotask) - must come before timer infrastructure so ProcessMicrotasks is available
        EmitQueueMicrotaskMethod(typeBuilder, runtime);
        // Virtual timer infrastructure (must come before DateMethods which calls ProcessPendingTimers)
        EmitTimerQueueInfrastructure(typeBuilder, runtime);
        // Date methods
        if (_features.UsesDate)
            EmitDateMethods(typeBuilder, runtime);
        // Date.prototype populate — must come AFTER EmitDateMethods, which is what
        // assigns the runtime.Date* helper builders the wiring below references.
        EmitDatePrototypePopulate(typeBuilder, runtime);
        // Fill in LookupBuiltInStaticMember's body now that IsArray, NumberIs*,
        // StringFrom*, TSFunctionCtor (#63) and DateNow (value-form `Date.now`,
        // gated on UsesDate) are all in place. Only the body is late — the
        // MethodBuilder was defined early, so earlier emitters can call it.
        EmitLookupBuiltInStaticMemberBody(runtime);
        // RegExp methods moved earlier — emitted before EmitStringPrototypePopulate.
        // Error methods
        EmitErrorMethods(typeBuilder, runtime);
        // Error.prototype populate body — must come AFTER EmitErrorMethods so
        // the spec-compliant ErrorToStringSpec helper can reference $Error
        // metadata (TSErrorType, ErrorGetName/ErrorGetMessage already populated).
        EmitErrorPrototypePopulate(typeBuilder, runtime);
        // Native-error subclass prototypes — distinct per ECMA-262 §20.5.6.4.
        // Must run after EmitErrorPrototypePopulate so PDSSetPrototype can chain
        // each subclass-proto's [[Prototype]] to %Error.prototype%.
        EmitNativeErrorPrototypePopulates(typeBuilder, runtime);
        // Function.prototype populate body — must come after $TSFunction +
        // $BoundTSFunction emission and after InvokeMethodValue is wired
        // (the call/apply helpers route through it). Emitted in the same tail
        // section as ErrorPrototype.
        EmitFunctionPrototypePopulate(typeBuilder, runtime);
        // RegExp.prototype populate body — must come after $RegExp's
        // TSRegExpSym* helpers are emitted (they're referenced from the
        // populate IL). Emitted gated on UsesRegExp; otherwise the helpers
        // were never created.
        if (_features.UsesRegExp)
            EmitRegExpPrototypePopulate(typeBuilder, runtime);
        // Promise.prototype helpers + populate. Helpers wrap runtime.PromiseThen
        // /PromiseCatch/PromiseFinally with an `__this`-aware signature so
        // `Promise.prototype.then.call(p, fn)` routes correctly. Must come
        // after EmitPromiseMethods so the helper bodies can reference the
        // state-machine entry points.
        EmitPromisePrototypeHelpers(typeBuilder, runtime);
        EmitPromisePrototypePopulate(typeBuilder, runtime);
        // Map methods — gated on UsesMap. EmitMapGroupBy (`Map.groupBy(...)`)
        // depends on Map's own MapHas/Set/Get methods so it folds up under
        // the same gate. ObjectGroupBy stays unconditional (it builds a plain
        // Dictionary<string,object>, not a Map).
        if (_features.UsesMap)
        {
            EmitMapMethods(typeBuilder, runtime);
            EmitMapGroupBy(typeBuilder, runtime);
        }
        // Set methods — gated on UsesSet.
        if (_features.UsesSet)
            EmitSetMethods(typeBuilder, runtime);
        // WeakMap methods — gated on UsesWeakMap (`new WeakMap()` / bare `WeakMap`).
        if (_features.UsesWeakMap)
            EmitWeakMapMethods(typeBuilder, runtime);
        // WeakSet methods — gated on UsesWeakSet.
        if (_features.UsesWeakSet)
            EmitWeakSetMethods(typeBuilder, runtime);
        // WeakRef methods — gated on UsesWeakRef.
        if (_features.UsesWeakRef)
            EmitWeakRefMethods(typeBuilder, runtime);
        // FinalizationRegistry methods
        EmitFinalizationRegistryMethods(typeBuilder, runtime);
        // Proxy methods — gated on UsesProxy (`new Proxy()` / bare `Proxy`).
        // Proxy trap dispatch late-binds to SharpTSProxy on its normal path, so the
        // compiled output needs SharpTS.dll present at runtime when Proxy is used.
        if (_features.UsesProxy)
        {
            runtime.RequireSharpTSRuntime("Proxy");
            EmitProxyMethods(typeBuilder, runtime);
        }
        // AbortController/AbortSignal methods were moved earlier (above
        // EmitGetProperty) — its dict-receiver branch dispatches to the
        // signal getters (#224).
        // Dynamic import support. Module registry + WrapTaskAsPromise stay
        // unconditional (used by multi-module bundling and dns/fs/http/timer
        // promise wrappers). The actual `import(specifier)` impl is gated
        // inside EmitDynamicImportMethods on UsesDynamicImport.
        EmitDynamicImportMethods(typeBuilder, runtime);
        // Async generator await continuation helper — gated on UsesAsyncGenerator
        // (any `async function*` or async-generator arrow in the AST).
        if (_features.UsesAsyncGenerator)
            EmitAsyncGeneratorAwaitContinueMethods(typeBuilder, moduleBuilder, runtime);
        // NodeError conversion helpers (must be before fs methods which use them)
        EmitNodeErrorHelpers(typeBuilder, runtime);
        // Built-in module methods (fs, os, dns) — path migrated to stdlib/node/path.ts.
        if (_features.UsesFs)
            EmitFsModuleMethods(typeBuilder, runtime);
        // os module — gated on UsesOs (set by `import 'os'` or `os.X` access).
        if (_features.UsesOs)
            EmitOsModuleMethods(typeBuilder, runtime);
        if (_features.UsesDns)
        {
            // Standalone posture (#1073, explicit decision): the module-level dns
            // surface (lookup/resolve*/result-order) is fully emitted BCL IL and
            // works standalone; only dns.Resolver and the dns.promises namespace
            // late-bind to RuntimeTypes (SharpTS.dll), so the soft-dependency is
            // KEPT and recorded here rather than re-emitting those as IL.
            runtime.RequireSharpTSRuntime("dns module");
            EmitDnsModuleMethods(typeBuilder, runtime);
            EmitDnsPromisesMethods(typeBuilder, runtime);
        }
        // Emit wrapper methods for named imports
        if (_features.UsesFs)
            EmitFsModuleMethodWrappers(typeBuilder, runtime);
        // Querystring module methods migrated to stdlib/node/querystring.ts.
        // Path module methods migrated to stdlib/node/path.ts.
        // Assert module methods migrated to stdlib/node/assert.ts.
        // TTY module methods
        // primitive:tty — just isatty; user-facing tty is stdlib/node/tty.ts.
        // Gated on UsesTty (set by `import 'tty'` or any `.isTTY` access).
        if (_features.UsesTty)
            EmitTtyPrimitiveMethods(typeBuilder, runtime);
        // URL module — migrated to stdlib/node/url.ts; no runtime helpers emitted.
        // HTTP module methods (fetch, http.createServer, etc.) - must be before globalThis
        if (_features.UsesHttp)
            EmitHttpModuleMethods(typeBuilder, runtime);
        // Net module methods (net.createServer, net.connect, etc.)
        if (_features.UsesNet)
            EmitNetModuleMethods(typeBuilder, runtime);
        // TLS module methods (tls.createServer, tls.connect, etc.)
        if (_features.UsesTls)
            EmitTlsModuleMethods(typeBuilder, runtime);
        // Dgram module methods (dgram.createSocket)
        if (_features.UsesDgram)
            EmitDgramModuleMethods(typeBuilder, runtime);
        // globalThis methods (ES2020) - must be after HTTP for fetch reference
        EmitGlobalThisMethods(typeBuilder, runtime);
        // Define util inspect method signatures before ConsoleExtensions (ConsoleDir uses UtilInspectValue)
        DefineUtilInspectSignatures(typeBuilder, runtime);
        // Console extensions (error, warn, clear, time, timeEnd, timeLog)
        EmitConsoleExtensions(typeBuilder, runtime);
        // Crypto module methods — gated alongside the crypto type emissions.
        if (_features.UsesCrypto)
        {
            EmitCryptoMethods(typeBuilder, runtime);
            EmitX509CertificateFactory(typeBuilder, runtime); // crypto.X509Certificate (#1064)
            // WebCrypto (#1063): byte-level $Runtime helpers, then the $CryptoKey/
            // $SubtleCrypto/$WebCrypto types (which call into those helpers) and
            // the GetWebCryptoObject singleton body.
            EmitWebCryptoRuntimeHelpers(typeBuilder, runtime);
            EmitWebCryptoTypes((System.Reflection.Emit.ModuleBuilder)typeBuilder.Module, runtime);
        }
        else
        {
            // The Phase1-reserved GetWebCryptoObject must still get a body.
            EmitGetWebCryptoObjectStub(runtime);
        }
        // Util inspect helper bodies (console.dir depends on them; the rest of
        // the emitted util surface died when util moved to stdlib/node/util.ts).
        EmitUtilStandaloneMethods(typeBuilder, runtime);
        // Readline module methods — gated on UsesReadline (flag was already
        // detected via `import 'readline'` but the call site used to ignore it).
        if (_features.UsesReadline)
            EmitReadlineMethods(typeBuilder, runtime);
        // Child process module methods — gated on UsesChildProcess.
        if (_features.UsesChildProcess)
            EmitChildProcessMethods(typeBuilder, runtime);
        // Reflect metadata API — gated on UsesReflectMetadata (orphan-flag fix).
        if (_features.UsesReflectMetadata)
            EmitReflectMetadataMethods(typeBuilder, runtime);
        // fs.watch / fs.watchFile / fs.unwatchFile — gated on UsesFs.
        if (_features.UsesFs)
            EmitFsWatchFactories(typeBuilder, runtime);
        // Timer methods (setTimeout, clearTimeout, setInterval, clearInterval)
        EmitSetTimeoutMethod(typeBuilder, runtime);
        EmitClearTimeoutMethod(typeBuilder, runtime);
        EmitSetIntervalMethod(typeBuilder, runtime);
        EmitClearIntervalMethod(typeBuilder, runtime);
        // Timer promise methods (timers/promises module)
        EmitTimerPromisesMethods(typeBuilder, runtime);
        // Timer module wrappers for namespace imports (import * as timers from 'timers')
        EmitTimerModuleWrappers(typeBuilder, runtime);
        // Timer promises module wrappers for named/namespace imports (import { setTimeout } from 'timers/promises')
        EmitTimerPromisesModuleWrappers(typeBuilder, runtime);
        // Process global methods (env, argv, nextTick) - must be after timer methods for nextTick
        EmitProcessMethods(typeBuilder, runtime);
        // Zlib module methods — gated.
        if (_features.UsesZlib)
            EmitZlibMethods(typeBuilder, runtime);
        // Buffer module helper functions (atob/btoa/isUtf8/isAscii/transcode/SlowBuffer/
        // constants) — gated; the $Buffer class they compose is also UsesBuffer-gated.
        if (_features.UsesBuffer)
            EmitBufferModuleMethods(typeBuilder, runtime);
        // DNS module methods — gated.
        if (_features.UsesDns)
            EmitDnsModuleMethods(typeBuilder, runtime);
        // primitive:perf — only the host-tied now() method; the rest of perf_hooks
        // is pure TypeScript in stdlib/node/perf_hooks.ts. Gated on UsesPerf
        // (set by `import 'perf_hooks'` or bare `performance` reference).
        if (_features.UsesPerf)
            EmitPerfPrimitiveMethods(typeBuilder, runtime);
        // string_decoder module migrated to stdlib/node/string_decoder.ts.

        // Intl support (Intl.NumberFormat / DateTimeFormat / Collator) — gated.
        // Every Intl operation late-binds to RuntimeTypes — needs SharpTS at runtime.
        if (_features.UsesIntl)
        {
            runtime.RequireSharpTSRuntime("Intl");
            EmitIntlMethods(typeBuilder, runtime);
        }

        // (TLS handshake is now emitted as pure-BCL IL inside $TlsSocket/$TlsServer —
        //  no late-bind helpers, so a --compile'd tls program is genuinely standalone.)

        // Worker Threads support (SharedArrayBuffer, TypedArrays, Atomics, MessagePort, Worker)
        EmitWorkerHelpers(typeBuilder, runtime);

        // Cluster module support
        if (_features.UsesCluster)
            EmitClusterHelpers(typeBuilder, runtime);

        // Vm module support — gated on UsesVm (set by `import 'vm'`).
        // vm delegates to VmModuleInterpreter via late binding — needs SharpTS at runtime.
        if (_features.UsesVm)
        {
            runtime.RequireSharpTSRuntime("vm module");
            EmitVmMethods(typeBuilder, runtime);
        }

        if (_features.UsesSourceExecution)
        {
            runtime.RequireSharpTSRuntime(
                "sharpts:execution module",
                SharpTSRuntimeRequirements.FullDependencyClosure |
                SharpTSRuntimeRequirements.ManagedCompilerHost);
            EmitSourceExecutionMethods(typeBuilder, runtime);
        }

        // Web Streams API (stream/web) is now fully pure-IL emitted via
        // RuntimeEmitter.QueuingStrategy.cs / WritableStream.cs /
        // ReadableStream.cs / TransformStream.cs. No late-binding helper
        // methods needed on $Runtime.

        // Private member helpers are no longer emitted; async/generator emitters
        // now bind directly to class-private storage and method tokens.

        // NOTE: CreateType() deferred to EmitRuntimeClassFinalize to allow
        // Phase 2 method bodies (e.g., TlsConnect) to be emitted after closure types.
    }

    private TypeBuilder? _runtimeTypeBuilder;

    /// <summary>
    /// Phase 2: Finalizes the $Runtime class after all deferred method bodies are emitted.
    /// </summary>
    internal void EmitRuntimeClassFinalize()
    {
        _runtimeTypeBuilder?.CreateType();
    }
}
