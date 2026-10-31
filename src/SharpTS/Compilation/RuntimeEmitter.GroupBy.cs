using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct ObjectGroupByInputs(
        EmittedArrayStorageRuntime ArrayStorage,
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedErrorRuntime Errors,
        MethodBuilder InvokeValue,
        CollectionConsumerInputs Iteration,
        TypeBuilder TSFunctionType,
        Type FunctionPrototypeType
    );

    private readonly record struct MapGroupByInputs(
        EmittedArrayStorageRuntime ArrayStorage,
        EmittedErrorRuntime Errors,
        MethodBuilder InvokeValue,
        MethodBuilder TypeOf,
        CollectionConsumerInputs Iteration
    );

    /// <summary>
    /// Emits Object.groupBy(iterable, callback) - groups array elements by callback return value (coerced to string).
    /// Returns a $Object whose keys are group names and values are $Array of elements.
    /// </summary>
    private void EmitObjectGroupBy(
        TypeBuilder typeBuilder,
        EmittedObjectOperationsRuntime objectOperations,
        ObjectGroupByInputs inputs
    )
    {
        var method = typeBuilder.DefineMethod(
            "ObjectGroupBy",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]  // (iterable, callback)
        );
        objectOperations.GroupBy = method;

        var il = method.GetILGenerator();

        // ECMA-262 §20.1.2.7 step 2: If IsCallable(callbackfn) is false,
        // throw TypeError. Pre-fix Object.groupBy([], non-callable) silently
        // did nothing and returned an empty object.
        var gbCallableOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, inputs.FunctionPrototypeType);
        il.Emit(OpCodes.Brtrue, gbCallableOkLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, inputs.TSFunctionType);
        il.Emit(OpCodes.Brtrue, gbCallableOkLabel);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor, "Object.groupBy: callback is not callable");
        il.MarkLabel(gbCallableOkLabel);

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var keyLocal = il.DeclareLocal(_types.Object);
        var keyStrLocal = il.DeclareLocal(_types.String);
        var existingLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        EmitCollectionConsumerLoop(il, inputs.Iteration,
            () => il.Emit(OpCodes.Ldarg_0), (valueLocal, indexLocal) =>
            {
                var hasExistingLabel = il.DefineLabel();
                var addElementLabel = il.DefineLabel();
                var keyNotNullLabel = il.DefineLabel();
                // args = new object[] { current value, (double)i }
                il.Emit(OpCodes.Ldc_I4_2);
                il.Emit(OpCodes.Newarr, _types.Object);
                // args[0] = current value
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ldloc, valueLocal);
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
                il.Emit(OpCodes.Call, inputs.InvokeValue);
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
                il.Emit(OpCodes.Newobj, inputs.ArrayStorage.Ctor);
                il.Emit(OpCodes.Stloc, existingLocal);
                // dict[keyStr] = existing
                il.Emit(OpCodes.Ldloc, dictLocal);
                il.Emit(OpCodes.Ldloc, keyStrLocal);
                il.Emit(OpCodes.Ldloc, existingLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

                il.MarkLabel(hasExistingLabel);

                // Get elements from existing $Array and add current element
                // ((existing as $Array).Elements).Add(current value)
                il.Emit(OpCodes.Ldloc, existingLocal);
                il.Emit(OpCodes.Castclass, inputs.ArrayStorage.Type);
                il.Emit(OpCodes.Callvirt, inputs.ArrayStorage.ElementsGetter);
                il.Emit(OpCodes.Ldloc, valueLocal);
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            });

        // Return an ordinary object with a null prototype (§20.1.2.7 step 2).
        // A plain dictionary is already the compiler's ordinary-object
        // carrier and, unlike $Object, does not have a baked-in prototype
        // fallback that can mask the explicit null PDS entry.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Map.groupBy(iterable, callback) - groups array elements by callback return value.
    /// Returns a Dictionary&lt;object, object?&gt; (compiled Map) whose keys are callback results and values are $Array.
    /// </summary>
    private void EmitMapGroupBy(TypeBuilder typeBuilder, EmittedMapRuntime map, MapGroupByInputs inputs)
    {
        var method = typeBuilder.DefineMethod("MapGroupBy", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        map.GroupBy = method;
        var il = method.GetILGenerator();
        var callable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, inputs.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, callable);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor,
            "Map.groupBy: callback is not callable");
        il.MarkLabel(callable);

        var result = il.DeclareLocal(_types.Object);
        var key = il.DeclareLocal(_types.Object);
        var group = il.DeclareLocal(inputs.ArrayStorage.Type);
        il.Emit(OpCodes.Call, map.Create);
        il.Emit(OpCodes.Stloc, result);
        EmitCollectionConsumerLoop(il, inputs.Iteration, () => il.Emit(OpCodes.Ldarg_0), (value, index) =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, inputs.InvokeValue);
            il.Emit(OpCodes.Stloc, key);
            var existing = il.DefineLabel();
            var append = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Call, map.Has);
            il.Emit(OpCodes.Brtrue, existing);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes));
            il.Emit(OpCodes.Newobj, inputs.ArrayStorage.Ctor);
            il.Emit(OpCodes.Stloc, group);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Ldloc, group);
            il.Emit(OpCodes.Call, map.Set);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, append);
            il.MarkLabel(existing);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldloc, key);
            il.Emit(OpCodes.Call, map.Get);
            il.Emit(OpCodes.Castclass, inputs.ArrayStorage.Type);
            il.Emit(OpCodes.Stloc, group);
            il.MarkLabel(append);
            il.Emit(OpCodes.Ldloc, group);
            il.Emit(OpCodes.Callvirt, inputs.ArrayStorage.ElementsGetter);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        });
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }
}
