using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits iterator helper methods and lazy wrapper types into the generated assembly.
/// Supports ES2025 Iterator Helpers: map, filter, take, drop, flatMap (lazy) and
/// reduce, toArray, forEach, some, every, find (eager).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all iterator helper methods and types.
    /// Must be called AFTER EmitIteratorMethodsAdvanced (needs IterateToList, InvokeMethodValue).
    /// </summary>
    private void EmitIteratorHelperMethods(TypeBuilder typeBuilder, ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Helper to normalize any iterable source to IEnumerator<object>
        EmitNormalizeToEnumerator(typeBuilder, runtime);

        // Lazy wrapper types
        EmitMapIteratorType(moduleBuilder, runtime);
        EmitFilterIteratorType(moduleBuilder, runtime);
        EmitTakeIteratorType(moduleBuilder, runtime);
        EmitDropIteratorType(moduleBuilder, runtime);
        EmitFlatMapIteratorType(moduleBuilder, runtime);

        // Lazy factory methods (on $Runtime)
        EmitIteratorMap(typeBuilder, runtime);
        EmitIteratorFilter(typeBuilder, runtime);
        EmitIteratorTake(typeBuilder, runtime);
        EmitIteratorDrop(typeBuilder, runtime);
        EmitIteratorFlatMap(typeBuilder, runtime);

        // Eager methods (on $Runtime)
        EmitIteratorReduce(typeBuilder, runtime);
        EmitIteratorToArray(typeBuilder, runtime);
        EmitIteratorForEach(typeBuilder, runtime);
        EmitIteratorSome(typeBuilder, runtime);
        EmitIteratorEvery(typeBuilder, runtime);
        EmitIteratorFind(typeBuilder, runtime);
        EmitIteratorNext(typeBuilder, runtime);
        EmitIteratorFrom(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits NormalizeToEnumerator: converts any iterable source to IEnumerator&lt;object&gt;.
    /// Handles List&lt;object&gt;, IEnumerable&lt;object&gt;, and custom iterators via $IteratorWrapper.
    /// Signature: IEnumerator&lt;object&gt; NormalizeToEnumerator(object source)
    /// </summary>
    private void EmitNormalizeToEnumerator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NormalizeToEnumerator",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IEnumeratorOfObject,
            [_types.Object]
        );
        runtime.NormalizeToEnumerator = method;

        var il = method.GetILGenerator();
        var tryEnumerableLabel = il.DefineLabel();
        var tryWrapperLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // If source is already IEnumerator<object>, return it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Brfalse, tryEnumerableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Ret);

        // If source is IEnumerable<object>, call GetEnumerator
        il.MarkLabel(tryEnumerableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerableOfObject);
        il.Emit(OpCodes.Brfalse, tryWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.IEnumerableOfObject);
        var getEnumerator = _types.GetMethodNoParams(_types.IEnumerableOfObject, "GetEnumerator");
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Ret);

        // Otherwise, wrap in $IteratorWrapper (for custom iterator objects with next())
        il.MarkLabel(tryWrapperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull); // runtimeType parameter (unused)
        il.Emit(OpCodes.Newobj, runtime.IteratorWrapperCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Value is not iterable.");
    }

    #region Lazy Iterator Types

    /// <summary>
    /// Emits $MapIterator: wraps a source enumerator and applies a callback to each element.
    /// Fields: _source (IEnumerator&lt;object&gt;), _callback (object), _index (int), _current (object)
    /// </summary>
    private void EmitMapIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$MapIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
        );

        var sourceField = typeBuilder.DefineField("_source", _types.IEnumeratorOfObject, FieldAttributes.Private);
        var callbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Private);
        var indexField = typeBuilder.DefineField("_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);

        // Constructor(IEnumerator<object> source, object callback)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.IEnumeratorOfObject, _types.Object]);
        runtime.MapIteratorCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, sourceField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, callbackField);
        ctorIl.Emit(OpCodes.Ret);

        // MoveNext: source.MoveNext() ? { _current = callback(source.Current, _index++); return true; } : false
        EmitCallbackMoveNext(typeBuilder, runtime, sourceField, callbackField, indexField, currentField,
            includeIndex: true, filterMode: false);

        EmitCurrentProperty(typeBuilder, currentField);
        EmitResetThrows(typeBuilder);
        EmitDisposeNoOp(typeBuilder);
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $FilterIterator: wraps a source enumerator and yields only elements matching a predicate.
    /// </summary>
    private void EmitFilterIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$FilterIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
        );

        var sourceField = typeBuilder.DefineField("_source", _types.IEnumeratorOfObject, FieldAttributes.Private);
        var callbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Private);
        var indexField = typeBuilder.DefineField("_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.IEnumeratorOfObject, _types.Object]);
        runtime.FilterIteratorCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, sourceField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, callbackField);
        ctorIl.Emit(OpCodes.Ret);

        // MoveNext: loop source.MoveNext(), call predicate, if truthy set current and return true
        EmitCallbackMoveNext(typeBuilder, runtime, sourceField, callbackField, indexField, currentField,
            includeIndex: true, filterMode: true);

        EmitCurrentProperty(typeBuilder, currentField);
        EmitResetThrows(typeBuilder);
        EmitDisposeNoOp(typeBuilder);
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $TakeIterator: wraps a source enumerator and yields at most 'limit' elements.
    /// </summary>
    private void EmitTakeIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TakeIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
        );

        var sourceField = typeBuilder.DefineField("_source", _types.IEnumeratorOfObject, FieldAttributes.Private);
        var limitField = typeBuilder.DefineField("_limit", _types.Int32, FieldAttributes.Private);
        var countField = typeBuilder.DefineField("_count", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);

        // Constructor(IEnumerator<object> source, int limit)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.IEnumeratorOfObject, _types.Int32]);
        runtime.TakeIteratorCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, sourceField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, limitField);
        ctorIl.Emit(OpCodes.Ret);

        // MoveNext: if (_count >= _limit) return false; if (!source.MoveNext()) return false; _current = source.Current; _count++; return true;
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();

        // if (_count >= _limit) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, countField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, limitField);
        il.Emit(OpCodes.Bge, returnFalseLabel);

        // if (!source.MoveNext()) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // _current = source.Current
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stfld, currentField);

        // _count++
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, countField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, countField);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        EmitCurrentProperty(typeBuilder, currentField);
        EmitResetThrows(typeBuilder);
        EmitDisposeNoOp(typeBuilder);
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $DropIterator: wraps a source enumerator and skips the first 'count' elements.
    /// </summary>
    private void EmitDropIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$DropIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
        );

        var sourceField = typeBuilder.DefineField("_source", _types.IEnumeratorOfObject, FieldAttributes.Private);
        var toDropField = typeBuilder.DefineField("_toDrop", _types.Int32, FieldAttributes.Private);
        var droppedField = typeBuilder.DefineField("_dropped", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);

        // Constructor(IEnumerator<object> source, int toDrop)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.IEnumeratorOfObject, _types.Int32]);
        runtime.DropIteratorCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, sourceField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, toDropField);
        ctorIl.Emit(OpCodes.Ret);

        // MoveNext: skip while _dropped < _toDrop; then yield from source
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var skipLoopLabel = il.DefineLabel();
        var skipCheckLabel = il.DefineLabel();
        var yieldLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // Skip loop: while (_dropped < _toDrop)
        il.Emit(OpCodes.Br, skipCheckLabel);

        il.MarkLabel(skipLoopLabel);
        // if (!source.MoveNext()) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, returnFalseLabel);
        // _dropped++
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, droppedField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, droppedField);

        il.MarkLabel(skipCheckLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, droppedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, toDropField);
        il.Emit(OpCodes.Blt, skipLoopLabel);

        // After skipping, yield from source
        il.MarkLabel(yieldLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // _current = source.Current
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stfld, currentField);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        EmitCurrentProperty(typeBuilder, currentField);
        EmitResetThrows(typeBuilder);
        EmitDisposeNoOp(typeBuilder);
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits $FlatMapIterator: wraps a source enumerator, calls callback for each element,
    /// and flattens the result by iterating inner results.
    /// </summary>
    private void EmitFlatMapIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$FlatMapIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEnumeratorOfObject, _types.IEnumerator, _types.IDisposable,
             _types.IEnumerableOfObject, _types.IEnumerable]
        );

        var sourceField = typeBuilder.DefineField("_source", _types.IEnumeratorOfObject, FieldAttributes.Private);
        var callbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Private);
        var indexField = typeBuilder.DefineField("_index", _types.Int32, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);
        var innerField = typeBuilder.DefineField("_inner", _types.IEnumeratorOfObject, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.IEnumeratorOfObject, _types.Object]);
        runtime.FlatMapIteratorCtor = ctor;

        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, sourceField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, callbackField);
        ctorIl.Emit(OpCodes.Ret);

        // MoveNext: advance inner; if exhausted advance outer -> callback -> new inner
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();
        var tryInnerLabel = il.DefineLabel();
        var advanceOuterLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // try inner first
        il.MarkLabel(tryInnerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, innerField);
        il.Emit(OpCodes.Brfalse, advanceOuterLabel);

        // if (inner.MoveNext())
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, innerField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, advanceOuterLabel);

        // _current = inner.Current; return true;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, innerField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stfld, currentField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // advance outer
        il.MarkLabel(advanceOuterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // result = InvokeMethodValue(null, callback, [source.Current, _index++])
        // Build args array
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // _index++
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, indexField);

        // Call InvokeMethodValue
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldnull); // receiver
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbackField);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        // this._inner = NormalizeToEnumerator(result)
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        var innerLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        il.Emit(OpCodes.Stloc, innerLocal);   // pop enumerator into local
        il.Emit(OpCodes.Ldarg_0);              // push this
        il.Emit(OpCodes.Ldloc, innerLocal);    // push enumerator
        il.Emit(OpCodes.Stfld, innerField);    // this._inner = enumerator

        // Go back to tryInner
        il.Emit(OpCodes.Br, tryInnerLabel);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        EmitCurrentProperty(typeBuilder, currentField);
        EmitResetThrows(typeBuilder);
        EmitDisposeNoOp(typeBuilder);
        EmitGetEnumeratorReturnsSelf(typeBuilder);

        typeBuilder.CreateType();
    }

    #endregion

    #region Shared Iterator Type Helpers

    /// <summary>
    /// Emits MoveNext for map/filter iterator types.
    /// For map: calls callback and stores result as current.
    /// For filter: loops until predicate returns truthy.
    /// </summary>
    private void EmitCallbackMoveNext(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder sourceField, FieldBuilder callbackField, FieldBuilder indexField, FieldBuilder currentField,
        bool includeIndex, bool filterMode)
    {
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        var il = moveNext.GetILGenerator();

        var loopStartLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        il.MarkLabel(loopStartLabel);

        // if (!source.MoveNext()) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Build args: [source.Current, index]
        var argCount = includeIndex ? 2 : 1;
        il.Emit(OpCodes.Ldc_I4, argCount);
        il.Emit(OpCodes.Newarr, _types.Object);

        // args[0] = source.Current
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sourceField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stelem_Ref);

        if (includeIndex)
        {
            // args[1] = (double)_index
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, indexField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Stelem_Ref);
        }

        // _index++
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, indexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, indexField);

        // result = InvokeMethodValue(null, callback, args)
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callbackField);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        if (filterMode)
        {
            // if (!IsTruthy(result)) goto loopStart (skip this element)
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, loopStartLabel);

            // Passed filter: _current = source.Current (not the callback result)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, sourceField);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
            il.Emit(OpCodes.Stfld, currentField);
        }
        else
        {
            // Map mode: _current = result (result is on stack from InvokeMethodValue)
            var resultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, resultLocal);  // pop result into local
            il.Emit(OpCodes.Ldarg_0);              // push this
            il.Emit(OpCodes.Ldloc, resultLocal);   // push result
            il.Emit(OpCodes.Stfld, currentField);  // this._current = result
        }

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCurrentProperty(TypeBuilder typeBuilder, FieldBuilder currentField)
    {
        // Generic Current property
        var currentProp = typeBuilder.DefineProperty("Current", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var currentGetter = typeBuilder.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        var il = currentGetter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, currentField);
        il.Emit(OpCodes.Ret);
        currentProp.SetGetMethod(currentGetter);

        // Non-generic IEnumerator.Current
        var ienumeratorCurrentGetter = typeBuilder.DefineMethod(
            "System.Collections.IEnumerator.get_Current",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.SpecialName |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Object, Type.EmptyTypes);
        var il2 = ienumeratorCurrentGetter.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ldfld, currentField);
        il2.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(ienumeratorCurrentGetter, _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);
    }

    private void EmitResetThrows(TypeBuilder typeBuilder)
    {
        var reset = typeBuilder.DefineMethod(
            "Reset", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        var il = reset.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "Reset is not supported");
        il.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitDisposeNoOp(TypeBuilder typeBuilder)
    {
        var dispose = typeBuilder.DefineMethod(
            "Dispose", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Void, Type.EmptyTypes);
        var il = dispose.GetILGenerator();
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetEnumeratorReturnsSelf(TypeBuilder typeBuilder)
    {
        // IEnumerable<object>.GetEnumerator()
        var getEnum = typeBuilder.DefineMethod(
            "GetEnumerator",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.IEnumeratorOfObject, Type.EmptyTypes);
        var il = getEnum.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // IEnumerable.GetEnumerator() explicit
        var getEnumNonGeneric = typeBuilder.DefineMethod(
            "System.Collections.IEnumerable.GetEnumerator",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.HideBySig |
            MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.IEnumerator, Type.EmptyTypes);
        var il2 = getEnumNonGeneric.GetILGenerator();
        il2.Emit(OpCodes.Ldarg_0);
        il2.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(getEnumNonGeneric,
            _types.GetMethod(_types.IEnumerable, "GetEnumerator")!);
    }

    #endregion

    #region Lazy Factory Methods (on $Runtime)

    private void EmitIteratorMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorMap(object source, object callback)
        // Returns a lazy $MapIterator wrapping the normalized source enumerator
        var method = typeBuilder.DefineMethod(
            "IteratorMap", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorMap = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.MapIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorFilter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorFilter", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorFilter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.FilterIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorTake(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorTake(object source, int limit)
        var method = typeBuilder.DefineMethod(
            "IteratorTake", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Int32]);
        runtime.IteratorTake = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TakeIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorDrop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorDrop", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Int32]);
        runtime.IteratorDrop = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.DropIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorFlatMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorFlatMap", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorFlatMap = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.FlatMapIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region Eager Methods (on $Runtime)

    private void EmitIteratorReduce(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorReduce(object source, object callback, object initial, bool hasInitial)
        var method = typeBuilder.DefineMethod(
            "IteratorReduce", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Object, _types.Boolean]);
        runtime.IteratorReduce = method;

        var il = method.GetILGenerator();
        var accLocal = il.DeclareLocal(_types.Object);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var notFirstLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // acc = initial; first = !hasInitial
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Stloc, firstLocal);

        // enum = NormalizeToEnumerator(source)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        // Allocate args array [2]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Loop
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // if (first) { acc = current; first = false; continue; }
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Brfalse, notFirstLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(notFirstLabel);
        // args[0] = acc; args[1] = current
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stelem_Ref);

        // acc = InvokeMethodValue(null, callback, args)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        // if (first) throw TypeError
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Reduce of empty iterator with no initial value.");
    }

    private void EmitIteratorToArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorToArray(object source)
        var method = typeBuilder.DefineMethod(
            "IteratorToArray", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.IteratorToArray = method;

        var il = method.GetILGenerator();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // var list = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, listLocal);

        // var enum = NormalizeToEnumerator(source)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, doneLabel);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        var addMethod = _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!;
        il.Emit(OpCodes.Callvirt, addMethod);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorForEach(object source, object callback)
        var method = typeBuilder.DefineMethod(
            "IteratorForEach", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorForEach = method;

        var il = method.GetILGenerator();
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // args[0] = current, args[1] = index
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorSome(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitIteratorPredicateMethod(typeBuilder, runtime, "IteratorSome",
            trueOnMatch: true, out var methodBuilder);
        runtime.IteratorSome = methodBuilder;
    }

    private void EmitIteratorEvery(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitIteratorPredicateMethod(typeBuilder, runtime, "IteratorEvery",
            trueOnMatch: false, out var methodBuilder);
        runtime.IteratorEvery = methodBuilder;
    }

    /// <summary>
    /// Emits a predicate-based iterator method (some/every).
    /// For some: returns true on first truthy match, false if none match.
    /// For every: returns false on first falsy match, true if all match.
    /// </summary>
    private void EmitIteratorPredicateMethod(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, bool trueOnMatch, out MethodBuilder methodBuilder)
    {
        var method = typeBuilder.DefineMethod(
            name, MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        methodBuilder = method;

        var il = method.GetILGenerator();
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var earlyReturnLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, doneLabel);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        if (trueOnMatch)
        {
            // some: if truthy -> return true
            il.Emit(OpCodes.Brtrue, earlyReturnLabel);
        }
        else
        {
            // every: if not truthy -> return false
            il.Emit(OpCodes.Brfalse, earlyReturnLabel);
        }
        il.Emit(OpCodes.Br, loopLabel);

        // Early return
        il.MarkLabel(earlyReturnLabel);
        il.Emit(trueOnMatch ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // Done (exhausted)
        il.MarkLabel(doneLabel);
        il.Emit(trueOnMatch ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorFind(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IteratorFind", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorFind = method;

        var il = method.GetILGenerator();
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var currentLocal = il.DeclareLocal(_types.Object);
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var foundLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Save current
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Stloc, currentLocal);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorNext(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorNext(object source, object sent)
        // Calls MoveNext on the source (which should already be an IEnumerator from prior normalization
        // or a lazy iterator type). Returns a Dictionary<string, object?> with value and done.
        // `sent` is the value passed to next(v); it only has an effect for SharpTS
        // generators, which thread it into the resumed yield (#452).
        var method = typeBuilder.DefineMethod(
            "IteratorNext", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object]);
        runtime.IteratorNext = method;

        var il = method.GetILGenerator();
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var doneLabel = il.DefineLabel();

        // SharpTS generators implement $IGenerator. Route through next(value) so the
        // sent value reaches the suspended yield; the call also drives MoveNext and
        // builds the {value,done} result. Other sources (BCL enumerators from
        // array.values(), user iterators) carry no resume slot — fall through and
        // drive MoveNext directly (the sent value is ignored, matching the spec's
        // "the next method of a built-in iterator ignores its argument").
        if (runtime.GeneratorInterfaceType != null)
        {
            var notGeneratorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
            il.Emit(OpCodes.Brfalse, notGeneratorLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.GeneratorNextMethod);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notGeneratorLabel);
        }

        // Normalize source to IEnumerator<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, enumLocal);

        // Call MoveNext
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, doneLabel);

        // MoveNext returned true — build { value: Current, done: false }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        // MoveNext returned false — build { value: <completion>, done: true }
        // ECMA-262 27.3.2: the completion value is the generator's return expression
        // result. SharpTS-emitted generators preserve it in Current even after MoveNext
        // returns false (see EmitReturn in GeneratorMoveNextEmitter.Statements.cs). Native
        // IEnumerators may throw on Current after done; swallow the exception to null.
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, dictLocal);

        var completionLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, completionLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        var currentLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Ldloc, currentLocal);
        var keepLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, keepLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Stloc, completionLocal);
        il.MarkLabel(keepLabel);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, completionLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIteratorFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // static object IteratorFrom(object source)
        // Wraps any iterable source into something that can use iterator helpers.
        // In compiled mode, this just returns the source as-is since our iterator helper
        // methods work with any IEnumerable/IEnumerator via NormalizeToEnumerator.
        var method = typeBuilder.DefineMethod(
            "IteratorFrom", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.IteratorFrom = method;

        var il = method.GetILGenerator();
        // Just return the source - our helper methods will normalize it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
