using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region PromiseFinally State Machine

    /// <summary>
    /// Defines the PromiseFinally state machine type structure.
    /// </summary>
    private PromiseFinallyStateMachine DefinePromiseFinallyStateMachine(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?>);

        // Define state machine struct: $PromiseFinally_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseFinally_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var promiseField = smType.DefineField("promise", typeof(Task<object?>), FieldAttributes.Public);
        var onFinallyField = smType.DefineField("onFinally", typeof(object), FieldAttributes.Public);
        var promiseAwaiterField = smType.DefineField("<>u__1", awaiterType, FieldAttributes.Private);
        var callbackAwaiterField = smType.DefineField("<>u__2", awaiterType, FieldAttributes.Private);
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

        return new PromiseFinallyStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            PromiseField = promiseField,
            OnFinallyField = onFinallyField,
            PromiseAwaiterField = promiseAwaiterField,
            CallbackAwaiterField = callbackAwaiterField,
            ValueField = valueField,
            ExceptionField = exceptionField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the PromiseFinally wrapper method that creates and starts the state machine.
    /// </summary>
    private void EmitPromiseFinallyWrapper(ILGenerator il, PromiseFinallyStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine
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

        // sm.onFinally = arg1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, sm.OnFinallyField);

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
    /// Emits the MoveNext body for PromiseFinally state machine.
    /// Simplified: await promise, invoke callback, return original value.
    /// Note: Exception capture from original promise is not fully implemented.
    /// </summary>
    private void EmitPromiseFinallyMoveNext(PromiseFinallyStateMachine sm, EmittedRuntime runtime)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var awaiterType = typeof(TaskAwaiter<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var callbackResultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();  // Resume after promise await
        var state1Label = il.DefineLabel();  // Resume after callback await
        var continue0Label = il.DefineLabel();
        var continue1Label = il.DefineLabel();
        var setResultLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

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

        // Check if onFinally is null
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFinallyField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Invoke callback with no args: result = InvokeCallbackNoArgs(onFinally)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.OnFinallyField);
        il.Emit(OpCodes.Call, runtime.InvokeCallbackNoArgs);
        il.Emit(OpCodes.Stloc, callbackResultLocal);

        // Check if result is Task<object?> (needs awaiting)
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Brfalse, setResultLabel);

        // Result is a Task - await it
        il.Emit(OpCodes.Ldloc, callbackResultLocal);
        il.Emit(OpCodes.Castclass, typeof(Task<object?>));
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        var callbackAwaiterLocal = il.DeclareLocal(awaiterType);
        il.Emit(OpCodes.Stloc, callbackAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, callbackAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.CallbackAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.CallbackAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue1Label);

        // Not completed - suspend at state 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.CallbackAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, awaitMethod);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 1: Resume after callback await ==========
        il.MarkLabel(state1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after callback await ==========
        il.MarkLabel(continue1Label);

        // GetResult from callback (just to throw if it failed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.CallbackAwaiterField);
        il.Emit(OpCodes.Call, awaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Pop);  // Discard result
        il.Emit(OpCodes.Br, setResultLabel);

        // ========== noCallbackLabel: no callback ==========
        il.MarkLabel(noCallbackLabel);

        // ========== setResultLabel: Return original value ==========
        il.MarkLabel(setResultLabel);

        // Set state to -2 (completed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetResult(value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ValueField);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetResult")!);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== Exception handler ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

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

