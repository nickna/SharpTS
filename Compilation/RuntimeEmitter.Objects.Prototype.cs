using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Object.create(proto, propertiesObject?) - creates a new object with prototype.
    /// Signature: object ObjectCreate(object proto, object propertiesObject)
    /// Fully standalone - uses emitted $PropertyDescriptorStore for descriptor storage.
    /// </summary>
    private void EmitObjectCreate(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder prototypeStoreField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectCreate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ObjectCreate = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.2 step 1: If Type(O) is neither Object nor Null,
        // throw TypeError. Object.create(undefined/number/string/...) throws.
        // null is explicitly permitted (creates a prototype-less object).
        var protoOkLabel = il.DefineLabel();
        var protoThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, protoOkLabel);  // null permitted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Br, protoOkLabel);

        il.MarkLabel(protoThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object prototype may only be an Object or null");

        il.MarkLabel(protoOkLabel);

        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var noPropsLabel = il.DefineLabel();

        // result = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Set prototype: $PropertyDescriptorStore.SetPrototype(result, proto)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);  // proto
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        // ECMA-262 §20.1.2.2 step 2: Let obj be OrdinaryObjectCreate(O).
        // OrdinaryObjectCreate creates a FRESH object whose [[Prototype]] is O.
        // It does NOT copy O's own properties — inherited properties are
        // reached via the prototype chain at access time (not by copying).
        // Pre-fix copied proto's own keys into result, which broke
        // hasOwnProperty / Object.keys / for-in on the created obj
        // (inherited keys leaked into "own"). PDS-installed prototype link
        // (above) handles inheritance correctly without copying.

        // ECMA-262 §20.1.2.2 step 3: only define properties if Properties is
        // not undefined. Everything else — including an explicit null, which
        // must TypeError out of ToObject (test262 15.2.3.5-4-3) — is handed to
        // ObjectDefineProperties, which IS step 3 (§20.1.2.3.1
        // ObjectDefineProperties). Delegating rather than re-walking the props
        // bag here matters: step 4 is `props.[[OwnPropertyKeys]]()` followed by
        // `Get(props, key)`, so an *accessor* property on the bag must have its
        // getter invoked. The bespoke loop this replaces read the backing
        // dictionary entries directly, so a bag built with
        // `Object.defineProperty(props, k, {get})` silently contributed nothing.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noPropsLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectDefineProperties);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noPropsLabel);

        // Return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        EmitObjectCreateValueForm(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the VALUE-FORM dispatch wrapper for Object.create. Reflection
    /// dispatch through $TSFunction pads missing args with CLR null, which
    /// ObjectCreate must treat as explicit JS null (TypeError per ECMA-262
    /// §20.1.2.2 step 3 / test262 15.2.3.5-4-3). This wrapper maps a
    /// null props slot — which through this path means ABSENT — to the
    /// $Undefined singleton before delegating, so `var oc = Object.create;
    /// oc(proto)` works. The syntactic call path emits the sentinel for the
    /// missing arg itself and keeps calling ObjectCreate directly, preserving
    /// the explicit-null throw.
    /// </summary>
    private void EmitObjectCreateValueForm(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectCreateValueForm",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ObjectCreateValueForm = method;

        var il = method.GetILGenerator();
        var propsPresentLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, propsPresentLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, runtime.ObjectCreate);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(propsPresentLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectCreate);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.preventExtensions(obj) - prevents adding new properties.
    /// Signature: object ObjectPreventExtensions(object obj)
    /// Uses PropertyDescriptorStore for enforcement and local table for standalone checks.
    /// </summary>
    private void EmitObjectPreventExtensions(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder nonExtensibleObjectsField, FieldBuilder frozenObjectsField, FieldBuilder sealedObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectPreventExtensions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.ObjectPreventExtensions = method;

        var il = method.GetILGenerator();
        var returnLabel = il.DefineLabel();

        // If obj is null, just return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnLabel);

        // If obj is a $Object, set its instance _isNonExtensible flag so the
        // instance-method SetProperty path honors non-extensibility for new
        // properties. The PDS/CWT bookkeeping below is the cross-type record.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectPreventExtensions);
        il.MarkLabel(notTSObjectLabel);

        // number[] unboxing: mark a $Array non-extensible so the unboxed PushDouble fast path refuses to
        // append (preventExtensions otherwise only records the array in external collections PushDouble
        // can't reach).
        var notTSArrayPxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayPxLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayMarkNonExtensible);
        il.MarkLabel(notTSArrayPxLabel);

        // Call $PropertyDescriptorStore.PreventExtensions(obj) - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSPreventExtensions);

        // Also add to local non-extensible objects table for standalone checks
        il.Emit(OpCodes.Ldsfld, nonExtensibleObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1); // true
        il.Emit(OpCodes.Box, _types.Boolean);
        var addOrUpdateMethod = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>)
            .GetMethod("AddOrUpdate");
        if (addOrUpdateMethod != null)
        {
            il.Emit(OpCodes.Callvirt, addOrUpdateMethod);
        }
        else
        {
            var setItem = _types.ConditionalWeakTable.GetMethod("set_Item")
                ?? _types.ConditionalWeakTable.GetProperty("Item")?.GetSetMethod();
            if (setItem != null)
            {
                il.Emit(OpCodes.Callvirt, setItem);
            }
            else
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
            }
        }

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.isExtensible(obj) - returns whether object can have new properties.
    /// Signature: bool ObjectIsExtensible(object obj)
    /// Checks both PropertyDescriptorStore and local tables for compatibility.
    /// Returns false for primitives, frozen, sealed, or explicitly non-extensible objects.
    /// </summary>
    private void EmitObjectIsExtensible(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder nonExtensibleObjectsField, FieldBuilder frozenObjectsField, FieldBuilder sealedObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectIsExtensible",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.ObjectIsExtensible = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var checkStringLabel = il.DefineLabel();
        var checkNumberLabel = il.DefineLabel();
        var checkBooleanLabel = il.DefineLabel();
        var checkPropertyStoreLabel = il.DefineLabel();
        var checkLocalTablesLabel = il.DefineLabel();
        var checkFrozenLabel = il.DefineLabel();
        var checkSealedLabel = il.DefineLabel();

        var valueLocal = il.DeclareLocal(_types.Object);

        // If obj is null, return false (primitives are not extensible)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkStringLabel);
        il.Emit(OpCodes.Br, returnFalseLabel);

        // If obj is string, return false (immutable)
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // If obj is double (boxed number), return false (immutable)
        il.MarkLabel(checkNumberLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // If obj is bool (boxed boolean), return false (immutable)
        il.MarkLabel(checkBooleanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // Check $PropertyDescriptorStore.IsExtensible(obj) - fully standalone, no reflection
        il.MarkLabel(checkPropertyStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSIsExtensible);
        il.Emit(OpCodes.Brfalse, returnFalseLabel); // Not extensible per property store

        // Also check local tables for backward compatibility
        // Check if obj is in the non-extensible objects table
        il.MarkLabel(checkLocalTablesLabel);
        il.Emit(OpCodes.Ldsfld, nonExtensibleObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        var tryGetValue = _types.ConditionalWeakTable.GetMethod("TryGetValue");
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel); // Found = not extensible

        // Check if obj is in the frozen objects table
        il.MarkLabel(checkFrozenLabel);
        il.Emit(OpCodes.Ldsfld, frozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel); // Frozen = not extensible

        // Check if obj is in the sealed objects table
        il.MarkLabel(checkSealedLabel);
        il.Emit(OpCodes.Ldsfld, sealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel); // Sealed = not extensible

        // Not in any table, object is extensible
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.getOwnPropertySymbols(obj) - returns array of symbol-keyed properties.
    /// Signature: object GetOwnPropertySymbols(object obj)
    /// Uses the compiled assembly's GetSymbolDict to retrieve symbol keys.
    /// </summary>
    private void EmitGetOwnPropertySymbols(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetOwnPropertySymbols",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.GetOwnPropertySymbols = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.11 step 1: Let obj be ? ToObject(O). ToObject throws
        // TypeError on null/undefined.
        var gOPSTypeOkLabel = il.DefineLabel();
        var gOPSThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, gOPSThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, gOPSThrowLabel);
        il.Emit(OpCodes.Br, gOPSTypeOkLabel);
        il.MarkLabel(gOPSThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(gOPSTypeOkLabel);

        // Create the result list
        // var result = new List<object?>();
        var resultLocal = il.DeclareLocal(_types.ListOfObjectNullable);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObjectNullable));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Get symbol dictionary: var symbolDict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Stloc, symbolDictLocal);

        // Get keys and iterate: foreach (var key in symbolDict.Keys) result.Add(key);
        // symbolDict.Keys
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        var keysProperty = _types.DictionaryObjectObject.GetProperty("Keys")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, keysProperty);

        // Get enumerator
        var keysCollectionType = keysProperty.ReturnType;
        var getEnumeratorMethod = keysCollectionType.GetMethod("GetEnumerator")!;
        il.Emit(OpCodes.Callvirt, getEnumeratorMethod);
        var enumeratorType = getEnumeratorMethod.ReturnType;
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop: while (enumerator.MoveNext())
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var moveNextMethod = enumeratorType.GetMethod("MoveNext")!;
        il.Emit(OpCodes.Call, moveNextMethod);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // result.Add(enumerator.Current);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        var currentProperty = enumeratorType.GetProperty("Current")!.GetGetMethod()!;
        il.Emit(OpCodes.Call, currentProperty);
        var addMethod = _types.ListOfObjectNullable.GetMethod("Add", [_types.Object])!;
        il.Emit(OpCodes.Callvirt, addMethod);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Pre-defines the ObjectGetPrototypeOf MethodBuilder so emitters that fire
    /// earlier (e.g. IsPrototypeOfHelper) can reference it. Body emitted in
    /// EmitObjectGetPrototypeOf.
    /// </summary>
    private void DefineObjectGetPrototypeOfShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ObjectGetPrototypeOf = typeBuilder.DefineMethod(
            "ObjectGetPrototypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
    }

    /// <summary>
    /// Emits Object.getPrototypeOf(obj) - returns the prototype of an object.
    /// Signature: object ObjectGetPrototypeOf(object obj)
    /// Checks PropertyDescriptorStore first, then local table for compatibility.
    /// </summary>
    private void EmitObjectGetPrototypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder prototypeStoreField)
    {
        var method = runtime.ObjectGetPrototypeOf;
        var il = method.GetILGenerator();
        var checkLocalTableLabel = il.DefineLabel();
        var foundInLocalLabel = il.DefineLabel();

        // ECMA-262 §20.1.2.13 step 1: Let O be ? ToObject(O). ToObject throws
        // TypeError on null/undefined. test262 15.2.3.2-1-{2,3,4} verify each.
        // Previously deferred for fear of cascading regressions on undefined
        // built-in slots — but Number/String prototype paths now return real
        // prototype dicts (not undefined), so this should be safe. Watch the
        // regen diff.
        var notNullForGpoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullForGpoLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.getPrototypeOf called on null or undefined");
        il.MarkLabel(notNullForGpoLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefForGpoLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefForGpoLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.getPrototypeOf called on null or undefined");
        il.MarkLabel(notUndefForGpoLabel);

        var tempLocal = il.DeclareLocal(_types.Object);

        // Distinguish "no PDS entry" from "entry with null value" so explicit
        // Object.create(null) / Object.setPrototypeOf(o, null) survive the
        // default-fallback below. HasPrototypeEntry returns the success bit
        // separately; GetPrototype returns the value.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brfalse, checkLocalTableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Ret);

        // Also check local _prototypeStore table for backward compatibility
        il.MarkLabel(checkLocalTableLabel);
        il.Emit(OpCodes.Ldsfld, prototypeStoreField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, tempLocal);
        var tryGetValue = _types.ConditionalWeakTable.GetMethod("TryGetValue");
        il.Emit(OpCodes.Callvirt, tryGetValue!);
        il.Emit(OpCodes.Brtrue, foundInLocalLabel);

        // Default-prototype fallback per ECMA-262: plain objects/arrays/dicts
        // have %Object.prototype% as their [[Prototype]] unless overridden.
        // Without this, Object.getPrototypeOf({}) returns null instead of
        // Object.prototype, breaking JSON.parse + literal-object tests.
        // $TSObject is the wrapped form (from `new Object()` and object
        // literals with accessors). It must also default to Object.prototype.
        var notDictForProtoLabel = il.DefineLabel();
        var notTSObjForProtoLabel = il.DefineLabel();
        var notListForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictForProtoLabel);
        // Object.prototype itself has [[Prototype]] = null per ECMA-262 §20.1.3.
        // Without this, getPrototypeOf(Object.prototype) circularly returns
        // Object.prototype instead of null, breaking isPrototypeOf chain walks
        // that terminate at Op (each step looks up via getPrototypeOf, which
        // infinite-loops without this base case).
        var dictIsNotOpLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Bne_Un, dictIsNotOpLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(dictIsNotOpLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDictForProtoLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObjForProtoLabel);

        // Native-error subclass instances → their distinct subclass prototype
        // (ECMA-262 §20.5.6.4: `Object.getPrototypeOf(new TypeError())` ===
        // %TypeError.prototype%, which is itself %Error.prototype%'s child).
        // MUST check subclasses BEFORE the base $Error check, since
        // `Isinst $Error` succeeds for any subclass.
        void EmitErrorInstanceBranch(Type subclassType, MethodBuilder populate, FieldBuilder protoField)
        {
            var notMatch = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, subclassType);
            il.Emit(OpCodes.Brfalse, notMatch);
            il.Emit(OpCodes.Call, populate);
            il.Emit(OpCodes.Ldsfld, protoField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notMatch);
        }
        EmitErrorInstanceBranch(runtime.TSTypeErrorType,      runtime.TypeErrorPrototypePopulateMethod,      runtime.TypeErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSRangeErrorType,     runtime.RangeErrorPrototypePopulateMethod,     runtime.RangeErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSReferenceErrorType, runtime.ReferenceErrorPrototypePopulateMethod, runtime.ReferenceErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSSyntaxErrorType,    runtime.SyntaxErrorPrototypePopulateMethod,    runtime.SyntaxErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSURIErrorType,       runtime.URIErrorPrototypePopulateMethod,       runtime.URIErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSEvalErrorType,      runtime.EvalErrorPrototypePopulateMethod,      runtime.EvalErrorPrototypeField);
        EmitErrorInstanceBranch(runtime.TSAggregateErrorType, runtime.AggregateErrorPrototypePopulateMethod, runtime.AggregateErrorPrototypeField);

        // Base $Error instances (plain `new Error(...)`) → Error.prototype.
        var notTSErrForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, notTSErrForProtoLabel);
        il.Emit(OpCodes.Call, runtime.ErrorPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.ErrorPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSErrForProtoLabel);

        // $RegExp instances → RegExp.prototype per ECMA-262 §22.2.3.
        if (_features.UsesRegExp)
        {
            var notTSRegExpForProtoLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notTSRegExpForProtoLabel);
            il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTSRegExpForProtoLabel);
        }

        // Promise instances ($TSPromise + raw Task<object>) → Promise.prototype
        // per ECMA-262 §27.2.5. Without this, Object.getPrototypeOf(promise)
        // returns null and `Promise.prototype.isPrototypeOf(p)` fails.
        if (runtime.TSPromiseType != null)
        {
            var notTSPromiseForProtoLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
            il.Emit(OpCodes.Brfalse, notTSPromiseForProtoLabel);
            il.Emit(OpCodes.Call, runtime.PromisePrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.PromisePrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTSPromiseForProtoLabel);
        }
        var notTaskForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskForProtoLabel);
        il.Emit(OpCodes.Call, runtime.PromisePrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.PromisePrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTaskForProtoLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notListForProtoLabel);

        // Constructor functions ($TSFunction wrappers and System.Type
        // instances representing built-in classes like Object/Array/Number/
        // String/RegExp/etc.) inherit from %Function.prototype% unless
        // explicitly remapped by PDS/_prototypeStore. User-defined classes
        // (`class B extends A {}`) currently have no PDS entry either, so
        // their fallback also resolves to Function.prototype — that's still
        // wrong (should be A) but no worse than the previous null.
        var notTSFnForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFnForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSFnForProtoLabel);
        var notTypeForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brfalse, notTypeForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTypeForProtoLabel);

        // Primitive coercions per ECMA-262 §20.1.2.13 step 1 (ToObject):
        // Object.getPrototypeOf(0) → Number.prototype,
        // Object.getPrototypeOf(true) → Boolean.prototype,
        // Object.getPrototypeOf("") → String.prototype.
        var notDoubleForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDoubleForProtoLabel);
        var notInt32ForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brfalse, notInt32ForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notInt32ForProtoLabel);
        var notBoolForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolForProtoLabel);
        var notStrForProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStrForProtoLabel);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrForProtoLabel);

        // Not found in either: return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Found in local table: return it
        il.MarkLabel(foundInLocalLabel);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.setPrototypeOf(obj, proto) - sets the prototype of an object.
    /// Signature: object ObjectSetPrototypeOf(object obj, object proto)
    /// Stores in the local prototype table for standalone checks.
    /// </summary>
    private void EmitObjectSetPrototypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder prototypeStoreField, FieldBuilder nonExtensibleObjectsField)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectSetPrototypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.ObjectSetPrototypeOf = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.21 step 1: RequireObjectCoercible(O) — throw
        // TypeError on null/undefined. Pre-fix the null fall-through skipped
        // the integrity checks but also skipped the spec-mandated throw,
        // returning the input untouched.
        var rocThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, rocThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, rocThrowLabel);
        var afterRocLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterRocLabel);
        il.MarkLabel(rocThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.setPrototypeOf called on null or undefined");
        il.MarkLabel(afterRocLabel);

        // ECMA-262 §20.1.2.21 step 3: throw TypeError if Type(proto) is
        // neither Object nor Null. CLR null is fine; otherwise reject any
        // primitive (undefined / bool / double / int / string / Symbol /
        // BigInt). Object-like values (Dict, $Object, $IHasFields, $TSFunction,
        // List, etc.) are not caught by any of these Isinst checks.
        var protoOkLabel = il.DefineLabel();
        var protoThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, protoOkLabel);  // null → OK
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, protoThrowLabel);
        if (runtime.TSSymbolType != null)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
            il.Emit(OpCodes.Brtrue, protoThrowLabel);
        }
        il.Emit(OpCodes.Br, protoOkLabel);
        il.MarkLabel(protoThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object prototype may only be an Object or null");
        il.MarkLabel(protoOkLabel);

        // Check if object is null - if so, skip checks (dead code now that
        // null throws above, kept for layout symmetry).
        var nullCheckDoneLabel = il.DefineLabel();
        var notExtensibleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullCheckDoneLabel);

        // Check if object is a class instance (IHasFields but not $Object) - throw TypeError
        var notClassInstanceLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notClassInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, notClassInstanceLabel);
        // It's a class instance - throw TypeError
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot set prototype of class instance");
        il.MarkLabel(notClassInstanceLabel);

        // Check if object is extensible - if not, throw TypeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectIsExtensible);
        il.Emit(OpCodes.Brtrue, nullCheckDoneLabel);  // Object is extensible, proceed

        // Object is not extensible - throw TypeError
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot set prototype of non-extensible object");

        il.MarkLabel(nullCheckDoneLabel);

        // Store in $PropertyDescriptorStore for standalone operation.
        var skipLocalStoreLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, skipLocalStoreLabel); // Skip if null

        // Call $PropertyDescriptorStore.SetPrototype(obj, proto)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        // Also store in local prototype table for backward compatibility
        il.Emit(OpCodes.Ldsfld, prototypeStoreField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        var addOrUpdateMethod = typeof(System.Runtime.CompilerServices.ConditionalWeakTable<object, object>)
            .GetMethod("AddOrUpdate");
        if (addOrUpdateMethod != null)
        {
            il.Emit(OpCodes.Callvirt, addOrUpdateMethod);
        }
        else
        {
            // Fallback: Remove then Add
            var removeMethod = _types.ConditionalWeakTable.GetMethod("Remove", [_types.Object]);
            il.Emit(OpCodes.Pop); // Pop proto
            il.Emit(OpCodes.Pop); // Pop target
            il.Emit(OpCodes.Pop); // Pop table
            il.Emit(OpCodes.Ldsfld, prototypeStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, removeMethod!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldsfld, prototypeStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            var addMethod = _types.ConditionalWeakTable.GetMethod("Add");
            il.Emit(OpCodes.Callvirt, addMethod!);
        }

        il.MarkLabel(skipLocalStoreLabel);
        // Return obj (arg_0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}
