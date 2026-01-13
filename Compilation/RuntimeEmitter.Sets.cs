using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitSetMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateSet(typeBuilder, runtime);
        EmitCreateSetFromArray(typeBuilder, runtime);
        EmitSetSize(typeBuilder, runtime);
        EmitSetAdd(typeBuilder, runtime);
        EmitSetHas(typeBuilder, runtime);
        EmitSetDelete(typeBuilder, runtime);
        EmitSetClear(typeBuilder, runtime);
        EmitSetKeys(typeBuilder, runtime);
        EmitSetValues(typeBuilder, runtime);
        EmitSetEntries(typeBuilder, runtime);
        EmitSetForEach(typeBuilder, runtime);

        // ES2025 Set Operations
        EmitSetUnion(typeBuilder, runtime);
        EmitSetIntersection(typeBuilder, runtime);
        EmitSetDifference(typeBuilder, runtime);
        EmitSetSymmetricDifference(typeBuilder, runtime);
        EmitSetIsSubsetOf(typeBuilder, runtime);
        EmitSetIsSupersetOf(typeBuilder, runtime);
        EmitSetIsDisjointFrom(typeBuilder, runtime);
    }

    private void EmitCreateSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateSet = method;

        var il = method.GetILGenerator();

        // new HashSet<object>($ReferenceEqualityComparer.Instance)
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;

        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateSetFromArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateSetFromArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateSetFromArray = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;

        // Local variables
        var setLocal = il.DeclareLocal(setType);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);

        // Labels
        var returnSetLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

        // var set = new HashSet<object>($ReferenceEqualityComparer.Instance)
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, setLocal);

        // if (values is not List<object?> list) return set;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brfalse, returnSetLabel);

        // int index = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: while (index < list.Count)
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // var item = list[index];
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, itemLocal);

        // if (item == null) continue;
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // set.Add(item);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop); // Discard bool return value

        // index++; goto loopStart;
        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // return set;
        il.MarkLabel(returnSetLabel);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetSize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.SetSize = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;

        var notSetLabel = il.DefineLabel();

        // if (set is HashSet<object> hashSet)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Brfalse, notSetLabel);

        // return (double)hashSet.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, setType);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(setType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        // return 0;
        il.MarkLabel(notSetLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetAdd",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.SetAdd = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;

        var returnSetLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return set;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Brfalse, returnSetLabel);

        // if (value == null) return set;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnSetLabel);

        // hashSet.Add(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, setType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop); // Discard bool return value

        // return set;
        il.MarkLabel(returnSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.SetHas = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;

        var returnFalseLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (value == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // return hashSet.Contains(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, setType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.SetDelete = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;

        var returnFalseLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (value == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // return hashSet.Remove(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, setType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Remove")!);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetClear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetClear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.SetClear = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;

        var endLabel = il.DefineLabel();

        // if (set is HashSet<object> hashSet)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Brfalse, endLabel);

        // hashSet.Clear();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, setType);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Clear")!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.SetKeys = method;

        // SetKeys and SetValues are identical for Sets - both return the values
        EmitSetIteratorBody(method.GetILGenerator(), runtime, addValueTwice: false);
    }

    private void EmitSetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.SetValues = method;

        // SetKeys and SetValues are identical for Sets - both return the values
        EmitSetIteratorBody(method.GetILGenerator(), runtime, addValueTwice: false);
    }

    /// <summary>
    /// Emits the body for SetKeys/SetValues (simple iteration).
    /// </summary>
    private void EmitSetIteratorBody(ILGenerator il, EmittedRuntime runtime, bool addValueTwice)
    {
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var setLocal = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return new List<object?>();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, setLocal);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = hashSet.GetEnumerator();
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // result.Add(current);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.SetEntries = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var setLocal = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);
        var pairLocal = il.DeclareLocal(_types.ListOfObject);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return new List<object?>();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, setLocal);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = hashSet.GetEnumerator();
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // var pair = new List<object?> { current, current }; (for Set entries, both are the value)
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pairLocal);

        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        // result.Add(pair);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.SetForEach = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var setLocal = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        var endLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (set is not HashSet<object> hashSet) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, setLocal);
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // if (callback == null) return;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, endLabel);

        // var enumerator = hashSet.GetEnumerator();
        il.Emit(OpCodes.Ldloc, setLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // InvokeValue(callback, [current, current, set]); - Per JS spec, callback receives (value, value, set)
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = current (value)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = current (value again, for compatibility with Map.forEach)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = set
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop); // Discard return value

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    #region ES2025 Set Operations

    private void EmitSetUnion(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetUnion",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.SetUnion = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(setType);
        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        var skipSet1Label = il.DefineLabel();
        var loop1EndLabel = il.DefineLabel();
        var skipSet2Label = il.DefineLabel();
        var loop2EndLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // var result = new HashSet<object>($ReferenceEqualityComparer.Instance);
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (set1 is HashSet<object> hashSet1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, skipSet1Label);

        // foreach (var value in hashSet1) result.Add(value);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        EmitSetUnionLoop(il, resultLocal, enumeratorLocal, enumeratorType, setType, loop1EndLabel);

        il.MarkLabel(skipSet1Label);

        // if (set2 is HashSet<object> hashSet2)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, skipSet2Label);

        // foreach (var value in hashSet2) result.Add(value);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);
        EmitSetUnionLoop(il, resultLocal, enumeratorLocal, enumeratorType, setType, loop2EndLabel);

        il.MarkLabel(skipSet2Label);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetUnionLoop(ILGenerator il, LocalBuilder resultLocal, LocalBuilder enumeratorLocal,
        Type enumeratorType, Type setType, Label loopEndLabel)
    {
        var loopStartLabel = il.DefineLabel();

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // result.Add(enumerator.Current);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
    }

    private void EmitSetIntersection(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIntersection",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.SetIntersection = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(setType);
        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var skipAddLabel = il.DefineLabel();

        // var result = new HashSet<object>($ReferenceEqualityComparer.Instance);
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (set1 is not HashSet<object> hashSet1) return result;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // if (set2 is not HashSet<object> hashSet2) return result;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // foreach (var value in hashSet1)
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (hashSet2.Contains(value)) result.Add(value);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brfalse, skipAddLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipAddLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetDifference(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetDifference",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.SetDifference = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(setType);
        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var skipAddLabel = il.DefineLabel();
        var set2NullLabel = il.DefineLabel();

        // var result = new HashSet<object>($ReferenceEqualityComparer.Instance);
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (set1 is not HashSet<object> hashSet1) return result;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var hashSet2 = set2 as HashSet<object>; (may be null)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);

        // foreach (var value in hashSet1)
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (hashSet2 == null || !hashSet2.Contains(value)) result.Add(value);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, set2NullLabel); // set2 is null, so add the value

        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, skipAddLabel); // set2 contains value, skip

        il.MarkLabel(set2NullLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipAddLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetSymmetricDifference(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetSymmetricDifference",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.SetSymmetricDifference = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var ctorWithComparer = setType.GetConstructor([_types.IEqualityComparerOfObject])!;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var resultLocal = il.DeclareLocal(setType);
        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var skipSet1Label = il.DefineLabel();
        var loop1StartLabel = il.DefineLabel();
        var loop1EndLabel = il.DefineLabel();
        var add1Label = il.DefineLabel();
        var skip1AddLabel = il.DefineLabel();
        var skipSet2Label = il.DefineLabel();
        var loop2StartLabel = il.DefineLabel();
        var loop2EndLabel = il.DefineLabel();
        var add2Label = il.DefineLabel();
        var skip2AddLabel = il.DefineLabel();

        // var result = new HashSet<object>($ReferenceEqualityComparer.Instance);
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var hashSet1 = set1 as HashSet<object>;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);

        // var hashSet2 = set2 as HashSet<object>;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);

        // if (hashSet1 != null) { iterate hashSet1 and add values not in hashSet2 }
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, skipSet1Label);

        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loop1StartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loop1EndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (hashSet2 == null || !hashSet2.Contains(value)) result.Add(value);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, add1Label);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, skip1AddLabel);

        il.MarkLabel(add1Label);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skip1AddLabel);
        il.Emit(OpCodes.Br, loop1StartLabel);

        il.MarkLabel(loop1EndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(skipSet1Label);

        // if (hashSet2 != null) { iterate hashSet2 and add values not in hashSet1 }
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, skipSet2Label);

        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loop2StartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loop2EndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (hashSet1 == null || !hashSet1.Contains(value)) result.Add(value);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, add2Label);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, skip2AddLabel);

        il.MarkLabel(add2Label);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skip2AddLabel);
        il.Emit(OpCodes.Br, loop2StartLabel);

        il.MarkLabel(loop2EndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(skipSet2Label);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetIsSubsetOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIsSubsetOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.SetIsSubsetOf = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var returnTrueLabel = il.DefineLabel();
        var checkSet1EmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // if (set1 is not HashSet<object> hashSet1) return true; // empty set is subset of everything
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // if (set2 is not HashSet<object> hashSet2) return hashSet1.Count == 0;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, checkSet1EmptyLabel);

        // foreach (var value in hashSet1)
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!hashSet2.Contains(value)) return false;
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return true;
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // return hashSet1.Count == 0;
        il.MarkLabel(checkSet1EmptyLabel);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(setType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetIsSupersetOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIsSupersetOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.SetIsSupersetOf = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);

        var returnTrueLabel = il.DefineLabel();
        var checkSet2EmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // if (set2 is not HashSet<object> hashSet2) return true; // everything is superset of empty set
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // if (set1 is not HashSet<object> hashSet1) return hashSet2.Count == 0;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, checkSet2EmptyLabel);

        // foreach (var value in hashSet2)
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!hashSet1.Contains(value)) return false;
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return true;
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // return hashSet2.Count == 0;
        il.MarkLabel(checkSet2EmptyLabel);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(setType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetIsDisjointFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIsDisjointFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.SetIsDisjointFrom = method;

        var il = method.GetILGenerator();
        var setType = _types.HashSetOfObject;
        var enumeratorType = _types.MakeGenericType(typeof(HashSet<>.Enumerator).GetGenericTypeDefinition(), _types.Object);

        var set1Local = il.DeclareLocal(setType);
        var set2Local = il.DeclareLocal(setType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(_types.Object);
        var smallerLocal = il.DeclareLocal(setType);
        var largerLocal = il.DeclareLocal(setType);

        var returnTrueLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var set1SmallerLabel = il.DefineLabel();
        var startIterationLabel = il.DefineLabel();

        // if (set1 is not HashSet<object> hashSet1) return true; // empty sets are disjoint from everything
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set1Local);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // if (set2 is not HashSet<object> hashSet2) return true;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, setType);
        il.Emit(OpCodes.Stloc, set2Local);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // Iterate over the smaller set for efficiency
        // if (hashSet1.Count <= hashSet2.Count) { smaller = set1, larger = set2 } else { smaller = set2, larger = set1 }
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(setType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(setType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ble, set1SmallerLabel);

        // set2 is smaller
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Stloc, smallerLocal);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Stloc, largerLocal);
        il.Emit(OpCodes.Br, startIterationLabel);

        // set1 is smaller
        il.MarkLabel(set1SmallerLabel);
        il.Emit(OpCodes.Ldloc, set1Local);
        il.Emit(OpCodes.Stloc, smallerLocal);
        il.Emit(OpCodes.Ldloc, set2Local);
        il.Emit(OpCodes.Stloc, largerLocal);

        il.MarkLabel(startIterationLabel);

        // foreach (var value in smaller)
        il.Emit(OpCodes.Ldloc, smallerLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (larger.Contains(value)) return false;
        il.Emit(OpCodes.Ldloc, largerLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Callvirt, setType.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return true;
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
