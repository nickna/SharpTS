using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits iterator protocol support methods into the generated assembly.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $IteratorWrapper class that adapts custom iterator objects to IEnumerator&lt;object&gt;.
    /// This allows for...of loops to work with any object that has a [Symbol.iterator]() method.
    /// NOTE: Must be called AFTER EmitIteratorMethods so that runtime.InvokeIteratorNext etc. are defined.
    /// </summary>
    private void EmitIteratorWrapperType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $IteratorWrapper : IEnumerator<object>, IEnumerator, IDisposable
        var typeBuilder = moduleBuilder.DefineType(
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
        ctorIl.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);
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
        typeBuilder.DefineMethodOverride(ienumeratorCurrentGetter, _types.IEnumerator.GetProperty("Current")!.GetGetMethod()!);

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

        // if (done) return false;
        var notDoneLabel = moveNextIl.DefineLabel();
        moveNextIl.Emit(OpCodes.Brfalse, notDoneLabel);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ret);

        moveNextIl.MarkLabel(notDoneLabel);

        // _current = GetIteratorValue(result);  -- DIRECT CALL
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldloc, resultLocal);
        moveNextIl.Emit(OpCodes.Call, runtime.GetIteratorValue);
        moveNextIl.Emit(OpCodes.Stfld, currentField);

        // return true;
        moveNextIl.Emit(OpCodes.Ldc_I4_1);
        moveNextIl.Emit(OpCodes.Ret);

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
        // Keep the original stub for backwards compatibility
        EmitGetIterator(typeBuilder, runtime);

        // Basic iterator protocol helpers - needed by $IteratorWrapper
        EmitGetIteratorDone(typeBuilder, runtime);
        EmitGetIteratorValue(typeBuilder, runtime);
        EmitInvokeIteratorNext(typeBuilder, runtime);
        EmitGetIteratorFunction(typeBuilder, runtime);
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
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits GetIteratorFunction: looks up Symbol.iterator (or asyncIterator) on an object.
    /// Returns the iterator function if found, null otherwise.
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
        var returnNullLabel = il.DefineLabel();
        var tryGetValueLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Locals
        var dictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var valueLocal = il.DeclareLocal(_types.Object);

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // Get symbol dict: var dict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict == null) return null;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (dict.TryGetValue(symbol, out value)) return value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        var tryGetValue = _types.DictionaryObjectObject.GetMethod("TryGetValue", [_types.Object, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brtrue, returnLabel);

        // return null;
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // return value;
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IterateToList: converts any iterable (including custom iterables with Symbol.iterator) to List&lt;object&gt;.
    /// Used by spread operators and yield* to collect values from any iterable source.
    /// Signature: List&lt;object&gt; IterateToList(object obj, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitIterateToList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IterateToList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, runtime.TSSymbolType, _types.Type]
        );
        runtime.IterateToList = method;

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
        var throwLabel = il.DefineLabel();

        // Create result list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Check for null input
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

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
            il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
            il.Emit(OpCodes.Bge, strLoopEnd);

            // result.Add(str[idx].ToString())
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, strLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [typeof(int)])!);
            var charToString = typeof(char).GetMethod("ToString", Type.EmptyTypes)!;
            il.Emit(OpCodes.Call, charToString);
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

        // If no iterator function found, try IEnumerable fallback
        il.Emit(OpCodes.Ldloc, iterFnLocal);
        il.Emit(OpCodes.Brfalse, tryIEnumerableLabel);

        // Call the iterator function: iterator = InvokeMethodValue(obj, iterFn, new object[0])
        il.Emit(OpCodes.Ldarg_0);          // receiver (this)
        il.Emit(OpCodes.Ldloc, iterFnLocal);  // function
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);  // empty args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, iteratorLocal);

        // Create $IteratorWrapper: wrapper = new $IteratorWrapper(iterator, runtimeType)
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Ldarg_2);  // runtimeType
        il.Emit(OpCodes.Newobj, runtime.IteratorWrapperCtor);
        il.Emit(OpCodes.Stloc, wrapperLocal);

        // Collect all values: while (wrapper.MoveNext()) result.Add(wrapper.Current);
        il.MarkLabel(collectLoopLabel);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.IEnumerator.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, collectDoneLabel);

        // result.Add(wrapper.Current)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, wrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.IEnumeratorOfObject.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, collectLoopLabel);

        il.MarkLabel(collectDoneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // 4. Try IEnumerable fallback (for generators and other .NET enumerables)
        il.MarkLabel(tryIEnumerableLabel);
        {
            var enumLoopLabel = il.DefineLabel();
            var enumDoneLabel = il.DefineLabel();
            var enumLocal = il.DeclareLocal(_types.IEnumerator);

            // Check if obj is IEnumerable
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.IEnumerable);
            il.Emit(OpCodes.Brfalse, throwLabel);

            // Get enumerator: enumerator = ((IEnumerable)obj).GetEnumerator()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.IEnumerable);
            il.Emit(OpCodes.Callvirt, _types.IEnumerable.GetMethod("GetEnumerator")!);
            il.Emit(OpCodes.Stloc, enumLocal);

            // Collect: while (enumerator.MoveNext()) result.Add(enumerator.Current)
            il.MarkLabel(enumLoopLabel);
            il.Emit(OpCodes.Ldloc, enumLocal);
            il.Emit(OpCodes.Callvirt, _types.IEnumerator.GetMethod("MoveNext")!);
            il.Emit(OpCodes.Brfalse, enumDoneLabel);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, enumLocal);
            il.Emit(OpCodes.Callvirt, _types.IEnumerator.GetProperty("Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, enumLoopLabel);

            il.MarkLabel(enumDoneLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // Throw error for non-iterable
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Value is not iterable. Expected an array, string, or object with [Symbol.iterator].");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits GetIterator: gets an enumerable from an object using the Symbol.iterator protocol.
    /// Signature: IEnumerable GetIterator(object obj, $TSSymbol iteratorSymbol)
    ///
    /// NOTE: This is the original stub kept for backwards compatibility.
    /// New code should use GetIteratorFunction + $IteratorWrapper.
    /// </summary>
    private void EmitGetIterator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIterator",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.IEnumerable,
            [_types.Object, runtime.TSSymbolType]
        );
        runtime.GetIterator = method;

        var il = method.GetILGenerator();

        // Simple fallback: check if it's already IEnumerable
        var returnLabel = il.DefineLabel();

        // if (obj is IEnumerable) return (IEnumerable)obj;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerable);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, returnLabel);
        il.Emit(OpCodes.Pop);

        // For non-IEnumerable, return null (caller should handle)
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }
}
