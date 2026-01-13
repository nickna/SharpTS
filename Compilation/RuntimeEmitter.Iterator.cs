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

        // Define fields
        var iteratorField = typeBuilder.DefineField("_iterator", _types.Object, FieldAttributes.Private);
        var currentField = typeBuilder.DefineField("_current", _types.Object, FieldAttributes.Private);
        var runtimeField = typeBuilder.DefineField("_runtime", _types.Type, FieldAttributes.Private);

        // Constructor: $IteratorWrapper(object iterator, Type runtimeType)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Type]
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
        // this._runtime = runtimeType
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, runtimeField);
        // this._current = null
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldnull);
        ctorIl.Emit(OpCodes.Stfld, currentField);
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

        // Method: bool MoveNext()
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes
        );
        var moveNextIl = moveNext.GetILGenerator();

        // Locals for MoveNext
        var resultLocal = moveNextIl.DeclareLocal(_types.Object);
        var doneLocal = moveNextIl.DeclareLocal(_types.Boolean);
        var methodInfoLocal = moveNextIl.DeclareLocal(_types.MethodInfo);

        // Call InvokeIteratorNext(_iterator) via reflection on runtime type
        // var invokeMethod = _runtime.GetMethod("InvokeIteratorNext");
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldfld, runtimeField);
        moveNextIl.Emit(OpCodes.Ldstr, "InvokeIteratorNext");
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        moveNextIl.Emit(OpCodes.Stloc, methodInfoLocal);

        // var result = invokeMethod.Invoke(null, new object[] { _iterator });
        moveNextIl.Emit(OpCodes.Ldloc, methodInfoLocal);
        moveNextIl.Emit(OpCodes.Ldnull); // static method, no instance
        moveNextIl.Emit(OpCodes.Ldc_I4_1);
        moveNextIl.Emit(OpCodes.Newarr, _types.Object);
        moveNextIl.Emit(OpCodes.Dup);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldfld, iteratorField);
        moveNextIl.Emit(OpCodes.Stelem_Ref);
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        moveNextIl.Emit(OpCodes.Stloc, resultLocal);

        // Call GetIteratorDone(result) via reflection
        // var doneMethod = _runtime.GetMethod("GetIteratorDone");
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldfld, runtimeField);
        moveNextIl.Emit(OpCodes.Ldstr, "GetIteratorDone");
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        moveNextIl.Emit(OpCodes.Stloc, methodInfoLocal);

        // var done = (bool)doneMethod.Invoke(null, new object[] { result });
        moveNextIl.Emit(OpCodes.Ldloc, methodInfoLocal);
        moveNextIl.Emit(OpCodes.Ldnull);
        moveNextIl.Emit(OpCodes.Ldc_I4_1);
        moveNextIl.Emit(OpCodes.Newarr, _types.Object);
        moveNextIl.Emit(OpCodes.Dup);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ldloc, resultLocal);
        moveNextIl.Emit(OpCodes.Stelem_Ref);
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        moveNextIl.Emit(OpCodes.Unbox_Any, _types.Boolean);
        moveNextIl.Emit(OpCodes.Stloc, doneLocal);

        // if (done) return false;
        var notDoneLabel = moveNextIl.DefineLabel();
        moveNextIl.Emit(OpCodes.Ldloc, doneLocal);
        moveNextIl.Emit(OpCodes.Brfalse, notDoneLabel);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ret);

        moveNextIl.MarkLabel(notDoneLabel);

        // Call GetIteratorValue(result) via reflection
        // var valueMethod = _runtime.GetMethod("GetIteratorValue");
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldfld, runtimeField);
        moveNextIl.Emit(OpCodes.Ldstr, "GetIteratorValue");
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        moveNextIl.Emit(OpCodes.Stloc, methodInfoLocal);

        // _current = valueMethod.Invoke(null, new object[] { result });
        moveNextIl.Emit(OpCodes.Ldarg_0);
        moveNextIl.Emit(OpCodes.Ldloc, methodInfoLocal);
        moveNextIl.Emit(OpCodes.Ldnull);
        moveNextIl.Emit(OpCodes.Ldc_I4_1);
        moveNextIl.Emit(OpCodes.Newarr, _types.Object);
        moveNextIl.Emit(OpCodes.Dup);
        moveNextIl.Emit(OpCodes.Ldc_I4_0);
        moveNextIl.Emit(OpCodes.Ldloc, resultLocal);
        moveNextIl.Emit(OpCodes.Stelem_Ref);
        moveNextIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
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
    /// Emits methods for iterator protocol support.
    /// </summary>
    private void EmitIteratorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Keep the original stub for backwards compatibility
        EmitGetIterator(typeBuilder, runtime);

        // New iterator protocol helpers
        EmitGetIteratorDone(typeBuilder, runtime);
        EmitGetIteratorValue(typeBuilder, runtime);
        EmitInvokeIteratorNext(typeBuilder, runtime);
        EmitGetIteratorFunction(typeBuilder, runtime);
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
