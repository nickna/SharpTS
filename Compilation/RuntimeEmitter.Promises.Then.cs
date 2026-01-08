using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    #region PromiseThen State Machine

    /// <summary>
    /// Defines the PromiseThen state machine type structure.
    /// </summary>
    private static PromiseThenStateMachine DefinePromiseThenStateMachine(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
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
        var valueField = smType.DefineField("<value>5__1", typeof(object), FieldAttributes.Private);
        var exceptionField = smType.DefineField("<exception>5__2", typeof(Exception), FieldAttributes.Private);

        // Define MoveNext method
        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, typeof(IAsyncStateMachine).GetMethod("MoveNext")!);

        // Define SetStateMachine method (empty body for value types)
        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        smType.DefineMethodOverride(setStateMachine, typeof(IAsyncStateMachine).GetMethod("SetStateMachine")!);
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
            ValueField = valueField,
            ExceptionField = exceptionField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the PromiseThen wrapper method that creates and starts the state machine.
    /// </summary>
    private static void EmitPromiseThenWrapper(ILGenerator il, PromiseThenStateMachine sm)
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
        var createMethod = sm.BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod)
            .MakeGenericMethod(sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = sm.BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext body for PromiseThen state machine.
    /// Implements: await promise, invoke callback, flatten nested tasks.
    /// </summary>
    private static void EmitPromiseThenMoveNext(PromiseThenStateMachine sm, EmittedRuntime runtime)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var awaiterType = typeof(TaskAwaiter<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));
        var callbackResultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();  // Resume after promise await
        var state1Label = il.DefineLabel();  // Resume after flatten await
        var continue0Label = il.DefineLabel();
        var continue1Label = il.DefineLabel();
        var checkFlattenLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var catchLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);  // state == 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, state1Label);  // state == 1

        // ========== STATE -1: Initial - await input promise ==========

        // Get awaiter for input promise
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.PromiseField);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);
        var awaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.PromiseAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue0Label);

        // Not completed - suspend at state 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(awaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after promise await ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after promise await ==========
        il.MarkLabel(continue0Label);

        // GetResult from promise - store in value field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.PromiseAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stfld, sm.ValueField);

        // Check if onFulfilled is null
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Invoke callback: result = InvokeCallback(onFulfilled, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFulfilledField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ValueField);
        il.Emit(OpCodes.Call, runtime.InvokeCallback);
        il.Emit(OpCodes.Stloc, callbackResultLocal);

        // Check if result is Task<object?> (needs flattening)
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Brfalse, checkFlattenLabel);

        // Result is a Task - flatten it
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Castclass, typeof(Task<object?>));
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);
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
        il.Emit(OpCodes.Br, setResultLabel);

        // ========== checkFlattenLabel: callback returned non-Task ==========
        il.MarkLabel(checkFlattenLabel);
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, setResultLabel);

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
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== Exception handler ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        // Check if onRejected is null
        var noRejectCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Brfalse, noRejectCallbackLabel);

        // Invoke onRejected: result = InvokeCallback(onRejected, exception.Message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnRejectedField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.InvokeCallback);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetResult(result)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // noRejectCallbackLabel: no onRejected, propagate exception
        il.MarkLabel(noRejectCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetException")!);
        il.Emit(OpCodes.Leave, returnLabel);

        il.EndExceptionBlock();

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
