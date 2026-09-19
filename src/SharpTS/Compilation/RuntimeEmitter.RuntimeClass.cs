using System.Reflection;
using System.Reflection.Emit;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Defines util inspect method signatures early so ConsoleDir can reference them.
    /// Method bodies are emitted later in EmitUtilStandaloneMethods.
    /// </summary>
    private void DefineUtilInspectSignatures(TypeBuilder typeBuilder, EmittedInspectionRuntime inspection)
    {
        // InspectValue(object value, int depth, int currentDepth) -> string
        inspection.InspectValue = typeBuilder.DefineMethod(
            "UtilInspectValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectArray(object arr, int depth, int currentDepth) -> string
        inspection.InspectArray = typeBuilder.DefineMethod(
            "UtilInspectArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32]);

        // InspectObject(object obj, int depth, int currentDepth) -> string
        inspection.InspectObject = typeBuilder.DefineMethod(
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
        var typeBuilder = runtime.RuntimeClass.Type;

        DefineCancellationFlag(typeBuilder, runtime.Cancellation);

        // Thread-static "original array-like receiver" — see EmittedArrayOperationsRuntime for
        // full rationale. Set by the Array.prototype.X.call(receiver, ...) pattern
        // matcher; read by EmitCallbackArgsAndInvoke when populating the callback's
        // 4th argument.
        var currentArrayLikeReceiverField = typeBuilder.DefineField(
            "_currentArrayLikeReceiver",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        currentArrayLikeReceiverField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        // Reuse `_currentArrayLikeReceiver` for the lazy iteration signal too.
        // (Historically forced by the layout-sensitive .NET 10 tier-0 JIT bug
        // behind issue #39 — fixed upstream in 10.0.x servicing, so adding
        // fields here is safe again.) The reuse is kept because it is also
        // semantically clean: the dispatch site already sets the field to the
        // original receiver, and LoadArrayLikeElement can decide eager vs lazy
        // by inspecting the receiver's type.
        runtime.ArrayOperations.CurrentReceiverField = currentArrayLikeReceiverField;

        // Thread-static "callback thisArg" for `arr.forEach(cb, thisArg)` and
        // similar Array prototype methods. Set by ArrayEmitter / $BoundArrayMethod
        // when the user passes a thisArg; read by EmitCallbackArgsAndInvoke as
        // the receiver passed to InvokeMethodValue.
        var currentCallbackThisArgField = typeBuilder.DefineField(
            "_currentCallbackThisArg",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static);
        currentCallbackThisArgField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayOperations.CallbackThisArgField = currentCallbackThisArgField;

        // Math singleton — a shared Dictionary<string, object> that user code
        // can mutate (`Math.length = 1`). `Math.PI` etc. still go through
        // MathStaticEmitter's compile-time interception, which fires *before*
        // this bare-reference path — so this field only surfaces when Math is
        // used as a receiver (e.g., `Array.prototype.every.call(Math, cb)`).
        var mathSingletonField = typeBuilder.DefineField(
            "_mathSingleton",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.Math.SingletonField = mathSingletonField;

        // globalThis/global sentinel (#271) — a plain object whose identity lets
        // the dynamic property paths recognize a value-position globalThis and
        // route reads/writes through GlobalThisGetProperty/GlobalThisSetProperty.
        // The field itself is forward-declared in DefineRuntimeClassPhase1 (so
        // $TSFunction.InvokeWithThis, emitted before this method, can coerce a
        // null sloppy-this thisArg to it — #735/#733). Only the .cctor init below
        // runs here; re-DefineField would throw a duplicate.
        var globalThisSingletonField = runtime.GlobalObject.SingletonField;

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
        runtime.Booleans.PrototypeField = booleanPrototypeField;
        var numberPrototypeField = typeBuilder.DefineField(
            "_numberPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.Numbers.PrototypeField = numberPrototypeField;
        // Date.prototype — addressable as a value so reflection over it works
        // (`Object.getOwnPropertyDescriptor(Date.prototype, "getTime")`).
        // Instance calls are emitted inline by DateEmitter and never read this.
        runtime.Dates.Prototype = typeBuilder.DefineField(
            "_datePrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        var stringPrototypeField = typeBuilder.DefineField(
            "_stringPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.Strings.PrototypeField = stringPrototypeField;
        var bigIntPrototypeField = typeBuilder.DefineField(
            "_bigIntPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.BigInt.PrototypeField = bigIntPrototypeField;
        var symbolPrototypeField = typeBuilder.DefineField(
            "_symbolPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.Symbols.Prototype = symbolPrototypeField;

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
        runtime.Json.SingletonField = jsonSingletonField;
        if (runtime.Reflect.Namespace is not null)
        {
            runtime.Reflect.RequireNamespace().SingletonField = typeBuilder.DefineField(
                "_reflectSingleton",
                _types.DictionaryStringObject,
                FieldAttributes.Public | FieldAttributes.Static);
        }

        // Array.prototype singleton — populated lazily after $TSFunction and the
        // Array* helper MethodBuilders are defined. Read by ArrayStaticEmitter
        // for `Array.prototype` value access. Required so `Array.prototype.sort`
        // is `typeof === "function"` (Test262 isConstructor harness probes this).
        var arrayPrototypeField = typeBuilder.DefineField(
            "_arrayPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.ArrayOperations.PrototypeField = arrayPrototypeField;

        // Object.prototype singleton — populated lazily with hasOwnProperty/
        // isPrototypeOf/toString/valueOf/etc. wrappers.
        var objectPrototypeField = typeBuilder.DefineField(
            "_objectPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.ObjectPrototypes.Prototype = objectPrototypeField;

        // Error.prototype singleton — populated with toString/constructor.
        // Returned by GetProperty's Type-receiver branch when receiver is
        // typeof($Error), so `Error.prototype.toString.call(non-error)` hits
        // the brand-checking helper instead of generic class reflection.
        var errorPrototypeField = typeBuilder.DefineField(
            "_errorPrototype",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);
        runtime.Errors.Prototype = errorPrototypeField;

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
        runtime.Errors.TypeErrorPrototype = typeErrorPrototypeField;
        runtime.Errors.RangeErrorPrototype = rangeErrorPrototypeField;
        runtime.Errors.ReferenceErrorPrototype = referenceErrorPrototypeField;
        runtime.Errors.SyntaxErrorPrototype = syntaxErrorPrototypeField;
        runtime.Errors.URIErrorPrototype = uriErrorPrototypeField;
        runtime.Errors.EvalErrorPrototype = evalErrorPrototypeField;
        runtime.Errors.AggregateErrorPrototype = aggregateErrorPrototypeField;

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
        runtime.FunctionPrototypes.Prototype = functionPrototypeField;

        // RegExp.prototype field forward-declared by DefineRuntimeClassPhase1 —
        // $RegExp's emission depends on the field token so the prototype's
        // proto-accessor helpers can compare against it. Reuse here.
        var regexpPrototypeField = runtime.RegExps.Prototype;

        // Promise.prototype is entirely absent from Promise-free assemblies.
        if (_features.UsesPromise)
        {
            runtime.RequirePromise().PrototypeField = typeBuilder.DefineField(
                "_promisePrototype",
                _types.DictionaryStringObject,
                FieldAttributes.Public | FieldAttributes.Static);
        }

        var classPrototypeCacheType = _types.MakeGenericType(
            _types.DictionaryOpen, _types.Type, _types.Object);
        var classPrototypeCacheField = typeBuilder.DefineField(
            "_classPrototypeCache",
            classPrototypeCacheType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        EmitClassPrototypeSupport(
            typeBuilder,
            runtime.ClassPrototypes,
            runtime.ObjectPrototypes,
            runtime.DescriptorStorage,
            classPrototypeCacheField
        );

        EmitCancellationCheck(typeBuilder, runtime.Cancellation);
        EmitCancellationExceptionFactory(typeBuilder, runtime.Cancellation);
        runtime.Cancellation.CompleteEmission();

        EmitRunClassDefinition(typeBuilder, runtime.ClassInitialization);
        runtime.ClassInitialization.CompleteEmission();

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
        runtime.ObjectState.FrozenObjects = frozenObjectsField;
        var sealedObjectsField = typeBuilder.DefineField(
            "_sealedObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.ObjectState.SealedObjects = sealedObjectsField;

        // Static field for non-extensible objects tracking: ConditionalWeakTable<object, object>
        var nonExtensibleObjectsField = typeBuilder.DefineField(
            "_nonExtensibleObjects",
            _types.ConditionalWeakTable,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.ObjectState.NonExtensibleObjects = nonExtensibleObjectsField;

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
        runtime.ObjectState.DeletedBuiltins = deletedBuiltinsField;

        // SharpTS standalone programs historically auto-await Task-valued
        // top-level expressions. A custom NewPromiseCapability result is still
        // an ordinary JavaScript value, however, and an ignored rejection must
        // not be rethrown by that host convenience. Track those result objects
        // weakly so the entry-point emitter can distinguish them without
        // changing their JavaScript identity or adding an observable property.
        FieldBuilder? nonAutoAwaitPromisesField = null;
        if (_features.UsesPromise)
        {
            nonAutoAwaitPromisesField = typeBuilder.DefineField(
                "_nonAutoAwaitPromises",
                _types.ConditionalWeakTable,
                FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
            );

            var markNonAutoAwaitPromise = typeBuilder.DefineMethod(
                "MarkNonAutoAwaitPromise",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Void,
                [_types.Object]);
            runtime.RequirePromise().MarkNonAutoAwaitPromiseMethod = markNonAutoAwaitPromise;
            {
                var il = markNonAutoAwaitPromise.GetILGenerator();
                var doneLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Brfalse, doneLabel);
                il.Emit(OpCodes.Ldsfld, nonAutoAwaitPromisesField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(
                    _types.ConditionalWeakTable, "AddOrUpdate", [_types.Object, _types.Object]));
                il.MarkLabel(doneLabel);
                il.Emit(OpCodes.Ret);
            }

            var shouldAutoAwaitPromise = typeBuilder.DefineMethod(
                "ShouldAutoAwaitPromise",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Boolean,
                [_types.Object]);
            runtime.RequirePromise().ShouldAutoAwaitPromiseMethod = shouldAutoAwaitPromise;
            {
                var il = shouldAutoAwaitPromise.GetILGenerator();
                var valueLocal = il.DeclareLocal(_types.Object);
                var nonNullLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Brtrue, nonNullLabel);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(nonNullLabel);
                il.Emit(OpCodes.Ldsfld, nonAutoAwaitPromisesField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldloca, valueLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(
                    _types.ConditionalWeakTable, "TryGetValue",
                    [_types.Object, _types.Object.MakeByRefType()]));
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Ret);
            }
        }

        // Static sentinel field for null/undefined Map keys
        var mapNullSentinelField = typeBuilder.DefineField(
            "_mapNullSentinel",
            _types.Object,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.CollectionKeys.NullSentinel = mapNullSentinelField;

        // Static field for FinalizationRegistry poke table
        runtime.FinalizationRegistry.PokeTable = typeBuilder.DefineField(
            "_finRegPokeTable",
            _types.ConditionalWeakTableObjectObject,
            FieldAttributes.Private | FieldAttributes.Static
        );

        // Reflection-backed host objects (Worker, parentPort, streams, crypto
        // handles, etc.) expose CLR methods through GetFieldsProperty. Cache the
        // bound $TSFunction per receiver/name so repeated calls do not repeat
        // Type/GetMethod reflection or reconstruct the wrapper's MethodInvoker
        // metadata. The weak key preserves collectible worker realms and host
        // object lifetimes; ConcurrentDictionary makes shared host objects safe.
        var reflectedMethodDictionaryType = _types.MakeGenericType(
            _types.ConcurrentDictionaryOpen, _types.String, _types.Object);
        var reflectedMethodCacheType = _types.MakeGenericType(
            typeof(ConditionalWeakTable<,>), _types.Object, reflectedMethodDictionaryType);
        var reflectedMethodCacheField = typeBuilder.DefineField(
            "_reflectedMethodCache",
            reflectedMethodCacheType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        runtime.ReflectedMethods.Cache = reflectedMethodCacheField;

        // Static field for console group indentation level (needed early for ConsoleLog)
        var consoleGroupLevelField = typeBuilder.DefineField(
            "_consoleGroupLevel",
            _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.Console.GroupLevelField = consoleGroupLevelField;

        // A generated program owns only the child processes it starts. This registry is
        // private to the generated $Runtime type, so parallel worktrees/processes cannot
        // observe or terminate one another's children.
        if (_features.UsesChildProcess)
        {
            runtime.RequireChildProcess().OwnedProcessesField = typeBuilder.DefineField(
                "_ownedChildProcesses",
                typeof(System.Collections.Concurrent.ConcurrentDictionary<int, System.Diagnostics.Process>),
                FieldAttributes.Private | FieldAttributes.Static);
            runtime.RequireChildProcess().OwnershipStoppingField = typeBuilder.DefineField(
                "_childProcessOwnershipStopping",
                _types.Int32,
                FieldAttributes.Private | FieldAttributes.Static);
        }

        // Pre-define populate-method shells before the cctor IL is generated
        // so the cctor can `Call` each populate to eagerly fill the
        // singletons. Without eager-population, `delete Number.prototype.toString;
        // n.toString` walks the chain and finds an empty Object.prototype dict
        // because populate is only invoked when Object.prototype is explicitly
        // referenced. Idempotent — populate methods early-return if Count > 0.
        DefineObjectPrototypePopulateShell(typeBuilder, runtime.ObjectPrototypes);
        DefineArrayPrototypePopulateShell(typeBuilder, runtime.ArrayOperations);
        DefineMathSingletonPopulateShell(typeBuilder, runtime.Math);
        DefineJsonSingletonPopulateShell(typeBuilder, runtime.Json);
        if (runtime.Reflect.Namespace is not null)
            DefineReflectSingletonPopulateShell(typeBuilder, runtime.Reflect.RequireNamespace());
        DefineStringPrototypePopulateShell(typeBuilder, runtime.Strings);
        DefineNumberPrototypePopulateShell(typeBuilder, runtime.Numbers);
        DefineBigIntPrototypePopulateShell(typeBuilder, runtime.BigInt);
        DefineSymbolPrototypePopulateShell(typeBuilder, runtime.Symbols);
        DefineBooleanPrototypePopulateShell(typeBuilder, runtime.Booleans);
        DefineDatePrototypePopulateShell(typeBuilder, runtime.Dates);
        DefineErrorPrototypePopulateShell(typeBuilder, runtime.Errors);
        DefineNativeErrorPrototypePopulateShells(typeBuilder, runtime.Errors);
        DefineFunctionPrototypePopulateShell(typeBuilder, runtime.FunctionPrototypes);
        DefineRegExpPrototypePopulateShell(typeBuilder, runtime.RegExps);
        if (_features.UsesPromise)
            DefinePromisePrototypePopulateShell(typeBuilder, runtime);

        // The exact toFixed fast path writes straight into the result string.
        // Predeclare its cached callback before emitting the type initializer so
        // hot calls do not allocate a delegate alongside every result string.
        DefineNumberFixedFormattingInfrastructure(typeBuilder, runtime.Numbers);

        // Static constructor to initialize Random and symbol storage
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();

        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(reflectedMethodCacheType));
        cctorIL.Emit(OpCodes.Stsfld, reflectedMethodCacheField);

        Type fixedStateType = typeof(ValueTuple<ulong, int, bool>);
        Type fixedFormatterType = EmitGenerics.MakeGenericType(typeof(SpanAction<,>),
            typeof(char), fixedStateType);
        cctorIL.Emit(OpCodes.Ldnull);
        cctorIL.Emit(OpCodes.Ldftn, runtime.Numbers.FixedUInt64FormatterCallback);
        cctorIL.Emit(OpCodes.Newobj, _types.GetConstructor(
            fixedFormatterType, _types.Object, typeof(IntPtr)));
        cctorIL.Emit(OpCodes.Stsfld, runtime.Numbers.FixedUInt64FormatterField);

        // Initialize _random = new Random()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Random));
        cctorIL.Emit(OpCodes.Stsfld, randomField);

        if (_features.UsesChildProcess)
        {
            cctorIL.Emit(OpCodes.Newobj, typeof(System.Collections.Concurrent.ConcurrentDictionary<int, System.Diagnostics.Process>)
                .GetConstructor(Type.EmptyTypes)!);
            cctorIL.Emit(OpCodes.Stsfld, runtime.RequireChildProcess().OwnedProcessesField);
        }

        // Initialize _mathSingleton = new Dictionary<string, object>()
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, mathSingletonField);

        // Initialize _globalThisSingleton = new object() (#271 sentinel identity)
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        cctorIL.Emit(OpCodes.Stsfld, globalThisSingletonField);
        runtime.GlobalObject.MarkInitializerEmitted();

        // Initialize the symbol-keyed accessor registry (#266).
        InitSymbolAccessorRegistry(cctorIL, runtime.SymbolAccessors);

        // Boolean/Number/String prototype singletons (lazy-feeling but eagerly
        // initialized so Type→prototype lookups never hit a null).
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, booleanPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, numberPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, stringPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, bigIntPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, symbolPrototypeField);
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, jsonSingletonField);
        if (runtime.Reflect.Namespace is not null)
        {
            cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            cctorIL.Emit(OpCodes.Stsfld, runtime.Reflect.RequireNamespace().SingletonField);
        }
        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        cctorIL.Emit(OpCodes.Stsfld, runtime.Dates.Prototype);

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

        if (_features.UsesPromise)
        {
            // Promise.prototype starts empty; populated lazily on first read.
            cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
            cctorIL.Emit(OpCodes.Stsfld, runtime.RequirePromise().PrototypeField);
        }

        cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(classPrototypeCacheType));
        cctorIL.Emit(OpCodes.Stsfld, classPrototypeCacheField);

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

        if (nonAutoAwaitPromisesField is not null)
        {
            cctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ConditionalWeakTable));
            cctorIL.Emit(OpCodes.Stsfld, nonAutoAwaitPromisesField);
        }

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
            cctorIL.Emit(OpCodes.Call, runtime.DescriptorStorage.SetPrototype);
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
        runtime.Process.UptimeBaselineField = uptimeBaselineField;
        cctorIL.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        cctorIL.Emit(OpCodes.Stsfld, uptimeBaselineField);

        // Initialize perf_hooks timing fields (must be called after fields are defined)
        // Note: Fields will be defined by EmitPerfHooksMethods, so we defer this initialization
        // The initialization is done inline in EmitPerfHooksMethods instead

        // Initialize _finRegPokeTable = new ConditionalWeakTable<object, object>()
        EmitFinRegPokeTableInit(cctorIL, runtime.FinalizationRegistry);

        // Define the event-subscription registry (field + two helper methods). Must be
        // emitted while we still hold the cctor IL generator so the field gets initialized.
        EmitEventSubscriptionHelpers(typeBuilder, runtime.EventSubscriptions, cctorIL);
        runtime.EventSubscriptions.CompleteEmission();

        // Eagerly populate the prototype singletons so cross-prototype-chain
        // walks (e.g. `delete Number.prototype.toString; n.toString` should
        // fall through to Object.prototype.toString) hit populated dicts.
        // Each populate is idempotent (early-returns if Count > 0).
        cctorIL.Emit(OpCodes.Call, runtime.ObjectPrototypes.Populate);
        cctorIL.Emit(OpCodes.Call, runtime.ArrayOperations.PrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.Numbers.PrototypePopulateMethod);
        if (_features.UsesBigInt)
            cctorIL.Emit(OpCodes.Call, runtime.BigInt.PrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.Symbols.PopulatePrototype);
        cctorIL.Emit(OpCodes.Call, runtime.Booleans.PrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.Dates.PopulatePrototype);
        cctorIL.Emit(OpCodes.Call, runtime.Strings.PrototypePopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.Errors.PrototypePopulate);
        cctorIL.Emit(OpCodes.Call, runtime.FunctionPrototypes.Populate);
        cctorIL.Emit(OpCodes.Call, runtime.RegExps.PopulatePrototype);
        // Math / JSON value-form singletons (`const m = Math; m.max(...)`). Each
        // populate is idempotent and skips null backings, so calling the JSON one
        // unconditionally is safe even when the program doesn't use JSON (#276).
        cctorIL.Emit(OpCodes.Call, runtime.Math.SingletonPopulateMethod);
        cctorIL.Emit(OpCodes.Call, runtime.Json.SingletonPopulateMethod);
        if (runtime.Reflect.Namespace is not null)
            cctorIL.Emit(OpCodes.Call, runtime.Reflect.RequireNamespace().SingletonPopulateMethod);

        cctorIL.Emit(OpCodes.Ret);

        // Emit all methods - these are now in partial class files
        // Core utilities

        // UnwrapIfBoxed's MethodBuilder must exist before Add/Equals/Stringify
        // so those helpers can reference its token to ToPrimitive boxed-primitive
        // wrappers before the type-based dispatch ($Object → __primitiveValue per
        // ECMA-262 §7.2.14 and §13.10.1). Its BODY is filled later
        // (EmitUnwrapIfBoxedBody, after EmitGetProperty) because the #574 own-
        // conversion dispatch calls GetProperty/InvokeMethodValue/HasOwnPropertyHelper.
        DeclareUnwrapIfBoxed(typeBuilder, runtime.BoxedPrimitives);

        EmitFormatNumberMethod(typeBuilder, runtime.Numbers);
        EmitConcatStringInt64Method(typeBuilder, runtime.StringCoercion);
        EmitStringify(typeBuilder, runtime.StringCoercion,
            runtime.ArrayStorage, runtime.Sentinels.UndefinedType, runtime.Numbers.Format, runtime.FunctionValues.Type, runtime.FunctionValues.InvokeWithThis);
        // EmitStringRaw is moved later in this method (after ToJsString/
        // ToNumber/GetProperty are emitted) so the spec-form String.raw can
        // resolve template.raw properties + ToString-coerce substitutions.
        // Format specifier helpers (must be emitted before ConsoleLog/ConsoleLogMultiple which call them)
        EmitHasFormatSpecifiers(typeBuilder, runtime.Console);
        EmitFormatSingleArg(typeBuilder, runtime.Console);
        EmitFormatAsInteger(typeBuilder, runtime);
        EmitFormatAsFloat(typeBuilder, runtime);
        EmitFormatAsJson(typeBuilder, runtime);
        EmitFormatConsoleArgs(typeBuilder, runtime);
        // GetConsoleIndent must be emitted before ConsoleLog/ConsoleLogMultiple which call it
        EmitGetConsoleIndent(typeBuilder, runtime.Console);
        EmitConsoleLog(typeBuilder, runtime);
        // JoinWithStringify must be emitted before ConsoleLogMultiple which uses it
        EmitJoinWithStringify(typeBuilder, runtime);
        EmitConsoleLogMultiple(typeBuilder, runtime.Console);
        // Exception helpers must come before ToNumber, since ToNumber emits a
        // CreateException + TSTypeErrorCtor throw on Symbol receivers per
        // ECMA-262 7.1.4 step 2. Without this earlier emit, runtime.Errors.CreateException
        // would be null at IL emission time → "Value cannot be null. (Parameter 'meth')"
        // bubbles up to every compiled program. The original line-378 EmitCreateException
        // is left in place; calling here just reuses the same MethodBuilder slot.
        EmitCreateException(runtime.Errors);
        // Reflection-backed Proxy bridges also use this helper to unwrap
        // SharpTS.dll guest exceptions and preserve emitted error branding.
        EmitInvokeMethodUnwrapped(typeBuilder, runtime.ReflectedMethods, runtime.Errors);
        EmitWrapException(
            typeBuilder,
            runtime.Errors,
            new WrapExceptionInputs(runtime.Promise, runtime.StructuredClone)
        );
        // ToNumber slot is forward-declared in DefineRuntimeClassPhase1 so
        // $RegExp's Symbol.split limit-coercion (which runs before $Runtime
        // body emit) can bind to it. Skip the duplicate-define here.
        DeclareConvertToNumber(typeBuilder, runtime.NumericCoercion);
        // ConvertToNumber's explicit Number(BigInt) branch needs the exact
        // binary64-rounding helper before its body is filled later below.
        EmitBigIntToNumber(typeBuilder, runtime.BigInt);
        EmitJsToInt32(typeBuilder, runtime.NumericCoercion);
        EmitJsLessThan(typeBuilder, runtime.Operators, runtime.NumericCoercion);
        EmitJsLessOrEqual(typeBuilder, runtime.Operators, runtime.NumericCoercion);
        EmitUpdateNumeric(typeBuilder, runtime.Operators, runtime.NumericCoercion);
        EmitIsTruthy(typeBuilder, runtime.Booleans, runtime.Sentinels.UndefinedType);
        // Promise resolving callbacks need these adoption tokens before their
        // bodies are filled by the resolve-value and capability emitters.
        if (_features.UsesPromise)
        {
            runtime.RequirePromise().CoerceAwaitableToTaskMethod = typeBuilder.DefineMethod(
                "CoerceAwaitableToTask",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.TaskOfObject,
                [_types.Object]);
            runtime.RequirePromise().ResolveValueMethod = typeBuilder.DefineMethod(
                "PromiseResolveValue",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.TaskOfObject,
                [_types.Object]);
            runtime.RequirePromise().ResolvePreparedPromiseCapabilityMethod = typeBuilder.DefineMethod(
                "ResolvePreparedPromiseCapability",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);
        }
        // Promise reaction state machines are emitted before the microtask
        // infrastructure. Reserve the shared FIFO job-enqueue token now; its
        // body is filled by EmitQueueMicrotaskMethod later in this method.
        runtime.Microtasks.QueuePromiseJob = typeBuilder.DefineMethod(
            "QueuePromiseJob",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [typeof(Action)]);
        // Promise resolving callbacks are callable built-ins. Define their
        // representation before TypeOf so it can classify them as functions;
        // InvokeValue consumes the same types later in this method.
        if (_features.UsesPromise)
            EmitPromiseCallbackTypes(moduleBuilder, runtime);
        EmitTypeOf(
            typeBuilder,
            runtime.Operators,
            new TypeOfInputs(
                runtime.ArrayOperations,
                runtime.FunctionBindings.AnyType,
                runtime.FunctionBindings.BoundType,
                runtime.FunctionBindings.ApplyType,
                runtime.FunctionBindings.BindType,
                runtime.FunctionBindings.CallType,
                runtime.UnionValues.Interface,
                runtime.UnionValues.ValueGetter,
                runtime.Map,
                runtime.Promise,
                runtime.Set,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitAdd(
            typeBuilder,
            runtime.Operators,
            new AddInputs(
                runtime.BoxedPrimitives,
                runtime.Errors,
                runtime.NumericCoercion,
                runtime.StringCoercion,
                runtime.Sentinels.UndefinedType
            )
        );
        // Equals body needs runtime.StringCoercion.ToJsString for the ECMA-262 7.2.14
        // Object-vs-String branch (`new String(s) == s` requires
        // ToPrimitive(wrapper) → string, then string compare). ToJsString is
        // emitted later (it depends on GetProperty + InvokeMethodValue which
        // depend on this same chain). Declare the Equals MethodBuilder shell
        // here so any caller that references it before EmitEquals runs gets
        // a non-null token; body fills in after EmitToJsString below.
        DeclareEquals(typeBuilder, runtime.Operators);
        EmitStrictEquals(typeBuilder, runtime.Operators, runtime.Sentinels.UndefinedType);
        // Object methods - must come BEFORE iterator methods since GetProperty, InvokeMethodValue are needed
        EmitCreateObject(typeBuilder, runtime.ObjectConstruction);
        EmitGetArrayMethod(typeBuilder, runtime);
        // Deleted-builtins tracking — used by HasOwnPropertyHelper /
        // GetFunctionMethod / ObjectGetOwnPropertyDescriptor / DeleteIndex to
        // hide name/length on a $TSFunction after `delete fn.name`. Emitted
        // before its consumers.
        EmitDeletedBuiltinsHelpers(typeBuilder, runtime.ObjectState);
        // Symbol helpers — moved before HasOwnPropertyHelper so its
        // Symbol-key arm can call IsSymbolMethod / GetSymbolDictMethod
        // (Object.prototype.hasOwnProperty must honor Symbol keys per
        // ECMA-262 §20.1.3.2 step 1's ToPropertyKey).
        EmitGetSymbolDict(typeBuilder, runtime.Symbols, symbolStorageField);
        EmitIsSymbol(typeBuilder, runtime.Symbols);
        // The shared built-in static inventory is consumed by HasOwnProperty
        // as well as GetProperty. Reserve its method token before either body;
        // the implementation is still filled later after its backing methods.
        DefineLookupBuiltInStaticMember(typeBuilder, runtime.BuiltInStatics);
        // hasOwnProperty + isPrototypeOf helpers — must come before
        // GetFunctionMethod so the corresponding arms can return $TSFunction
        // wrappers.
        runtime.ObjectState.IsExtensible = typeBuilder.DefineMethod(
            "ObjectIsExtensible",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]);
        DeclareObjectGetOwnPropertyDescriptor(typeBuilder, runtime.ObjectDescriptors);
        EmitHasOwnPropertyHelper(
            typeBuilder,
            runtime.ObjectOwnProperties,
            new HasOwnPropertyHelperInputs(
                runtime.ArrayStorage,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.GlobalObject.SingletonField,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                runtime.Json,
                runtime.BuiltInStatics.Lookup,
                runtime.Math,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                new ProxyDescriptorCallInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                ),
                runtime.RegExps,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        // propertyIsEnumerable shares HasOwn's plumbing (PDS lookup + dict
        // fallback) so emit it immediately after.
        EmitPropertyIsEnumerableHelper(
            typeBuilder,
            runtime.ObjectOwnProperties,
            new PropertyIsEnumerableHelperInputs(
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.RegExps,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        // Shell ObjectGetPrototypeOf early so IsPrototypeOfHelper can call it
        // and pick up the default-fallback to Object.prototype / Array.prototype
        // for plain Dict/List receivers without explicit PDS entries.
        DefineObjectGetPrototypeOfShell(typeBuilder, runtime.ObjectPrototypes);
        EmitIsPrototypeOfHelper(
            typeBuilder,
            runtime.ObjectPrototypes,
            new IsPrototypeOfHelperInputs(runtime.Errors, runtime.Symbols, runtime.Sentinels.UndefinedType)
        );
        // ObjectPrototypePopulate / ArrayPrototypePopulate shells already
        // defined above (before cctor) so the cctor can call them eagerly.
        EmitGetFunctionMethod(
            typeBuilder,
            runtime.FunctionIntrospection,
            new GetFunctionMethodInputs(
                runtime.DescriptorStorage,
                runtime.FunctionAttributes,
                runtime.FunctionBindings,
                runtime.FunctionConstruction,
                runtime.FunctionPrototypes,
                runtime.FunctionValues,
                runtime.ObjectOwnProperties,
                runtime.ObjectPrototypes,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );  // For bind/call/apply on functions
        // Pre-define IsBoxedPrimitiveOfType shell so InstanceOf can reference
        // it. Body emitted later (after the prototype singletons are defined).
        DefineIsBoxedPrimitiveOfTypeShell(typeBuilder, runtime.BoxedPrimitives);
        // Pre-define the AbortSignal/Intl namespace singleton fields so
        // InstanceOf can brand-check the AbortSignal singleton (#246).
        // Populate bodies are emitted later (EmitNamespaceSingletons).
        DefineNamespaceSingletonFields(typeBuilder, runtime.Abort, runtime.Intl);
        // InstanceOf walks the prototype chain via GetFunctionMethod (for the
        // `F.prototype` fetch) — must be emitted AFTER GetFunctionMethod so
        // `runtime.FunctionIntrospection.GetProperty` is populated when InstanceOf references it.
        EmitInstanceOf(
            typeBuilder,
            runtime.Operators,
            new InstanceOfInputs(
                runtime.Abort,
                runtime.BoxedPrimitives,
                runtime.FunctionIntrospection.GetProperty,
                runtime.ObjectPrototypes,
                runtime.Promise,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitToPascalCase(typeBuilder, runtime.ReflectedMethods);  // Must be emitted before GetFieldsProperty/SetFieldsProperty
        EmitSafeGetMethod(typeBuilder, runtime.ReflectedMethods); // Must be emitted before GetFieldsProperty/SetFieldsProperty
        // ArrayConstructor (#61) must come before InvokeValue since InvokeValue's
        // Type-callee dispatch branch emits a direct call to it for `Array(n)`
        // patterns where Array was stored as a value.
        EmitArrayConstructor(typeBuilder, runtime);
        // InvokeValue/InvokeMethodValue must come before GetFieldsProperty (needs InvokeMethodValue for getters)
        // and before Promise methods (needed by InvokeCallback)
        // Pre-declare ArrayLikeMaterialize's MethodBuilder so InvokeMethodValue
        // can reference it for the $BoundArrayMethod receiver-rebind path.
        // The body is filled in later (EmitArrayLikeMaterialize, line 544).
        DeclareArrayLikeMaterialize(typeBuilder, runtime.ArrayOperations);
        // Companion lazy-aware materializer + element reader for iterator
        // helpers (issue #90). Pre-declared so the iterator emitters can
        // reference them; bodies emitted after EmitGetProperty (which they
        // call).
        DeclareArrayLikeMaterializeForIteration(typeBuilder, runtime.ArrayOperations);
        DeclareLoadArrayLikeElement(typeBuilder, runtime.ArrayOperations);
        DeclareHasArrayLikeProperty(typeBuilder, runtime.ArrayOperations);
        // Promise combinators are emitted before the iterator wrapper, but
        // their normalization path consumes arbitrary iterables. Reserve the
        // method token now and fill its body in EmitIteratorMethodsAdvanced.
        DeclareIterateToList(typeBuilder, runtime.IteratorCollection, runtime.Symbols.Type);
        runtime.Invocation.Method = typeBuilder.DefineMethod(
            "InvokeMethodValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.ObjectArray]);
        runtime.Reflect.Get = typeBuilder.DefineMethod(
            "ReflectGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String, _types.Object]);
        EmitInvokeValue(
            typeBuilder,
            runtime.Invocation,
            new InvokeValueInputs(
                runtime.ArrayOperations,
                runtime.Cancellation.Check,
                runtime.Errors,
                runtime.FunctionBindings,
                runtime.FunctionValues,
                runtime.Map,
                runtime.NodeStreams,
                runtime.ObjectRead,
                runtime.Operators,
                runtime.Promise,
                runtime.ReflectedMethods,
                runtime.RegExps,
                runtime.Set,
                runtime.TextEncoding,
                runtime.TypedArrays,
                runtime.Sentinels.UndefinedType,
                _features.HasAnyTypedArray,
                _features.UsesNodeStreams,
                _features.UsesPromise,
                _features.UsesTextEncoding,
                runtime.FunctionConstruction,
                runtime.ObjectFields.Interface,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.Sentinels.UndefinedInstance
            )
        );
        EmitInvokeMethodValue(
            typeBuilder,
            runtime.Invocation,
            new InvokeMethodValueInputs(
                runtime.ArrayOperations,
                runtime.Cancellation.Check,
                runtime.Errors,
                runtime.FunctionBindings,
                runtime.FunctionValues,
                runtime.Map,
                runtime.NodeStreams,
                runtime.ObjectRead,
                runtime.Operators,
                runtime.Promise,
                runtime.ReflectedMethods,
                runtime.RegExps,
                runtime.Set,
                runtime.TextEncoding,
                runtime.TypedArrays,
                runtime.Sentinels.UndefinedType,
                _features.HasAnyTypedArray,
                _features.UsesNodeStreams,
                _features.UsesPromise,
                _features.UsesTextEncoding
            )
        );
        EmitInvokeMethodValue0(
            typeBuilder,
            runtime.Invocation,
            new InvokeMethodValue0Inputs(runtime.Cancellation.Check, runtime.Errors, runtime.FunctionValues)
        );
        runtime.Invocation.CompleteEmission();
        EmitGetFieldsProperty(
            typeBuilder,
            runtime.ObjectRead,
            new GetFieldsPropertyInputs(
                runtime.ArrayOperations,
                runtime.FunctionBindings.BoundType,
                runtime.Dates,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectFields.GetProperty,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                runtime.Invocation.Method,
                runtime.ReflectedMethods.CallableConstructor,
                runtime.ObjectOwnProperties,
                runtime.ObjectPrototypes,
                runtime.ObjectStorage,
                runtime.Records,
                runtime.Reflect,
                runtime.ReflectedMethods.Cache,
                runtime.ReflectedMethods.FindMethod,
                runtime.FunctionConstruction.Constructor,
                runtime.FunctionValues.Type,
                runtime.ReflectedMethods.ToPascalCase,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitGetListProperty(
            typeBuilder,
            runtime.ObjectRead,
            new GetListPropertyInputs(
                runtime.Arguments.LengthField,
                runtime.Arguments.Type,
                runtime.ArrayOperations,
                _features.UsesArrayPrototypeMutation,
                runtime.DescriptorStorage,
                _features.UsesDynamicPropertyDescriptors,
                runtime.Invocation.Method,
                runtime.ObjectPrototypes,
                runtime.Templates,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        // GetMapProperty / GetSetProperty are the duck-typed property dispatchers
        // for Dictionary<object,object> / HashSet<object> receivers. Each calls
        // its respective Map/Set method MethodBuilders (and BoundMapMethodCtor
        // / BoundSetMethodCtor wrappers), so they fold up under UsesMap / UsesSet.
        if (runtime.Map is not null)
            EmitGetMapProperty(typeBuilder, runtime.RequireMap());
        if (runtime.Set is not null)
            EmitGetSetProperty(typeBuilder, runtime.RequireSet());
        // Exception helpers were moved earlier (above EmitToNumber) since
        // ToNumber's Symbol-throw branch emits a CreateException call that
        // must resolve to a non-null MethodBuilder.
        EmitSetFieldsProperty(
            typeBuilder,
            runtime.ObjectWrite,
            new SetFieldsPropertyInputs(
                runtime.Dates,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                runtime.ObjectFields.SetProperty,
                runtime.Invocation.Method,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                runtime.RegExps,
                runtime.ReflectedMethods.FindMethod
            )
        );
        EmitSetFieldsPropertyStrict(
            typeBuilder,
            runtime.ObjectWrite,
            new SetFieldsPropertyStrictInputs(
                runtime.Dates,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                runtime.ObjectFields.SetProperty,
                runtime.Invocation.Method,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                runtime.RegExps,
                runtime.ReflectedMethods.FindMethod
            )
        );
        // Promise static wrappers validate their `this` value with the shared
        // constructor predicate. Emit it before Promise methods; Reflect also
        // consumes the same helper later.
        EmitIsConstructor(
            typeBuilder,
            runtime.FunctionIntrospection,
            new IsConstructorInputs(
                runtime.FunctionBindings,
                runtime.FunctionValues,
                runtime.Sentinels.UndefinedType,
                runtime.ReflectedMethods.InvokeUnwrapped
            )
        );
        runtime.FunctionIntrospection.CompleteEmission();
        // Promise methods must come before GetProperty (which needs PromiseThen for typeof p.then)
        if (_features.UsesPromise)
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
            EmitFireAbortEvent(typeBuilder, runtime.RequireAbort(), runtime.FunctionValues.Type,
                runtime.FunctionValues.Invoke, runtime.FunctionBindings.BoundType, runtime.FunctionBindings.BoundInvoke);
            EmitAbortControllerMethods(typeBuilder, runtime.RequireAbort(),
                reason => runtime.RequireSharpTSRuntime(reason), _features.UsesAbortSignalAny);
            // stream.addAbortSignal destroy-on-abort wiring (#1027) — needs the AbortSignal
            // helpers above + the $StreamAbortCallback closure emitted in the stream block.
            if (_features.UsesNodeStreams)
                EmitStreamAddAbortSignalMethod(typeBuilder, runtime);
        }
        // DataView method values are bound by GetProperty below. Emit the
        // constructor/property/method adapters now so those wrappers can use
        // their MethodInfo tokens; the remaining Worker helpers stay in their
        // usual late block.
        if (_features.HasAnyTypedArray)
            EmitDataViewHelper(typeBuilder, runtime);
        // Weak collections/references and FinalizationRegistry are represented
        // by BCL types, so generic property access cannot discover their
        // JavaScript method names. Emit their helpers before GetProperty so its
        // receiver branches can bind them into $TSFunction wrappers.
        if (runtime.WeakMap is { } weakMap)
            EmitWeakMapMethods(typeBuilder, weakMap);
        if (runtime.WeakSet is { } weakSet)
            EmitWeakSetMethods(typeBuilder, weakSet);
        if (runtime.WeakRef is { } weakRef)
            EmitWeakRefMethods(typeBuilder, weakRef);
        if (runtime.FinalizationRegistry.Implementation is { } finalizationRegistry)
            EmitFinalizationRegistryMethods(typeBuilder, finalizationRegistry,
                runtime.FinalizationRegistry.PokeTable, runtime.Sentinels.UndefinedInstance);
        // Boxed Symbol property access binds these helpers directly.
        EmitSymbolPrototypePopulate(
            typeBuilder,
            runtime.Symbols,
            new SymbolPrototypePopulateInputs(
                runtime.Errors.CreateException,
                runtime.DescriptorStorage,
                runtime.ObjectPrototypes.Prototype,
                runtime.ObjectStorage,
                runtime.FunctionConstruction.GetOrCreate,
                runtime.Errors.TypeErrorConstructor
            )
        );
        // String/Number/Boolean populate shells already defined above
        // (before cctor) so the cctor can call them eagerly.
        EmitGetProperty(
            runtime.ObjectRead,
            new GetPropertyInputs(
                runtime.Abort,
                runtime.Arguments.LengthField,
                runtime.Arguments.Type,
                runtime.ArrayBuffer,
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.BigInt,
                runtime.Booleans,
                runtime.FunctionBindings.AnyType,
                runtime.FunctionBindings.BoundType,
                runtime.Buffer,
                runtime.ClassPrototypes,
                runtime.Modules.CommonJs,
                runtime.DataView,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.FileSystem,
                runtime.FinalizationRegistry,
                runtime.FunctionBindings.ApplyType,
                runtime.FunctionBindings.BindType,
                runtime.FunctionBindings.CallType,
                runtime.FunctionPrototypes.Prototype,
                runtime.FunctionPrototypes.Populate,
                runtime.FunctionIntrospection.GetProperty,
                runtime.GlobalObject.GetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ObjectFields.GetProperty,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Invocation.Method,
                runtime.BuiltInStatics.Lookup,
                runtime.Map,
                runtime.Numbers,
                runtime.ObjectDescriptors,
                runtime.ObjectOwnProperties,
                runtime.ObjectPrototypes,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                runtime.Reflect,
                runtime.RegExps,
                runtime.ReflectedMethods.FindMethod,
                runtime.Set,
                runtime.SharedArrayBuffer,
                runtime.Strings,
                runtime.SymbolAccessors,
                runtime.Symbols,
                runtime.FunctionValues.BindThis,
                runtime.FunctionConstruction.Constructor,
                runtime.FunctionConstruction.CachedConstructor,
                runtime.FunctionValues.ExpectsThisField,
                runtime.FunctionConstruction.GetOrCreate,
                runtime.FunctionValues.InvokeWithThis,
                runtime.FunctionValues.Type,
                runtime.Namespaces.Get,
                runtime.Namespaces.Type,
                runtime.TypedArrays,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType,
                runtime.WeakMap,
                runtime.WeakRef,
                runtime.WeakSet
            )
        );
        // UnwrapIfBoxed's body is filled after GetIndex below.  In addition to
        // GetProperty/InvokeMethodValue it now uses indexed symbol lookup for
        // the @@toPrimitive hook.
        // Dynamic iterator-protocol bridge — must come after GetProperty +
        // InvokeMethodValue since its non-enumerator fallback calls both.
        EmitIteratorProtocolCall(
            typeBuilder,
            runtime.IteratorProtocol,
            new IteratorProtocolCallInputs(runtime.Generators, runtime.Invocation, runtime.ObjectRead, runtime.Sentinels.UndefinedInstance)
        );
        // GetSymbolDict / IsSymbol already emitted above (moved earlier so
        // HasOwnPropertyHelper's Symbol-key arm can call them).
        // ToJsString depends on GetProperty + InvokeMethodValue + Stringify; emit after those.
        EmitToJsString(typeBuilder, runtime.StringCoercion, runtime.ArrayStorage, runtime.ArrayOperations,
            new StringCoercionInputs(
                runtime.Sentinels.UndefinedType, runtime.Symbols.Type, runtime.GlobalObject.SingletonField, runtime.GlobalObject.GetProperty,
                runtime.Operators.TypeOf, runtime.Invocation.Method, runtime.Arguments.Type, runtime.ObjectRead.Property, runtime.ObjectStorage.Type,
                runtime.FunctionValues.Type, runtime.FunctionBindings.AnyType, runtime.ObjectOwnProperties.HasOwnProperty, runtime.ObjectFields.Interface,
                runtime.Symbols.GetStorage, runtime.Symbols.ToPrimitive, runtime.DescriptorStorage.DescriptorType,
                runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!,
                runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!, runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!,
                runtime.DescriptorStorage.HasPrototypeEntry, runtime.DescriptorStorage.GetPrototype, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor),
            runtime.RegExps.Implementation is not null ? runtime.RegExps.RequireImplementation().Type : null);
        // StringFromValue (String(x) call form) wraps ToJsString with the
        // §22.1.1.1 Symbol exemption; emit right after it.
        EmitStringFromValue(typeBuilder, runtime.StringCoercion, runtime.Symbols.Type);
        // StringifyCoerce (implicit-coercion sites) wraps Stringify with the
        // §7.1.17 Symbol TypeError; the body needs TSSymbolType/TSTypeErrorCtor,
        // both bound by this point (ToJsString's Symbol arm uses them too).
        EmitStringifyCoerce(runtime.StringCoercion,
            runtime.Symbols.Type, runtime.ObjectStorage.Type, runtime.ObjectFields.Interface, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        // Equals body — must come after ToJsString since the Object-vs-String
        // branch calls runtime.StringCoercion.ToJsString.
        EmitEquals(
            runtime.Operators,
            new EqualsInputs(
                runtime.BoxedPrimitives,
                runtime.NumericCoercion,
                runtime.ObjectStorage,
                runtime.StringCoercion,
                runtime.Sentinels.UndefinedType
            )
        );
        // ToNumber/ConvertToNumber bodies: emit AFTER GetProperty/InvokeMethodValue
        // so their ToPrimitive(value, "number") on Dictionary/$Object args can
        // call those helpers.
        EmitToNumber(typeBuilder, runtime.NumericCoercion,
            new AbstractNumberInputs(runtime.Sentinels.UndefinedType, runtime.Symbols.Type, runtime.ObjectStorage.Type,
                runtime.FunctionValues.Type, runtime.FunctionBindings.AnyType, runtime.ObjectFields.Interface,
                runtime.DescriptorStorage.DescriptorType, runtime.DescriptorStorage.DescriptorGetter.GetGetMethod()!,
                runtime.DescriptorStorage.DescriptorSetter.GetGetMethod()!, runtime.DescriptorStorage.DescriptorValue.GetGetMethod()!,
                runtime.Symbols.ToPrimitive, runtime.Symbols.GetStorage, runtime.ObjectRead.Property, runtime.Invocation.Method,
                runtime.Operators.TypeOf, runtime.StringCoercion.ToJsString, runtime.BoxedPrimitives.UnwrapIfBoxed,
                runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitConvertToNumber(typeBuilder, runtime.NumericCoercion,
            new ExplicitNumberInputs(runtime.Sentinels.UndefinedType, runtime.Symbols.Type, runtime.ObjectStorage.Type,
                runtime.ObjectRead.Property, runtime.Invocation.Method, runtime.StringCoercion.ToJsString,
                runtime.BigInt.ToNumber, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        // String.raw lives here so its body can read `template.raw` via
        // GetProperty and ToString-coerce substitutions via ToJsString.
        EmitStringRaw(typeBuilder, runtime.Templates,
            runtime.Sentinels.UndefinedType, runtime.ObjectRead.Property, runtime.NumericCoercion.ToNumber, runtime.StringCoercion.ToJsString, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        EmitSetProperty(
            runtime.ObjectWrite,
            new SetPropertyInputs(
                runtime.Abort,
                runtime.Arguments.LengthField,
                runtime.Arguments.Type,
                runtime.ArrayStorage,
                runtime.FunctionBindings.AnyType,
                runtime.FunctionBindings.BoundType,
                runtime.Modules.CommonJs,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.GlobalObject.SetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Invocation.Method,
                runtime.BuiltInStatics.Lookup,
                runtime.NumericCoercion,
                runtime.ObjectDescriptors,
                runtime.ObjectOwnProperties,
                runtime.ObjectPrototypes,
                runtime.ObjectRead,
                runtime.ObjectState,
                runtime.ObjectStorage,
                _features.UsesProxy,
                runtime.Reflect.Assignment,
                runtime.RegExps,
                runtime.FunctionValues.InvokeWithThis,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitSetPropertyStrict(
            typeBuilder,
            runtime.ObjectWrite,
            new SetPropertyStrictInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.FunctionBindings.AnyType,
                runtime.FunctionBindings.BoundType,
                runtime.Modules.CommonJs,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.GlobalObject.SetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Invocation.Method,
                runtime.ObjectDescriptors,
                runtime.ObjectRead,
                runtime.ObjectState,
                runtime.ObjectStorage,
                _features.UsesProxy,
                runtime.Reflect.Assignment,
                runtime.FunctionValues.Type
            )
        );
        EmitDeleteProperty(
            typeBuilder,
            runtime.ObjectDeletion,
            new DeletePropertyInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.GlobalObject.Properties,
                runtime.GlobalObject.SingletonField,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.ObjectDescriptors,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Records,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitDeletePropertyStrict(
            typeBuilder,
            runtime.ObjectDeletion,
            new DeletePropertyStrictInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.GlobalObject.Properties,
                runtime.GlobalObject.SingletonField,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.ObjectDescriptors,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Records,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitMergeIntoTSObject(
            typeBuilder,
            runtime.ObjectConstruction,
            new MergeIntoTSObjectInputs(
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ObjectStorage
            )
        );
        // (Symbol helpers EmitGetSymbolDict + EmitIsSymbol now emitted earlier
        // — before EmitToJsString — so the @@toPrimitive lookup can use them.)
        // HasIn operator depends on IsSymbol and GetSymbolDict
        EmitHasIn(
            typeBuilder,
            runtime.Operators,
            new HasInInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.ObjectDescriptors,
                runtime.ObjectPrototypes,
                runtime.ObjectRead,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.ReflectedMethods.ToPascalCase,
                runtime.Sentinels.UndefinedType
            )
        );
        // Array SetElement helpers - must come BEFORE GetIndex/SetIndex which reference them.
        // Previously only Typed variants were emitted here; the Object variant was deferred to
        // the Arrays section below. That left SetIndex's object-list branch unable to call it,
        // so we emit all of them up front now (including Object) for JS-spec auto-extend semantics.
        foreach (var desc in ArrayElements.All)
            EmitSetArrayElementFor(typeBuilder, runtime.ArrayOperations, desc);
        // Note: TypedArray detection helpers are emitted earlier (before GetProperty)
        EmitGetIndex(
            typeBuilder,
            runtime.ObjectRead,
            new GetIndexInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.FunctionBindings.BoundType,
                runtime.Buffer,
                runtime.DescriptorStorage,
                runtime.FunctionPrototypes.Prototype,
                runtime.FunctionPrototypes.Populate,
                runtime.GlobalObject.GetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Invocation.Method,
                runtime.ObjectDescriptors,
                runtime.ObjectStorage,
                runtime.RegExps,
                runtime.StringCoercion,
                runtime.Strings,
                runtime.SymbolAccessors,
                runtime.Symbols,
                runtime.FunctionConstruction.Constructor,
                runtime.FunctionValues.Type,
                runtime.TypedArrays,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        // DisposeResource uses the shared Symbol indexed-get path so descriptor
        // carriers and accessors are observed correctly.
        EmitDisposeResource(typeBuilder, runtime.ResourceDisposal, runtime.ObjectRead.Index,
            runtime.Sentinels.UndefinedType, runtime.Invocation.Method);
        runtime.ResourceDisposal.CompleteEmission();
        EmitSetIndex(
            typeBuilder,
            runtime.ObjectWrite,
            new SetIndexInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.Buffer,
                runtime.DescriptorStorage,
                runtime.GlobalObject.SetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Invocation.Method,
                runtime.Math,
                runtime.ObjectDescriptors,
                runtime.ObjectOwnProperties,
                runtime.ObjectPrototypes,
                runtime.ObjectRead,
                runtime.ObjectState,
                runtime.ObjectStorage,
                _features.UsesProxy,
                runtime.Reflect.Assignment,
                runtime.StringCoercion,
                runtime.SymbolAccessors,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.TypedArrays,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitSetIndexStrict(
            typeBuilder,
            runtime.ObjectWrite,
            new SetIndexStrictInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.Invocation.Method,
                runtime.ObjectState,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitDeleteIndex(
            typeBuilder,
            runtime.ObjectDeletion,
            new DeleteIndexInputs(
                runtime.ArrayStorage,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.Json,
                runtime.Math,
                runtime.ObjectState,
                runtime.Promise,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.FunctionValues.Type
            )
        );
        EmitDeleteIndexStrict(
            typeBuilder,
            runtime.ObjectDeletion,
            new DeleteIndexStrictInputs(
                runtime.ArrayStorage,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.Json,
                runtime.Math,
                runtime.ObjectState,
                runtime.Promise,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.FunctionValues.Type
            )
        );
        EmitStrictModeHelpers(typeBuilder, runtime.Operators, runtime.Errors);
        // Basic iterator protocol methods - must come AFTER object methods (need GetProperty, InvokeMethodValue)
        EmitIteratorMethodsBasic(typeBuilder, runtime);
        runtime.IteratorProtocol.CompleteEmission();
        // Adapt custom iterator objects after captured-next and result helpers exist.
        // Iterator collection and normalization helpers follow.
        EmitIteratorWrapperType(
            moduleBuilder,
            runtime.IteratorWrappers,
            new IteratorWrapperInputs(runtime.IteratorRecords, runtime.IteratorProtocol.Done, runtime.IteratorProtocol.Value)
        );
        runtime.IteratorWrappers.CompleteEmission();
        EmitArrayIteratorType(moduleBuilder, runtime.ArrayOperations, new ArrayIteratorInputs(runtime.Arguments, runtime.ObjectRead.Index));
        if (runtime.Map is not null)
            EmitMapCollectionIteratorType(moduleBuilder, runtime.CollectionKeys, runtime.RequireMap());
        if (runtime.Set is not null)
            EmitSetCollectionIteratorType(moduleBuilder, runtime.RequireSet());
        if (_features.UsesPromise)
            EmitPromiseResolveValue(moduleBuilder, runtime);
        // Promise combinators reserve their normalization method token early,
        // but its incremental custom-iterator body needs both the basic
        // protocol helpers and $IteratorWrapper.
        if (_features.UsesPromise)
            EmitNormalizePromiseList(typeBuilder, runtime);
        // Fill the previously declared iterable-to-list methods.
        EmitIteratorMethodsAdvanced(
            typeBuilder,
            runtime.IteratorCollection,
            new IteratorCollectionInputs(
                runtime.ArrayStorage, runtime.CollectionKeys, runtime.Errors, runtime.Invocation,
                runtime.IteratorProtocol, runtime.IteratorRecords, runtime.ObjectRead,
                runtime.Sentinels.UndefinedType, runtime.Sentinels.UndefinedInstance, runtime.TypedArrays.Implementation,
                _features.UsesBuffer ? runtime.RequireBuffer() : null,
                runtime.Map is not null, _features.UsesArrayPrototypeMutation)
        );
        runtime.IteratorCollection.CompleteEmission();
        // ES2025 Iterator Helper methods and lazy wrapper types
        EmitIteratorHelperMethods(
            typeBuilder, moduleBuilder, runtime.IteratorHelpers,
            new IteratorHelperInputs(runtime.Errors, runtime.IteratorWrappers.Ctor,
                runtime.Invocation.Method, runtime.Booleans.IsTruthy,
                runtime.Generators, runtime.Sentinels.UndefinedInstance));
        runtime.IteratorHelpers.CompleteEmission();
        // Arrays - must come AFTER iterator methods since ConcatArrays/ExpandCallArgs use IterateToList.
        // SetArrayElement* helpers (including Object variant) are emitted earlier, BEFORE SetIndex,
        // since SetIndex's object-list branch also calls SetArrayElement for auto-extend semantics.
        EmitCreateArray(typeBuilder, runtime);
        EmitGetLength(
            typeBuilder,
            runtime.ObjectRead,
            new GetLengthInputs(runtime.Arguments.LengthField, runtime.Arguments.Type, runtime.ArrayStorage)
        );
        EmitGetElement(
            typeBuilder,
            runtime.ObjectRead,
            new GetElementInputs(runtime.ArrayStorage, runtime.Sentinels.UndefinedInstance)
        );
        // Proxy [[OwnPropertyKeys]] needs compiler-owned descriptor and
        // extensibility callbacks even though their public Object methods are
        // emitted later in this section. Declare the descriptor shell and emit
        // isExtensible up front so key consumers can capture stable delegates.
        EmitObjectIsExtensible(runtime.ObjectState, new ObjectIsExtensibleInputs(runtime.DescriptorStorage, runtime.ObjectRead.Property, runtime.ReflectedMethods.InvokeUnwrapped));
        DeclareProxyOwnKeysHelpers(typeBuilder, runtime.ObjectKeys);
        EmitNormalizeOwnPropertyKeys(typeBuilder, runtime.ObjectKeys);
        EmitGetOwnPropertyNames(
            typeBuilder,
            runtime.ObjectKeys,
            new GetOwnPropertyNamesInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ObjectStorage,
                runtime.Promise,
                new ProxyDescriptorCallInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                ),
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                runtime.RegExps,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        // Object.assign consumes both halves of [[OwnPropertyKeys]], so make
        // the Symbol-key collector available before emitting assign.
        EmitGetOwnPropertySymbols(
            typeBuilder,
            runtime.ObjectKeys,
            new GetOwnPropertySymbolsInputs(
                runtime.Booleans,
                runtime.Errors,
                runtime.ObjectRead.Property,
                new ProxyDescriptorCallInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                ),
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitProxyOwnKeysHelperBodies(
            runtime.ObjectKeys,
            new ProxyOwnKeysHelperBodiesInputs(runtime.ObjectRead.Property, runtime.NumericCoercion)
        );
        EmitGetKeys(
            typeBuilder,
            runtime.ObjectKeys,
            new GetKeysInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ObjectStorage,
                new ProxyDescriptorCallInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                ),
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        // MergeIntoObject implements CopyDataProperties through GetKeys and
        // GetProperty, so its body must be emitted after GetKeys is available.
        EmitMergeIntoObject(
            typeBuilder,
            runtime.ObjectConstruction,
            new MergeIntoObjectInputs(
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.ObjectRead.Index,
                runtime.ObjectRead.Property,
                runtime.ObjectDescriptors,
                runtime.ObjectKeys,
                runtime.ObjectOwnProperties,
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                _features.UsesProxy,
                runtime.ObjectWrite.Index,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitGetValues(
            typeBuilder,
            runtime.ObjectOperations,
            new GetValuesInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ObjectKeys,
                runtime.ObjectStorage,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitGetEntries(
            typeBuilder,
            runtime.ObjectOperations,
            new GetEntriesInputs(
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ObjectKeys,
                runtime.ObjectStorage,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectFromEntries(
            typeBuilder,
            runtime.ObjectOperations,
            new ObjectFromEntriesInputs(runtime.Errors, runtime.IteratorCollection.ToList, runtime.Symbols, runtime.Sentinels.UndefinedType)
        );
        EmitObjectHasOwn(
            typeBuilder,
            runtime.ObjectOwnProperties,
            new ObjectHasOwnInputs(runtime.Errors, runtime.Sentinels.UndefinedType)
        );
        EmitObjectIs(typeBuilder, runtime.ObjectOperations);
        EmitObjectAssign(
            typeBuilder,
            runtime.ObjectOperations,
            new ObjectAssignInputs(
                runtime.Booleans,
                runtime.BoxedPrimitives,
                runtime.Errors,
                runtime.ObjectRead.Index,
                runtime.ObjectRead.Property,
                runtime.ObjectDescriptors,
                runtime.ObjectKeys,
                runtime.ObjectOwnProperties,
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                runtime.ObjectWrite.IndexStrict,
                runtime.ObjectWrite.PropertyStrict,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectFreeze(typeBuilder, runtime.ObjectState, new ObjectIntegrityInputs(runtime.ArrayStorage, runtime.DescriptorStorage, runtime.ObjectStorage));
        EmitObjectSeal(typeBuilder, runtime.ObjectState, new ObjectIntegrityInputs(runtime.ArrayStorage, runtime.DescriptorStorage, runtime.ObjectStorage));
        EmitObjectIsFrozen(typeBuilder, runtime.ObjectState);
        EmitObjectIsSealed(typeBuilder, runtime.ObjectState);
        EmitObjectDefineProperty(
            typeBuilder,
            runtime.ObjectDescriptors,
            new ObjectDefinePropertyInputs(
                runtime.ArrayOperations,
                runtime.ArrayStorage,
                runtime.Booleans,
                runtime.FunctionBindings.AnyType,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectOwnProperties.HasOwnProperty,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.NumericCoercion,
                runtime.ObjectOperations.Is,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Records,
                runtime.RegExps,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        // Math.* adapters must precede gOPD so its Math singleton synth can
        // reach the adapter MethodBuilders to produce identity-stable
        // `desc.value === Math.X` for built-in methods. Moved up from the
        // original site at the end of the runtime emit. Dep: runtime.ToNumber
        // (emitted at line 580, before this site).
        EmitMathAdapters(typeBuilder, runtime.Math, runtime.NumericCoercion.ToNumber, runtime.NumericCoercion.JsToInt32);
        // Error.isError is a small standalone type-brand helper. Emit it before
        // gOPD so built-in static descriptor synthesis can reference it.
        EmitErrorIsError(typeBuilder, runtime.Errors);
        // EmitRandom moved here from the original late-site so gOPD's Math
        // singleton synth can reach runtime.Math.Random and produce an identity-
        // stable `desc.value === Math.random` descriptor. The Random method
        // builder only needs randomField (defined at line 215) — both
        // available now.
        EmitRandom(typeBuilder, runtime.Math, randomField);
        EmitObjectGetOwnPropertyDescriptor(
            runtime.ObjectDescriptors,
            new ObjectGetOwnPropertyDescriptorInputs(
                runtime.ArrayStorage,
                runtime.Dates,
                runtime.DescriptorStorage,
                runtime.ObjectOperations.Entries,
                runtime.ObjectKeys.Keys,
                runtime.ObjectKeys.Names,
                runtime.ObjectRead.Property,
                runtime.GlobalObject.GetProperty,
                runtime.GlobalObject.SingletonField,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.Json,
                runtime.BuiltInStatics.Lookup,
                runtime.Math,
                runtime.ObjectOperations.Assign,
                runtime.ObjectOperations.FromEntries,
                runtime.ObjectOwnProperties.HasOwn,
                runtime.ObjectOperations.Is,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                new ProxyDescriptorCallInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                ),
                runtime.RegExps,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.FunctionConstruction.GetOrCreate,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectDefineProperties(
            typeBuilder,
            runtime.ObjectDescriptors,
            new ObjectDefinePropertiesInputs(
                runtime.Errors,
                runtime.ObjectKeys.Keys,
                runtime.ObjectRead.Property,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectGetOwnPropertyDescriptors(
            typeBuilder,
            runtime.ObjectDescriptors,
            new ObjectGetOwnPropertyDescriptorsInputs(
                runtime.Errors,
                runtime.ObjectKeys.Ordinary,
                new ProxyOwnKeysCallInputs(
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.CreateProxyList,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.Symbols.IsSymbol,
                    runtime.ObjectRead.Property,
                    runtime.ReflectedMethods.InvokeUnwrapped
                ),
                runtime.Symbols,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectCreate(
            typeBuilder,
            runtime.ObjectPrototypes,
            new ObjectCreateInputs(
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectDescriptors,
                runtime.Symbols,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        // Promise keyed-combinator shells are declared with Promise methods,
        // but their implementation needs all of the object-model helpers above.
        if (_features.UsesPromise)
            EmitPromiseKeyedMethodBodies(runtime);
        EmitObjectPreventExtensions(typeBuilder, runtime.ObjectState, new ObjectPreventExtensionsInputs(runtime.ArrayStorage, runtime.Errors.CreateException, runtime.DescriptorStorage, runtime.ObjectRead.Property, runtime.ReflectedMethods.InvokeUnwrapped, runtime.ObjectStorage, runtime.Errors.TypeErrorConstructor));
        EmitObjectGetPrototypeOf(
            runtime.ClassPrototypes,
            runtime.ObjectPrototypes,
            new ObjectGetPrototypeOfInputs(
                runtime.ArrayOperations,
                runtime.Booleans,
                runtime.FunctionBindings.BoundType,
                runtime.Dates,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.FunctionPrototypes.Prototype,
                runtime.FunctionPrototypes.Populate,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.Numbers,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Promise,
                runtime.Records,
                runtime.RegExps,
                runtime.Strings,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            ),
            prototypeStoreField
        );
        EmitObjectSetPrototypeOf(
            typeBuilder,
            runtime.ObjectPrototypes,
            new ObjectSetPrototypeOfInputs(
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.Interface,
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.ObjectState,
                runtime.ObjectStorage,
                runtime.Records,
                runtime.Symbols,
                runtime.Sentinels.UndefinedType
            ),
            prototypeStoreField
        );
        // __lookupGetter__ / __lookupSetter__ helpers (ECMA-262 §B.2.2.4/5).
        // Depends on PDSGetPropertyDescriptor, HasOwnPropertyHelperMethod,
        // ObjectGetPrototypeOf, ToJsString — all emitted earlier.
        EmitLookupAccessorHelpers(
            typeBuilder,
            runtime.ObjectOwnProperties,
            new LookupAccessorHelpersInputs(
                runtime.FunctionBindings.BoundType,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.ObjectDescriptors,
                runtime.ObjectPrototypes,
                runtime.StringCoercion,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        EmitObjectGroupBy(
            typeBuilder,
            runtime.ObjectOperations,
            new ObjectGroupByInputs(
                runtime.ArrayStorage,
                runtime.DescriptorStorage,
                runtime.Errors,
                runtime.IteratorProtocol.Function,
                runtime.Invocation.Value,
                runtime.IteratorCollection.ToList,
                runtime.RuntimeClass.Type,
                runtime.Symbols,
                runtime.FunctionValues.Type,
                runtime.Sentinels.UndefinedType
            )
        );
        // Reflect.set / setPrototypeOf / defineProperty / ownKeys / apply /
        // construct — gated on UsesReflect. (Reflect.metadata uses
        // UsesReflectMetadata, gated separately at line 848.)
        // IsConstructor is emitted before Promise methods above; Reflect uses
        // the already-defined shared helper here.
        // Proxy [[Set]] forwarding also uses the receiver-aware ordinary-set
        // helper even when guest code never names the Reflect object.
        // ReflectGet is also the receiver-preserving ordinary [[Get]] helper
        // used by prototype recursion in GetProperty, so its reserved method
        // must always receive a body even when guest code never names Reflect.
        EmitReflectGet(
            typeBuilder,
            runtime.Reflect,
            new ReflectGetInputs(
                runtime.ReflectedMethods.InvokeUnwrapped,
                runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                runtime.ObjectOwnProperties.HasOwnProperty,
                runtime.FunctionIntrospection.GetProperty,
                runtime.Invocation.Method,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType,
                runtime.ObjectRead.Property
            )
        );
        if (runtime.Reflect.Assignment is not null)
        {
            EmitReflectSet(
                typeBuilder,
                runtime.Reflect.RequireAssignment(),
                new ReflectSetInputs(
                    new ProxySetCallInputs(
                        runtime.Reflect.RequireAssignment().Set,
                        runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                        runtime.ObjectRead.Property,
                        runtime.ReflectedMethods.InvokeUnwrapped
                    ),
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.DescriptorStorage.IsFrozen,
                    runtime.ObjectOwnProperties.HasOwnProperty,
                    runtime.StringCoercion.ToJsString,
                    runtime.ObjectPrototypes.GetPrototypeOf,
                    runtime.Booleans.IsTruthy,
                    runtime.Invocation.Method,
                    runtime.Sentinels.UndefinedType,
                    runtime.ObjectRead.Property,
                    runtime.ObjectWrite.Property
                )
            );
            EmitReflectDefineProperty(
                typeBuilder,
                runtime.Reflect.RequireAssignment(),
                new ReflectDefinePropertyInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectOwnProperties.HasOwnProperty,
                    runtime.StringCoercion.ToJsString,
                    runtime.ObjectDescriptors.DefineProperty,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                )
            );
        }
        if (runtime.Reflect.Namespace is not null)
        {
            EmitReflectDeleteProperty(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectDeletePropertyInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.StringCoercion.ToJsString,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectDeletion.Property,
                    runtime.Sentinels.UndefinedType,
                    runtime.ObjectRead.Property
                )
            );
            EmitReflectPreventExtensions(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectPreventExtensionsInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectState.PreventExtensions,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property
                )
            );
            EmitReflectSetPrototypeOf(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                prototypeStoreField,
                nonExtensibleObjectsField,
                new ReflectSetPrototypeOfInputs(
                    runtime.Errors.CreateException,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectPrototypes.GetPrototypeOf,
                    runtime.ObjectPrototypes.SetPrototypeOf,
                    runtime.ObjectState.IsExtensible,
                    runtime.Sentinels.UndefinedType,
                    runtime.Symbols.Type,
                    runtime.ObjectRead.Property
                )
            );
            EmitReflectOwnKeys(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectOwnKeysInputs(
                    runtime.Errors.CreateException,
                    runtime.Errors.TypeErrorConstructor,
                    new ProxyOwnKeysCallInputs(
                        runtime.ObjectKeys.Ordinary,
                        runtime.ObjectKeys.CreateProxyList,
                        runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                        runtime.ObjectState.IsExtensible,
                        runtime.Symbols.IsSymbol,
                        runtime.ObjectRead.Property,
                        runtime.ReflectedMethods.InvokeUnwrapped
                    ),
                    runtime.ObjectKeys.Ordinary,
                    runtime.ObjectKeys.Symbols,
                    runtime.ObjectKeys.Names,
                    runtime.Sentinels.UndefinedType,
                    runtime.Symbols.Type
                )
            );
            EmitReflectApply(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectApplyInputs(runtime.Invocation.Method)
            );
            EmitReflectConstruct(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectConstructInputs(
                    runtime.Errors.CreateException,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.Errors.CreateErrorFromTypeOrNull,
                    runtime.NumericCoercion.ToNumber,
                    runtime.FunctionIntrospection.IsConstructor,
                    runtime.DynamicConstruction.Function,
                    runtime.Sentinels.UndefinedType,
                    runtime.ObjectRead.Property,
                    runtime.DataView,
                    runtime.Promise
                )
            );
            EmitReflectValueFormMethods(
                typeBuilder,
                runtime.Reflect.RequireNamespace(),
                new ReflectValueFormMethodsInputs(
                    runtime.Reflect.Get,
                    runtime.Reflect.RequireAssignment().Set,
                    runtime.Reflect.RequireAssignment().DefineProperty,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.StringCoercion.ToJsString,
                    runtime.ObjectPrototypes.GetPrototypeOf,
                    runtime.ObjectState.IsExtensible,
                    runtime.Operators.HasIn
                )
            );
            EmitReflectSingletonPopulate(runtime.Reflect.RequireNamespace(), GetBuiltinSingletonInputs(runtime));
        }
        EmitIsArray(typeBuilder, runtime);
        EmitConcatArrays(typeBuilder, runtime);
        EmitExpandCallArgs(
            typeBuilder,
            runtime.CallArguments,
            new ExpandCallArgsInputs(runtime.Symbols, runtime.IteratorCollection.ToList)
        );
        runtime.CallArguments.CompleteEmission();
        EmitArrayPop(typeBuilder, runtime);
        EmitArrayPopProto(typeBuilder, runtime);
        EmitArrayShift(typeBuilder, runtime);
        EmitArrayShiftTyped(typeBuilder, runtime, ArrayElements.Double);
        EmitArrayShiftTyped(typeBuilder, runtime, ArrayElements.Bool);
        EmitArrayShiftProto(typeBuilder, runtime);
        EmitArrayShiftNumber(typeBuilder, runtime);
        EmitArrayUnshift(typeBuilder, runtime);
        EmitArrayUnshiftTyped(typeBuilder, runtime, ArrayElements.Double);
        EmitArrayUnshiftTyped(typeBuilder, runtime, ArrayElements.Bool);
        EmitArrayUnshiftProto(typeBuilder, runtime);
        EmitArrayUnshiftNumber(typeBuilder, runtime);
        EmitArraySlice(typeBuilder, runtime);
        // Array callback methods must come after InvokeValue and IsTruthy
        EmitArrayMap(typeBuilder, runtime);
        EmitArrayMapDirect(typeBuilder, runtime);
        EmitArrayMapDouble(typeBuilder, runtime.ArrayOperations);
        EmitArrayFilterDouble(typeBuilder, runtime.ArrayOperations);
        EmitArrayFilter(typeBuilder, runtime);
        EmitArrayFilterDirect(typeBuilder, runtime);
        EmitArrayFilterDirectBool(typeBuilder, runtime);
        EmitArrayForEach(typeBuilder, runtime);
        EmitArrayForEachDirect(typeBuilder, runtime);
        EmitArrayPush(typeBuilder, runtime);
        EmitArrayPushTyped(typeBuilder, runtime.ArrayOperations, ArrayElements.Double);
        EmitArrayPushTyped(typeBuilder, runtime.ArrayOperations, ArrayElements.Bool);
        EmitArrayPushProto(typeBuilder, runtime);
        EmitArrayPushOneDiscarded(typeBuilder, runtime);
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
        EmitArrayReduceDouble(typeBuilder, runtime.ArrayOperations);
        EmitArrayReduceRight(typeBuilder, runtime);
        // Search helpers use ToIntegerOrInfinity for spec-compliant fromIndex clamping.
        EmitToIntegerOrInfinityHelper(typeBuilder, runtime.NumericCoercion,
            new IntegerOrInfinityInputs(runtime.Sentinels.UndefinedType, runtime.ObjectStorage.Type, runtime.ObjectFields.Interface,
                runtime.Symbols.ToPrimitive, runtime.Symbols.GetStorage, runtime.ObjectRead.Property, runtime.Invocation.Method,
                runtime.Operators.TypeOf, runtime.BoxedPrimitives.IsOfType, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitArrayIncludes(typeBuilder, runtime);
        EmitArrayIncludesProto(typeBuilder, runtime);
        EmitArrayIncludesDouble(typeBuilder, runtime);
        EmitArrayIndexOf(typeBuilder, runtime);
        EmitArrayLastIndexOf(typeBuilder, runtime);
        // ECMA-262 Array.prototype.* accepts any array-like (length + indexed
        // props) as receiver — materializer unpacks objects/strings/TSArrays.
        // Not currently called from any emit path (see ILEmitter.Calls.cs
        // Stage 3 deferral note). Kept in the runtime class so a future
        // full-prototype-surface implementation can wire it up.
        EmitArrayLikeMaterialize(typeBuilder, runtime);
        EmitArrayLikeMaterializeForIteration(typeBuilder, runtime);
        EmitArrayLikeMaterializeForCopy(typeBuilder, runtime);
        EmitHasArrayLikeProperty(typeBuilder, runtime);
        EmitLoadArrayLikeElement(typeBuilder, runtime);
        // RequireObjectCoercible(this) — emitted after TSError so it can
        // construct $TypeError directly. Called from $TSFunction.CoercePrimitiveArgs
        // via late-bound reflection.
        EmitRequireObjectCoercibleThis(typeBuilder, runtime.ReceiverGuard,
            runtime.Sentinels.UndefinedType, runtime.Symbols.Type,
            runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        runtime.ReceiverGuard.CompleteEmission();
        EmitArrayJoin(typeBuilder, runtime);
        EmitArrayConcat(typeBuilder, runtime);
        EmitArrayReverse(typeBuilder, runtime);
        EmitArrayReverseProto(typeBuilder, runtime);
        EmitArrayFlatHelper(typeBuilder, runtime); // Must be before EmitArrayFlat
        EmitArrayFlat(typeBuilder, runtime.ArrayOperations);
        EmitArrayFlatMap(typeBuilder, runtime);
        EmitArrayFrom(typeBuilder, runtime);
        EmitArrayOf(typeBuilder, runtime);
        EmitArrayFromAdapter(typeBuilder, runtime);
        // EmitArrayConstructor is emitted earlier (before InvokeValue) so its
        // MethodBuilder is available to InvokeValue's Type-callee dispatch.
        EmitArraySort(typeBuilder, runtime);
        EmitArraySortDirect(typeBuilder, runtime);
        EmitArraySliceNumber(typeBuilder, runtime);
        EmitArraySortNumeric(typeBuilder, runtime);
        EmitArraySortProto(typeBuilder, runtime);
        EmitArrayToSorted(typeBuilder, runtime);
        EmitArrayToSortedGeneric(typeBuilder, runtime);
        // ToIntegerOrInfinity now emitted earlier (before EmitArrayIndexOf).
        EmitArraySplice(typeBuilder, runtime);
        EmitArraySpliceProto(typeBuilder, runtime);
        EmitArrayToSpliced(typeBuilder, runtime);
        EmitArrayToSplicedProto(typeBuilder, runtime);
        EmitArrayToReversed(typeBuilder, runtime);
        EmitArrayWith(typeBuilder, runtime);
        EmitArrayAt(typeBuilder, runtime);
        EmitArrayFill(typeBuilder, runtime);
        EmitArrayFillProto(typeBuilder, runtime);
        EmitArrayCopyWithin(typeBuilder, runtime);
        EmitArrayCopyWithinProto(typeBuilder, runtime);
        EmitArrayEntries(typeBuilder, runtime.ArrayOperations);
        EmitArrayKeys(typeBuilder, runtime.ArrayOperations);
        EmitArrayValues(typeBuilder, runtime.ArrayOperations);
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
        EmitObjectPrototypePopulate(
            runtime.ObjectPrototypes,
            new ObjectPrototypePopulateInputs(
                runtime.ObjectOwnProperties.DefineGetter,
                runtime.ObjectOwnProperties.DefineSetter,
                runtime.DescriptorStorage,
                runtime.ObjectOwnProperties.HasOwnProperty,
                runtime.ObjectOwnProperties.LookupGetter,
                runtime.ObjectOwnProperties.LookupSetter,
                runtime.ObjectOwnProperties.IsEnumerable,
                new PrototypeDescriptorInputs(
                    runtime.DescriptorStorage.DescriptorConstructor,
                    runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!,
                    runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
                    runtime.DescriptorStorage.DefineProperty
                ),
                runtime.FunctionConstruction.GetOrCreate
            )
        );
        // Boxed primitive helpers — must come AFTER prototype populates so
        // BooleanPrototypePopulateMethod / Number / String / Object are non-null.
        EmitNewBoxedPrimitive(typeBuilder, runtime.BoxedPrimitives,
            runtime.Strings, new BoxedPrimitiveInputs(
                runtime.ObjectStorage.Type,
                runtime.ObjectStorage.Constructor,
                runtime.DescriptorStorage.DescriptorType,
                runtime.DescriptorStorage.DescriptorConstructor,
                runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!,
                runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!,
                runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
                runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!,
                runtime.DescriptorStorage.DefineProperty,
                runtime.DescriptorStorage.SetPrototype,
                runtime.Booleans.PrototypeField,
                runtime.Booleans.PrototypePopulateMethod,
                runtime.Numbers.PrototypeField,
                runtime.Numbers.PrototypePopulateMethod,
                runtime.Symbols.Prototype,
                runtime.Symbols.PopulatePrototype),
            _features.UsesBigInt ? new BoxedBigIntPrototype(runtime.BigInt.PrototypeField, runtime.BigInt.PrototypePopulateMethod) : null);
        EmitNormalizeForeignEvalValue(typeBuilder, runtime.BoxedPrimitives,
            runtime.ObjectStorage.Type, runtime.Sentinels.UndefinedInstance, runtime.ObjectRead.Property);
        EmitToObject(typeBuilder, runtime.BoxedPrimitives,
            runtime.ObjectStorage.Constructor, runtime.Sentinels.UndefinedType, runtime.Symbols.Type);
        EmitIsBoxedPrimitiveOfType(typeBuilder, runtime.BoxedPrimitives,
            runtime.ObjectStorage.Type, runtime.ObjectStorage.GetProperty);
        EmitUnwrapStringReceiver(typeBuilder, runtime.BoxedPrimitives,
            runtime.ObjectStorage.Type, runtime.ObjectRead.Property, runtime.StringCoercion);
        // EmitUnwrapIfBoxed moved earlier — see comment above EmitStringify.
        // String methods
        EmitStringCharAt(typeBuilder, runtime.Strings, runtime.NumericCoercion.ToNumber);
        EmitStringSubstring(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, runtime.NumericCoercion.ToIntegerOrInfinity,
            runtime.StringCoercion.ToJsString, runtime.Sentinels.UndefinedInstance, runtime.Sentinels.UndefinedType);
        EmitStringSubstr(typeBuilder, runtime.Strings, runtime.NumericCoercion.ToIntegerOrInfinity);
        EmitStringIndexOf(typeBuilder, runtime.Strings, runtime.StringCoercion.ToJsString);
        EmitStringIndexOfFrom(typeBuilder, runtime.Strings, runtime.NumericCoercion.ToIntegerOrInfinity, runtime.StringCoercion.ToJsString);
        EmitPrimitiveStringIntrinsics(typeBuilder, runtime.Strings);
        EmitStringReplace(typeBuilder, runtime.Strings);
        EmitStringIncludes(typeBuilder, runtime.Strings, new StringSearchInputs(runtime.RegExps.Implementation?.Type, runtime.Sentinels.UndefinedType, runtime.Symbols.Match,
                runtime.ObjectRead.Index, runtime.Booleans.IsTruthy, runtime.NumericCoercion.ToIntegerOrInfinity, runtime.StringCoercion.ToJsString, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitStringStartsWith(typeBuilder, runtime.Strings, new StringSearchInputs(runtime.RegExps.Implementation?.Type, runtime.Sentinels.UndefinedType, runtime.Symbols.Match,
                runtime.ObjectRead.Index, runtime.Booleans.IsTruthy, runtime.NumericCoercion.ToIntegerOrInfinity, runtime.StringCoercion.ToJsString, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitStringEndsWith(typeBuilder, runtime.Strings, new StringSearchInputs(runtime.RegExps.Implementation?.Type, runtime.Sentinels.UndefinedType, runtime.Symbols.Match,
                runtime.ObjectRead.Index, runtime.Booleans.IsTruthy, runtime.NumericCoercion.ToIntegerOrInfinity, runtime.StringCoercion.ToJsString, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitStringSlice(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, runtime.NumericCoercion.ToIntegerOrInfinity,
            runtime.StringCoercion.ToJsString, runtime.Sentinels.UndefinedInstance, runtime.Sentinels.UndefinedType);
        EmitStringRepeat(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor, runtime.NumericCoercion.ToNumber);
        EmitStringPadStart(typeBuilder, runtime.Strings, runtime.StringCoercion.ToJsString, runtime.NumericCoercion.ToNumber, runtime.Sentinels.UndefinedType);
        EmitStringPadEnd(typeBuilder, runtime.Strings, runtime.StringCoercion.ToJsString, runtime.NumericCoercion.ToNumber, runtime.Sentinels.UndefinedType);
        EmitStringCharCodeAt(typeBuilder, runtime.Strings);
        EmitStringConcat(typeBuilder, runtime.Strings, runtime.StringCoercion.ToJsString);
        EmitStringLastIndexOf(typeBuilder, runtime.Strings);
        EmitStringReplaceAll(typeBuilder, runtime.Strings);
        EmitStringAt(typeBuilder, runtime.Strings, runtime.Sentinels.UndefinedInstance);
        EmitStringFromCharCode(typeBuilder, runtime.Strings, runtime.NumericCoercion.ToNumber);
        EmitStringCodePointAt(typeBuilder, runtime.Strings, runtime.NumericCoercion.ToIntegerOrInfinity, runtime.Sentinels.UndefinedInstance);
        EmitStringWellFormedMethods(typeBuilder, runtime.Strings);
        EmitStringIterator(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.IteratorHelpers.NormalizeToEnumerator, runtime.Errors.TypeErrorConstructor,
            runtime.StringCoercion.ToJsString, runtime.Sentinels.UndefinedType);
        EmitStringFromCodePoint(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor, runtime.StringCoercion.ToJsString,
            runtime.NumericCoercion.ToNumber);
        EmitStringNormalize(typeBuilder, runtime.Strings, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor, runtime.StringCoercion.ToJsString,
            runtime.Sentinels.UndefinedType);
        EmitStringLocaleCompare(typeBuilder, runtime.Strings);
        EmitStringTryInvokeSymbolMethod(typeBuilder, runtime.Strings,
            new StringSymbolDispatchInputs(runtime.Symbols.Type, runtime.Sentinels.UndefinedType,
                runtime.Operators.TypeOf, runtime.RegExps.Implementation?.Type,
                runtime.Symbols.GetStorage, runtime.ObjectRead.Index, runtime.Invocation.Method));
        // RegExp methods emitted before String.prototype populate so the
        // spec-correct match/matchAll/search/split slots can reference the
        // regex-aware helpers (Stage 1 of issue #91 follow-up). Moved up
        // from after EmitDateMethods; no downstream consumer needs the old
        // ordering. Without this, the populate wires those slots to a
        // null-returning stub that drops args and breaks `new String(...)
        // .search(...)` (45 Test262 regressions, root-caused 2026-05-01).
        if (runtime.RegExps.Implementation is not null)
            EmitRegExpMethods(
                typeBuilder,
                runtime.RegExps,
                new RegExpMethodsInputs(
                    runtime.ArrayStorage,
                    runtime.Booleans,
                    runtime.Errors.CreateException,
                    runtime.DescriptorStorage,
                    runtime.ObjectRead.Index,
                    runtime.ObjectRead.Property,
                    runtime.Invocation.Method,
                    runtime.IteratorHelpers.NormalizeToEnumerator,
                    runtime.NumericCoercion,
                    runtime.FunctionAttributes.PadUndefinedCtor,
                    runtime.ObjectWrite.Property,
                    runtime.StringCoercion,
                    runtime.Strings.TryInvokeSymbolMethod,
                    runtime.Symbols,
                    runtime.FunctionValues.GetMethodInfo,
                    runtime.FunctionValues.InvokeWithThis,
                    runtime.FunctionValues.Type,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Operators.TypeOf,
                    runtime.Sentinels.UndefinedInstance,
                    runtime.Sentinels.UndefinedType
                )
            );
        // String.prototype dict populate — must come AFTER all the String* helpers,
        // the stubs (emitted earlier), AND the RegExp methods above.
        EmitStringPrototypePopulate(runtime.Strings,
            new StringPrototypeInputs(runtime.DescriptorStorage.DescriptorType,
                new PrototypeDescriptorInputs(runtime.DescriptorStorage.DescriptorConstructor, runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!,
                    runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!, runtime.DescriptorStorage.DefineProperty),
                runtime.FunctionConstruction.GetOrCreate, runtime.FunctionConstruction.CachedConstructor, runtime.Symbols.GetStorage,
                runtime.Symbols.Iterator, runtime.ObjectPrototypes.Prototype, runtime.DescriptorStorage.SetPrototype),
            runtime.RegExps.Implementation is not null ? new StringPrototypeRegExpInputs(runtime.RegExps.RequireImplementation().StringMatch, runtime.RegExps.RequireImplementation().StringMatchAll,
                runtime.RegExps.RequireImplementation().StringSearch, runtime.RegExps.RequireImplementation().StringReplaceAll, runtime.RegExps.RequireImplementation().StringSplitProto) : null);
        // Boolean.prototype population wires dedicated toString and valueOf helpers.
        EmitBooleanPrototypePopulate(typeBuilder, runtime.Booleans,
            new BooleanPrototypeInputs(
                new PrototypeDescriptorInputs(runtime.DescriptorStorage.DescriptorConstructor,
                    runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
                    runtime.DescriptorStorage.DefineProperty),
                runtime.DescriptorStorage.DescriptorType, runtime.FunctionConstruction.GetOrCreate,
                runtime.ObjectPrototypes.Prototype, runtime.DescriptorStorage.SetPrototype,
                new BooleanReceiverInputs(runtime.ObjectStorage.Type, runtime.ObjectStorage.FieldsGetter,
                    runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor)));
        // Number.prototype populate is wired after EmitNumberMethods below.
        // Object utilities
        EmitGetSuperMethod(typeBuilder, runtime.ReflectedMethods, runtime.FunctionConstruction.Constructor);
        // EmitCreateException and EmitWrapException moved earlier (before Promise methods)
        EmitThrowUndefinedVariable(typeBuilder, runtime.Errors);
        // EmitRandom moved to before gOPD (see line ~660). The original site
        // here is now empty.
        EmitMathSumPrecise(typeBuilder, runtime.Math, new MathSumInputs(
            runtime.Symbols.GetStorage, runtime.Symbols.Iterator, runtime.IteratorProtocol.Function, runtime.Sentinels.UndefinedType, runtime.Invocation.Method, runtime.IteratorRecords.NextMethod, runtime.IteratorRecords.InvokeNext, runtime.IteratorProtocol.Done, runtime.IteratorProtocol.Value, runtime.ObjectRead.Property, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        EmitDefineSymbolAccessor(
            typeBuilder,
            runtime.ObjectConstruction,
            new SymbolAccessorInputs(
                runtime.DescriptorStorage,
                runtime.ObjectStorage,
                runtime.StringCoercion,
                runtime.Symbols
            )
        );
        EmitTSObjectMergeEnumerable(
            typeBuilder,
            runtime.ObjectConstruction,
            new TSObjectMergeEnumerableInputs(
                runtime.DescriptorStorage,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.FieldsGetter,
                runtime.ObjectFields.Interface,
                runtime.Invocation.Method,
                runtime.ObjectKeys,
                runtime.ObjectStorage
            )
        );
        // Math.* adapters moved earlier (before EmitObjectGetOwnPropertyDescriptor)
        // so gOPD's Math singleton synth can produce identity-stable
        // `desc.value === Math.X` descriptors.
        EmitGetEnumMemberName(typeBuilder, runtime.Enums);
        runtime.Enums.CompleteEmission();
        EmitConcatTemplate(typeBuilder, runtime.Templates, runtime.StringCoercion.StringifyCoerce);
        EmitInvokeTaggedTemplate(typeBuilder, runtime.Templates,
            runtime.ObjectState.Freeze, runtime.Invocation.Value, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        EmitInvokeTaggedTemplateWithThis(typeBuilder, runtime.Templates,
            runtime.ObjectState.Freeze, runtime.Invocation.Method, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor);
        EmitObjectRest(
            typeBuilder,
            runtime.ObjectConstruction,
            new ObjectRestInputs(runtime.ObjectFields.FieldsGetter, runtime.ObjectFields.Interface)
        );
        // #685: array binding-pattern source normalizer uses the declared iterator
        // collection helper and array storage constructor.
        EmitArrayDestructureSource(typeBuilder, runtime.ArrayOperations,
            runtime.Symbols.Type, runtime.IteratorCollection.ToList, runtime.ArrayStorage.Ctor);
        // JSON methods — gated on UsesJSON (also implied by UsesHttp).
        if (runtime.Json.Implementation is not null)
        {
            EmitJsonParse(
                typeBuilder,
                runtime.Json.RequireImplementation(),
                new JsonParseInputs(
                    runtime.Errors.CreateException,
                    runtime.Records.RequireScalars().ArrayConstructor,
                    runtime.Records.ScalarInlineCtors,
                    runtime.Records.TypedScalarCtors,
                    runtime.Records.TypedScalarShapeFields,
                    runtime.Errors.SyntaxErrorConstructor,
                    runtime.Errors.ThrownValueType,
                    _features.JsonScalarRecordShapes
                )
            );
            EmitJsonParseWithReviver(
                typeBuilder,
                runtime.Json.RequireImplementation(),
                new JsonParseWithReviverInputs(
                    runtime.ObjectDeletion.Property,
                    runtime.DescriptorStorage,
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.ObjectKeys.Normalize,
                    runtime.NumericCoercion,
                    runtime.ObjectDescriptors.DefineProperty,
                    runtime.FunctionValues.InvokeWithThis,
                    runtime.FunctionValues.Type,
                    runtime.Sentinels.UndefinedType
                )
            );
            EmitJsonStringify(
                typeBuilder,
                runtime.Json.RequireImplementation(),
                new JsonStringifyInputs(
                    runtime.ArrayOperations,
                    runtime.ArrayStorage,
                    runtime.FunctionBindings.BoundType,
                    runtime.Errors.CreateException,
                    runtime.DescriptorStorage,
                    runtime.ObjectKeys.Keys,
                    runtime.ObjectRead.Property,
                    runtime.ObjectFields.Interface,
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.Invocation.Method,
                    runtime.Records.RequireScalars().GetValue,
                    runtime.Records.RequireScalars().IsMaterializedGetter,
                    runtime.Records.RequireScalars().ShapeGetter,
                    runtime.Records.RequireScalars().Type,
                    runtime.Records.TypedScalarShapeFields,
                    runtime.Records.TypedScalarTypes,
                    runtime.Records.TypedScalarValueFields,
                    runtime.Numbers,
                    runtime.NumericCoercion,
                    runtime.ObjectPrototypes.Prototype,
                    runtime.ObjectPrototypes.Populate,
                    runtime.ObjectStorage,
                    runtime.StringCoercion,
                    runtime.FunctionValues.Type,
                    runtime.ObjectConstruction.GetEnumerableFields,
                    runtime.RegExps.Implementation?.Type,
                    runtime.Symbols.Type,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Operators.TypeOf,
                    runtime.Sentinels.UndefinedInstance,
                    runtime.Sentinels.UndefinedType,
                    _features.JsonScalarRecordShapes,
                    _features.PotentiallyMaterializesUnknownCompactObjectRecordShape,
                    _features.UsesArrayPrototypeMutation,
                    _features.UsesClassPrototypeMutation,
                    _features.UsesDynamicPropertyDescriptors,
                    _features.UsesObjectIntegrityMutation
                )
            );
            EmitJsonStringifyFull(
                typeBuilder,
                runtime.Json.RequireImplementation(),
                new JsonStringifyFullInputs(
                    runtime.ArrayStorage,
                    runtime.FunctionBindings.BoundInvokeWithThis,
                    runtime.FunctionBindings.BoundType,
                    runtime.Errors.CreateException,
                    runtime.DescriptorStorage,
                    runtime.ObjectKeys.Keys,
                    runtime.ObjectRead.Property,
                    runtime.ObjectFields.Interface,
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.Invocation.Method,
                    runtime.Invocation.Value,
                    runtime.Numbers,
                    runtime.NumericCoercion,
                    runtime.ObjectPrototypes.Prototype,
                    runtime.ObjectPrototypes.Populate,
                    runtime.ObjectStorage,
                    runtime.StringCoercion,
                    runtime.FunctionValues.InvokeWithThis,
                    runtime.FunctionValues.Type,
                    runtime.ObjectConstruction.GetEnumerableFields,
                    runtime.RegExps.Implementation?.Type,
                    runtime.Symbols.Type,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Operators.TypeOf,
                    runtime.Sentinels.UndefinedInstance,
                    runtime.Sentinels.UndefinedType
                )
            );
            EmitJsonRawJsonMethods(
                typeBuilder,
                runtime.Json.RequireImplementation(),
                new JsonRawJsonMethodsInputs(
                    runtime.ArrayStorage,
                    runtime.Errors.CreateException,
                    runtime.ObjectStorage,
                    runtime.StringCoercion,
                    runtime.Errors.SyntaxErrorConstructor
                )
            );
        }
        // Math / JSON value-form singleton populate bodies. Emitted here — after
        // EmitMathAdapters (Math.*Adapter) and the JSON methods above — so their
        // backing MethodBuilders are resolved. JSON helpers are null when JSON is
        // unused; EmitBuiltinSingletonPopulate skips null backings. (#276)
        EmitMathSingletonPopulate(runtime.Math, GetBuiltinSingletonInputs(runtime));
        EmitJsonSingletonPopulate(runtime.Json, GetBuiltinSingletonInputs(runtime));
        // BigInt methods — gated on UsesBigInt. Detector flips it on for any
        // `123n` literal, bare `BigInt` identifier, or BigInt64Array/BigUint64Array
        // typed-array reference. EmitBigIntBinary in ILEmitter.Operators.cs only
        // runs when the type-checker has marked an operand as TypeInfo.BigInt,
        // which itself requires a BigInt source — so the call sites are
        // naturally aligned with this gate.
        if (_features.UsesBigInt)
        {
            var bigInt = runtime.BigInt.RequireImplementation();
            EmitCreateBigInt(typeBuilder, bigInt,
                new BigIntConversionInputs(
                    new BigIntPrimitiveInputs(runtime.ObjectRead.Index, runtime.ObjectRead.Property, runtime.Invocation.Method,
                        runtime.Symbols.ToPrimitive, runtime.Symbols.Type, runtime.Operators.TypeOf, runtime.Sentinels.UndefinedType,
                        runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor),
                    runtime.ObjectStorage.Type, runtime.StringCoercion.ToJsString, runtime.Errors.RangeErrorConstructor, runtime.Errors.SyntaxErrorConstructor));
            EmitBigIntStaticMethods(typeBuilder, bigInt, runtime.NumericCoercion.ToNumber, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor);
            EmitBigIntArithmetic(typeBuilder, bigInt);
            EmitBigIntComparison(typeBuilder, bigInt, runtime.Errors.CreateException, runtime.Errors.RangeErrorConstructor);
            EmitBigIntBitwise(typeBuilder, bigInt);
            EmitBigIntPrototypePopulate(typeBuilder, runtime.BigInt, bigInt.ToStringRadix,
                new BigIntPrototypeInputs(
                    new PrototypeDescriptorInputs(runtime.DescriptorStorage.DescriptorConstructor,
                        runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
                        runtime.DescriptorStorage.DefineProperty),
                    runtime.DescriptorStorage.DescriptorType,
                    runtime.DescriptorStorage.DescriptorWritable.GetSetMethod()!, runtime.DescriptorStorage.DescriptorConfigurable.GetSetMethod()!,
                    runtime.FunctionConstruction.GetOrCreate, runtime.Symbols.GetStorage, runtime.Symbols.ToStringTag,
                    runtime.ObjectPrototypes.Prototype, runtime.DescriptorStorage.SetPrototype,
                    runtime.ObjectStorage.Type, runtime.ObjectStorage.FieldsGetter, runtime.NumericCoercion.ToNumber, runtime.Sentinels.UndefinedType,
                    runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor));
        }
        // Promise methods moved earlier (before GetProperty, which needs PromiseThen for typeof p.then)
        // Number methods
        EmitNumberMethods(typeBuilder, runtime.Numbers, runtime.StringCoercion,
            new NumberMethodInputs(
                runtime.ObjectRead.Property,
                runtime.Sentinels.UndefinedType,
                runtime.NumericCoercion.ToIntegerOrInfinity,
                runtime.ObjectStorage.Type,
                runtime.Symbols.Type,
                runtime.Errors.CreateException,
                runtime.Errors.TypeErrorConstructor,
                runtime.Errors.RangeErrorConstructor));
        // Number.prototype populate body — must come AFTER EmitNumberMethods so
        // NumberToFixed/etc. MethodBuilders are non-null.
        EmitNumberPrototypePopulate(typeBuilder, runtime.Numbers,
            new NumberPrototypeInputs(
                new PrototypeDescriptorInputs(runtime.DescriptorStorage.DescriptorConstructor,
                    runtime.DescriptorStorage.DescriptorValue.GetSetMethod()!, runtime.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!,
                    runtime.DescriptorStorage.DefineProperty),
                runtime.DescriptorStorage.DescriptorType,
                runtime.FunctionConstruction.GetOrCreate,
                runtime.ObjectPrototypes.Prototype,
                runtime.DescriptorStorage.SetPrototype,
                runtime.ObjectStorage.Type,
                runtime.ObjectRead.Property,
                runtime.Errors.CreateException,
                runtime.Errors.TypeErrorConstructor));
        // Fill in the symbol-keyed accessor registry helper bodies (#266).
        EmitSymbolAccessorRegistryBodies(runtime.SymbolAccessors, runtime.Symbols, runtime.StringCoercion);
        // Microtask method (queueMicrotask) - must come before timer infrastructure so ProcessMicrotasks is available
        EmitQueueMicrotaskMethod(typeBuilder, runtime);
        // Virtual timer infrastructure (must come before DateMethods which calls ProcessPendingTimers)
        EmitTimerQueueInfrastructure(typeBuilder, runtime);
        // Date methods
        if (runtime.Dates.Implementation is not null)
            EmitDateMethods(
                typeBuilder,
                runtime.Dates.RequireImplementation(),
                new DateMethodsInputs(runtime.FunctionAttributes.NonConstructibleCtor, runtime.Timers)
            );
        // Date.prototype populate — must come AFTER EmitDateMethods, which is what
        // assigns the runtime.Date* helper builders the wiring below references.
        EmitDatePrototypePopulate(
            typeBuilder,
            runtime.Dates,
            new DatePrototypePopulateInputs(
                runtime.DescriptorStorage,
                runtime.ObjectPrototypes.Prototype,
                runtime.FunctionConstruction.GetOrCreate
            )
        );
        // Fill the default-hint ToPrimitive body after every dependency is
        // bound, including DateToString for Date's special default hint.
        EmitUnwrapIfBoxedBody(runtime.BoxedPrimitives,
            new UnwrapPrimitiveInputs(
                runtime.ObjectStorage.Type,
                runtime.Symbols.ToPrimitive,
                runtime.ObjectRead.Index,
                runtime.Sentinels.UndefinedType,
                runtime.Operators.TypeOf,
                runtime.Invocation.Method,
                runtime.ObjectStorage.GetProperty,
                runtime.ObjectOwnProperties.HasOwnProperty,
                runtime.ObjectRead.Property,
                runtime.Errors.CreateException,
                runtime.Errors.TypeErrorConstructor),
            runtime.Dates.Implementation is not null ? new BoxedDateInputs(runtime.Dates.RequireImplementation().Type, runtime.Dates.RequireImplementation().ToStringMethod) : null);
        // Fill in LookupBuiltInStaticMember's body now that IsArray, NumberIs*,
        // StringFrom*, TSFunctionCtor (#63) and DateNow (value-form `Date.now`,
        // gated on UsesDate) are all in place. Only the body is late — the
        // MethodBuilder was defined early, so earlier emitters can call it.
        EmitLookupBuiltInStaticMemberBody(runtime.BuiltInStatics,
            new BuiltInStaticDispatchInputs(
                runtime.FunctionConstruction.GetOrCreate, runtime.ArrayOperations.IsArray, runtime.Numbers,
                runtime.Strings.FromCharCode, runtime.Strings.FromCodePoint, runtime.Templates.Raw,
                runtime.ObjectKeys, runtime.ObjectOperations, runtime.ObjectState, runtime.ObjectPrototypes,
                runtime.ObjectDescriptors, runtime.ObjectOwnProperties.HasOwn,
                runtime.Symbols.Type, runtime.Symbols.For, runtime.Symbols.KeyFor,
                runtime.BigInt.Implementation, _features.UsesPromise ? runtime.RequirePromise() : null,
                runtime.Errors.Type, runtime.Errors.IsError, runtime.Dates.Implementation));
        runtime.BuiltInStatics.CompleteEmission();
        // RegExp methods moved earlier — emitted before EmitStringPrototypePopulate.
        // Error methods
        EmitErrorMethods(
            typeBuilder,
            runtime.Errors,
            new ErrorMethodsInputs(
                runtime.DescriptorStorage,
                runtime.ObjectRead.Property,
                runtime.ObjectFields.GetProperty,
                runtime.ObjectFields.HasProperty,
                runtime.ObjectFields.Interface,
                new ProxyHasInputs(
                    runtime.ReflectedMethods.InvokeUnwrapped,
                    runtime.Operators.ProxyOrdinaryHas,
                    runtime.ObjectDescriptors.GetOwnPropertyDescriptor,
                    runtime.ObjectState.IsExtensible,
                    runtime.ObjectRead.Property,
                    runtime.Booleans.IsTruthy
                ),
                runtime.StringCoercion,
                runtime.Sentinels.UndefinedInstance,
                runtime.Sentinels.UndefinedType
            )
        );
        // Error.prototype populate body — must come AFTER EmitErrorMethods so
        // the spec-compliant ErrorToStringSpec helper can reference $Error
        // metadata (TSErrorType, ErrorGetName/ErrorGetMessage already populated).
        EmitErrorPrototypePopulate(
            typeBuilder,
            runtime.Errors,
            new ErrorPrototypePopulateInputs(
                runtime.DescriptorStorage,
                runtime.ObjectRead.Property,
                runtime.ObjectPrototypes.Prototype,
                runtime.StringCoercion,
                runtime.Symbols,
                runtime.FunctionConstruction.CachedConstructor,
                runtime.Sentinels.UndefinedType
            )
        );
        // Native-error subclass prototypes — distinct per ECMA-262 §20.5.6.4.
        // Must run after EmitErrorPrototypePopulate so PDSSetPrototype can chain
        // each subclass-proto's [[Prototype]] to %Error.prototype%.
        EmitNativeErrorPrototypePopulates(runtime.Errors, runtime.DescriptorStorage);
        // Function.prototype populate body — must come after $TSFunction +
        // $BoundTSFunction emission and after InvokeMethodValue is wired
        // (the call/apply helpers route through it). Emitted in the same tail
        // section as ErrorPrototype.
        EmitFunctionPrototypePopulate(
            typeBuilder,
            runtime.FunctionPrototypes,
            new FunctionPrototypePopulateInputs(
                runtime.DescriptorStorage,
                runtime.FunctionValues,
                runtime.ObjectPrototypes,
                runtime.FunctionConstruction,
                runtime.Sentinels.UndefinedInstance,
                runtime.Invocation.Method,
                runtime.Sentinels.UndefinedType,
                runtime.Operators,
                runtime.FunctionBindings,
                runtime.Errors
            )
        );
        runtime.FunctionPrototypes.CompleteEmission();
        // RegExp.prototype populate body — must come after $RegExp's
        // TSRegExpSym* helpers are emitted (they're referenced from the
        // populate IL). Emitted gated on UsesRegExp; otherwise the helpers
        // were never created.
        if (runtime.RegExps.Implementation is not null)
            EmitRegExpPrototypePopulate(
                typeBuilder,
                runtime.RegExps,
                new RegExpPrototypePopulateInputs(
                    runtime.DescriptorStorage,
                    runtime.ObjectPrototypes.Prototype,
                    runtime.Symbols,
                    runtime.FunctionConstruction.CachedConstructor,
                    runtime.Sentinels.UndefinedInstance
                )
            );
        // Promise.prototype helpers + populate. Helpers wrap runtime.RequirePromise().Then
        // /PromiseCatch/PromiseFinally with an `__this`-aware signature so
        // `Promise.prototype.then.call(p, fn)` routes correctly. Must come
        // after EmitPromiseMethods so the helper bodies can reference the
        // state-machine entry points.
        if (_features.UsesPromise)
        {
            EmitPromisePrototypeHelpers(typeBuilder, runtime);
            EmitPromisePrototypePopulate(typeBuilder, runtime);
        }
        // Map methods — gated on UsesMap. EmitMapGroupBy (`Map.groupBy(...)`)
        // depends on Map's own MapHas/Set/Get methods so it folds up under
        // the same gate. ObjectGroupBy stays unconditional (it builds a plain
        // Dictionary<string,object>, not a Map).
        if (runtime.Map is not null)
        {
            EmitMapMethods(
                typeBuilder,
                runtime.CollectionKeys,
                runtime.RequireMap(),
                new MapMethodsInputs(runtime.ArrayStorage, runtime.Invocation.Method, runtime.Sentinels.UndefinedInstance)
            );
            EmitMapGroupBy(
                typeBuilder,
                runtime.RequireMap(),
                new MapGroupByInputs(
                    runtime.ArrayStorage,
                    runtime.Errors.CreateException,
                    runtime.IteratorProtocol.Function,
                    runtime.Invocation.Value,
                    runtime.IteratorCollection.ToList,
                    runtime.RuntimeClass.Type,
                    runtime.Symbols.Iterator,
                    runtime.Errors.TypeErrorConstructor,
                    runtime.Operators.TypeOf,
                    runtime.Sentinels.UndefinedType
                )
            );
        }
        // Set methods — gated on UsesSet.
        if (runtime.Set is not null)
            EmitSetMethods(
                typeBuilder,
                runtime.CollectionKeys,
                runtime.RequireSet(),
                new SetMethodsInputs(
                    runtime.ArrayOperations,
                    runtime.ArrayStorage,
                    runtime.Errors.CreateException,
                    runtime.Invocation.Method,
                    runtime.Errors.TypeErrorConstructor
                )
            );
        // WeakMap/WeakSet/WeakRef/FinalizationRegistry helpers are emitted
        // before GetProperty, which binds their BCL-backed receiver methods.
        // Proxy methods — gated on UsesProxy (`new Proxy()` / bare `Proxy`).
        // Proxy trap dispatch late-binds to SharpTSProxy on its normal path, so the
        // compiled output needs SharpTS.dll present at runtime when Proxy is used.
        if (_features.UsesProxy)
        {
            runtime.RequireSharpTSRuntime("Proxy");
            runtime.BeginProxyConstructionEmission();
            var proxies = runtime.RequireProxyConstruction();
            EmitProxyMethods(typeBuilder, proxies,
                new ProxyConstructionInputs(runtime.Sentinels.UndefinedType, runtime.Symbols.Type,
                    runtime.Sentinels.UndefinedInstance, runtime.Errors.CreateException,
                    runtime.Errors.TypeErrorConstructor));
            proxies.CompleteEmission();
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
        {
            runtime.RequireAsyncGenerators().BeginContinuationsEmission();
            EmitAsyncGeneratorAwaitContinueMethods(
                typeBuilder,
                moduleBuilder,
                runtime.RequireAsyncGenerators().RequireContinuations()
            );
            runtime.RequireAsyncGenerators().RequireContinuations().CompleteEmission();
        }
        // NodeError conversion helpers (must be before fs methods which use them)
        EmitNodeErrorHelpers(typeBuilder, runtime.NodeErrors);
        // Built-in module methods (fs, os, dns) — path migrated to stdlib/node/path.ts.
        if (_features.UsesFs)
            EmitFsModuleMethods(typeBuilder, runtime);
        // os module — gated on UsesOs (set by `import 'os'` or `os.X` access).
        if (_features.UsesOs)
            EmitOsModuleMethods(typeBuilder, runtime.RequireOs());
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
            EmitTtyPrimitiveMethods(typeBuilder, runtime.RequireTty(), runtime.NumericCoercion.ToNumber);
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
        DefineUtilInspectSignatures(typeBuilder, runtime.Inspection);
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
            EmitGetWebCryptoObjectStub(runtime.WebCrypto);
        }
        // Util inspect helper bodies (console.dir depends on them; the rest of
        // the emitted util surface died when util moved to stdlib/node/util.ts).
        EmitUtilStandaloneMethods(runtime.Inspection);
        // Readline module methods — gated on UsesReadline (flag was already
        // detected via `import 'readline'` but the call site used to ignore it).
        if (_features.UsesReadline)
            EmitReadlineMethods(typeBuilder, runtime.RequireReadline());
        // Child process module methods — gated on UsesChildProcess.
        if (_features.UsesChildProcess)
            EmitChildProcessMethods(typeBuilder, runtime);
        // Reflect metadata API — gated on UsesReflectMetadata (orphan-flag fix).
        if (runtime.Reflect.Metadata is not null)
            EmitReflectMetadataMethods(typeBuilder, runtime.Reflect.RequireMetadata(), runtime.ArrayStorage.Ctor);
        // fs.watch / fs.watchFile / fs.unwatchFile — gated on UsesFs.
        if (_features.UsesFs)
            EmitFsWatchFactories(typeBuilder, runtime);
        // Timer methods (setTimeout, clearTimeout, setInterval, clearInterval)
        EmitSetTimeoutMethod(typeBuilder, runtime);
        EmitClearTimeoutMethod(typeBuilder, runtime.Timers);
        EmitSetIntervalMethod(typeBuilder, runtime);
        EmitClearIntervalMethod(typeBuilder, runtime.Timers);
        // Timer promise methods (timers/promises module)
        if (_features.UsesPromise)
            EmitTimerPromisesMethods(typeBuilder, runtime);
        // Timer module wrappers for namespace imports (import * as timers from 'timers')
        EmitTimerModuleWrappers(typeBuilder, runtime);
        // Timer promises module wrappers for named/namespace imports (import { setTimeout } from 'timers/promises')
        if (_features.UsesPromise)
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
            EmitPerfPrimitiveMethods(typeBuilder, runtime.RequirePerformance());
        // string_decoder module migrated to stdlib/node/string_decoder.ts.

        // Intl support (Intl.NumberFormat / DateTimeFormat / Collator) — gated.
        // Every Intl operation late-binds to RuntimeTypes — needs SharpTS at runtime.
        if (_features.UsesIntl)
        {
            runtime.RequireSharpTSRuntime("Intl");
            EmitIntlMethods(typeBuilder, runtime.RequireIntl());
        }

        // (TLS handshake is now emitted as pure-BCL IL inside $TlsSocket/$TlsServer —
        //  no late-bind helpers, so a --compile'd tls program is genuinely standalone.)

        // Worker Threads support (SharedArrayBuffer, TypedArrays, Atomics, MessagePort, Worker)
        EmitWorkerHelpers(typeBuilder, runtime);

        // Cluster module support
        if (_features.UsesCluster)
            EmitClusterHelpers(typeBuilder, runtime.RequireCluster(), runtime.EventLoop, EntryModulePath);

        // Vm module support — gated on UsesVm (set by `import 'vm'`).
        // vm delegates to VmModuleInterpreter via late binding — needs SharpTS at runtime.
        if (_features.UsesVm)
        {
            runtime.RequireSharpTSRuntime("vm module");
            // Keep the shared module index at the orchestration boundary. Helpers register
            // each declaration before emitting its body, preserving forward lookup timing.
            EmitVmMethods(typeBuilder, runtime.RequireVm(), runtime.RequirePromise(),
                (name, method) => runtime.RegisterBuiltInModuleMethod("vm", name, method));
        }

        if (_features.UsesSourceExecution)
        {
            runtime.RequireSharpTSRuntime(
                "sharpts:execution module",
                SharpTSRuntimeRequirements.FullDependencyClosure |
                SharpTSRuntimeRequirements.ManagedCompilerHost);
            EmitSourceExecutionMethods(typeBuilder, runtime.RequireSourceExecution(),
                (name, method) => runtime.RegisterBuiltInModuleMethod("sharpts:execution", name, method));
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

    /// <summary>
    /// Phase 2: Finalizes the $Runtime class after all deferred method bodies are emitted.
    /// </summary>
    internal static void EmitRuntimeClassFinalize(EmittedRuntimeClass runtimeClass)
    {
        runtimeClass.Type.CreateType();
        runtimeClass.CompleteEmission();
    }
}
