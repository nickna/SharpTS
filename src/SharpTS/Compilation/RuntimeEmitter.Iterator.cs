using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits iterator protocol support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the live iterator shared by Array.prototype entries/keys/values.
    /// Its MoveNext reads Count and the current indexed value on every call,
    /// so mutations after iterator creation remain observable.
    /// </summary>
    private void EmitArrayIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$ArrayIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]);
        runtime.ArrayIteratorType = typeBuilder;

        var arrayField = typeBuilder.DefineField(
            "_array", _types.ListOfObject, FieldAttributes.Private | FieldAttributes.InitOnly);
        var kindField = typeBuilder.DefineField(
            "_kind", _types.Int32, FieldAttributes.Private | FieldAttributes.InitOnly);
        var indexField = typeBuilder.DefineField(
            "_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField(
            "_current", _types.Object, FieldAttributes.Private);
        var completedField = typeBuilder.DefineField(
            "_completed", _types.Boolean, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ListOfObject, _types.Int32]);
        runtime.ArrayIteratorCtor = ctor;
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, arrayField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, kindField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_M1);
        ctorIl.Emit(OpCodes.Stfld, indexField);
        ctorIl.Emit(OpCodes.Ret);

        var currentProperty = typeBuilder.DefineProperty(
            "Current", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var currentGetter = typeBuilder.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual
                | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);
        var currentIl = currentGetter.GetILGenerator();
        currentIl.Emit(OpCodes.Ldarg_0);
        currentIl.Emit(OpCodes.Ldfld, currentField);
        currentIl.Emit(OpCodes.Ret);
        currentProperty.SetGetMethod(currentGetter);

        var nongenericCurrent = typeBuilder.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual
                | MethodAttributes.SpecialName | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object,
            Type.EmptyTypes);
        var nongenericCurrentIl = nongenericCurrent.GetILGenerator();
        nongenericCurrentIl.Emit(OpCodes.Ldarg_0);
        nongenericCurrentIl.Emit(OpCodes.Ldfld, currentField);
        nongenericCurrentIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            nongenericCurrent,
            _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);

        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var nextIndex = il.DeclareLocal(_types.Int32);
        var value = il.DeclareLocal(_types.Object);
        var haveElement = il.DefineLabel();
        var emitValue = il.DefineLabel();
        var emitEntry = il.DefineLabel();
        var storeCurrent = il.DefineLabel();
        var useListCount = il.DefineLabel();
        var haveLiveLength = il.DefineLabel();
        var liveLength = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, completedField);
        il.Emit(OpCodes.Brfalse, haveElement);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(haveElement);
        var indexInRange = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, nextIndex);
        // Array iterators observe length on every next() call. $Arguments has
        // an independent JS-visible length slot that may be truncated without
        // shrinking its List backing store.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrayField);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, useListCount);
        il.Emit(OpCodes.Ldfld, runtime.ArgumentsLengthField);
        il.Emit(OpCodes.Stloc, liveLength);
        il.Emit(OpCodes.Br, haveLiveLength);
        il.MarkLabel(useListCount);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrayField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(
            _types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, liveLength);
        il.MarkLabel(haveLiveLength);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Ldloc, liveLength);
        il.Emit(OpCodes.Blt, indexInRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, completedField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(indexInRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Stfld, indexField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, kindField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, emitValue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, kindField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, emitEntry);

        // keys(): current = index
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, storeCurrent);

        // values(): current = Get(array, index)
        il.MarkLabel(emitValue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrayField);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Br, storeCurrent);

        // entries(): current = [index, Get(array, index)]
        il.MarkLabel(emitEntry);
        var pair = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(
            _types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pair);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, arrayField);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Stloc, value);

        il.MarkLabel(storeCurrent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stfld, currentField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        var reset = typeBuilder.DefineMethod(
            "Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            Type.EmptyTypes);
        var resetIl = reset.GetILGenerator();
        resetIl.Emit(OpCodes.Ldstr, "Reset is not supported for array iterators");
        resetIl.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        resetIl.Emit(OpCodes.Throw);

        var dispose = typeBuilder.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            Type.EmptyTypes);
        dispose.GetILGenerator().Emit(OpCodes.Ret);

        // JavaScript iterator objects are iterable themselves. The CLR
        // interfaces keep yield* and generic for-of delegation compatible.
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a Map iterator backed by the original dictionary plus its initial
    /// ordered key list. Each MoveNext re-reads the key from the live map, so
    /// entries deleted or cleared after iterator creation are skipped instead
    /// of leaking snapshot values. Kind: 0=keys, 1=values, 2=entries.
    /// </summary>
    private void EmitMapCollectionIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$MapCollectionIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]);
        runtime.MapCollectionIteratorType = typeBuilder;

        var mapField = typeBuilder.DefineField("_map", _types.DictionaryObjectObject,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var keysField = typeBuilder.DefineField("_keys", _types.ListOfObject,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var kindField = typeBuilder.DefineField("_kind", _types.Int32,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var indexField = typeBuilder.DefineField("_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);
        var completedField = typeBuilder.DefineField("_completed", _types.Boolean, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.DictionaryObjectObject, _types.ListOfObject, _types.Int32]);
        runtime.MapCollectionIteratorCtor = ctor;
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, mapField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, keysField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_3);
        ctorIl.Emit(OpCodes.Stfld, kindField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_M1);
        ctorIl.Emit(OpCodes.Stfld, indexField);
        ctorIl.Emit(OpCodes.Ret);

        var currentProperty = typeBuilder.DefineProperty(
            "Current", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var currentGetter = typeBuilder.DefineMethod("get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var currentIl = currentGetter.GetILGenerator();
        currentIl.Emit(OpCodes.Ldarg_0);
        currentIl.Emit(OpCodes.Ldfld, currentField);
        currentIl.Emit(OpCodes.Ret);
        currentProperty.SetGetMethod(currentGetter);

        var nongenericCurrent = typeBuilder.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object, Type.EmptyTypes);
        var nongenericIl = nongenericCurrent.GetILGenerator();
        nongenericIl.Emit(OpCodes.Ldarg_0);
        nongenericIl.Emit(OpCodes.Ldfld, currentField);
        nongenericIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(nongenericCurrent,
            _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);

        var moveNext = typeBuilder.DefineMethod("MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var nextIndex = il.DeclareLocal(_types.Int32);
        var key = il.DeclareLocal(_types.Object);
        var value = il.DeclareLocal(_types.Object);
        var output = il.DeclareLocal(_types.Object);
        var pair = il.DeclareLocal(_types.ListOfObject);
        var loop = il.DefineLabel();
        var inRange = il.DefineLabel();
        var emitValue = il.DefineLabel();
        var emitEntry = il.DefineLabel();
        var denormalized = il.DefineLabel();
        var storeCurrent = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, completedField);
        il.Emit(OpCodes.Brfalse, loop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, nextIndex);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, keysField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Blt, inRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, completedField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(inRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Stfld, indexField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, keysField);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, mapField);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue")!);
        il.Emit(OpCodes.Brfalse, loop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, kindField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, emitValue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, kindField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, emitEntry);

        // keys(): denormalize the internal null sentinel.
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldsfld, runtime.MapNullSentinel);
        var keyNotNullSentinel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, keyNotNullSentinel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, denormalized);
        il.MarkLabel(keyNotNullSentinel);
        il.Emit(OpCodes.Ldloc, key);
        il.MarkLabel(denormalized);
        il.Emit(OpCodes.Stloc, output);
        il.Emit(OpCodes.Br, storeCurrent);

        il.MarkLabel(emitValue);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stloc, output);
        il.Emit(OpCodes.Br, storeCurrent);

        il.MarkLabel(emitEntry);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pair);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldsfld, runtime.MapNullSentinel);
        var entryKeyNotNull = il.DefineLabel();
        var entryKeyReady = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, entryKeyNotNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, entryKeyReady);
        il.MarkLabel(entryKeyNotNull);
        il.Emit(OpCodes.Ldloc, key);
        il.MarkLabel(entryKeyReady);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Ldloc, pair);
        il.Emit(OpCodes.Stloc, output);

        il.MarkLabel(storeCurrent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, output);
        il.Emit(OpCodes.Stfld, currentField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        var reset = typeBuilder.DefineMethod("Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        var resetIl = reset.GetILGenerator();
        resetIl.Emit(OpCodes.Ldstr, "Reset is not supported for map iterators");
        resetIl.Emit(OpCodes.Newobj,
            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        resetIl.Emit(OpCodes.Throw);

        var dispose = typeBuilder.DefineMethod("Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        dispose.GetILGenerator().Emit(OpCodes.Ret);

        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a Set value iterator backed by the live set plus its initial
    /// insertion-order snapshot. Values deleted before visitation are skipped.
    /// </summary>
    private void EmitSetCollectionIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$SetCollectionIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]);
        runtime.SetCollectionIteratorType = typeBuilder;

        var setField = typeBuilder.DefineField("_set", _types.HashSetOfObject,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var valuesField = typeBuilder.DefineField("_values", _types.ListOfObject,
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var indexField = typeBuilder.DefineField("_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);
        var completedField = typeBuilder.DefineField("_completed", _types.Boolean, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, [_types.HashSetOfObject, _types.ListOfObject]);
        runtime.SetCollectionIteratorCtor = ctor;
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, setField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, valuesField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_M1);
        ctorIl.Emit(OpCodes.Stfld, indexField);
        ctorIl.Emit(OpCodes.Ret);

        var currentProperty = typeBuilder.DefineProperty(
            "Current", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var currentGetter = typeBuilder.DefineMethod("get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var currentIl = currentGetter.GetILGenerator();
        currentIl.Emit(OpCodes.Ldarg_0);
        currentIl.Emit(OpCodes.Ldfld, currentField);
        currentIl.Emit(OpCodes.Ret);
        currentProperty.SetGetMethod(currentGetter);

        var nongenericCurrent = typeBuilder.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object, Type.EmptyTypes);
        var nongenericIl = nongenericCurrent.GetILGenerator();
        nongenericIl.Emit(OpCodes.Ldarg_0);
        nongenericIl.Emit(OpCodes.Ldfld, currentField);
        nongenericIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(nongenericCurrent,
            _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);

        var moveNext = typeBuilder.DefineMethod("MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var nextIndex = il.DeclareLocal(_types.Int32);
        var value = il.DeclareLocal(_types.Object);
        var loop = il.DefineLabel();
        var inRange = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, completedField);
        il.Emit(OpCodes.Brfalse, loop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, nextIndex);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, valuesField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Blt, inRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, completedField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(inRange);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Stfld, indexField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, valuesField);
        il.Emit(OpCodes.Ldloc, nextIndex);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, setField);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfObject, "Contains", _types.Object));
        il.Emit(OpCodes.Brfalse, loop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Stfld, currentField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        var reset = typeBuilder.DefineMethod("Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        var resetIl = reset.GetILGenerator();
        resetIl.Emit(OpCodes.Ldstr, "Reset is not supported for set iterators");
        resetIl.Emit(OpCodes.Newobj,
            typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        resetIl.Emit(OpCodes.Throw);

        var dispose = typeBuilder.DefineMethod("Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        dispose.GetILGenerator().Emit(OpCodes.Ret);

        EmitGetEnumeratorReturnsSelf(typeBuilder);
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $IteratorWrapper class that adapts custom iterator objects to IEnumerator&lt;object&gt;.
    /// This allows for...of loops to work with any object that has a [Symbol.iterator]() method.
    /// NOTE: Must be called AFTER EmitIteratorMethods so that runtime.InvokeIteratorNext etc. are defined.
    /// </summary>
    private void EmitIteratorWrapperType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $IteratorWrapper : IEnumerator<object>, IEnumerator, IDisposable
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$IteratorWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable]
        );
        runtime.IteratorWrapperType = typeBuilder;

        // Define fields - simplified, no longer need _runtime field
        var iteratorField = typeBuilder.DefineField("_iterator", _types.Object, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);

        // Constructor: $IteratorWrapper(object iterator)
        // NOTE: runtimeType parameter kept for backward compatibility but not used
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Type]  // Keep signature for compatibility
        );
        runtime.IteratorWrapperCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        // Call base constructor
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        // this._iterator = iterator
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, iteratorField);
        // this._current = null
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldnull);
        ctorIl.Emit(OpCodes.Stfld, currentField);
        // runtimeType (arg_2) is ignored - no longer needed
        ctorIl.Emit(OpCodes.Ret);

        // Property: object Current { get; } - generic version
        var currentProp = typeBuilder.DefineProperty(
            "Current",
            PropertyAttributes.None,
            _types.Object,
            Type.EmptyTypes
        );
        var currentGetter = typeBuilder.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes
        );
        var currentGetterIl = currentGetter.GetILGenerator();
        currentGetterIl.Emit(OpCodes.Ldarg_0);
        currentGetterIl.Emit(OpCodes.Ldfld, currentField);
        currentGetterIl.Emit(OpCodes.Ret);
        currentProp.SetGetMethod(currentGetter);

        // Explicit interface implementation for IEnumerator.Current (non-generic)
        var ienumeratorCurrentGetter = typeBuilder.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object,
            Type.EmptyTypes
        );
        var ienumeratorCurrentGetterIl = ienumeratorCurrentGetter.GetILGenerator();
        ienumeratorCurrentGetterIl.Emit(OpCodes.Ldarg_0);
        ienumeratorCurrentGetterIl.Emit(OpCodes.Ldfld, currentField);
        ienumeratorCurrentGetterIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(ienumeratorCurrentGetter, _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);

        // Method: bool MoveNext() - uses DIRECT method calls instead of reflection
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        var moveNextIl = moveNext.GetILGenerator();

        // Locals for MoveNext
        var resultLocal = moveNextIl.DeclareLocal(_types.Object);

        // var result = InvokeIteratorNext(_iterator);  -- DIRECT CALL
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldfld, iteratorField);
        moveNextIl.Emit(OpCodes.Call, runtime.InvokeIteratorNext);
        moveNextIl.Emit(OpCodes.Stloc, resultLocal);

        // var done = GetIteratorDone(result);  -- DIRECT CALL
        moveNextIl.Emit(OpCodes.Ldloc, resultLocal);
        moveNextIl.Emit(OpCodes.Call, runtime.GetIteratorDone);

        // Preserve IteratorValue(result) even when done is true.  `yield*` reads
        // Current after MoveNext returns false to obtain the delegated iterator's
        // completion value; ordinary iterator consumers simply ignore Current.
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldloc, resultLocal);
        moveNextIl.Emit(OpCodes.Call, runtime.GetIteratorValue);
        moveNextIl.Emit(OpCodes.Stfld, currentField);

        // if (done) return false;
        var notDoneLabel = moveNextIl.DefineLabel();
        moveNextIl.Emit(OpCodes.Brfalse, notDoneLabel);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ret);

        moveNextIl.MarkLabel(notDoneLabel);

        // return true;
        moveNextIl.Emit(OpCodes.Ldc_I4_1);
        moveNextIl.Emit(OpCodes.Ret);

        // Method: bool MoveNextWithSent(object? sent) — forwards sent value to next(v) (#503).
        // Called by the yield* drive loop instead of MoveNext() when the delegate is a
        // $IteratorWrapper so the outer generator's resume value reaches the iterator's next(v).
        var moveNextWithSent = typeBuilder.DefineMethod(
            "MoveNextWithSent",
            MethodAttributes.Public | MethodAttributes.HideBySig,
            _types.Boolean,
            [_types.Object]
        );
        runtime.IteratorWrapperMoveNextWithSent = moveNextWithSent;
        var mwsIl = moveNextWithSent.GetILGenerator();

        var mwsResultLocal = mwsIl.DeclareLocal(_types.Object);

        // var result = InvokeIteratorNextWithSent(_iterator, sent);
        mwsIl.Emit(OpCodes.Ldarg_0);
        mwsIl.Emit(OpCodes.Ldfld, iteratorField);
        mwsIl.Emit(OpCodes.Ldarg_1);               // sent value
        mwsIl.Emit(OpCodes.Call, runtime.InvokeIteratorNextWithSent);
        mwsIl.Emit(OpCodes.Stloc, mwsResultLocal);

        mwsIl.Emit(OpCodes.Ldloc, mwsResultLocal);
        mwsIl.Emit(OpCodes.Call, runtime.GetIteratorDone);

        // As above, retain the completion record's value for `yield*` when
        // this call reports done.
        mwsIl.Emit(OpCodes.Ldarg_0);
        mwsIl.Emit(OpCodes.Ldloc, mwsResultLocal);
        mwsIl.Emit(OpCodes.Call, runtime.GetIteratorValue);
        mwsIl.Emit(OpCodes.Stfld, currentField);

        var mwsNotDoneLabel = mwsIl.DefineLabel();
        mwsIl.Emit(OpCodes.Brfalse, mwsNotDoneLabel);
        mwsIl.Emit(OpCodes.Ldc_I4_0);
        mwsIl.Emit(OpCodes.Ret);

        mwsIl.MarkLabel(mwsNotDoneLabel);
        mwsIl.Emit(OpCodes.Ldc_I4_1);
        mwsIl.Emit(OpCodes.Ret);

        // Method: void Reset() - throws NotSupportedException
        var reset = typeBuilder.DefineMethod(
            "Reset",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            Type.EmptyTypes
        );
        var resetIl = reset.GetILGenerator();
        resetIl.Emit(OpCodes.Ldstr, "Reset is not supported for iterator wrappers");
        resetIl.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        resetIl.Emit(OpCodes.Throw);

        // Method: void Dispose() - no-op
        var dispose = typeBuilder.DefineMethod(
            "Dispose",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void,
            Type.EmptyTypes
        );
        var disposeIl = dispose.GetILGenerator();
        disposeIl.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits basic iterator protocol methods (GetIteratorDone, GetIteratorValue, InvokeIteratorNext, GetIteratorFunction).
    /// These must be called before EmitIteratorWrapperType because $IteratorWrapper uses them.
    /// </summary>
    private void EmitIteratorMethodsBasic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Basic iterator protocol helpers - needed by $IteratorWrapper
        EmitGetIteratorDone(typeBuilder, runtime);
        EmitGetIteratorValue(typeBuilder, runtime);
        EmitInvokeIteratorNext(typeBuilder, runtime);
        EmitInvokeIteratorNextWithSent(typeBuilder, runtime);
        EmitGetIteratorFunction(typeBuilder, runtime);
        EmitIteratorClose(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the shared IteratorClose primitive used by every compiled consumer.
    /// When an existing throw completion is being propagated, failures produced
    /// while retrieving/calling <c>return</c> are suppressed as required by
    /// ECMA-262 §7.4.11; normal completions propagate those failures.
    /// </summary>
    private void EmitIteratorClose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorClose",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Boolean]);
        runtime.IteratorClose = method;

        var il = method.GetILGenerator();
        var returnMethod = il.DeclareLocal(_types.Object);
        var closeResult = il.DeclareLocal(_types.Object);
        var lookupReturn = il.DefineLabel();
        var validateResult = il.DefineLabel();
        var finishTry = il.DefineLabel();
        var done = il.DefineLabel();
        var resultIsObject = il.DefineLabel();

        // A throw completion wins over every abrupt completion produced by
        // IteratorClose. A small catch around the normal algorithm preserves it.
        il.BeginExceptionBlock();

        // Emitted generators expose return through $IGenerator rather than an
        // own-property dictionary. Keep that representation detail inside the
        // shared close primitive so every iterator consumer observes the same
        // GeneratorResumeAbrupt semantics.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Brfalse, lookupReturn);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, runtime.GeneratorReturnMethod);
        il.Emit(OpCodes.Stloc, closeResult);
        il.Emit(OpCodes.Br, validateResult);

        il.MarkLabel(lookupReturn);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, returnMethod);

        il.Emit(OpCodes.Ldloc, returnMethod);
        il.Emit(OpCodes.Brfalse, finishTry);
        il.Emit(OpCodes.Ldloc, returnMethod);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, finishTry);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, returnMethod);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, closeResult);

        // IteratorClose requires the return method's result to be an Object.
        il.MarkLabel(validateResult);
        il.Emit(OpCodes.Ldloc, closeResult);
        il.Emit(OpCodes.Brfalse, resultIsObject);
        il.Emit(OpCodes.Ldloc, closeResult);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, resultIsObject);
        il.Emit(OpCodes.Ldloc, closeResult);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, resultIsObject);
        il.Emit(OpCodes.Ldloc, closeResult);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, resultIsObject);
        il.Emit(OpCodes.Ldloc, closeResult);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, finishTry);
        il.MarkLabel(resultIsObject);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Iterator .return() must return an object");

        il.MarkLabel(finishTry);
        il.Emit(OpCodes.Leave, done);
        il.BeginCatchBlock(_types.Exception);
        var propagateCloseError = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, propagateCloseError);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, done);
        il.MarkLabel(propagateCloseError);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IterateToList method which depends on $IteratorWrapper.
    /// Must be called after EmitIteratorWrapperType.
    /// </summary>
    private void EmitIteratorMethodsAdvanced(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitIterateToList(typeBuilder, runtime);
    }

    /// <summary>
    /// Forward-declares IterateToList so earlier-emitted helpers (notably the
    /// Promise combinators) can reference it. Its body is filled only after
    /// the iterator wrapper type and protocol helpers are available.
    /// </summary>
    private void DeclareIterateToList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.IterateToList = typeBuilder.DefineMethod(
            "IterateToList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, runtime.TSSymbolType, _types.Type]
        );
    }

    /// <summary>
    /// Emits GetIteratorDone: extracts the 'done' property from an iterator result and returns bool.
    /// Signature: bool GetIteratorDone(object result)
    /// </summary>
    private void EmitGetIteratorDone(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIteratorDone",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.GetIteratorDone = method;

        var il = method.GetILGenerator();

        // Call GetProperty(result, "done")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Call, runtime.GetProperty);

        // Call IsTruthy on the result
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits GetIteratorValue: extracts the 'value' property from an iterator result.
    /// Signature: object GetIteratorValue(object result)
    /// </summary>
    private void EmitGetIteratorValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIteratorValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.GetIteratorValue = method;

        var il = method.GetILGenerator();

        // Call GetProperty(result, "value")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits InvokeIteratorNext: gets the 'next' method from iterator and calls it with proper 'this' binding.
    /// Signature: object InvokeIteratorNext(object iterator)
    /// </summary>
    private void EmitInvokeIteratorNext(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeIteratorNext",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.InvokeIteratorNext = method;

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();
        var nextMethodLocal = il.DeclareLocal(_types.Object);

        // Get "next" property from iterator
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, nextMethodLocal);

        // Check if null
        il.Emit(OpCodes.Ldloc, nextMethodLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // Call InvokeMethodValue(iterator, nextMethod, new object[0]) to properly bind 'this'
        il.Emit(OpCodes.Ldarg_0);                    // iterator (receiver/"this")
        il.Emit(OpCodes.Ldloc, nextMethodLocal);    // nextMethod
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);     // empty args array
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        // Throw error if next is null
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Iterator must have a next() method.");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits InvokeIteratorNextWithSent: gets the 'next' method from iterator and calls it with
    /// a sent value argument, forwarding the outer generator's resume value (ECMA-262 §14.4.14, #503).
    /// Signature: object InvokeIteratorNextWithSent(object iterator, object sent)
    /// </summary>
    private void EmitInvokeIteratorNextWithSent(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeIteratorNextWithSent",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]  // iterator, sent
        );
        runtime.InvokeIteratorNextWithSent = method;

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();
        var nextMethodLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Get "next" property from iterator
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, nextMethodLocal);

        // Check if null
        il.Emit(OpCodes.Ldloc, nextMethodLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // Build args array: new object[] { sent }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);               // sent value
        il.Emit(OpCodes.Stelem_Ref);

        // Call InvokeMethodValue(iterator, nextMethod, args) to properly bind 'this'
        il.Emit(OpCodes.Ldarg_0);               // iterator (receiver/"this")
        il.Emit(OpCodes.Ldloc, nextMethodLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        // Throw error if next is null
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Iterator must have a next() method.");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits GetIteratorFunction: looks up Symbol.iterator (or asyncIterator) on an object.
    /// Returns the iterator function if found, or the emitted undefined
    /// singleton when the property is absent. An explicit null value must stay
    /// distinguishable from absence: GetMethod treats only null/undefined as
    /// missing, while GetIterator must throw when @@iterator is present but
    /// non-callable.
    /// Signature: object GetIteratorFunction(object obj, $TSSymbol symbol)
    /// </summary>
    private void EmitGetIteratorFunction(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIteratorFunction",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, runtime.TSSymbolType]
        );
        runtime.GetIteratorFunction = method;

        var il = method.GetILGenerator();
        var returnUndefinedLabel = il.DefineLabel();

        // if (obj == null) return undefined;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnUndefinedLabel);

        // Emitted generators are iterator objects, but their intrinsic
        // @@iterator method lives on $IGenerator rather than in an own symbol
        // dictionary. Return the interface MethodInfo; InvokeMethodValue binds
        // the generator receiver and the method returns that receiver unchanged.
        var notGeneratorIterator = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Bne_Un, notGeneratorIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Brfalse, notGeneratorIterator);
        EmitInstanceMethodInfoLiteral(il, runtime.GeneratorIteratorMethod, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notGeneratorIterator);

        // #1024: node:stream $Readable exposes [Symbol.asyncIterator] via GetAsyncIterator().
        // It carries no per-object symbol dict and isn't a user class, so hook it here:
        //   if (symbol == SymbolAsyncIterator && obj is $Readable) return new $TSFunction(obj, GetAsyncIterator);
        if (runtime.TSReadableType != null && runtime.TSReadableGetAsyncIterator != null)
        {
            var notReadableAsyncIter = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldsfld, runtime.SymbolAsyncIterator);
            il.Emit(OpCodes.Bne_Un, notReadableAsyncIter);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSReadableType);
            il.Emit(OpCodes.Brfalse, notReadableAsyncIter);
            il.Emit(OpCodes.Ldarg_0); // target
            EmitInstanceMethodInfoLiteral(il, runtime.TSReadableGetAsyncIterator, runtime.TSReadableType);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notReadableAsyncIter);
        }

        var dictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var tryRegistryLabel = il.DefineLabel();
        var rawValueLabel = il.DefineLabel();
        var registryValueLabel = il.DefineLabel();

        // Host-backed iterables can publish their intrinsic iterator directly
        // through the runtime symbol dictionary, so retain that lookup before
        // consulting the class-method registry.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, tryRegistryLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "TryGetValue",
            [_types.Object, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryRegistryLabel);

        // Object.defineProperty stores a compiled descriptor in the symbol
        // dictionary. Route only that shape through ordinary symbol [[Get]] so
        // accessor getters are invoked (and abrupt completion is propagated).
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Brfalse, rawValueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rawValueLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tryRegistryLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FindSymbolMethod);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, registryValueLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, returnUndefinedLabel);

        il.MarkLabel(registryValueLabel);
        il.Emit(OpCodes.Ret);

        // return undefined;
        il.MarkLabel(returnUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IterateToList: converts any iterable (including custom iterables with Symbol.iterator) to List&lt;object&gt;.
    /// Used by spread operators and yield* to collect values from any iterable source.
    /// Signature: List&lt;object&gt; IterateToList(object obj, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitIterateToList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.IterateToList;

        var il = method.GetILGenerator();

        // Locals
        var resultLocal = il.DeclareLocal(_types.ListOfObject);     // result list
        var iterFnLocal = il.DeclareLocal(_types.Object);           // iterator function
        var iteratorLocal = il.DeclareLocal(_types.Object);         // iterator object
        var wrapperLocal = il.DeclareLocal(_types.IEnumeratorOfObject); // $IteratorWrapper

        // Labels
        var tryStringLabel = il.DefineLabel();
        var tryIteratorLabel = il.DefineLabel();
        var collectLoopLabel = il.DefineLabel();
        var collectDoneLabel = il.DefineLabel();
        var tryBufferLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        var customArrayIteratorLabel = il.DefineLabel();

        // Create result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Check for null input
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // An array can carry an own @@iterator. Only programs that can install one pay
        // this probe; the stable majority retain the zero-dispatch backing-list path.
        if (_features.UsesArrayPrototypeMutation)
        {
            var noCustomArrayIterator = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSArrayType);
            il.Emit(OpCodes.Brfalse, noCustomArrayIterator);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
            il.Emit(OpCodes.Stloc, iterFnLocal);
            il.Emit(OpCodes.Ldloc, iterFnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brfalse, customArrayIteratorLabel);
            il.MarkLabel(noCustomArrayIterator);
        }

        // 1a. Stage E.2: fast path for $Array — return its backing list directly.
        // Since `$Array` inherits List<object?> (M2 decision), Elements is just
        // `this`. The sparse tail past base Count is lost in this fast path,
        // which is acceptable because IterateToList is called by spread /
        // Array.from / concat — all of which either produce fresh dense arrays
        // or receive dense prefixes. Callers needing sparse-aware iteration
        // use the long-indexed GetLong / HasIndex accessors directly.
        var notTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSArrayLabel);

        // 1b. Fast path for emitted $TypedArray — only present when program uses typed arrays.
        if (runtime.TypedArrayBaseType != null)
        {
            var notTypedArrayLabel = il.DefineLabel();
            var taLoopStartLabel = il.DefineLabel();
            var taLoopDoneLabel = il.DefineLabel();
            var taSrcLocal = il.DeclareLocal(_types.Object);
            var taLenLocal = il.DeclareLocal(_types.Int32);
            var taILocal = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Brfalse, notTypedArrayLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Stloc, taSrcLocal);
            il.Emit(OpCodes.Ldloc, taSrcLocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayLengthGetter);
            il.Emit(OpCodes.Stloc, taLenLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, taILocal);
            il.MarkLabel(taLoopStartLabel);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldloc, taLenLocal);
            il.Emit(OpCodes.Bge, taLoopDoneLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, taSrcLocal);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayElementGet);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Ldloc, taILocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, taILocal);
            il.Emit(OpCodes.Br, taLoopStartLabel);
            il.MarkLabel(taLoopDoneLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notTypedArrayLabel);
        }

        // 1c. Map fast path — a compiled Map is a Dictionary<object, object?> (see
        // RuntimeEmitter.Maps.cs CreateMap). Its default iteration must yield real
        // [key, value] arrays (matching the interpreter's SharpTSMap.EnumerateEntries),
        // not the boxed KeyValuePair<object,object?> structs the generic IEnumerable arm
        // below would otherwise produce. Boxed KeyValuePairs aren't List<object?>, so
        // Array.isArray/JSON.stringify treat them as non-arrays → `[...map]` serialized as
        // [null,...] (#953). Each pair is a List<object?> [denormalizedKey, value], which is
        // recognized as an array everywhere ($Array subclasses List<object?>). Gated on
        // UsesMap: no Map in the program ⇒ no Dictionary<object,object?> ⇒ dead arm.
        // Mirrors the IL in RuntimeEmitter.Maps.cs EmitMapEntries.
        if (_features.UsesMap)
        {
            var dictType = _types.DictionaryObjectObject;
            var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
            var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.Object, _types.Object);

            var notMapLabel = il.DefineLabel();
            var mapLoopStart = il.DefineLabel();
            var mapLoopEnd = il.DefineLabel();
            var mapKeyDone = il.DefineLabel();
            var dictLocal = il.DeclareLocal(dictType);
            var mapEnumLocal = il.DeclareLocal(enumeratorType);
            var mapCurrentLocal = il.DeclareLocal(kvpType);
            var mapPairLocal = il.DeclareLocal(_types.ListOfObject);
            var mapKeyLocal = il.DeclareLocal(_types.Object);

            // if (obj is not Dictionary<object, object?> dict) goto notMap;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, dictType);
            il.Emit(OpCodes.Stloc, dictLocal);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Brfalse, notMapLabel);

            // var e = dict.GetEnumerator();
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
            il.Emit(OpCodes.Stloc, mapEnumLocal);

            // while (e.MoveNext())
            il.MarkLabel(mapLoopStart);
            il.Emit(OpCodes.Ldloca, mapEnumLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
            il.Emit(OpCodes.Brfalse, mapLoopEnd);

            // var current = e.Current;
            il.Emit(OpCodes.Ldloca, mapEnumLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, mapCurrentLocal);

            // var pair = new List<object?>();
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
            il.Emit(OpCodes.Stloc, mapPairLocal);

            // key = current.Key; if (key == _mapNullSentinel) key = null;  (inline DenormalizeMapKey)
            il.Emit(OpCodes.Ldloca, mapCurrentLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, mapKeyLocal);
            il.Emit(OpCodes.Ldloc, mapKeyLocal);
            il.Emit(OpCodes.Ldsfld, runtime.MapNullSentinel);
            il.Emit(OpCodes.Bne_Un, mapKeyDone);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, mapKeyLocal);
            il.MarkLabel(mapKeyDone);

            // pair.Add(key);
            il.Emit(OpCodes.Ldloc, mapPairLocal);
            il.Emit(OpCodes.Ldloc, mapKeyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            // pair.Add(current.Value);
            il.Emit(OpCodes.Ldloc, mapPairLocal);
            il.Emit(OpCodes.Ldloca, mapCurrentLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            // result.Add(pair);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, mapPairLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            il.Emit(OpCodes.Br, mapLoopStart);

            il.MarkLabel(mapLoopEnd);
            il.Emit(OpCodes.Ldloca, mapEnumLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notMapLabel);
        }

        // 1. If obj is already List<object>, return it directly (fast path for arrays)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, tryStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ret);

        // 2. If obj is string, iterate characters
        il.MarkLabel(tryStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, tryIteratorLabel);
        {
            // for each char in string, add char.ToString() to result
            var strLocal = il.DeclareLocal(_types.String);
            var idxLocal = il.DeclareLocal(_types.Int32);
            var strLoopStart = il.DefineLabel();
            var strLoopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Stloc, strLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, idxLocal);

            il.MarkLabel(strLoopStart);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
            il.Emit(OpCodes.Bge, strLoopEnd);

            // String iteration advances by Unicode code point, so a surrogate
            // pair is yielded as one string rather than two lone UTF-16 chars.
            var charLocal = il.DeclareLocal(_types.Char);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
            il.Emit(OpCodes.Stloc, charLocal);
            var singleChar = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, charLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsHighSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, singleChar);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
            il.Emit(OpCodes.Bge, singleChar);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "IsLowSurrogate", _types.Char));
            il.Emit(OpCodes.Brfalse, singleChar);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32, _types.Int32));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Br, strLoopStart);
            il.MarkLabel(singleChar);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloca, charLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Br, strLoopStart);

            il.MarkLabel(strLoopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // 3. Check for Symbol.iterator
        var tryIEnumerableLabel = il.DefineLabel();
        il.MarkLabel(tryIteratorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // Symbol.iterator
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Stloc, iterFnLocal);

        if (_features.UsesArrayPrototypeMutation)
            il.MarkLabel(customArrayIteratorLabel);

        // If no iterator function was found, try the CLR enumerable fallback.
        // Explicit null is not absence and flows to InvokeMethodValue, which
        // raises the required guest TypeError for a non-callable method.
        il.Emit(OpCodes.Ldloc, iterFnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, tryIEnumerableLabel);

        // Call the iterator function: iterator = InvokeMethodValue(obj, iterFn, new object[0])
        il.Emit(OpCodes.Ldarg_0);          // receiver (this)
        il.Emit(OpCodes.Ldloc, iterFnLocal);  // function
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);  // empty args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, iteratorLocal);

        // GetIterator requires the iterator method to return an Object.
        // Reject null/undefined and primitive results before the wrapper turns
        // them into an implementation-level "missing next" exception.
        var iteratorObjectOkLabel = il.DefineLabel();
        var iteratorTypeErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Brfalse, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, iteratorObjectOkLabel);
        il.MarkLabel(iteratorTypeErrorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Iterator method must return an object");
        il.MarkLabel(iteratorObjectOkLabel);

        // Create $IteratorWrapper: wrapper = new $IteratorWrapper(iterator, runtimeType)
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Ldarg_2);  // runtimeType
        il.Emit(OpCodes.Newobj, runtime.IteratorWrapperCtor);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // Collect all values: while (wrapper.MoveNext()) result.Add(wrapper.Current);
        il.MarkLabel(collectLoopLabel);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, collectDoneLabel);

        // result.Add(wrapper.Current)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IEnumeratorOfObject, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, collectLoopLabel);

        il.MarkLabel(collectDoneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // 4. Try IEnumerator<object> (from iterator helpers, ArrayValues, etc.)
        il.MarkLabel(tryIEnumerableLabel);
        {
            var tryNonGenericEnumerableLabel = il.DefineLabel();
            var ienumLoopLabel = il.DefineLabel();
            var ienumDoneLabel = il.DefineLabel();
            var ienumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.IEnumeratorOfObject);
            il.Emit(OpCodes.Brfalse, tryNonGenericEnumerableLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.IEnumeratorOfObject);
            il.Emit(OpCodes.Stloc, ienumLocal);

            il.MarkLabel(ienumLoopLabel);
            il.Emit(OpCodes.Ldloc, ienumLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
            il.Emit(OpCodes.Brfalse, ienumDoneLabel);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, ienumLocal);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, ienumLoopLabel);

            il.MarkLabel(ienumDoneLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);

            // 5. Try IEnumerable fallback (for generators and other .NET enumerables)
            il.MarkLabel(tryNonGenericEnumerableLabel);
        }
        {
            var enumLoopLabel = il.DefineLabel();
            var enumDoneLabel = il.DefineLabel();
            var enumLocal = il.DeclareLocal(_types.IEnumerator);

            // Check if obj is IEnumerable
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.IEnumerable);
            il.Emit(OpCodes.Brfalse, tryBufferLabel);

            // Get enumerator: enumerator = ((IEnumerable)obj).GetEnumerator()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.IEnumerable);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerable, "GetEnumerator")!);
            il.Emit(OpCodes.Stloc, enumLocal);

            // Collect: while (enumerator.MoveNext()) result.Add(enumerator.Current)
            il.MarkLabel(enumLoopLabel);
            il.Emit(OpCodes.Ldloc, enumLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext")!);
            il.Emit(OpCodes.Brfalse, enumDoneLabel);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, enumLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, enumLoopLabel);

            il.MarkLabel(enumDoneLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // 6. Check for $Buffer — iterate bytes as doubles (matches interpreter's SharpTSBuffer handling).
        // Gated together with the dispatch branch above; when not gated, the IEnumerable
        // arm's Brfalse-to-tryBufferLabel falls through directly to the throwLabel below.
        il.MarkLabel(tryBufferLabel);
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brfalse, throwLabel);

            // byte[] data = (($Buffer)obj).GetData()
            var bufDataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
            il.Emit(OpCodes.Stloc, bufDataLocal);

            // for (int i = 0; i < data.Length; i++) result.Add((double)data[i])
            var bufIdxLocal = il.DeclareLocal(_types.Int32);
            var bufLoopStart = il.DefineLabel();
            var bufLoopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, bufIdxLocal);

            il.MarkLabel(bufLoopStart);
            il.Emit(OpCodes.Ldloc, bufIdxLocal);
            il.Emit(OpCodes.Ldloc, bufDataLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bge, bufLoopEnd);

            // result.Add((object)(double)data[i])
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, bufDataLocal);
            il.Emit(OpCodes.Ldloc, bufIdxLocal);
            il.Emit(OpCodes.Ldelem_U1);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

            il.Emit(OpCodes.Ldloc, bufIdxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, bufIdxLocal);
            il.Emit(OpCodes.Br, bufLoopStart);

            il.MarkLabel(bufLoopEnd);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // Throw error for non-iterable
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Value is not iterable. Expected an array, string, or object with [Symbol.iterator].");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

}
