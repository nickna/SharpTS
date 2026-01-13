using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    #region PromiseAny State Machine

    /// <summary>
    /// Defines the $AnyState class for PromiseAny.
    /// </summary>
    private AnyStateClass DefineAnyStateClass(ModuleBuilder moduleBuilder)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$AnyState",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        // Fields
        var pendingCountField = typeBuilder.DefineField("PendingCount", typeof(int), FieldAttributes.Public);
        var rejectionReasonsField = typeBuilder.DefineField("RejectionReasons", typeof(List<object?>), FieldAttributes.Public);
        var tcsField = typeBuilder.DefineField("Tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Public);
        var lockField = typeBuilder.DefineField("Lock", typeof(object), FieldAttributes.Public);

        // Constructor: Initialize all fields
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(int)]  // pendingCount parameter
        );

        var ctorIL = ctor.GetILGenerator();

        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

        // this.PendingCount = pendingCount
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, pendingCountField);

        // this.RejectionReasons = new List<object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, typeof(List<object?>).GetConstructor(Type.EmptyTypes)!);
        ctorIL.Emit(OpCodes.Stfld, rejectionReasonsField);

        // this.Tcs = new TaskCompletionSource<object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, typeof(TaskCompletionSource<object?>).GetConstructor(Type.EmptyTypes)!);
        ctorIL.Emit(OpCodes.Stfld, tcsField);

        // this.Lock = new object()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIL.Emit(OpCodes.Stfld, lockField);

        ctorIL.Emit(OpCodes.Ret);

        return new AnyStateClass
        {
            Type = typeBuilder,
            PendingCountField = pendingCountField,
            RejectionReasonsField = rejectionReasonsField,
            TcsField = tcsField,
            LockField = lockField,
            Constructor = ctor
        };
    }

    /// <summary>
    /// Emits the HandleAnyCompletionShim static method.
    /// This is a shim that casts the object? state parameter to $AnyState and calls HandleAnyCompletion.
    /// Signature: void HandleAnyCompletionShim(Task&lt;object?&gt; task, object? state)
    /// </summary>
    private void EmitHandleAnyCompletionShim(ILGenerator il, AnyStateClass anyState, MethodBuilder handleAnyCompletion)
    {
        // Load task (arg0)
        il.Emit(OpCodes.Ldarg_0);
        // Load state (arg1) and cast to $AnyState
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, anyState.Type);
        // Call HandleAnyCompletion(task, ($AnyState)state)
        il.Emit(OpCodes.Call, handleAnyCompletion);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the HandleAnyCompletion static method.
    /// Called by ContinueWith for each task in PromiseAny.
    /// </summary>
    private void EmitHandleAnyCompletion(ILGenerator il, AnyStateClass anyState)
    {
        // Parameters: arg0 = Task<object?>, arg1 = $AnyState

        // Check if task completed successfully
        var failedLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (task.IsCompletedSuccessfully) goto successLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetProperty("IsCompletedSuccessfully")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, failedLabel);

        // Success path: state.Tcs.TrySetResult(task.Result)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetProperty("Result")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod("TrySetResult")!);
        il.Emit(OpCodes.Pop);  // discard bool result
        il.Emit(OpCodes.Br, endLabel);

        // Failed path: lock and handle rejection
        il.MarkLabel(failedLabel);

        // Monitor.Enter(state.Lock)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.LockField);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [typeof(object)])!);

        // try block
        il.BeginExceptionBlock();

        // state.RejectionReasons.Add(task.Exception?.Message ?? "Unknown error")
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.RejectionReasonsField);

        // Get exception message
        var hasExceptionLabel = il.DefineLabel();
        var afterExceptionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetProperty("Exception")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasExceptionLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Unknown error");
        il.Emit(OpCodes.Br, afterExceptionLabel);

        il.MarkLabel(hasExceptionLabel);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);

        il.MarkLabel(afterExceptionLabel);
        il.Emit(OpCodes.Callvirt, typeof(List<object?>).GetMethod("Add")!);

        // state.PendingCount--
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.PendingCountField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stfld, anyState.PendingCountField);

        // if (state.PendingCount == 0)
        var notAllFailedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.PendingCountField);
        il.Emit(OpCodes.Brtrue, notAllFailedLabel);

        // All failed: state.Tcs.TrySetException(new Exception("AggregateError: All promises rejected"))
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Ldstr, "AggregateError: All promises were rejected");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!);
        il.Emit(OpCodes.Pop);  // discard bool result

        il.MarkLabel(notAllFailedLabel);
        il.Emit(OpCodes.Leave, endLabel);

        // finally: Monitor.Exit(state.Lock)
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.LockField);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [typeof(object)])!);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Defines the PromiseAny state machine type structure.
    /// </summary>
    private PromiseAnyStateMachine DefinePromiseAnyStateMachine(ModuleBuilder moduleBuilder, AnyStateClass anyState)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?>);

        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseAny_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Fields
        var stateField = typeBuilder.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = typeBuilder.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = typeBuilder.DefineField("iterable", typeof(object), FieldAttributes.Public);
        var stateObjField = typeBuilder.DefineField("anyState", anyState.Type, FieldAttributes.Public);
        var awaiterField = typeBuilder.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        // MoveNext method
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        typeBuilder.DefineMethodOverride(moveNext, typeof(IAsyncStateMachine).GetMethod("MoveNext")!);

        // SetStateMachine
        var setStateMachine = typeBuilder.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        typeBuilder.DefineMethodOverride(setStateMachine, typeof(IAsyncStateMachine).GetMethod("SetStateMachine")!);
        var setIL = setStateMachine.GetILGenerator();
        setIL.Emit(OpCodes.Ret);

        return new PromiseAnyStateMachine
        {
            Type = typeBuilder,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            StateObjField = stateObjField,
            AwaiterField = awaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the wrapper method for PromiseAny.
    /// </summary>
    private void EmitPromiseAnyWrapper(ILGenerator il, PromiseAnyStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.iterable = arg0
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.IterableField);

        // sm.<>t__builder = AsyncTaskMethodBuilder<object>.Create()
        il.Emit(OpCodes.Ldloca, smLocal);
        var createMethod = sm.BuilderType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm)
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "Start" && m.IsGenericMethod)
            .MakeGenericMethod(sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = sm.BuilderType.GetProperty("Task", BindingFlags.Public | BindingFlags.Instance)!.GetGetMethod()!;
        il.Emit(OpCodes.Call, taskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the MoveNext body for PromiseAny state machine.
    /// Creates state, fires ContinueWith per element, awaits Tcs.Task.
    /// </summary>
    private void EmitPromiseAnyMoveNext(PromiseAnyStateMachine sm, AnyStateClass anyState, MethodBuilder handleAnyCompletion)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();

        // Begin outer try block
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        // Cast iterable to List<object?>
        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.IterableField);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listLocal);

        // Get count
        var countLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // Check for empty list - throw AggregateException
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - throw exception
        il.Emit(OpCodes.Ldstr, "AggregateError: All promises were rejected");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notEmptyLabel);

        // Create $AnyState instance
        var stateLocal = il.DeclareLocal(anyState.Type);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Newobj, anyState.Constructor);
        il.Emit(OpCodes.Stloc, stateLocal);

        // Store in state machine field for later access
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Stfld, sm.StateObjField);

        // Loop through elements and set up ContinueWith
        var indexLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));
        var taskLocal = il.DeclareLocal(typeof(Task<object?>));

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // element = list[index]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // Check if element is Task<object?>
        var isTaskLabel = il.DefineLabel();
        var afterTaskSetupLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isTaskLabel);
        il.Emit(OpCodes.Pop);

        // Not a task - wrap in Task.FromResult and try to set result immediately
        // First wins, so if this is the first non-task element, it wins
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod("TrySetResult")!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, afterTaskSetupLabel);

        il.MarkLabel(isTaskLabel);
        il.Emit(OpCodes.Stloc, taskLocal);

        // Set up ContinueWith: task.ContinueWith(t => HandleAnyCompletion(t, state))
        // We'll use Action<Task<object?>> delegate pointing to HandleAnyCompletion
        // But HandleAnyCompletion also needs state, so we need a closure or different approach

        // Simpler approach: Use ContinueWith with state object
        // task.ContinueWith((t, s) => HandleAnyCompletion(t, ($AnyState)s), state)

        // Use: task.ContinueWith(HandleAnyCompletionWrapper, state, TaskContinuationOptions.ExecuteSynchronously)
        // where HandleAnyCompletionWrapper is Action<Task<object?>, object?>

        // Let's emit a wrapper method that takes (Task<object?>, object?) and calls HandleAnyCompletion
        // Actually, the handleAnyCompletion method signature needs to match Action<Task<object?>, object?>

        // For now, let's use a different approach:
        // Register completion inline using GetAwaiter().OnCompleted()

        // Get awaiter
        il.Emit(OpCodes.Ldloca, taskLocal);
        il.Emit(OpCodes.Call, typeof(Task<object?>).GetMethod("GetAwaiter")!);
        var taskAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter<object?>));
        il.Emit(OpCodes.Stloc, taskAwaiterLocal);

        // Check if already completed
        var notCompletedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, taskAwaiterLocal);
        il.Emit(OpCodes.Call, typeof(TaskAwaiter<object?>).GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notCompletedLabel);

        // Already completed - call HandleAnyCompletion directly
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Call, handleAnyCompletion);
        il.Emit(OpCodes.Br, afterTaskSetupLabel);

        il.MarkLabel(notCompletedLabel);
        // Not completed - use ContinueWith with the shim delegate

        // task.ContinueWith(Action<Task<object?>, object?>, object? state, TaskContinuationOptions)
        // Load task
        il.Emit(OpCodes.Ldloc, taskLocal);

        // Create Action<Task<object?>, object?> delegate pointing to handleAnyCompletion (which is the shim)
        // For static method: ldnull, ldftn method, newobj Action::.ctor(object, IntPtr)
        il.Emit(OpCodes.Ldnull);  // null target for static method
        il.Emit(OpCodes.Ldftn, handleAnyCompletion);  // handleAnyCompletion is actually the shim
        var actionType = typeof(Action<Task<object?>, object?>);
        var actionCtor = actionType.GetConstructor([typeof(object), typeof(IntPtr)])!;
        il.Emit(OpCodes.Newobj, actionCtor);

        // Load boxed state
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Box, anyState.Type);

        // Load TaskContinuationOptions.ExecuteSynchronously
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);

        // Call ContinueWith(Action<Task<TResult>, object?>, object?, TaskContinuationOptions)
        var continueWithMethod = typeof(Task<object?>).GetMethods()
            .First(m => m.Name == "ContinueWith" &&
                       m.GetParameters().Length == 3 &&
                       m.GetParameters()[0].ParameterType == typeof(Action<Task<object?>, object?>) &&
                       m.GetParameters()[1].ParameterType == typeof(object) &&
                       m.GetParameters()[2].ParameterType == typeof(TaskContinuationOptions));
        il.Emit(OpCodes.Callvirt, continueWithMethod);
        il.Emit(OpCodes.Pop);  // Discard the continuation task returned by ContinueWith

        il.MarkLabel(afterTaskSetupLabel);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Now await state.Tcs.Task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateObjField);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);

        var awaiterLocal = il.DeclareLocal(sm.AwaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.AwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continueLabel);

        // Not completed - suspend
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(sm.AwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);

        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after await ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== continueLabel: Completed ==========
        il.MarkLabel(continueLabel);

        // GetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Set state to -2 and SetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
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

