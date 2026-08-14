using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Object.groupBy(iterable, callback) - groups array elements by callback return value (coerced to string).
    /// Returns a $Object whose keys are group names and values are $Array of elements.
    /// </summary>
    private void EmitObjectGroupBy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ObjectGroupBy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]  // (iterable, callback)
        );
        runtime.ObjectGroupBy = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.7 step 2: If IsCallable(callbackfn) is false,
        // throw TypeError. Pre-fix Object.groupBy([], non-callable) silently
        // did nothing and returned an empty object.
        var gbCallableOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, gbCallableOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.groupBy: callback is not callable");
        il.MarkLabel(gbCallableOkLabel);

        // Locals
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);     // groups dict
        var listLocal = il.DeclareLocal(_types.ListOfObject);               // array elements
        var indexLocal = il.DeclareLocal(_types.Int32);                     // loop index
        var argsLocal = il.DeclareLocal(_types.ObjectArray);                // callback args
        var keyLocal = il.DeclareLocal(_types.Object);                      // callback result
        var keyStrLocal = il.DeclareLocal(_types.String);                   // key as string
        var existingLocal = il.DeclareLocal(_types.Object);                 // existing list

        // Labels
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var hasExistingLabel = il.DefineLabel();
        var addElementLabel = il.DefineLabel();
        var keyNotNullLabel = il.DefineLabel();

        // dict = new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // GroupBy requires a JavaScript iterable. The general materializer has
        // a CLR-IEnumerable compatibility fallback for host APIs; reject an
        // ordinary non-iterable object here so a nullish/absent @@iterator
        // produces the guest TypeError mandated by GetIterator.
        var groupByMaterializeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, groupByMaterializeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, groupByMaterializeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, groupByMaterializeLabel);
        var groupByIteratorLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Stloc, groupByIteratorLocal);
        var groupByHasIteratorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, groupByIteratorLocal);
        il.Emit(OpCodes.Brfalse, groupByHasIteratorLabel);
        il.Emit(OpCodes.Ldloc, groupByIteratorLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, groupByMaterializeLabel);
        il.MarkLabel(groupByHasIteratorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Object.groupBy: items is not iterable");
        il.MarkLabel(groupByMaterializeLabel);

        // Materialize through the shared iterator protocol. Besides arrays,
        // this handles custom iterables and produces the spec TypeError for a
        // nullish/non-callable Symbol.iterator instead of a CLR cast error.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle")!);
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, listLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop start
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // args = new object[] { list[i], (double)i }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);
        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // key = InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1);  // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, keyLocal);

        // keyStr = key?.ToString() ?? "undefined"
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brtrue, keyNotNullLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, addElementLabel);
        il.MarkLabel(keyNotNullLabel);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(addElementLabel);
        il.Emit(OpCodes.Stloc, keyStrLocal);

        // if (dict.TryGetValue(keyStr, out existing))
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Ldloca, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brtrue, hasExistingLabel);

        // Create new list, wrap in $Array, store in dict
        // existing = new $Array(new List<object?>())
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, existingLocal);
        // dict[keyStr] = existing
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.MarkLabel(hasExistingLabel);

        // Get elements from existing $Array and add current element
        // ((existing as $Array).Elements).Add(list[i])
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Return an ordinary object with a null prototype (§20.1.2.7 step 2).
        // A plain dictionary is already the compiler's ordinary-object
        // carrier and, unlike $Object, does not have a baked-in prototype
        // fallback that can mask the explicit null PDS entry.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Map.groupBy(iterable, callback) - groups array elements by callback return value.
    /// Returns a Dictionary&lt;object, object?&gt; (compiled Map) whose keys are callback results and values are $Array.
    /// </summary>
    private void EmitMapGroupBy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapGroupBy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]  // (iterable, callback)
        );
        runtime.MapGroupBy = method;

        var il = method.GetILGenerator();

        // Locals
        var mapLocal = il.DeclareLocal(_types.Object);                      // result map
        var listLocal = il.DeclareLocal(_types.ListOfObject);               // array elements
        var indexLocal = il.DeclareLocal(_types.Int32);                     // loop index
        var argsLocal = il.DeclareLocal(_types.ObjectArray);                // callback args
        var keyLocal = il.DeclareLocal(_types.Object);                      // callback result (key)
        var existingLocal = il.DeclareLocal(_types.Object);                 // existing $Array

        // Labels
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var hasExistingLabel = il.DefineLabel();
        var addElementLabel = il.DefineLabel();

        var isTSArrayLabel = il.DefineLabel();
        var afterUnwrapLabel = il.DefineLabel();

        // map = CreateMap() — creates Dictionary<object, object?> with ReferenceEqualityComparer
        il.Emit(OpCodes.Call, runtime.CreateMap);
        il.Emit(OpCodes.Stloc, mapLocal);

        // list = unwrap iterable (handle both List<object?> and $Array)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, isTSArrayLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Br, afterUnwrapLabel);

        il.MarkLabel(isTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Stloc, listLocal);

        il.MarkLabel(afterUnwrapLabel);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop start
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // args = new object[] { list[i], (double)i }
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // key = InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Use MapHas(map, key) to check if key exists
        // MapHas returns object (boxed bool) — check with unbox
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.MapHas);
        il.Emit(OpCodes.Brtrue, hasExistingLabel);

        // Key doesn't exist: create new $Array, store in map
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Stloc, existingLocal);
        // MapSet(map, key, existing)
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Call, runtime.MapSet);
        il.Emit(OpCodes.Pop);  // discard returned map
        il.Emit(OpCodes.Br, addElementLabel);

        // Key exists: get existing $Array
        il.MarkLabel(hasExistingLabel);
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.MapGet);
        il.Emit(OpCodes.Stloc, existingLocal);

        // Add current element to existing $Array
        il.MarkLabel(addElementLabel);
        il.Emit(OpCodes.Ldloc, existingLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // return map
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ret);
    }
}
