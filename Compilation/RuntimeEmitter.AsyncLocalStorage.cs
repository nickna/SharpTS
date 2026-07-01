using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $AsyncLocalStorage class for standalone async context propagation.
/// Uses .NET's AsyncLocal&lt;object&gt; which automatically flows context across
/// await boundaries and Task continuations.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitAsyncLocalStorageClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var asyncLocalType = typeof(AsyncLocal<object>);
        var asyncLocalCtor = asyncLocalType.GetConstructor([])!;
        var asyncLocalGetValue = asyncLocalType.GetProperty("Value")!.GetGetMethod()!;
        var asyncLocalSetValue = asyncLocalType.GetProperty("Value")!.GetSetMethod()!;

        // Define class: public sealed class $AsyncLocalStorage
        var typeBuilder = moduleBuilder.DefineType(
            "$AsyncLocalStorage",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        _ = typeBuilder;

        // Field: private AsyncLocal<object> _store
        var storeField = typeBuilder.DefineField("_store", asyncLocalType, FieldAttributes.Private);

        // Field: private bool _enabled
        var enabledField = typeBuilder.DefineField("_enabled", _types.Boolean, FieldAttributes.Private);

        // Constructor: public $AsyncLocalStorage()
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSAsyncLocalStorageCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        // base()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));
        // _store = new AsyncLocal<object>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, asyncLocalCtor);
        ctorIL.Emit(OpCodes.Stfld, storeField);
        // _enabled = true
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_1);
        ctorIL.Emit(OpCodes.Stfld, enabledField);
        ctorIL.Emit(OpCodes.Ret);

        // Method: public object GetStore()
        EmitGetStoreMethod(typeBuilder, storeField, enabledField, asyncLocalGetValue);

        // Method: public void EnterWith(object store)
        EmitEnterWithMethod(typeBuilder, storeField, asyncLocalSetValue);

        // Method: public void Disable()
        EmitDisableMethod(typeBuilder, storeField, enabledField, asyncLocalSetValue);

        // Method: public object Run(object store, object callback)
        EmitRunMethod(typeBuilder, runtime, storeField, asyncLocalGetValue, asyncLocalSetValue);

        // Method: public object Exit(object callback)
        EmitExitMethod(typeBuilder, runtime, storeField, asyncLocalGetValue, asyncLocalSetValue);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public object GetStore() => _enabled ? _store.Value : null;
    /// </summary>
    private void EmitGetStoreMethod(
        TypeBuilder typeBuilder, FieldBuilder storeField, FieldBuilder enabledField,
        MethodInfo asyncLocalGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "GetStore",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var disabledLabel = il.DefineLabel();

        // if (!_enabled) goto disabled
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, enabledField);
        il.Emit(OpCodes.Brfalse, disabledLabel);

        // return _store.Value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Callvirt, asyncLocalGetValue);
        il.Emit(OpCodes.Ret);

        // disabled: return null
        il.MarkLabel(disabledLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void EnterWith(object store) { _store.Value = store; }
    /// </summary>
    private void EmitEnterWithMethod(
        TypeBuilder typeBuilder, FieldBuilder storeField,
        MethodInfo asyncLocalSetValue)
    {
        var method = typeBuilder.DefineMethod(
            "EnterWith",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldarg_1); // store
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public void Disable() { _enabled = false; _store.Value = null; }
    /// </summary>
    private void EmitDisableMethod(
        TypeBuilder typeBuilder, FieldBuilder storeField, FieldBuilder enabledField,
        MethodInfo asyncLocalSetValue)
    {
        var method = typeBuilder.DefineMethod(
            "Disable",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        // _enabled = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, enabledField);
        // _store.Value = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Run(object store, object callback)
    /// Saves old value, sets store, invokes callback via reflection, restores in finally.
    /// </summary>
    private void EmitRunMethod(
        TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder storeField,
        MethodInfo asyncLocalGetValue, MethodInfo asyncLocalSetValue)
    {
        var method = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);    // 0: old store value
        var resultLocal = il.DeclareLocal(_types.Object);      // 1: callback result

        // oldValue = _store.Value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Callvirt, asyncLocalGetValue);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // _store.Value = store (arg1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldarg_1); // store
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);

        // try {
        il.BeginExceptionBlock();

        // result = InvokeCallback(callback)
        EmitCallbackInvocation(il, runtime);
        il.Emit(OpCodes.Stloc, resultLocal);

        // } finally {
        il.BeginFinallyBlock();

        // _store.Value = oldValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);

        // }
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Exit(object callback)
    /// Saves current value, clears store, invokes callback, restores in finally.
    /// </summary>
    private void EmitExitMethod(
        TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder storeField,
        MethodInfo asyncLocalGetValue, MethodInfo asyncLocalSetValue)
    {
        var method = typeBuilder.DefineMethod(
            "Exit",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var oldValueLocal = il.DeclareLocal(_types.Object);    // 0: old store value
        var resultLocal = il.DeclareLocal(_types.Object);      // 1: callback result

        // oldValue = _store.Value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Callvirt, asyncLocalGetValue);
        il.Emit(OpCodes.Stloc, oldValueLocal);

        // _store.Value = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);

        // try {
        il.BeginExceptionBlock();

        // result = InvokeCallback(callback) — arg1 for Exit
        // For Exit, the callback is arg1 (not arg2 like Run)
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object); // empty args
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Stloc, resultLocal);

        // } finally {
        il.BeginFinallyBlock();

        // _store.Value = oldValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, storeField);
        il.Emit(OpCodes.Ldloc, oldValueLocal);
        il.Emit(OpCodes.Callvirt, asyncLocalSetValue);

        // }
        il.EndExceptionBlock();

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL to invoke a callback: (($TSFunction)callback).Invoke(new object[0]).
    /// Assumes callback is on arg2 (for Run method pattern).
    /// Pushes the result onto the evaluation stack.
    /// </summary>
    private void EmitCallbackInvocation(ILGenerator il, EmittedRuntime runtime)
    {
        // (($TSFunction)callback).Invoke(new object[0])
        il.Emit(OpCodes.Ldarg_2); // callback (arg2 in Run: this=0, store=1, callback=2)
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object); // empty args array
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
    }
}
