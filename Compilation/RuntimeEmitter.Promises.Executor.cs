using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Promise executor constructor support: new Promise((resolve, reject) => { ... })
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $PromiseResolveCallback and $PromiseRejectCallback types,
    /// and the PromiseFromExecutor method that creates promises from executor functions.
    /// </summary>
    private void EmitPromiseExecutorSupport(TypeBuilder runtimeType, EmittedRuntime runtime, ModuleBuilder moduleBuilder)
    {
        // Emit the $PromiseResolveCallback type
        var resolveCallbackType = EmitPromiseResolveCallbackType(moduleBuilder, runtime);

        // Emit the $PromiseRejectCallback type
        var rejectCallbackType = EmitPromiseRejectCallbackType(moduleBuilder, runtime);

        // Emit the PromiseFromExecutor method
        EmitPromiseFromExecutorMethod(runtimeType, runtime, resolveCallbackType, rejectCallbackType);
    }

    /// <summary>
    /// Emits the $PromiseResolveCallback type with:
    /// - TaskCompletionSource field
    /// - SettledFlag field (object for locking + bool tracking)
    /// - Constructor(TaskCompletionSource, object settledLock, ref bool settledFlag)
    /// - Invoke(object?[] args) method
    /// </summary>
    private TypeBuilder EmitPromiseResolveCallbackType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseResolveCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);

        // Constructor: (TaskCompletionSource<object?> tcs, object lockObj)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TaskCompletionSource<object?>), _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            // Call base constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);
            // this._tcs = tcs
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            // this._lock = lockObj
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, lockField);
            // this._settled = false
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(object?[] args) method - compatible with TSFunction invocation
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        {
            var il = invokeMethod.GetILGenerator();
            var alreadySettledLabel = il.DefineLabel();
            var endLockLabel = il.DefineLabel();
            var notTaskLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            var valueLocal = il.DeclareLocal(_types.Object);
            var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
            var innerTaskLocal = il.DeclareLocal(_types.TaskOfObject);

            // value = args.Length > 0 ? args[0] : null
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, notTaskLabel);  // if args.Length <= 0, jump (using notTaskLabel temporarily)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, valueLocal);
            var afterValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Br, afterValueLabel);
            il.MarkLabel(notTaskLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.MarkLabel(afterValueLabel);

            // Load _tcs for later use
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Stloc, tcsLocal);

            // lock (_lock) { if (_settled) return; _settled = true; }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Enter", _types.Object));

            // try { if (_settled) goto alreadySettled; _settled = true; } finally { Monitor.Exit(_lock); }
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, settledField);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // Just call TrySetResult(value) - no flattening for now (simplification)
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            var trySetResult = typeof(TaskCompletionSource<object?>).GetMethod("TrySetResult")!;
            il.Emit(OpCodes.Callvirt, trySetResult);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.PromiseResolveCallbackType = typeBuilder;
        runtime.PromiseResolveCallbackCtor = ctor;
        return typeBuilder;
    }

    /// <summary>
    /// Emits the $PromiseRejectCallback type with similar structure to resolve callback.
    /// </summary>
    private TypeBuilder EmitPromiseRejectCallbackType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseRejectCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TaskCompletionSource<object?>), _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, lockField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke method
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        {
            var il = invokeMethod.GetILGenerator();
            var alreadySettledLabel = il.DefineLabel();
            var endLockLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            var reasonLocal = il.DeclareLocal(_types.Object);
            var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));

            // reason = args.Length > 0 ? args[0] : null
            var noReasonLabel = il.DefineLabel();
            var afterReasonLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, noReasonLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.Emit(OpCodes.Br, afterReasonLabel);
            il.MarkLabel(noReasonLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.MarkLabel(afterReasonLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Stloc, tcsLocal);

            // lock (_lock) { if (_settled) return; _settled = true; }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Enter", _types.Object));

            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, settledField);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // tcs.TrySetException(new Exception(reason?.ToString() ?? "Promise rejected"))
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, reasonLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
            var exceptionCtor = _types.GetConstructor(_types.Exception, [_types.String]);
            il.Emit(OpCodes.Newobj, exceptionCtor);
            var trySetException = typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!;
            il.Emit(OpCodes.Callvirt, trySetException);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.PromiseRejectCallbackType = typeBuilder;
        runtime.PromiseRejectCallbackCtor = ctor;
        return typeBuilder;
    }

    /// <summary>
    /// Emits the PromiseFromExecutor(object executor) -> Task<object?> method.
    /// </summary>
    private void EmitPromiseFromExecutorMethod(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        TypeBuilder resolveCallbackType,
        TypeBuilder rejectCallbackType)
    {
        var method = runtimeType.DefineMethod(
            "PromiseFromExecutor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.PromiseFromExecutor = method;

        var il = method.GetILGenerator();

        // Local variables
        var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
        var lockLocal = il.DeclareLocal(_types.Object);
        var resolveLocal = il.DeclareLocal(resolveCallbackType);
        var rejectLocal = il.DeclareLocal(rejectCallbackType);
        var argsLocal = il.DeclareLocal(typeof(object[]));

        // TaskCompletionSource<object?> tcs = new TaskCompletionSource<object?>();
        var tcsCtor = typeof(TaskCompletionSource<object?>).GetConstructor([])!;
        il.Emit(OpCodes.Newobj, tcsCtor);
        il.Emit(OpCodes.Stloc, tcsLocal);

        // object lockObj = new object();
        il.Emit(OpCodes.Newobj, _types.Object.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, lockLocal);

        // var resolveCallback = new $PromiseResolveCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseResolveCallbackCtor);
        il.Emit(OpCodes.Stloc, resolveLocal);

        // var rejectCallback = new $PromiseRejectCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseRejectCallbackCtor);
        il.Emit(OpCodes.Stloc, rejectLocal);

        // Create args array [resolveCallback, rejectCallback]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resolveLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // try { InvokeValue(executor, args); }
        // catch (Exception ex) { tcs.TrySetException(ex); }
        var exLocal = il.DeclareLocal(_types.Exception);
        var endTryLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // Call the executor: InvokeValue(executor, args)
        // This invokes the executor function with (resolve, reject) arguments
        il.Emit(OpCodes.Ldarg_0);  // executor
        il.Emit(OpCodes.Ldloc, argsLocal);  // args
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);  // Discard executor return value

        il.Emit(OpCodes.Leave, endTryLabel);

        // catch (Exception ex)
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);

        // tcs.TrySetException(ex)
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        var trySetException = typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!;
        il.Emit(OpCodes.Callvirt, trySetException);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Leave, endTryLabel);

        il.EndExceptionBlock();
        il.MarkLabel(endTryLabel);

        // return tcs.Task;
        il.Emit(OpCodes.Ldloc, tcsLocal);
        var taskProperty = typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, taskProperty);
        il.Emit(OpCodes.Ret);
    }
}
