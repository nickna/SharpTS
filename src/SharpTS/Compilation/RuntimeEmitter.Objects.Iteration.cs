using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits ArrayDestructureSource: normalizes an array binding-pattern source through the
    /// iterator protocol (#685). Index-addressable sources — any
    /// <see cref="System.Collections.IList"/> (arrays, <c>$Array</c>, typed lists) — pass through
    /// unchanged so the desugared positional index access reads them directly and stays consistent
    /// with the matching pass-through type the type checker assigned. Any other iterable (Set, Map,
    /// generators, strings, <c>[Symbol.iterator]</c> objects, <c>IEnumerable&lt;object&gt;</c>) is
    /// materialized via <c>IterateToList</c> into a <c>List&lt;object&gt;</c> so positional access
    /// yields the iterated elements. A <b>string</b> is deliberately not on the pass-through path: it
    /// materializes to a fresh character array so a rest element binds an array rather than the trailing
    /// substring (<c>const [a, ...rest] = "hi"</c>), matching ECMA-262 (#753) — non-rest character
    /// values are identical either way. Non-iterable sources pass through, preserving the existing
    /// lenient behavior.
    /// Signature: object ArrayDestructureSource(object value, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitArrayDestructureSource(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayDestructureSource",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, runtime.TSSymbolType, _types.Type]
        );
        runtime.ArrayDestructureSource = method;

        var il = method.GetILGenerator();
        var ilistType = typeof(System.Collections.IList);

        var passThroughLabel = il.DefineLabel();

        // IList (List<object>, $Array, typed lists) → pass through: already index-addressable, and
        // routing a typed list (List<double>/List<bool>) through IterateToList would re-box it.
        // Note: a .NET string is NOT an IList, so it falls through to the IterateToList path below and
        // is materialized into a character array (#753) — required so a rest element binds an array.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, ilistType);
        il.Emit(OpCodes.Brtrue, passThroughLabel);

        // Everything else → materialize through the iterator protocol via IterateToList (Set, Map,
        // generators, [Symbol.iterator] objects, .NET enumerables). This is the same routine spread
        // uses, so destructuring and spread agree on what is iterable; it throws for genuinely
        // non-iterable values (and null), matching JS's "x is not iterable". The List<object> is
        // wrapped in a $Array so the subsequent positional index access has JS array semantics —
        // notably an out-of-range index (a pattern longer than the iterable) yields undefined (so a
        // binding default applies) instead of throwing IndexOutOfRange off the bare list.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(passThroughLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitObjectRest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Accept object instead of Dictionary to support both object literals and class instances
        var method = typeBuilder.DefineMethod(
            "ObjectRest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.Object, _types.ListOfObject]
        );
        runtime.ObjectRest = method;

        var il = method.GetILGenerator();

        var dictLabel = il.DefineLabel();
        var tsObjectLabel = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var processLabel = il.DefineLabel();

        var sourceDictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        // Check if arg0 is Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if arg0 is $IHasFields (covers $Object and class instances)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // Primitive wrappers other than String have no enumerable own properties, so their
        // CopyDataProperties result is a fresh empty object. The prior host exception for Number
        // and Symbol violated ToObject and leaked an implementation detail into destructuring.
        // (String sources are represented through the ordinary indexed-object paths before here.)
        il.Emit(OpCodes.Br, emptyLabel);

        // Dictionary path: cast arg0 directly
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, sourceDictLocal);
        il.Emit(OpCodes.Br, processLabel);

        // $IHasFields path: use Fields getter
        il.MarkLabel(tsObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, sourceDictLocal);
        il.Emit(OpCodes.Br, processLabel);

        // Empty result fallback
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Ret);

        // Process the source dictionary (now in sourceDictLocal)
        il.MarkLabel(processLabel);

        // Create result dictionary
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Create HashSet<string> from excludeKeys
        var excludeSetLocal = il.DeclareLocal(_types.HashSetOfString);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.HashSetOfString));
        il.Emit(OpCodes.Stloc, excludeSetLocal);

        // Add each exclude key to the set
        var excludeIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);

        var excludeLoopStart = il.DefineLabel();
        var excludeLoopEnd = il.DefineLabel();

        il.MarkLabel(excludeLoopStart);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, excludeLoopEnd);

        // Get excludeKeys[i] and add to set if not null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        var keyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        var skipAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfString, "Add", _types.String));
        il.Emit(OpCodes.Pop); // discard bool return

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);
        il.Emit(OpCodes.Br, excludeLoopStart);

        il.MarkLabel(excludeLoopEnd);

        // Iterate over source dictionary keys using sourceDictLocal
        // We need the KeyCollection.Enumerator
        var keyCollectionType = typeof(Dictionary<string, object>.KeyCollection);
        var keysEnumType = typeof(Dictionary<string, object>.KeyCollection.Enumerator);

        var keysEnumLocal = il.DeclareLocal(keysEnumType);
        il.Emit(OpCodes.Ldloc, sourceDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(keyCollectionType, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, keysEnumLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);
        // MoveNext
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(keysEnumType, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get Current key
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumType, "Current")!.GetGetMethod()!);
        var currentKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // Check if key is in excludeSet
        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfString, "Contains", _types.String));
        var skipKey = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipKey);

        // Add to result: result[key] = sourceDict[key]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Ldloc, sourceDictLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Item")!.GetSetMethod()!);

        il.MarkLabel(skipKey);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Constrained, keysEnumType);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetValues = method;

        var il = method.GetILGenerator();
        // number[] unboxing: materialize a numeric-mode $Array before enumerating it as an object.
        EmitDeoptArgIfNumericArray(il, runtime, 0);
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;
        var kvpType = _types.KeyValuePairStringObject;
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var fieldsDictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);

        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();
        var fieldsLoopStart = il.DefineLabel();
        var fieldsLoopEnd = il.DefineLabel();

        var notProxyForValuesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notProxyForValuesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notProxyForValuesLabel);
        EmitProxyEnumerableOwnPropertiesCheck(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0),
            notProxyForValuesLabel, entries: false);
        il.MarkLabel(notProxyForValuesLabel);

        // EnumerableOwnProperties is defined in terms of [[OwnPropertyKeys]],
        // followed by [[Get]] for each enumerable string key. Keep that single
        // algorithm here instead of maintaining carrier-specific copies of the
        // key filtering and ordering logic below. This is also essential for
        // Proxy observability: ownKeys/gOPD happen in GetKeys, then each value
        // read dispatches the proxy's get trap.
        {
            var keysLocal = il.DeclareLocal(listType);
            var keyIndexLocal = il.DeclareLocal(_types.Int32);
            var keyLocal = il.DeclareLocal(_types.String);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.GetKeys);
            il.Emit(OpCodes.Stloc, keysLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, keyIndexLocal);
            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Ldloc, keysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd);
            il.Emit(OpCodes.Ldloc, keysLocal);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, keyLocal);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, keyIndexLocal);
            il.Emit(OpCodes.Br, loopStart);
            il.MarkLabel(loopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // ECMA-262 §20.1.2.23 step 1: Let obj be ? ToObject(O). ToObject throws
        // TypeError on null/undefined.
        var notNullForValsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullForValsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.values called on null or undefined");
        il.MarkLabel(notNullForValsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefForValsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefForValsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.values called on null or undefined");
        il.MarkLabel(notUndefForValsLabel);

        // String primitive: ToObject wraps it as a String exotic object whose
        // OwnPropertyKeys are the indexed chars. ECMA-262 §20.1.2.23 calls
        // EnumerableOwnProperties which iterates those — so `Object.values("abc")`
        // returns ["a","b","c"].
        var notStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStrLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var strLocal = il.DeclareLocal(_types.String);
            var sIdxLocal = il.DeclareLocal(_types.Int32);
            var sLenLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, strLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, sLenLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, sIdxLocal);
            var sLoop = il.DefineLabel();
            var sLoopEnd = il.DefineLabel();
            il.MarkLabel(sLoop);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Ldloc, sLenLocal);
            il.Emit(OpCodes.Bge, sLoopEnd);
            var cLocal = il.DeclareLocal(_types.Char);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
            il.Emit(OpCodes.Stloc, cLocal);
            il.Emit(OpCodes.Ldloca, cLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, sIdxLocal);
            il.Emit(OpCodes.Br, sLoop);
            il.MarkLabel(sLoopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(notStrLabel);

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Create result list and add all values from dictionary
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        var valDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // current = enumerator.Current
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // ECMA-262 §20.1.2.23 EnumerableOwnProperties: skip a key whose installed
        // descriptor is non-enumerable. The Math/JSON singleton dicts carry their
        // built-in methods with Enumerable=false, so without this filter
        // Object.values leaks the built-ins while Object.keys (which already
        // filters) does not — making keys and values mutually inconsistent.
        var valIncludeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, valDescLocal);
        il.Emit(OpCodes.Ldloc, valDescLocal);
        il.Emit(OpCodes.Brfalse, valIncludeLabel);  // no descriptor → enumerable by default
        il.Emit(OpCodes.Ldloc, valDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, dictLoopStart);  // non-enumerable → skip
        il.MarkLabel(valIncludeLabel);

        // result.Add(current.Value)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // PDS-only enumerable own properties (accessor-only created via
        // defineProperty without backing-dict write). Iterate the extra-keys
        // list and call GetProperty for each — which triggers the accessor's
        // getter per ECMA-262 §10.1.11.1 / §20.1.2.23.
        var pdsKeysListV = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsKeysListV);
        var pdsLoopStartV = il.DefineLabel();
        var pdsLoopEndV = il.DefineLabel();
        var pdsKeyIdxV = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, pdsKeyIdxV);
        il.MarkLabel(pdsLoopStartV);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxV);
        il.Emit(OpCodes.Ldloc, pdsKeysListV);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
        il.Emit(OpCodes.Bge, pdsLoopEndV);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, pdsKeysListV);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxV);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxV);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, pdsKeyIdxV);
        il.Emit(OpCodes.Br, pdsLoopStartV);
        il.MarkLabel(pdsLoopEndV);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Emitted $Object path for class instances (standalone-safe)
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // Stage E.2 M5: $Array / List<object?> path — Object.values on an
        // array returns its PRESENT elements (holes skipped, spec 20.1.2.23).
        // $Array inherits List<object?>, so one Isinst covers both.
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var listIterLocal = il.DeclareLocal(listType);
            var iLocal = il.DeclareLocal(_types.Int32);
            var elemLocal = il.DeclareLocal(_types.Object);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();
            var advance = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, listType);
            il.Emit(OpCodes.Stloc, listIterLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, listIterLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd);

            il.Emit(OpCodes.Ldloc, listIterLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Stloc, elemLocal);

            // Skip holes.
            il.Emit(OpCodes.Ldloc, elemLocal);
            il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
            il.Emit(OpCodes.Brtrue, advance);

            // result.Add(elem);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, elemLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);

            il.MarkLabel(advance);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, loopStart);
            il.MarkLabel(loopEnd);
        }

        // Append PDS extras for arrays: accessor properties via Object.defineProperty.
        // Mirror the dict-path append; iterate PDS keys and emit GetProperty(obj, key).
        var valPdsKeysLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, valPdsKeysLocal);
        {
            var iLocal3 = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal3);
            var pdsValStart = il.DefineLabel();
            var pdsValEnd = il.DefineLabel();
            il.MarkLabel(pdsValStart);
            il.Emit(OpCodes.Ldloc, iLocal3);
            il.Emit(OpCodes.Ldloc, valPdsKeysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, pdsValEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, valPdsKeysLocal);
            il.Emit(OpCodes.Ldloc, iLocal3);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, iLocal3);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal3);
            il.Emit(OpCodes.Br, pdsValStart);
            il.MarkLabel(pdsValEnd);
        }

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notListLabel);

        // $TSFunction: user-installed properties (`fn.x = 1`) tracked in PDS.
        // Mirror the EmitGetKeys $TSFunction branch — iterate PDS extras and
        // emit the corresponding values via GetProperty.
        var notTSFnForValuesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFnForValuesLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        var fnValsKeysLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Stloc, fnValsKeysLocal);
        {
            var iLocal2 = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal2);
            var loopStart2 = il.DefineLabel();
            var loopEnd2 = il.DefineLabel();
            il.MarkLabel(loopStart2);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Ldloc, fnValsKeysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd2);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, fnValsKeysLocal);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal2);
            il.Emit(OpCodes.Br, loopStart2);
            il.MarkLabel(loopEnd2);
        }
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSFnForValuesLabel);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // $IHasFields supports class-instance key/value storage.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, returnResultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // Iterate _fields and add values
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        var fieldsDictEnumeratorLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Stloc, fieldsDictEnumeratorLocal);

        il.MarkLabel(fieldsLoopStart);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);

        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, fieldsLoopStart);

        il.MarkLabel(fieldsLoopEnd);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // $TSObject literal accessors: iterate _getters keys, call
        // GetProperty(obj, key) (which fires the getter). Mirrors GetKeys'
        // recent extension. Without this, Object.values on a literal with
        // accessors misses the getter-backed values.
        var notTSObjForVal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjForVal);
        var tsoValGettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetGettersDict);
        il.Emit(OpCodes.Stloc, tsoValGettersDict);
        var skipValGetters = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoValGettersDict);
        il.Emit(OpCodes.Brfalse, skipValGetters);
        var keysType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection).GetGenericTypeDefinition(), _types.String, _types.Object);
        var keysEnumType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);
        var valGettersEnum = il.DeclareLocal(keysEnumType);
        il.Emit(OpCodes.Ldloc, tsoValGettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, valGettersEnum);
        var valGettersStart = il.DefineLabel();
        var valGettersEnd = il.DefineLabel();
        var valGettersKey = il.DeclareLocal(_types.String);
        il.MarkLabel(valGettersStart);
        il.Emit(OpCodes.Ldloca, valGettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, valGettersEnd);
        il.Emit(OpCodes.Ldloca, valGettersEnum);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valGettersKey);
        // PDS enum check
        var valGetterDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valGettersKey);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, valGetterDescLocal);
        var valGetterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valGetterDescLocal);
        il.Emit(OpCodes.Brfalse, valGetterAddLabel);
        il.Emit(OpCodes.Ldloc, valGetterDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, valGettersStart);
        il.MarkLabel(valGetterAddLabel);
        // result.Add(GetProperty(obj, key))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valGettersKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, valGettersStart);
        il.MarkLabel(valGettersEnd);
        il.Emit(OpCodes.Ldloca, valGettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumType, "Dispose")!);
        il.MarkLabel(skipValGetters);

        // Symmetric iteration of _setters for setter-only literal accessors.
        var tsoValSettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetSettersDict);
        il.Emit(OpCodes.Stloc, tsoValSettersDict);
        var skipValSetters = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoValSettersDict);
        il.Emit(OpCodes.Brfalse, skipValSetters);
        var valSettersEnum = il.DeclareLocal(keysEnumType);
        il.Emit(OpCodes.Ldloc, tsoValSettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, valSettersEnum);
        var valSettersStart = il.DefineLabel();
        var valSettersEnd = il.DefineLabel();
        var valSettersKey = il.DeclareLocal(_types.String);
        il.MarkLabel(valSettersStart);
        il.Emit(OpCodes.Ldloca, valSettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, valSettersEnd);
        il.Emit(OpCodes.Ldloca, valSettersEnum);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valSettersKey);
        // Skip if paired getter already added.
        il.Emit(OpCodes.Ldloc, tsoValGettersDict);
        var skipPairCheck = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipPairCheck);
        il.Emit(OpCodes.Ldloc, tsoValGettersDict);
        il.Emit(OpCodes.Ldloc, valSettersKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "ContainsKey")!);
        il.Emit(OpCodes.Brtrue, valSettersStart);
        il.MarkLabel(skipPairCheck);
        // PDS enum check
        var valSetterDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valSettersKey);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, valSetterDescLocal);
        var valSetterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valSetterDescLocal);
        il.Emit(OpCodes.Brfalse, valSetterAddLabel);
        il.Emit(OpCodes.Ldloc, valSetterDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, valSettersStart);
        il.MarkLabel(valSetterAddLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valSettersKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, valSettersStart);
        il.MarkLabel(valSettersEnd);
        il.Emit(OpCodes.Ldloca, valSettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumType, "Dispose")!);
        il.MarkLabel(skipValSetters);
        il.MarkLabel(notTSObjForVal);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetEntries = method;

        var il = method.GetILGenerator();
        // number[] unboxing: materialize a numeric-mode $Array before enumerating it as an object.
        EmitDeoptArgIfNumericArray(il, runtime, 0);
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;
        var kvpType = _types.KeyValuePairStringObject;
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var fieldsDictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);
        var entryLocal = il.DeclareLocal(listType);

        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();
        var fieldsLoopStart = il.DefineLabel();
        var fieldsLoopEnd = il.DefineLabel();

        var notProxyForEntriesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notProxyForEntriesLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notProxyForEntriesLabel);
        EmitProxyEnumerableOwnPropertiesCheck(
            il, runtime, () => il.Emit(OpCodes.Ldarg_0),
            notProxyForEntriesLabel, entries: true);
        il.MarkLabel(notProxyForEntriesLabel);

        // Object.entries shares the same EnumerableOwnProperties key walk as
        // Object.values. Build pairs from GetKeys + GetProperty so ordering,
        // descriptor filtering, getters, and Proxy traps stay aligned.
        {
            var keysLocal = il.DeclareLocal(listType);
            var keyIndexLocal = il.DeclareLocal(_types.Int32);
            var keyLocal = il.DeclareLocal(_types.String);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.GetKeys);
            il.Emit(OpCodes.Stloc, keysLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, keyIndexLocal);
            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Ldloc, keysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd);
            il.Emit(OpCodes.Ldloc, keysLocal);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, keyLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, entryLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, keyIndexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, keyIndexLocal);
            il.Emit(OpCodes.Br, loopStart);
            il.MarkLabel(loopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // ECMA-262 §20.1.2.5 step 1: Let obj be ? ToObject(O). ToObject throws
        // TypeError on null/undefined.
        var notNullForEntsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullForEntsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.entries called on null or undefined");
        il.MarkLabel(notNullForEntsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefForEntsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefForEntsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.entries called on null or undefined");
        il.MarkLabel(notUndefForEntsLabel);

        // String primitive: yields [["0","a"], ["1","b"], ...] per ECMA-262
        // §20.1.2.5 + §10.4.3 String exotic indexed-char own properties.
        var notStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStrLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var strLocal = il.DeclareLocal(_types.String);
            var sIdxLocal = il.DeclareLocal(_types.Int32);
            var sLenLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, strLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, sLenLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, sIdxLocal);
            var sLoop = il.DefineLabel();
            var sLoopEnd = il.DefineLabel();
            il.MarkLabel(sLoop);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Ldloc, sLenLocal);
            il.Emit(OpCodes.Bge, sLoopEnd);
            // entry = new List<object?>()
            var sEntry = il.DeclareLocal(listType);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, sEntry);
            // entry.Add(idx.ToString())
            il.Emit(OpCodes.Ldloc, sEntry);
            il.Emit(OpCodes.Ldloca, sIdxLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
            // entry.Add(str[idx].ToString())
            var sChar = il.DeclareLocal(_types.Char);
            il.Emit(OpCodes.Ldloc, sEntry);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
            il.Emit(OpCodes.Stloc, sChar);
            il.Emit(OpCodes.Ldloca, sChar);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
            // result.Add(entry)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, sEntry);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
            il.Emit(OpCodes.Ldloc, sIdxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, sIdxLocal);
            il.Emit(OpCodes.Br, sLoop);
            il.MarkLabel(sLoopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(notStrLabel);

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Create result list and add [key, value] entries
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // ECMA-262 §20.1.2.5 EnumerableOwnProperties: skip a key whose installed
        // descriptor is non-enumerable, mirroring the GetKeys/GetValues filter so
        // Object.entries stays consistent with Object.keys (the Math/JSON
        // singleton built-ins carry Enumerable=false).
        var entDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var entIncludeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, entDescLocal);
        il.Emit(OpCodes.Ldloc, entDescLocal);
        il.Emit(OpCodes.Brfalse, entIncludeLabel);  // no descriptor → enumerable by default
        il.Emit(OpCodes.Ldloc, entDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, dictLoopStart);  // non-enumerable → skip
        il.MarkLabel(entIncludeLabel);

        // var entry = new List<object?> { kvp.Key, kvp.Value };
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // PDS-only enumerable own properties: accessor-only defineProperty
        // entries. Iterate the extra-keys list and append [key, GetProperty(obj,key)]
        // — GetProperty triggers the accessor's getter.
        var pdsKeysListE = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsKeysListE);
        var pdsLoopStartE = il.DefineLabel();
        var pdsLoopEndE = il.DefineLabel();
        var pdsKeyIdxE = il.DeclareLocal(_types.Int32);
        var pdsKeyE = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, pdsKeyIdxE);
        il.MarkLabel(pdsLoopStartE);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxE);
        il.Emit(OpCodes.Ldloc, pdsKeysListE);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
        il.Emit(OpCodes.Bge, pdsLoopEndE);
        il.Emit(OpCodes.Ldloc, pdsKeysListE);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxE);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, pdsKeyE);
        // var entry = new List<object?> { key, GetProperty(obj, key) };
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloc, pdsKeyE);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, pdsKeyE);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pdsKeyIdxE);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, pdsKeyIdxE);
        il.Emit(OpCodes.Br, pdsLoopStartE);
        il.MarkLabel(pdsLoopEndE);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Emitted $Object path for class instances (standalone-safe)
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // Stage E.2 M5: $Array / List<object?> path — Object.entries on an
        // array returns [[index_string, value], ...] for PRESENT elements,
        // skipping holes per spec 20.1.2.5.
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var listIterLocal = il.DeclareLocal(listType);
            var iLocal = il.DeclareLocal(_types.Int32);
            var elemLocal = il.DeclareLocal(_types.Object);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();
            var advance = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, listType);
            il.Emit(OpCodes.Stloc, listIterLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal);
            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldloc, listIterLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd);

            il.Emit(OpCodes.Ldloc, listIterLocal);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Stloc, elemLocal);

            // Skip holes.
            il.Emit(OpCodes.Ldloc, elemLocal);
            il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
            il.Emit(OpCodes.Brtrue, advance);

            // entry = new List<object> { i.ToString(), elem }; result.Add(entry);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, entryLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloca, iLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, elemLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);

            il.MarkLabel(advance);
            il.Emit(OpCodes.Ldloc, iLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal);
            il.Emit(OpCodes.Br, loopStart);
            il.MarkLabel(loopEnd);
        }

        // Append PDS extras for arrays: each accessor key + GetProperty value.
        var entPdsKeysLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, entPdsKeysLocal);
        {
            var iLocal4 = il.DeclareLocal(_types.Int32);
            var keyLocal2 = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal4);
            var pdsEntStart = il.DefineLabel();
            var pdsEntEnd = il.DefineLabel();
            il.MarkLabel(pdsEntStart);
            il.Emit(OpCodes.Ldloc, iLocal4);
            il.Emit(OpCodes.Ldloc, entPdsKeysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, pdsEntEnd);
            il.Emit(OpCodes.Ldloc, entPdsKeysLocal);
            il.Emit(OpCodes.Ldloc, iLocal4);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, keyLocal2);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, entryLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, keyLocal2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, keyLocal2);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, iLocal4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal4);
            il.Emit(OpCodes.Br, pdsEntStart);
            il.MarkLabel(pdsEntEnd);
        }

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notListLabel);

        // $TSFunction: emit [key, GetProperty(fn, key)] for each PDS extra.
        var notTSFnForEntriesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFnForEntriesLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        var fnEntKeysLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Stloc, fnEntKeysLocal);
        {
            var iLocal2 = il.DeclareLocal(_types.Int32);
            var keyLocal = il.DeclareLocal(_types.String);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, iLocal2);
            var loopStart2 = il.DefineLabel();
            var loopEnd2 = il.DefineLabel();
            il.MarkLabel(loopStart2);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Ldloc, fnEntKeysLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
            il.Emit(OpCodes.Bge, loopEnd2);
            il.Emit(OpCodes.Ldloc, fnEntKeysLocal);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, keyLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, entryLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, entryLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.Emit(OpCodes.Ldloc, iLocal2);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, iLocal2);
            il.Emit(OpCodes.Br, loopStart2);
            il.MarkLabel(loopEnd2);
        }
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSFnForEntriesLabel);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, returnResultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        var fieldsDictEnumeratorLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, fieldsDictEnumeratorLocal);

        il.MarkLabel(fieldsLoopStart);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);

        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, fieldsLoopStart);

        il.MarkLabel(fieldsLoopEnd);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // $TSObject literal accessors: iterate _getters keys, append
        // [key, GetProperty(obj, key)] (GetProperty fires the accessor's
        // getter). Mirrors GetKeys/GetValues' recent extensions.
        var notTSObjForEnt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjForEnt);
        var tsoEntGettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetGettersDict);
        il.Emit(OpCodes.Stloc, tsoEntGettersDict);
        var skipEntGetters = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoEntGettersDict);
        il.Emit(OpCodes.Brfalse, skipEntGetters);
        var entKeysType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection).GetGenericTypeDefinition(), _types.String, _types.Object);
        var entKeysEnumType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);
        var entGettersEnum = il.DeclareLocal(entKeysEnumType);
        il.Emit(OpCodes.Ldloc, tsoEntGettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(entKeysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, entGettersEnum);
        var entGettersStart = il.DefineLabel();
        var entGettersEnd = il.DefineLabel();
        var entGettersKey = il.DeclareLocal(_types.String);
        il.MarkLabel(entGettersStart);
        il.Emit(OpCodes.Ldloca, entGettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(entKeysEnumType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, entGettersEnd);
        il.Emit(OpCodes.Ldloca, entGettersEnum);
        il.Emit(OpCodes.Call, _types.GetProperty(entKeysEnumType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, entGettersKey);
        // PDS enum check
        var entGetterDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, entGettersKey);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, entGetterDescLocal);
        var entGetterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, entGetterDescLocal);
        il.Emit(OpCodes.Brfalse, entGetterAddLabel);
        il.Emit(OpCodes.Ldloc, entGetterDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, entGettersStart);
        il.MarkLabel(entGetterAddLabel);
        // entry = [key, GetProperty(obj, key)]
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entGettersKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, entGettersKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, entGettersStart);
        il.MarkLabel(entGettersEnd);
        il.Emit(OpCodes.Ldloca, entGettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(entKeysEnumType, "Dispose")!);
        il.MarkLabel(skipEntGetters);

        // Symmetric iteration of _setters for setter-only literal accessors.
        var tsoEntSettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetSettersDict);
        il.Emit(OpCodes.Stloc, tsoEntSettersDict);
        var skipEntSetters = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoEntSettersDict);
        il.Emit(OpCodes.Brfalse, skipEntSetters);
        var entSettersEnum = il.DeclareLocal(entKeysEnumType);
        il.Emit(OpCodes.Ldloc, tsoEntSettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(entKeysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, entSettersEnum);
        var entSettersStart = il.DefineLabel();
        var entSettersEnd = il.DefineLabel();
        var entSettersKey = il.DeclareLocal(_types.String);
        il.MarkLabel(entSettersStart);
        il.Emit(OpCodes.Ldloca, entSettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(entKeysEnumType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, entSettersEnd);
        il.Emit(OpCodes.Ldloca, entSettersEnum);
        il.Emit(OpCodes.Call, _types.GetProperty(entKeysEnumType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, entSettersKey);
        // Skip if paired getter already emitted.
        il.Emit(OpCodes.Ldloc, tsoEntGettersDict);
        var entSkipPairCheck = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, entSkipPairCheck);
        il.Emit(OpCodes.Ldloc, tsoEntGettersDict);
        il.Emit(OpCodes.Ldloc, entSettersKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "ContainsKey")!);
        il.Emit(OpCodes.Brtrue, entSettersStart);
        il.MarkLabel(entSkipPairCheck);
        // PDS enum check
        var entSetterDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, entSettersKey);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, entSetterDescLocal);
        var entSetterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, entSetterDescLocal);
        il.Emit(OpCodes.Brfalse, entSetterAddLabel);
        il.Emit(OpCodes.Ldloc, entSetterDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, entSettersStart);
        il.MarkLabel(entSetterAddLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entSettersKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, entSettersKey);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, entSettersStart);
        il.MarkLabel(entSettersEnd);
        il.Emit(OpCodes.Ldloca, entSettersEnum);
        il.Emit(OpCodes.Call, _types.GetMethod(entKeysEnumType, "Dispose")!);
        il.MarkLabel(skipEntSetters);
        il.MarkLabel(notTSObjForEnt);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIsArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.IsArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // ECMA-262 23.1.2.2: Array.isArray returns false for arguments objects.
        // The $Arguments marker subclass would otherwise match the IList check
        // below (it inherits from List<object>), so screen it out first.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        var notArgumentsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notArgumentsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArgumentsLabel);

        // IsArray(proxy) recursively follows [[ProxyTarget]], but a revoked
        // proxy must throw rather than report false. The proxy type lives in
        // SharpTS.dll, so inspect its public state through reflection.
        var notProxyLabel = il.DefineLabel();
        var proxyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notProxyLabel);
        EmitProxyTypeCheck(
            il, () => il.Emit(OpCodes.Ldarg_0), proxyLabel, notProxyLabel);
        il.MarkLabel(proxyLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "IsRevoked");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var proxyActiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, proxyActiveLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot perform operation on a revoked proxy");
        il.MarkLabel(proxyActiveLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Target");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notProxyLabel);

        // Check if IList<object?> (covers List<object?>, $Array, and any other array-like type)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IListOfObject);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // ECMA-262 23.1.3: Array.prototype itself is an Array exotic object.
        // Tests probe `Array.isArray(Array.prototype)` and expect true. We
        // back the prototype with a Dictionary singleton (so user code can
        // do `Array.prototype.X = ...`), so reference-equality unlocks the
        // spec without changing the prototype's actual storage type.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // False
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, endLabel);

        // True
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Object.fromEntries(entries) - converts iterable of [key, value] pairs to object.
    /// Signature: Dictionary&lt;string, object&gt; ObjectFromEntries(object entries, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitObjectFromEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectFromEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.Object, runtime.TSSymbolType, _types.Type]
        );
        runtime.ObjectFromEntries = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        // Locals
        var resultLocal = il.DeclareLocal(dictType);
        var iterableLocal = il.DeclareLocal(listType);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var entryLocal = il.DeclareLocal(_types.Object);
        var entryListLocal = il.DeclareLocal(listType);
        var keyLocal = il.DeclareLocal(_types.String);
        var valueLocal = il.DeclareLocal(_types.Object);

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var notNullLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // ECMA-262 §20.1.2.7 Object.fromEntries step 1:
        // RequireObjectCoercible — throws TypeError on null/undefined.
        // Use TSTypeError + CreateException so __tsValue carries through to
        // catch/then handlers as a real TypeError instance.
        var requireOcThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, requireOcThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, requireOcThrowLabel);
        il.Emit(OpCodes.Br, notNullLabel);

        il.MarkLabel(requireOcThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.fromEntries: argument must not be null or undefined");

        il.MarkLabel(notNullLabel);

        // Convert input to list using IterateToList(entries, iteratorSymbol, runtimeType)
        il.Emit(OpCodes.Ldarg_0);  // entries
        il.Emit(OpCodes.Ldarg_1);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_2);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, iterableLocal);

        // Create result dictionary
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Initialize loop counter
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop start
        il.MarkLabel(loopStartLabel);

        // Check if index < iterable.Count
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // Get entry = iterable[index]
        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, entryLocal);

        // Cast entry to List<object>
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Stloc, entryListLocal);

        // If not a list, throw
        il.Emit(OpCodes.Ldloc, entryListLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // Check if list has at least 2 elements
        il.Emit(OpCodes.Ldloc, entryListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, throwLabel);

        // Get key = entryList[0]?.ToString() ?? ""
        il.Emit(OpCodes.Ldloc, entryListLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item")!.GetGetMethod()!);
        var keyNullLabel = il.DefineLabel();
        var keyDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, keyNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, keyDoneLabel);
        il.MarkLabel(keyNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(keyDoneLabel);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Get value = entryList[1]
        il.Emit(OpCodes.Ldloc, entryListLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // result[key] = value
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        // Increment index
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        // Throw error for invalid entry
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Object.fromEntries() requires [key, value] pairs");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // Return result
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}
