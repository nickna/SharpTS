using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a zero-allocation awaiter whose continuation is always appended to
    /// the shared FIFO microtask queue. Promise state machines use it after the
    /// source task settles so even completed Tasks cannot invoke reactions
    /// inline in the caller's JavaScript job (#1440).
    /// </summary>
    private Type DefinePromiseJobAwaiter(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime)
    {
        var awaiter = moduleBuilder.DefineType(
            "$PromiseJobAwaiter",
            TypeAttributes.Public | TypeAttributes.Sealed |
                TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(ICriticalNotifyCompletion)]);

        var getResult = awaiter.DefineMethod(
            "GetResult", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        getResult.GetILGenerator().Emit(OpCodes.Ret);

        var onCompleted = awaiter.DefineMethod(
            "OnCompleted",
            MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.Final | MethodAttributes.HideBySig |
                MethodAttributes.NewSlot,
            typeof(void),
            [typeof(Action)]);
        {
            var il = onCompleted.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.QueuePromiseJob);
            il.Emit(OpCodes.Ret);
        }
        awaiter.DefineMethodOverride(
            onCompleted, typeof(INotifyCompletion).GetMethod("OnCompleted")!);

        var unsafeOnCompleted = awaiter.DefineMethod(
            "UnsafeOnCompleted",
            MethodAttributes.Public | MethodAttributes.Virtual |
                MethodAttributes.Final | MethodAttributes.HideBySig |
                MethodAttributes.NewSlot,
            typeof(void),
            [typeof(Action)]);
        {
            var il = unsafeOnCompleted.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.QueuePromiseJob);
            il.Emit(OpCodes.Ret);
        }
        awaiter.DefineMethodOverride(
            unsafeOnCompleted,
            typeof(ICriticalNotifyCompletion).GetMethod("UnsafeOnCompleted")!);

        return awaiter.CreateType()!;
    }

    #region PromiseThen State Machine

    /// <summary>
    /// Defines the PromiseThen state machine type structure.
    /// </summary>
    private PromiseThenStateMachine DefinePromiseThenStateMachine(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        Type promiseJobAwaiterType)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?>);

        // Define state machine struct: $PromiseThen_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseThen_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var promiseField = smType.DefineField("promise", typeof(Task<object?>), FieldAttributes.Public);
        var onFulfilledField = smType.DefineField("onFulfilled", typeof(object), FieldAttributes.Public);
        var onRejectedField = smType.DefineField("onRejected", typeof(object), FieldAttributes.Public);
        var promiseAwaiterField = smType.DefineField("<>u__1", awaiterType, FieldAttributes.Private);
        var flattenAwaiterField = smType.DefineField("<>u__2", awaiterType, FieldAttributes.Private);
        var jobAwaiterField = smType.DefineField(
            "<>u__3", promiseJobAwaiterType, FieldAttributes.Private);
        var valueField = smType.DefineField("<value>5__1", typeof(object), FieldAttributes.Private);
        var exceptionField = smType.DefineField("<exception>5__2", typeof(Exception), FieldAttributes.Private);

        // Define MoveNext method
        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, _types.AsyncStateMachineMoveNext);

        // Define SetStateMachine method (empty body for value types)
        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        smType.DefineMethodOverride(setStateMachine, _types.AsyncStateMachineSetStateMachine);
        var setSmIL = setStateMachine.GetILGenerator();
        setSmIL.Emit(OpCodes.Ret);

        return new PromiseThenStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            PromiseField = promiseField,
            OnFulfilledField = onFulfilledField,
            OnRejectedField = onRejectedField,
            PromiseAwaiterField = promiseAwaiterField,
            FlattenAwaiterField = flattenAwaiterField,
            JobAwaiterField = jobAwaiterField,
            ValueField = valueField,
            ExceptionField = exceptionField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the PromiseThen wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseThenWrapper(ILGenerator il, PromiseThenStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine: var sm = default($PromiseThen_SM);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.promise = arg0;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.PromiseField);

        // sm.onFulfilled = arg1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, sm.OnFulfilledField);

        // sm.onRejected = arg2;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, sm.OnRejectedField);

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create();
        il.Emit(OpCodes.Ldloca, smLocal);
        var createMethod = _types.GetMethod(sm.BuilderType, "Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = EmitGenerics.MakeGenericMethod(_types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod), sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = _types.GetProperty(sm.BuilderType, "Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext body for PromiseThen state machine.
    /// Implements: await promise, invoke callback, flatten nested tasks.
    /// </summary>
    private void EmitPromiseThenMoveNext(
        PromiseThenStateMachine sm,
        EmittedRuntime runtime,
        Type promiseJobAwaiterType)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var awaiterType = typeof(TaskAwaiter<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));
        var callbackResultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();  // Resume after promise await
        var state1Label = il.DefineLabel();  // Resume after flatten await (inside handler try)
        var state3Label = il.DefineLabel();  // Resume in the queued Promise job
        var queueJobLabel = il.DefineLabel();
        var continue0Label = il.DefineLabel();
        var continue1Label = il.DefineLabel();
        var setResultLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var rejectionFlattenLabel = il.DefineLabel();
        var handlerTryStartLabel = il.DefineLabel();  // First instruction of the onFulfilled guard try

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch. State 1 resumes inside the nested onFulfilled guard
        // try — IL only permits entering a protected region at its first
        // instruction, so branch there and let the nested dispatch take over
        // (same shape Roslyn emits for await-inside-try).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);  // state == 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, handlerTryStartLabel);  // state == 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Beq, state3Label);  // state == 3
        var notRejectionFlattenResumeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bne_Un, notRejectionFlattenResumeLabel);
        il.Emit(OpCodes.Leave, rejectionFlattenLabel); // state == 2
        il.MarkLabel(notRejectionFlattenResumeLabel);

        // ========== STATE -1: Initial - await input promise ==========

        // Get awaiter for input promise
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.PromiseField);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        var awaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.PromiseAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, queueJobLabel);

        // Not completed - suspend at state 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = EmitGenerics.MakeGenericMethod(_types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod), awaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after promise await ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // A settled source only makes the reaction eligible. Always suspend
        // once more into the shared Promise-job queue before observing the
        // result or invoking either handler.
        il.MarkLabel(queueJobLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.JobAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var jobAwaitMethod = EmitGenerics.MakeGenericMethod(
            _types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod),
            promiseJobAwaiterType,
            sm.Type);
        il.Emit(OpCodes.Call, jobAwaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 3: Execute the queued Promise job ==========
        il.MarkLabel(state3Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue inside Promise job ==========
        il.MarkLabel(continue0Label);

        // GetResult from promise - store in value field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stfld, sm.ValueField);

        // Check if onFulfilled is callable (ECMA-262 §27.2.5.4 step 3):
        // not just null — also $Undefined.Instance and any non-callable value
        // must fall through to the identity-pass branch. Without this,
        // `then(undefined, ...)` on a fulfilled promise tries to invoke
        // $Undefined as a callback and the SM treats the resulting throw
        // as a value, masking the rejection chain (test262 then/A4.1_T2).
        var noCallbackLabel = il.DefineLabel();
        var onFulfilledCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);
        // Isinst against known callable shapes; anything else → pass-through.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, onFulfilledCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, onFulfilledCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, onFulfilledCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Brtrue, onFulfilledCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Brtrue, onFulfilledCallableLabel);
        il.Emit(OpCodes.Br, noCallbackLabel);
        il.MarkLabel(onFulfilledCallableLabel);

        // ========== onFulfilled guard try ==========
        // ECMA-262 §27.2.5.4: "input promise rejected" and "onFulfilled threw"
        // are distinct — a throwing onFulfilled (or a rejecting thenable it
        // returned) rejects the OUTPUT promise and must NOT dispatch to this
        // same then's onRejected (which only handles rejection of the input
        // promise). Guard the invocation + flatten await with a nested try
        // whose catch rejects the builder task directly (#195).
        var fulfillExceptionLocal = il.DeclareLocal(typeof(Exception));
        il.BeginExceptionBlock();
        il.MarkLabel(handlerTryStartLabel);

        // Nested dispatch: state 1 (flatten await resume) re-enters here.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, state1Label);

        // Invoke callback: result = InvokeCallback(onFulfilled, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ValueField);
        il.Emit(OpCodes.Call, runtime.InvokeCallback);
        il.Emit(OpCodes.Stloc, callbackResultLocal);

        // Resolve the handler result through the Promise Resolve Functions
        // algorithm before flattening it. This deliberately performs the
        // observable `then` lookup even when the host value is already a
        // Task-backed native Promise (an own `then` override must win), and
        // turns an abrupt getter into a rejected task.
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Call, runtime.PromiseResolveValueMethod);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        var flattenAwaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, flattenAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, flattenAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.FlattenAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue1Label);

        // Not completed - suspend at state 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 1: Resume after flatten await ==========
        il.MarkLabel(state1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after flatten await ==========
        il.MarkLabel(continue1Label);

        // GetResult from flattened task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        // onFulfilled threw (or its returned thenable rejected) — reject the
        // output promise; deliberately bypasses the outer catch's onRejected
        // dispatch.
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, fulfillExceptionLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, fulfillExceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);
        il.EndExceptionBlock();

        // ========== noCallbackLabel: no callback, use original value ==========
        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ValueField);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== Success path ==========
        il.MarkLabel(setResultLabel);

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetResult(result)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== Exception handler ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        // Check if onRejected is callable (ECMA-262 §27.2.5.4 step 4):
        // null, undefined, or any non-callable value → rethrow (propagate
        // rejection). Mirrors the onFulfilled IsCallable check above.
        var noRejectCallbackLabel = il.DefineLabel();
        var onRejectedCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Brfalse, noRejectCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, onRejectedCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, onRejectedCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, onRejectedCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Isinst, runtime.PromiseResolveCallbackType);
        il.Emit(OpCodes.Brtrue, onRejectedCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Isinst, runtime.PromiseRejectCallbackType);
        il.Emit(OpCodes.Brtrue, onRejectedCallableLabel);
        il.Emit(OpCodes.Br, noRejectCallbackLabel);
        il.MarkLabel(onRejectedCallableLabel);

        // Invoke onRejected: result = InvokeCallback(onRejected, WrapException(exception))
        // Use WrapException so the user-thrown value (TypeError instance,
        // AggregateError, primitive payloads via $PromiseRejectedException, etc.)
        // is preserved instead of falling back to Exception.Message (a string).
        // Required for spec patterns like `err instanceof TypeError`.
        //
        // The invocation runs inside this catch handler, so it MUST be guarded
        // by its own nested try/catch: an exception thrown inside a catch
        // handler is not covered by that handler's try, escapes MoveNext, and
        // — because MoveNext runs as an awaiter continuation on the thread
        // pool — gets rethrown via Task.ThrowAsync, killing the process. A
        // throwing onRejected must instead reject the output promise
        // (ECMA-262 §27.2.5.4).
        var handlerExceptionLocal = il.DeclareLocal(typeof(Exception));
        var handlerInvokeDoneLabel = il.DefineLabel();
        var handlerOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, handlerExceptionLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, runtime.WrapException);
        il.Emit(OpCodes.Call, runtime.InvokeCallback);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, handlerInvokeDoneLabel);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, handlerExceptionLocal);
        il.EndExceptionBlock();
        il.MarkLabel(handlerInvokeDoneLabel);

        // Keep the awaiter in the state machine so a pending recovery promise
        // resumes outside this catch handler; CLR IL does not permit branching
        // back into a catch region on resume.
        var rejectionTaskLocal = il.DeclareLocal(_types.TaskOfObject);
        var rejectionHandlerNonTaskLabel = il.DefineLabel();
        var rejectionTaskCompletedLabel = il.DefineLabel();

        // A throwing handler rejects directly. Successful handler results use
        // the same observable Promise Resolve path as fulfilled reactions.
        il.Emit(OpCodes.Ldloc, handlerExceptionLocal);
        il.Emit(OpCodes.Brtrue, rejectionHandlerNonTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, runtime.PromiseResolveValueMethod);
        il.Emit(OpCodes.Stloc, rejectionTaskLocal);
        il.Emit(OpCodes.Ldloc, rejectionTaskLocal);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        var rejectionAwaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, rejectionAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, rejectionAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, rejectionTaskCompletedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);
        il.MarkLabel(rejectionTaskCompletedLabel);
        il.Emit(OpCodes.Leave, rejectionFlattenLabel);

        il.MarkLabel(rejectionHandlerNonTaskLabel);

        // Set state to -2 (completed) on both outcomes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldloc, handlerExceptionLocal);
        il.Emit(OpCodes.Brfalse, handlerOkLabel);

        // onRejected threw — builder.SetException(handlerException)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, handlerExceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);

        il.MarkLabel(handlerOkLabel);
        // builder.SetResult(result)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // noRejectCallbackLabel: no onRejected, propagate exception
        il.MarkLabel(noRejectCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);

        il.EndExceptionBlock();

        // Resume/complete adoption of the Task returned by onRejected. This is
        // deliberately outside the outer source-promise catch so a rejection
        // from the returned Task rejects the output promise instead of invoking
        // the same onRejected callback a second time.
        il.MarkLabel(rejectionFlattenLabel);
        var rejectionFlattenExceptionLocal = il.DeclareLocal(_types.Exception);
        var rejectionFlattenDoneLabel = il.DefineLabel();
        // Publish completion before SetResult/SetException can run continuations
        // synchronously and re-enter observable promise machinery.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.FlattenAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult")!);
        il.Emit(OpCodes.Leave, rejectionFlattenDoneLabel);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, rejectionFlattenExceptionLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, rejectionFlattenExceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException")!);
        il.Emit(OpCodes.Leave, rejectionFlattenDoneLabel);
        il.EndExceptionBlock();
        il.MarkLabel(rejectionFlattenDoneLabel);
        il.Emit(OpCodes.Br, returnLabel);

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Defines the smaller fulfillment-only state machine used after the compiler
    /// proves that a direct intrinsic Promise.then callback returns a primitive.
    /// </summary>
    private PrimitivePromiseThenStateMachine DefinePrimitivePromiseThenStateMachine(
        ModuleBuilder moduleBuilder,
        Type promiseJobAwaiterType)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?>);
        var smType = moduleBuilder.DefineType(
            "$PromiseThenPrimitive_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]);

        var stateField = smType.DefineField(
            "<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField(
            "<>t__builder", builderType, FieldAttributes.Public);
        var promiseField = smType.DefineField(
            "promise", typeof(Task<object?>), FieldAttributes.Public);
        var onFulfilledField = smType.DefineField(
            "onFulfilled", typeof(Func<double, double>), FieldAttributes.Public);
        var promiseAwaiterField = smType.DefineField(
            "<>u__1", awaiterType, FieldAttributes.Private);
        var jobAwaiterField = smType.DefineField(
            "<>u__2", promiseJobAwaiterType, FieldAttributes.Private);

        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes);
        smType.DefineMethodOverride(moveNext, _types.AsyncStateMachineMoveNext);

        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]);
        smType.DefineMethodOverride(setStateMachine, _types.AsyncStateMachineSetStateMachine);
        setStateMachine.GetILGenerator().Emit(OpCodes.Ret);

        return new PrimitivePromiseThenStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            PromiseField = promiseField,
            OnFulfilledField = onFulfilledField,
            PromiseAwaiterField = promiseAwaiterField,
            JobAwaiterField = jobAwaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    private void EmitPrimitivePromiseThenWrapper(
        ILGenerator il,
        PrimitivePromiseThenStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.PromiseField);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, sm.OnFulfilledField);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(
            sm.BuilderType, "Create", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = EmitGenerics.MakeGenericMethod(
            _types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
                .First(method => method.Name == "Start" && method.IsGenericMethod),
            sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Call, _types.GetProperty(
            sm.BuilderType, "Task", BindingFlags.Public | BindingFlags.Instance)!
            .GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitPrimitivePromiseThenMoveNext(
        PrimitivePromiseThenStateMachine sm,
        Type promiseJobAwaiterType)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var awaiterType = typeof(TaskAwaiter<object?>);
        var awaiterLocal = il.DeclareLocal(awaiterType);
        var valueLocal = il.DeclareLocal(typeof(object));
        var resultLocal = il.DeclareLocal(typeof(object));
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resumeLabel = il.DefineLabel();
        var jobResumeLabel = il.DefineLabel();
        var queueJobLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, resumeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, jobResumeLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.PromiseField);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.PromiseAwaiterField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, queueJobLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = EmitGenerics.MakeGenericMethod(
            _types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
                .First(method => method.Name == "AwaitUnsafeOnCompleted" && method.IsGenericMethod),
            awaiterType,
            sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        il.MarkLabel(resumeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.MarkLabel(queueJobLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.JobAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var jobAwaitMethod = EmitGenerics.MakeGenericMethod(
            _types.GetMethods(sm.BuilderType, BindingFlags.Public | BindingFlags.Instance)
                .First(method => method.Name == "AwaitUnsafeOnCompleted" && method.IsGenericMethod),
            promiseJobAwaiterType,
            sm.Type);
        il.Emit(OpCodes.Call, jobAwaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        il.MarkLabel(jobResumeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Callvirt, typeof(Func<double, double>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);
        il.EndExceptionBlock();

        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}

