using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a SetArrayElement method for the given backing type descriptor.
    /// Auto-extends the list with default entries if index &gt;= Count (JS semantics).
    /// Descriptor-driven: one implementation for all backing types (List&lt;double&gt;, List&lt;bool&gt;, List&lt;object?&gt;).
    /// </summary>
    private void EmitSetArrayElementFor(TypeBuilder typeBuilder, EmittedRuntime runtime, ArrayElementsDescriptor desc)
    {
        var listType = desc.GetListType(_types);
        var elemType = desc.GetElementType(_types);
        var methodName = desc.Kind == ArrayElementsKind.Object
            ? "SetArrayElement"
            : $"SetArrayElement{desc.Kind}";

        var method = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [listType, _types.Int32, elemType]
        );

        // Assign to the correct EmittedRuntime property
        switch (desc.Kind)
        {
            case ArrayElementsKind.Double: runtime.SetArrayElementDouble = method; break;
            case ArrayElementsKind.Bool: runtime.SetArrayElementBool = method; break;
            default: runtime.SetArrayElement = method; break;
        }

        var il = method.GetILGenerator();
        var setExistingLabel = il.DefineLabel();
        var loopCheckLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();

        var countGetter = _types.GetProperty(listType, "Count").GetGetMethod()!;
        var addMethod = _types.GetMethod(listType, "Add", [elemType])!;
        var setItemMethod = _types.GetMethod(listType, "set_Item", [_types.Int32, elemType])!;

        // if (index < list.Count) goto setExisting
        il.Emit(OpCodes.Ldarg_1); // index
        il.Emit(OpCodes.Ldarg_0); // list
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Blt, setExistingLabel);

        // Auto-extend: while (list.Count < index) list.Add(default)
        il.Emit(OpCodes.Br, loopCheckLabel);
        il.MarkLabel(loopBodyLabel);
        il.Emit(OpCodes.Ldarg_0); // list
        desc.EmitDefaultValue(il);
        il.Emit(OpCodes.Callvirt, addMethod);
        il.MarkLabel(loopCheckLabel);
        il.Emit(OpCodes.Ldarg_0); // list
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldarg_1); // index
        il.Emit(OpCodes.Blt, loopBodyLabel);

        // list.Add(value)
        il.Emit(OpCodes.Ldarg_0); // list
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Callvirt, addMethod);
        il.Emit(OpCodes.Ret);

        // setExisting: list[index] = value
        il.MarkLabel(setExistingLabel);
        il.Emit(OpCodes.Ldarg_0); // list
        il.Emit(OpCodes.Ldarg_1); // index
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Callvirt, setItemMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Stage E.2 M2: returns $Array (not List<object?>). Every array-literal
        // and rest-parameter path in the emitter routes through this, so the
        // change propagates without per-caller updates — downstream consumers
        // either use SetStackUnknown() (the stack-type tracker accepts any ref)
        // or hand off to $Runtime dispatchers that already Isinst $Array first.
        var method = typeBuilder.DefineMethod(
            "CreateArray",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSArrayType,
            [_types.ObjectArray]
        );
        runtime.CreateArray = method;

        var il = method.GetILGenerator();
        // return new $Array(new List<object>(elements));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object]
        );
        runtime.GetLength = method;

        var il = method.GetILGenerator();
        var tsArrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // $Arguments — return _length (sloppy arguments object, ECMA-262 10.4.4).
        // Must come before the List<object> check below since $Arguments inherits
        // from List<object>.
        var notArgumentsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArgumentsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArgumentsType);
        il.Emit(OpCodes.Ldfld, runtime.ArgumentsLengthField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArgumentsLabel);

        // $Array (wrapper around List<object?>) - check before typed lists
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayLabel);

        // Descriptor-driven: emit isinst check for each backing type
        var listLabels = new List<(ArrayElementsDescriptor desc, Label label)>();
        foreach (var desc in ArrayElements.All)
        {
            var label = il.DefineLabel();
            listLabels.Add((desc, label));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, desc.GetListType(_types));
            il.Emit(OpCodes.Brtrue, label);
        }

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // $Array handler: use the sparse-aware Length getter (clamps to
        // int.MaxValue when the array is sparse-long past that; callers
        // receiving int accept the clamp). Reading base.Count here would
        // miss the sparse tail — `new Array(10_000_000).length` would
        // report 0 instead of 10_000_000.
        il.MarkLabel(tsArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLengthGetter);
        il.Emit(OpCodes.Ret);

        // Descriptor-driven: emit Count handler for each backing type
        foreach (var (desc, label) in listLabels)
        {
            var listType = desc.GetListType(_types);
            il.MarkLabel(label);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, listType);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32]
        );
        runtime.GetElement = method;

        var il = method.GetILGenerator();
        var tsArrayElLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // $Array (wrapper around List<object?>) - check before List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayElLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // $Array handler: unwrap to elements, get item, convert hole sentinel
        // to undefined at the language boundary (ECMA-262: `arr[i]` on a hole
        // reads as undefined). Stage E.2 M2 added this unhole — holes start
        // appearing once ArrayConstructor uses SetLength to create sparse
        // initial arrays instead of zero-padding.
        il.MarkLabel(tsArrayElLabel);
        var tsArrayGetItemResult = il.DeclareLocal(_types.Object);
        var tsArrayGetItemNotHole = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, tsArrayGetItemResult);
        il.Emit(OpCodes.Ldloc, tsArrayGetItemResult);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, tsArrayGetItemNotHole);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrayGetItemNotHole);
        il.Emit(OpCodes.Ldloc, tsArrayGetItemResult);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitNormalizeOwnPropertyKeys(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "GetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetKeys = method;

        var il = method.GetILGenerator();
        // number[] unboxing: materialize a numeric-mode $Array before enumerating it as an object.
        EmitDeoptArgIfNumericArray(il, runtime, 0);
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var listLocal = il.DeclareLocal(listType);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var fieldsDictLocal = il.DeclareLocal(dictType);

        var checkListLabel = il.DefineLabel();
        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // ECMA-262 §20.1.2.18 step 1: Let obj be ? ToObject(O). ToObject throws
        // TypeError on null/undefined. test262 15.2.3.14-1-{4,5} verify each.
        var notNullForKeysLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, notNullForKeysLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.keys called on null or undefined");
        il.MarkLabel(notNullForKeysLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefForKeysLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefForKeysLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.keys called on null or undefined");
        il.MarkLabel(notUndefForKeysLabel);

        // Proxy short-circuit (#92): if obj is SharpTSProxy, dispatch TrapOwnKeys
        // and return. A revoked proxy throws inside TrapOwnKeys.
        var notProxyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notProxyLabel);
        EmitProxyOwnKeysCheck(il, runtime, () => il.Emit(OpCodes.Ldarg_0), notProxyLabel, enumerableOnly: true);
        il.MarkLabel(notProxyLabel);

        // String primitive: indexed-char keys "0", "1", ... per ECMA-262
        // §10.4.3 String exotic objects. `Object.keys("abc")` returns ["0","1","2"].
        var notStrKeysLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStrKeysLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        {
            var strLenLocal = il.DeclareLocal(_types.Int32);
            var strIdxLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, strLenLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, strIdxLocal);
            var sLoop = il.DefineLabel();
            var sEnd = il.DefineLabel();
            il.MarkLabel(sLoop);
            il.Emit(OpCodes.Ldloc, strIdxLocal);
            il.Emit(OpCodes.Ldloc, strLenLocal);
            il.Emit(OpCodes.Bge, sEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloca, strIdxLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
            il.Emit(OpCodes.Ldloc, strIdxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, strIdxLocal);
            il.Emit(OpCodes.Br, sLoop);
            il.MarkLabel(sEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(notStrKeysLabel);

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, checkListLabel);

        // return dict.Keys.Where(k => isEnumerable(obj, k)).Select(k => (object?)k).ToList();
        // ECMA-262 §19.1.2.18 Object.keys returns OWN enumerable property keys.
        // For each dict key, check PDSGetPropertyDescriptor — if a descriptor
        // is installed with Enumerable=false, skip the key. Used by both
        // Object.keys AND for-in (see StatementEmitterBase.EmitForIn → GetKeys).
        // Without this, RegExp.prototype's built-in methods that carry
        // PDS-installed non-enumerable descriptors still surface in Object.keys.
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Use KeyCollection and iterate
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var keysType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection).GetGenericTypeDefinition(), _types.String, _types.Object);
        var keysEnumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);
        var keysEnumeratorLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumeratorLocal);
        var currentKeyLocal = il.DeclareLocal(_types.String);
        var keyDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        var keysLoopStart = il.DefineLabel();
        var keysLoopEnd = il.DefineLabel();
        var keysLoopSkip = il.DefineLabel();
        il.MarkLabel(keysLoopStart);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, keysLoopEnd);

        // current = enumerator.Current
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // descriptor = PDSGetPropertyDescriptor(dict, current)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, keyDescLocal);
        // if (descriptor != null && !descriptor.Enumerable) skip
        il.Emit(OpCodes.Ldloc, keyDescLocal);
        il.Emit(OpCodes.Brfalse, /*include*/ keysLoopSkip /*placeholder, will overwrite*/);
        // descriptor exists — check Enumerable
        il.Emit(OpCodes.Ldloc, keyDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, keysLoopStart);  // skip non-enumerable: jump back to loop top
        il.MarkLabel(keysLoopSkip);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, keysLoopStart);

        il.MarkLabel(keysLoopEnd);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose")!);

        // ECMA-262 §10.1.11.1 OrdinaryOwnPropertyKeys: also include accessor-only
        // own properties (created via Object.defineProperty without writing to
        // the backing dict). PDSGetOwnEnumerableKeys returns the list of
        // enumerable PDS keys NOT already in dict.Keys.
        var pdsKeysList = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsKeysList);
        // Append each element to resultLocal: resultLocal.AddRange(pdsKeysList).
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pdsKeysList);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "AddRange", [_types.IEnumerableOfObject])!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);

        // Check if obj is List<object?>
        il.MarkLabel(checkListLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Return indices as strings: Enumerable.Range(0, list.Count).Select(i => (object?)i.ToString()).ToList()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        var listLoopSkip = il.DefineLabel();
        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listLoopEnd);

        // Stage E.2 M5: for-in / GetKeys on arrays skips holes per ECMA-262
        // 14.7.5.6 ForIn/OfBodyEvaluation (uses OrdinaryOwnPropertyKeys which
        // only returns kPresent indices). Without the check, an array with
        // `delete arr[2]` would yield "2" in `for (k in arr)`.
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, listLoopSkip);

        // Indexed array/list properties can carry descriptor metadata in PDS.
        // Object.keys and for-in must omit a present element whose own
        // descriptor is enumerable:false (for example an index created by
        // Object.defineProperties with no enumerable member).
        var listKeyLocal = il.DeclareLocal(_types.String);
        var listKeyDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, listKeyLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, listKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, listKeyDescLocal);
        var listKeyEnumerableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listKeyDescLocal);
        il.Emit(OpCodes.Brfalse, listKeyEnumerableLabel);
        il.Emit(OpCodes.Ldloc, listKeyDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, listLoopSkip);
        il.MarkLabel(listKeyEnumerableLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, listKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);

        il.MarkLabel(listLoopSkip);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);

        // Append PDS extras: arrays can have user-defined accessor properties
        // (`Object.defineProperty(arr, "prop", {get: ...})`) whose keys aren't
        // numeric indices and aren't in the list's element slots. Mirror the
        // dict-path PDSGetEnumerableExtraKeys append. Test262 keys/15.2.3.14-
        // 5-12 covers this.
        var pdsArrayKeysList = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsArrayKeysList);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pdsArrayKeysList);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "AddRange", [_types.IEnumerableOfObject])!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);

        // Emitted $Object path for class instances (standalone-safe)
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // $TSFunction: function objects can carry user-installed properties
        // (`fn.x = 1`) tracked in PDS. Object.keys must surface those even
        // though $TSFunction has no _fields dict. Mirror the dict-path
        // PDSGetEnumerableExtraKeys append.
        var notTSFnForKeysLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFnForKeysLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Function intrinsic keys predate user expandos. If defineProperty
        // makes one enumerable after `fn.a` was created, changing attributes
        // must not move the intrinsic key behind `a`.
        void EmitEnumerableFunctionIntrinsic(string name)
        {
            var skip = il.DefineLabel();
            var desc = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, desc);
            il.Emit(OpCodes.Ldloc, desc);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, desc);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
            il.MarkLabel(skip);
        }
        EmitEnumerableFunctionIntrinsic("length");
        EmitEnumerableFunctionIntrinsic("name");
        EmitEnumerableFunctionIntrinsic("prototype");

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        var fnPdsKeysLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Stloc, fnPdsKeysLocal);
        var fnKeyIndexLocal = il.DeclareLocal(_types.Int32);
        var fnKeyLocal = il.DeclareLocal(_types.Object);
        var fnKeyLoop = il.DefineLabel();
        var fnKeyNext = il.DefineLabel();
        var fnKeyEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, fnKeyIndexLocal);
        il.MarkLabel(fnKeyLoop);
        il.Emit(OpCodes.Ldloc, fnKeyIndexLocal);
        il.Emit(OpCodes.Ldloc, fnPdsKeysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Count")!);
        il.Emit(OpCodes.Bge, fnKeyEnd);
        il.Emit(OpCodes.Ldloc, fnPdsKeysLocal);
        il.Emit(OpCodes.Ldloc, fnKeyIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, fnKeyLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, fnKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Contains", [_types.Object])!);
        il.Emit(OpCodes.Brtrue, fnKeyNext);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, fnKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", [_types.Object])!);
        il.MarkLabel(fnKeyNext);
        il.Emit(OpCodes.Ldloc, fnKeyIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, fnKeyIndexLocal);
        il.Emit(OpCodes.Br, fnKeyLoop);
        il.MarkLabel(fnKeyEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSFnForKeysLabel);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        // if (!(obj is $IHasFields)) return empty list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // Add keys from _fields dictionary
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var keysEnumeratorLocal2 = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumeratorLocal2);

        var fieldsKeysLoopStart = il.DefineLabel();
        var fieldsKeysLoopEnd = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.String);

        il.MarkLabel(fieldsKeysLoopStart);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsKeysLoopEnd);

        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Skip if already in result (avoid duplicates) OR if a PDS descriptor
        // for this key marks it non-enumerable. ECMA-262 §19.1.2.18 (Object.keys)
        // returns OWN enumerable keys only. The e8bac219 write-through means
        // \$Object._fields can hold a value that was installed via
        // Object.defineProperty with enumerable:false — that key must NOT
        // appear in Object.keys.
        var skipKeyLabel = il.DefineLabel();
        // Filter internal boxed-primitive markers (__primitiveType / __primitiveValue).
        // ECMA-262 String/Number/Boolean wrappers don't expose [[PrimitiveData]]
        // via [[OwnPropertyKeys]]; the markers are our internal storage.
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, skipKeyLabel);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, skipKeyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Contains")!);
        il.Emit(OpCodes.Brtrue, skipKeyLabel);

        // PDS descriptor lookup; if present AND non-enumerable, skip.
        var fieldsKeyDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, fieldsKeyDescLocal);
        var fieldsAddKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldsKeyDescLocal);
        il.Emit(OpCodes.Brfalse, fieldsAddKeyLabel);
        il.Emit(OpCodes.Ldloc, fieldsKeyDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, skipKeyLabel);
        il.MarkLabel(fieldsAddKeyLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);

        il.MarkLabel(skipKeyLabel);
        il.Emit(OpCodes.Br, fieldsKeysLoopStart);

        il.MarkLabel(fieldsKeysLoopEnd);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose")!);

        // $TSObject literal accessors: iterate _getters / _setters maps too.
        // Object literal `{get bar(){...}}` stores accessor functions in
        // _getters (and _setters) dicts, separate from _fields. Without this,
        // Object.keys / for-in miss them. Use TSObjectGetGettersDict accessor
        // to read the dict; iterate keys; add unless already in result.
        var notTSObjectForGetters = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectForGetters);
        var tsoGettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetGettersDict);
        il.Emit(OpCodes.Stloc, tsoGettersDict);
        var skipGettersIter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoGettersDict);
        il.Emit(OpCodes.Brfalse, skipGettersIter);
        // Iterate getters' Keys.
        il.Emit(OpCodes.Ldloc, tsoGettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var gettersEnumLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, gettersEnumLocal);
        var gettersLoopStart = il.DefineLabel();
        var gettersLoopEnd = il.DefineLabel();
        var gettersKeyLocal = il.DeclareLocal(_types.String);
        il.MarkLabel(gettersLoopStart);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, gettersLoopEnd);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, gettersKeyLocal);
        // Skip if already in result.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, gettersKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Contains")!);
        il.Emit(OpCodes.Brtrue, gettersLoopStart);
        // PDS descriptor: skip if Enumerable=false.
        var gettersDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, gettersKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, gettersDescLocal);
        il.Emit(OpCodes.Ldloc, gettersDescLocal);
        var gettersAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, gettersAddLabel);
        il.Emit(OpCodes.Ldloc, gettersDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, gettersLoopStart);
        il.MarkLabel(gettersAddLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, gettersKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, gettersLoopStart);
        il.MarkLabel(gettersLoopEnd);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose")!);
        il.MarkLabel(skipGettersIter);

        // Symmetric iteration of _setters for setter-only literal accessors.
        var tsoSettersDict = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetSettersDict);
        il.Emit(OpCodes.Stloc, tsoSettersDict);
        var skipSettersIter = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsoSettersDict);
        il.Emit(OpCodes.Brfalse, skipSettersIter);
        il.Emit(OpCodes.Ldloc, tsoSettersDict);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var settersEnumLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, settersEnumLocal);
        var settersLoopStart = il.DefineLabel();
        var settersLoopEnd = il.DefineLabel();
        var settersKeyLocal = il.DeclareLocal(_types.String);
        il.MarkLabel(settersLoopStart);
        il.Emit(OpCodes.Ldloca, settersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, settersLoopEnd);
        il.Emit(OpCodes.Ldloca, settersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, settersKeyLocal);
        // Skip if already in result (avoid duplicates with paired getter).
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, settersKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Contains")!);
        il.Emit(OpCodes.Brtrue, settersLoopStart);
        // PDS descriptor: skip if Enumerable=false.
        var settersDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, settersKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, settersDescLocal);
        il.Emit(OpCodes.Ldloc, settersDescLocal);
        var settersAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, settersAddLabel);
        il.Emit(OpCodes.Ldloc, settersDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, settersLoopStart);
        il.MarkLabel(settersAddLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, settersKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add")!);
        il.Emit(OpCodes.Br, settersLoopStart);
        il.MarkLabel(settersLoopEnd);
        il.Emit(OpCodes.Ldloca, settersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose")!);
        il.MarkLabel(skipSettersIter);
        il.MarkLabel(notTSObjectForGetters);

        // PDS extra keys (accessor-only own properties not in _fields).
        // This tail also serves CLR-backed JS objects that don't implement
        // $IHasFields (notably Error and Date): their expando/accessor own
        // properties live entirely in PDS, so they must reach this append
        // instead of returning an empty list at returnResultLabel.
        il.MarkLabel(returnResultLabel);
        var pdsKeysListIH = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetEnumerableExtraKeys);
        il.Emit(OpCodes.Stloc, pdsKeysListIH);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pdsKeysListIH);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "AddRange", [_types.IEnumerableOfObject])!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Return empty list
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Applies OrdinaryOwnPropertyKeys string ordering to an already-collected
    /// list: canonical array indices first in ascending numeric order, followed
    /// by all other strings in their original creation order.
    /// </summary>
    private void EmitNormalizeOwnPropertyKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var listOfUInt32 = typeof(List<uint>);
        var method = typeBuilder.DefineMethod(
            "NormalizeOwnPropertyKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject]
        );
        runtime.NormalizeOwnPropertyKeys = method;

        var il = method.GetILGenerator();
        var numericLocal = il.DeclareLocal(listOfUInt32);
        var ordinaryLocal = il.DeclareLocal(_types.ListOfObject);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);
        var textLocal = il.DeclareLocal(_types.String);
        var uintLocal = il.DeclareLocal(_types.UInt32);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listOfUInt32, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, numericLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ordinaryLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var scanStart = il.DefineLabel();
        var scanEnd = il.DefineLabel();
        var ordinaryKey = il.DefineLabel();
        var afterKey = il.DefineLabel();
        il.MarkLabel(scanStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")!);
        il.Emit(OpCodes.Bge, scanEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, itemLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, textLocal);
        il.Emit(OpCodes.Ldloc, textLocal);
        il.Emit(OpCodes.Brfalse, ordinaryKey);
        il.Emit(OpCodes.Ldloc, textLocal);
        il.Emit(OpCodes.Ldloca, uintLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, ordinaryKey);
        il.Emit(OpCodes.Ldloc, uintLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, ordinaryKey);
        // Reject non-canonical spellings such as "01" and "+1".
        il.Emit(OpCodes.Ldloca, uintLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Ldloc, textLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, ordinaryKey);
        il.Emit(OpCodes.Ldloc, numericLocal);
        il.Emit(OpCodes.Ldloc, uintLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listOfUInt32, "Add", [_types.UInt32])!);
        il.Emit(OpCodes.Br, afterKey);
        il.MarkLabel(ordinaryKey);
        il.Emit(OpCodes.Ldloc, ordinaryLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.MarkLabel(afterKey);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, scanStart);
        il.MarkLabel(scanEnd);

        il.Emit(OpCodes.Ldloc, numericLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listOfUInt32, "Sort", Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        var numericStart = il.DefineLabel();
        var numericEnd = il.DefineLabel();
        il.MarkLabel(numericStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, numericLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listOfUInt32, "get_Count")!);
        il.Emit(OpCodes.Bge, numericEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, numericLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listOfUInt32, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, uintLocal);
        il.Emit(OpCodes.Ldloca, uintLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, numericStart);
        il.MarkLabel(numericEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, ordinaryLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", [_types.IEnumerableOfObject])!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetOwnPropertyNames(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetOwnPropertyNames",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetOwnPropertyNames = method;

        var il = method.GetILGenerator();

        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var objectLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();

        // Local for result list
        var namesLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // ECMA-262 §20.1.2.10 step 1: Let O be ? ToObject(obj). ToObject throws
        // TypeError for null/undefined. Tests 15.2.3.4-1-{1,2,3} verify each.
        var gopnTypeOkLabel = il.DefineLabel();
        var gopnThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, gopnThrowLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, gopnThrowLabel);
        il.Emit(OpCodes.Br, gopnTypeOkLabel);

        il.MarkLabel(gopnThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(gopnTypeOkLabel);

        // Proxy short-circuit (#92): if obj is SharpTSProxy, dispatch TrapOwnKeys
        // and return. A revoked proxy throws inside TrapOwnKeys.
        var notProxyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notProxyLabel);
        EmitProxyOwnKeysCheck(il, runtime, () => il.Emit(OpCodes.Ldarg_0), notProxyLabel, enumerableOnly: false);
        il.MarkLabel(notProxyLabel);

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // if (obj is List<object?> list)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (obj != null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, objectLabel);

        // return empty list
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Ret);

        // Dictionary case: return dict.Keys as list
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, namesLocal);

        // Get the Keys collection and iterate
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.DictionaryStringObject, "Keys"));
        // Get enumerator
        var keysEnumeratorLocal = il.DeclareLocal(_types.IEnumeratorOfString);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerableOfString, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, keysEnumeratorLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        il.MarkLabel(dictLoopStart);
        // while (enumerator.MoveNext())
        il.Emit(OpCodes.Ldloc, keysEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictLoopEnd);
        // Skip internal marker keys (__primitiveType / __primitiveValue on boxed
        // wrappers). Per ECMA-262, String/Number/Boolean wrappers don't expose
        // their [[PrimitiveData]] slot via [[OwnPropertyKeys]] — but user code
        // may legitimately use other __-prefixed keys (e.g. lodash _ utilities),
        // so we filter exactly these two reserved names rather than "__" broadly.
        var dictKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, keysEnumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfString, "Current"));
        il.Emit(OpCodes.Stloc, dictKeyLocal);
        il.Emit(OpCodes.Ldloc, dictKeyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, dictLoopStart);
        il.Emit(OpCodes.Ldloc, dictKeyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, dictLoopStart);
        // names.Add(current)
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, dictKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Append PDS extras (accessor-only own properties + non-enumerable
        // own properties created via Object.defineProperty).
        var pdsExtraNamesLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Call, runtime.PDSGetAllExtraKeys);
        il.Emit(OpCodes.Stloc, pdsExtraNamesLocal);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, pdsExtraNamesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", [_types.IEnumerableOfObject])!);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);

        // List case: return ["0", "1", ..., "length"] (skipping holes).
        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, namesLocal);

        // for (int i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        var listLoopSkip = il.DefineLabel();
        il.Emit(OpCodes.Br, listLoopEnd);

        il.MarkLabel(listLoopStart);
        // Stage E.2 M5: getOwnPropertyNames skips holes — interpreter matches
        // via SharpTSArray.HasIndex; compile mode must check the List entry
        // against the $ArrayHole sentinel.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, listLoopSkip);

        // names.Add(i.ToString())
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloca, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(listLoopSkip);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(listLoopEnd);
        // i < list.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Blt, listLoopStart);

        // names.Add("length")
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // Append PDS extras (named props installed via `arr.foo = X` or
        // Object.defineProperty(arr, "foo", ...)). Required for Test262
        // 15.2.3.4-4-47 (gOPN on array with own named data property).
        var listPdsNamesLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetAllExtraKeys);
        il.Emit(OpCodes.Stloc, listPdsNamesLocal);
        var listExtraIndexLocal = il.DeclareLocal(_types.Int32);
        var listExtraKeyLocal = il.DeclareLocal(_types.Object);
        var listExtraLoop = il.DefineLabel();
        var listExtraNext = il.DefineLabel();
        var listExtraEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, listExtraIndexLocal);
        il.MarkLabel(listExtraLoop);
        il.Emit(OpCodes.Ldloc, listExtraIndexLocal);
        il.Emit(OpCodes.Ldloc, listPdsNamesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")!);
        il.Emit(OpCodes.Bge, listExtraEnd);
        il.Emit(OpCodes.Ldloc, listPdsNamesLocal);
        il.Emit(OpCodes.Ldloc, listExtraIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, listExtraKeyLocal);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, listExtraKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Contains", [_types.Object])!);
        il.Emit(OpCodes.Brtrue, listExtraNext);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, listExtraKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.MarkLabel(listExtraNext);
        il.Emit(OpCodes.Ldloc, listExtraIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, listExtraIndexLocal);
        il.Emit(OpCodes.Br, listExtraLoop);
        il.MarkLabel(listExtraEnd);

        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);

        // Object case: use $IHasFields fields dictionary (standalone-safe)
        il.MarkLabel(objectLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, namesLocal);

        // System.Object Type → return the spec-known static names for the
        // JS Object constructor. ECMA-262 §20.1.2 lists prototype/name/length
        // and all the static methods. Mirrors the HasOwnProperty + gOPD names
        // lists below.
        var notObjectTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notObjectTypeLabel);
        var addToList = _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!;
        void AddName(string name)
        {
            il.Emit(OpCodes.Ldloc, namesLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Callvirt, addToList);
        }
        AddName("length");
        AddName("name");
        AddName("prototype");
        AddName("assign");
        AddName("create");
        AddName("defineProperties");
        AddName("defineProperty");
        AddName("entries");
        AddName("freeze");
        AddName("fromEntries");
        AddName("getOwnPropertyDescriptor");
        AddName("getOwnPropertyDescriptors");
        AddName("getOwnPropertyNames");
        AddName("getOwnPropertySymbols");
        AddName("getPrototypeOf");
        AddName("groupBy");
        AddName("hasOwn");
        AddName("is");
        AddName("isExtensible");
        AddName("isFrozen");
        AddName("isSealed");
        AddName("keys");
        AddName("preventExtensions");
        AddName("seal");
        AddName("setPrototypeOf");
        AddName("values");
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notObjectTypeLabel);

        // IList<object> Type → JS Array constructor own static names.
        var notArrayTypeForNamesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notArrayTypeForNamesLabel);
        AddName("length");
        AddName("name");
        AddName("prototype");
        AddName("from");
        AddName("fromAsync");
        AddName("isArray");
        AddName("of");
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArrayTypeForNamesLabel);

        // System.Double Type → JS Number constructor own static names.
        var notNumberTypeForNamesLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Bne_Un, notNumberTypeForNamesLabel);
        AddName("length");
        AddName("name");
        AddName("prototype");
        AddName("MAX_VALUE");
        AddName("MIN_VALUE");
        AddName("NaN");
        AddName("NEGATIVE_INFINITY");
        AddName("POSITIVE_INFINITY");
        AddName("MAX_SAFE_INTEGER");
        AddName("MIN_SAFE_INTEGER");
        AddName("EPSILON");
        AddName("isFinite");
        AddName("isInteger");
        AddName("isNaN");
        AddName("isSafeInteger");
        AddName("parseFloat");
        AddName("parseInt");
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumberTypeForNamesLabel);

        // RegExp instances are created with an own non-enumerable lastIndex
        // property before any user expando. Keep that intrinsic at the front
        // even when defineProperty later changes its attributes/value.
        if (_features.UsesRegExp)
        {
            var notRegExpForNamesLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpForNamesLabel);
            il.Emit(OpCodes.Ldloc, namesLocal);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
            il.MarkLabel(notRegExpForNamesLabel);
        }

        var noFieldsDictLabel = il.DefineLabel();
        var fieldsDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, noFieldsDictLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, noFieldsDictLabel);

        // Iterate fieldsDict.Keys
        var dictKeysEnumLocal = il.DeclareLocal(_types.IEnumeratorOfString);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.DictionaryStringObject, "Keys"));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerableOfString, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, dictKeysEnumLocal);

        var dictKeysLoopStart = il.DefineLabel();
        var dictKeysLoopEnd = il.DefineLabel();
        il.MarkLabel(dictKeysLoopStart);
        il.Emit(OpCodes.Ldloc, dictKeysEnumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictKeysLoopEnd);

        // var key = enumerator.Current
        var keyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, dictKeysEnumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfString, "Current"));
        il.Emit(OpCodes.Stloc, keyLocal);

        // Skip boxed-primitive markers (see dict case for rationale).
        var skipAddKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, skipAddKeyLabel);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, skipAddKeyLabel);

        // if (!names.Contains(key)) names.Add(key)
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Contains", _types.Object));
        il.Emit(OpCodes.Brtrue, skipAddKeyLabel);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(skipAddKeyLabel);
        il.Emit(OpCodes.Br, dictKeysLoopStart);

        il.MarkLabel(dictKeysLoopEnd);

        il.MarkLabel(noFieldsDictLabel);

        // Append PDS extras (accessor-only own props + non-enumerable own
        // props installed via Object.defineProperty). Mirrors the dict path
        // earlier. Receiver is arg0; pass the fieldsDict as the "already in"
        // set when present (null when not $IHasFields, so PDS-only props
        // surface).
        var objPdsExtraNamesLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetAllExtraKeys);
        il.Emit(OpCodes.Stloc, objPdsExtraNamesLocal);
        var objExtraIndexLocal = il.DeclareLocal(_types.Int32);
        var objExtraKeyLocal = il.DeclareLocal(_types.Object);
        var objExtraLoop = il.DefineLabel();
        var objExtraNext = il.DefineLabel();
        var objExtraEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, objExtraIndexLocal);
        il.MarkLabel(objExtraLoop);
        il.Emit(OpCodes.Ldloc, objExtraIndexLocal);
        il.Emit(OpCodes.Ldloc, objPdsExtraNamesLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")!);
        il.Emit(OpCodes.Bge, objExtraEnd);
        il.Emit(OpCodes.Ldloc, objPdsExtraNamesLocal);
        il.Emit(OpCodes.Ldloc, objExtraIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, objExtraKeyLocal);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, objExtraKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Contains", [_types.Object])!);
        il.Emit(OpCodes.Brtrue, objExtraNext);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, objExtraKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.MarkLabel(objExtraNext);
        il.Emit(OpCodes.Ldloc, objExtraIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, objExtraIndexLocal);
        il.Emit(OpCodes.Br, objExtraLoop);
        il.MarkLabel(objExtraEnd);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Call, runtime.NormalizeOwnPropertyKeys);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ConcatArrays: concatenates multiple iterables into a single $Array.
    /// Supports arrays, strings, and custom iterables with Symbol.iterator.
    /// Signature: <c>$Array ConcatArrays(object[] arrays, $TSSymbol iteratorSymbol, Type runtimeType)</c>.
    /// Stage E.2 M2: returns <c>$Array</c> (was <c>List&lt;object?&gt;</c>).
    /// </summary>
    private void EmitConcatArrays(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatArrays",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSArrayType,
            [_types.ObjectArray, runtime.TSSymbolType, _types.Type]  // Added iteratorSymbol and runtimeType
        );
        runtime.ConcatArrays = method;

        var il = method.GetILGenerator();
        // var result = new List<object>();
        // foreach (var element in arrays) result.AddRange(IterateToList(element, iteratorSymbol, runtimeType));
        // return result;
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var iteratedLocal = il.DeclareLocal(_types.ListOfObject);  // Result of IterateToList
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Call IterateToList(arrays[index], iteratorSymbol, runtimeType)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_1);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_2);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, iteratedLocal);

        // result.AddRange(iterated)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iteratedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Wrap the List<object?> in $Array on the way out.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ExpandCallArgs: expands function call arguments with spread support.
    /// Supports arrays, strings, and custom iterables with Symbol.iterator.
    /// Signature: object[] ExpandCallArgs(object[] args, bool[] isSpread, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitExpandCallArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandCallArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ObjectArray,
            [_types.ObjectArray, _types.BoolArray, runtime.TSSymbolType, _types.Type]  // Added iteratorSymbol and runtimeType
        );
        runtime.ExpandCallArgs = method;

        var il = method.GetILGenerator();
        // Create result list, iterate args, expand spreads using IterateToList
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var iteratedLocal = il.DeclareLocal(_types.ListOfObject);  // Result of IterateToList
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if this is a spread
        var notSpreadLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_I1);
        il.Emit(OpCodes.Brfalse, notSpreadLabel);

        // Is spread - use IterateToList to handle any iterable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_3);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, iteratedLocal);

        // result.AddRange(iterated)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iteratedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));
        il.Emit(OpCodes.Br, continueLabel);

        // Not spread - add single element
        il.MarkLabel(notSpreadLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray", _types.EmptyTypes));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Phase 1: Define $BoundArrayMethod type, fields, and constructor.
    /// Must be called before EmitRuntimeClass so GetListProperty can use the constructor.
    /// </summary>
    internal void EmitBoundArrayMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $BoundArrayMethod
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundArrayMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundArrayMethodType = typeBuilder;

        // Fields. Use Assembly visibility so GetProperty's callable-wrapper handler
        // can read `_methodName` to return the method name for `arr.push.name === 'push'`.
        var listField = typeBuilder.DefineField("_list", _types.ListOfObject, FieldAttributes.Assembly);
        var methodNameField = typeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Assembly);
        runtime.BoundArrayMethodListField = listField;
        runtime.BoundArrayMethodNameField = methodNameField;

        // Constructor: public $BoundArrayMethod(List<object> list, string methodName)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ListOfObject, _types.String]
        );
        runtime.BoundArrayMethodCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._list = list
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, listField);
        // this._methodName = methodName
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, methodNameField);
        ctorIL.Emit(OpCodes.Ret);

        // Define Invoke method signature in Phase 1 so EmitInvokeValue can reference it.
        // The IL body is emitted in Phase 2 (EmitBoundArrayMethodFinalize).
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.BoundArrayMethodInvoke = invokeBuilder;
    }

    /// <summary>
    /// Phase 2: Emit Invoke method body for $BoundArrayMethod and create the type.
    /// Must be called after EmitRuntimeClass so array methods are available.
    /// </summary>
    internal void EmitBoundArrayMethodFinalize(EmittedRuntime runtime)
    {
        var typeBuilder = runtime.BoundArrayMethodType;
        var listField = runtime.BoundArrayMethodListField;
        var methodNameField = runtime.BoundArrayMethodNameField;
        var invokeBuilder = runtime.BoundArrayMethodInvoke;

        var il = invokeBuilder.GetILGenerator();

        // Numeric-array deopt (number[] unboxing): _list may be a numeric-mode
        // $Array whose elements live unboxed in its double[] store, with an EMPTY
        // base List<object?>. Every array method below operates on that base list
        // directly, so a numeric receiver would read/mutate the wrong (empty)
        // storage and silently corrupt — this is the one dynamic-dispatch boundary
        // the static-call deopt sites don't cover (`(arr as any).push(x)` etc.).
        // EnsureBoxed materializes the unboxed store back into the base list and
        // self-guards (no-op unless actually numeric), so boxed arrays and plain
        // List<object?> receivers pay only a single isinst on this cold path.
        {
            var notNumeric = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            il.Emit(OpCodes.Isinst, runtime.TSArrayType);
            il.Emit(OpCodes.Brfalse, notNumeric);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayEnsureBoxed);
            il.MarkLabel(notNumeric);
        }

        // Switch on _methodName to dispatch to appropriate runtime method. Each case
        // must leave exactly one `object` value on the stack before branching to
        // endLabel. The fall-through path emits `ldnull` + `ret`.
        var endLabel = il.DefineLabel();

        // Box a single-value return to object based on the runtime method's return type.
        // Shared by all case helpers below.
        void EmitReturnBoxing(MethodBuilder runtimeMethod)
        {
            if (runtimeMethod.ReturnType == _types.Double)
                il.Emit(OpCodes.Box, _types.Double);
            else if (runtimeMethod.ReturnType == _types.Boolean)
                il.Emit(OpCodes.Box, _types.Boolean);
            else if (runtimeMethod.ReturnType == _types.Void)
                // ECMA-262: void-returning prototype methods (forEach) return
                // undefined, not null. Push $Undefined.Instance for spec-aligned
                // `arr.forEach(...) === undefined` strict-equality tests.
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            // Object/String/ListOfObject/etc. are already object-compatible.
        }

        // Load args[0] onto the stack, or null if args is empty.
        void EmitArgZeroOrNull()
        {
            var noArgsLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Brfalse, noArgsLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, doneLabel);
            il.MarkLabel(noArgsLabel);
            il.Emit(OpCodes.Ldnull);
            il.MarkLabel(doneLabel);
        }

        // Load args[0], or the JS undefined sentinel when omitted. Optional
        // callable arguments such as sort's compareFn must distinguish an
        // omitted argument from an explicit null value.
        void EmitArgZeroOrUndefined()
        {
            var noArgsLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Brfalse, noArgsLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, doneLabel);
            il.MarkLabel(noArgsLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.MarkLabel(doneLabel);
        }

        // Case: runtime.Method(_list) — no trailing args.
        void EmitNoArgCase(string methodName, MethodBuilder runtimeMethod)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // Case: runtime.Method(_list, args[0]) — a single-arg method that takes
        // one JS argument (includes element / map callback / join separator /
        // sort comparator / ...). The runtime helper's second param is a plain
        // `object`, not an `object[]`.
        void EmitSingleArgCase(string methodName, MethodBuilder runtimeMethod)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            EmitArgZeroOrNull();
            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        void EmitOptionalCallableCase(string methodName, MethodBuilder runtimeMethod)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            EmitArgZeroOrUndefined();
            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // Case: callback-taking method (map/filter/forEach/find/findIndex/some/every/flatMap/...).
        // The runtime helper takes (list, callback), but the spec also accepts an
        // optional thisArg as args[1] which must be plumbed via the
        // `_currentCallbackThisArg` thread-static — same mechanism the direct
        // ArrayEmitter path uses via EmitCallbackAndStashThisArg. Without this,
        // `Array.prototype.map.call(obj, cb, thisArg)` and (more importantly)
        // dynamic dispatch where the receiver type is unknown silently drop
        // thisArg and the callback's `this` defaults to undefined.
        void EmitCallbackCase(string methodName, MethodBuilder runtimeMethod)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            // Save prior thread-static value so nested forEach/map calls don't
            // see ours leak out.
            var savedThisArg = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.CurrentCallbackThisArgField);
            il.Emit(OpCodes.Stloc, savedThisArg);

            // Stash args[1] into _currentCallbackThisArg; default to $Undefined
            // when args.Length < 2 so strict-mode callbacks see `this===undefined`.
            var haveThisArgLabel = il.DefineLabel();
            var afterStashLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Bge, haveThisArgLabel);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Br, afterStashLabel);
            il.MarkLabel(haveThisArgLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.MarkLabel(afterStashLabel);
            il.Emit(OpCodes.Stsfld, runtime.CurrentCallbackThisArgField);

            // Wrap the call in try/finally so the thread-static is restored
            // even if the callback throws (Test262Error etc).
            il.BeginExceptionBlock();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            EmitArgZeroOrNull();
            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            // Result needs to leave the protected region; stash to local.
            var resultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldloc, savedThisArg);
            il.Emit(OpCodes.Stsfld, runtime.CurrentCallbackThisArgField);
            il.EndExceptionBlock();

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // Case: runtime.Method(_list, args[0], args[1]?) — indexOf/lastIndexOf.
        // The private ArrayHole singleton distinguishes an omitted fromIndex
        // from explicit JavaScript null/undefined.
        void EmitSearchCase(
            string methodName,
            MethodBuilder runtimeMethod,
            bool missingSearchIsUndefined = false)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            if (missingSearchIsUndefined)
            {
                var haveSearch = il.DefineLabel();
                var afterSearch = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Brtrue, haveSearch);
                il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
                il.Emit(OpCodes.Br, afterSearch);
                il.MarkLabel(haveSearch);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldelem_Ref);
                il.MarkLabel(afterSearch);
            }
            else
            {
                EmitArgZeroOrNull();
            }

            // args[1] if args.Length >= 2, else null
            var noSecond = il.DefineLabel();
            var afterSecond = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Blt, noSecond);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, afterSecond);
            il.MarkLabel(noSecond);
            if (methodName is "indexOf" or "lastIndexOf")
                il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
            else
                il.Emit(OpCodes.Ldnull);
            il.MarkLabel(afterSecond);

            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // Case: runtime.Method(_list, args) — forwards the whole object[] args
        // (for slice/reduce/reduceRight/splice which the runtime helper unpacks itself).
        void EmitArgsArrayCase(string methodName, MethodBuilder runtimeMethod)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtimeMethod);
            EmitReturnBoxing(runtimeMethod);

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // No-arg methods
        EmitNoArgCase("pop", runtime.ArrayPop);
        EmitNoArgCase("shift", runtime.ArrayShiftProto);
        EmitNoArgCase("reverse", runtime.ArrayReverse);
        EmitNoArgCase("toReversed", runtime.ArrayToReversed);
        EmitNoArgCase("entries", runtime.ArrayEntries);
        EmitNoArgCase("keys", runtime.ArrayKeys);
        EmitNoArgCase("values", runtime.ArrayValues);

        // JS-variadic methods forward the complete argument list so their
        // observable indexed writes and final length update stay atomic.
        EmitArgsArrayCase("push", runtime.ArrayPushProto);
        EmitArgsArrayCase("unshift", runtime.ArrayUnshiftProto);

        // Single-arg methods (runtime helper takes `object`, not `object[]`).
        // Aligns with Emitters/ArrayEmitter.cs which also uses the shared
        // EmitterArgumentHelpers.EmitBoxedArgumentOrNull for these methods, so
        // dynamic bound dispatch matches the direct-call path.
        // indexOf/lastIndexOf take searchElement + optional fromIndex.
        EmitSearchCase("indexOf", runtime.ArrayIndexOf, missingSearchIsUndefined: true);
        EmitSearchCase("lastIndexOf", runtime.ArrayLastIndexOf, missingSearchIsUndefined: true);
        EmitSearchCase("includes", runtime.ArrayIncludes, missingSearchIsUndefined: true);
        EmitArgsArrayCase("concat", runtime.ArrayConcat);
        EmitSingleArgCase("join", runtime.ArrayJoin);
        // Callback methods accept (callback, thisArg). thisArg is plumbed via
        // the `_currentCallbackThisArg` thread-static; see EmitCallbackCase.
        EmitCallbackCase("map", runtime.ArrayMap);
        EmitCallbackCase("filter", runtime.ArrayFilter);
        EmitCallbackCase("forEach", runtime.ArrayForEach);
        EmitCallbackCase("find", runtime.ArrayFind);
        EmitCallbackCase("findIndex", runtime.ArrayFindIndex);
        EmitCallbackCase("findLast", runtime.ArrayFindLast);
        EmitCallbackCase("findLastIndex", runtime.ArrayFindLastIndex);
        EmitCallbackCase("some", runtime.ArraySome);
        EmitCallbackCase("every", runtime.ArrayEvery);
        EmitOptionalCallableCase("sort", runtime.ArraySort);
        EmitOptionalCallableCase("toSorted", runtime.ArrayToSorted);
        EmitSingleArgCase("flat", runtime.ArrayFlat);
        EmitCallbackCase("flatMap", runtime.ArrayFlatMap);
        EmitSingleArgCase("at", runtime.ArrayAt);

        // object[]-args methods (runtime helper takes the whole object[] args).
        EmitArgsArrayCase("slice", runtime.ArraySlice);
        EmitArgsArrayCase("reduce", runtime.ArrayReduce);
        EmitArgsArrayCase("reduceRight", runtime.ArrayReduceRight);
        EmitArgsArrayCase("splice", runtime.ArraySplice);
        EmitArgsArrayCase("toSpliced", runtime.ArrayToSpliced);
        EmitArgsArrayCase("with", runtime.ArrayWith);
        EmitArgsArrayCase("fill", runtime.ArrayFill);
        EmitArgsArrayCase("copyWithin", runtime.ArrayCopyWithin);

        // toString / toLocaleString — call ArrayProtoToStringHelper(__this).
        // Helper takes the receiver as `__this`-named param and internally
        // materializes + joins. We pass the bound list directly (already a
        // List<object>) since it satisfies the materializer's pass-through.
        void EmitToStringCase(string methodName)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            var liveMethodLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, liveMethodLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, listField);
            il.Emit(OpCodes.Ldloc, liveMethodLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(skipLabel);
        }
        EmitToStringCase("toString");
        EmitToStringCase("toLocaleString");

        // Default: return null
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        // Create the type
        typeBuilder.CreateType();
    }
}

