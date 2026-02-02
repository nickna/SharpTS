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
            _types.Object
        );

        // Fields
        var pendingCountField = typeBuilder.DefineField("PendingCount", _types.Int32, FieldAttributes.Public);
        var rejectionReasonsField = typeBuilder.DefineField("RejectionReasons", _types.ListOfObject, FieldAttributes.Public);
        var tcsField = typeBuilder.DefineField("Tcs", _types.TaskCompletionSourceOfObject, FieldAttributes.Public);
        var lockField = typeBuilder.DefineField("Lock", _types.Object, FieldAttributes.Public);
        var ctsField = typeBuilder.DefineField("Cts", _types.CancellationTokenSource, FieldAttributes.Public);

        // Constructor: Initialize all fields
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]  // pendingCount parameter
        );

        var ctorIL = ctor.GetILGenerator();

        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // this.PendingCount = pendingCount
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, pendingCountField);

        // this.RejectionReasons = new List<object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        ctorIL.Emit(OpCodes.Stfld, rejectionReasonsField);

        // this.Tcs = new TaskCompletionSource<object?>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
        ctorIL.Emit(OpCodes.Stfld, tcsField);

        // this.Lock = new object()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Stfld, lockField);

        // this.Cts = new CancellationTokenSource()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.CancellationTokenSource));
        ctorIL.Emit(OpCodes.Stfld, ctsField);

        ctorIL.Emit(OpCodes.Ret);

        return new AnyStateClass
        {
            Type = typeBuilder,
            PendingCountField = pendingCountField,
            RejectionReasonsField = rejectionReasonsField,
            TcsField = tcsField,
            LockField = lockField,
            CtsField = ctsField,
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
    private void EmitHandleAnyCompletion(ILGenerator il, AnyStateClass anyState, EmittedRuntime runtime)
    {
        // Parameters: arg0 = Task<object?>, arg1 = $AnyState

        // Check if task completed successfully
        var failedLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (task.IsCompletedSuccessfully) goto successLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCompletedSuccessfully").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, failedLabel);

        // Success path: if (state.Tcs.TrySetResult(task.Result)) state.Cts.Cancel();
        // This cancels remaining pending continuations to prevent orphaned tasks.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskOfObject, "Result").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetResult", [_types.Object]));

        // If TrySetResult returned true (we were first), cancel remaining continuations
        var skipCancelLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipCancelLabel);

        // state.Cts.Cancel() - cancel pending continuations to allow clean process exit
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.CtsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.CancellationTokenSource, "Cancel"));

        il.MarkLabel(skipCancelLabel);
        il.Emit(OpCodes.Br, endLabel);

        // Failed path: lock and handle rejection
        il.MarkLabel(failedLabel);

        // Monitor.Enter(state.Lock)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.LockField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Monitor, "Enter", [_types.Object]));

        // try block
        il.BeginExceptionBlock();

        // state.RejectionReasons.Add(task.Exception?.Message ?? "Unknown error")
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.RejectionReasonsField);

        // Get exception message
        var hasExceptionLabel = il.DefineLabel();
        var afterExceptionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "Exception").GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, hasExceptionLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "Unknown error");
        il.Emit(OpCodes.Br, afterExceptionLabel);

        il.MarkLabel(hasExceptionLabel);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);

        il.MarkLabel(afterExceptionLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));

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

        // All failed: state.Tcs.TrySetException($Runtime.CreateException(new $AggregateError(errors, null)))
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        // Create $AggregateError(state.RejectionReasons, null)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.RejectionReasonsField);  // errors list
        il.Emit(OpCodes.Ldnull);  // message (use default)
        il.Emit(OpCodes.Newobj, runtime.TSAggregateErrorCtor);
        // Wrap with CreateException to store in Data["__tsValue"]
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetException", [_types.Exception]));
        il.Emit(OpCodes.Pop);  // discard bool result

        // Cancel the token source for cleanup (all continuations have run, but ensures proper disposal)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.CtsField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.CancellationTokenSource, "Cancel"));

        il.MarkLabel(notAllFailedLabel);
        il.Emit(OpCodes.Leave, endLabel);

        // finally: Monitor.Exit(state.Lock)
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, anyState.LockField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Monitor, "Exit", [_types.Object]));
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Defines the PromiseAny state machine type structure.
    /// </summary>
    private PromiseAnyStateMachine DefinePromiseAnyStateMachine(ModuleBuilder moduleBuilder, AnyStateClass anyState)
    {
        var builderType = _types.AsyncTaskMethodBuilderOfObject;
        var awaiterType = _types.TaskAwaiterOfObject;

        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseAny_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // Fields
        var stateField = typeBuilder.DefineField("<>1__state", _types.Int32, FieldAttributes.Public);
        var builderField = typeBuilder.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = typeBuilder.DefineField("iterable", _types.Object, FieldAttributes.Public);
        var stateObjField = typeBuilder.DefineField("anyState", anyState.Type, FieldAttributes.Public);
        var awaiterField = typeBuilder.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        // MoveNext method
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            []
        );
        typeBuilder.DefineMethodOverride(moveNext, _types.GetMethodNoParams(_types.IAsyncStateMachine, "MoveNext"));

        // SetStateMachine
        var setStateMachine = typeBuilder.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );
        typeBuilder.DefineMethodOverride(setStateMachine, _types.GetMethod(_types.IAsyncStateMachine, "SetStateMachine", [_types.IAsyncStateMachine]));
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
        var createMethod = _types.GetMethodStatic(sm.BuilderType, "Create");
        il.Emit(OpCodes.Call, createMethod);
        il.Emit(OpCodes.Stfld, sm.BuilderField);

        // sm.<>t__builder.Start(ref sm)
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloca, smLocal);
        var startMethod = _types.GetGenericMethod(sm.BuilderType, "Start")
            .MakeGenericMethod(sm.Type);
        il.Emit(OpCodes.Call, startMethod);

        // return sm.<>t__builder.Task
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        var taskGetter = _types.GetPropertyGetter(sm.BuilderType, "Task");
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
        var listType = _types.ListOfObject;

        // Local variables
        var exceptionLocal = il.DeclareLocal(_types.Exception);
        var resultLocal = il.DeclareLocal(_types.Object);

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
        var countLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // Check for empty list - throw AggregateException
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - throw exception
        il.Emit(OpCodes.Ldstr, "AggregateError: All promises were rejected");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String]));
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
        var indexLocal = il.DeclareLocal(_types.Int32);
        var elementLocal = il.DeclareLocal(_types.Object);
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);

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
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // Check if element is Task<object?>
        var isTaskLabel = il.DefineLabel();
        var afterTaskSetupLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isTaskLabel);
        il.Emit(OpCodes.Pop);

        // Not a task - wrap in Task.FromResult and try to set result immediately
        // First wins, so if this is the first non-task element, it wins
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldfld, anyState.TcsField);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetResult", [_types.Object]));
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

        // Get awaiter (Task is reference type, use Ldloc not Ldloca)
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter"));
        var taskAwaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
        il.Emit(OpCodes.Stloc, taskAwaiterLocal);

        // Check if already completed
        var notCompletedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, taskAwaiterLocal);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.TaskAwaiterOfObject, "IsCompleted"));
        il.Emit(OpCodes.Brfalse, notCompletedLabel);

        // Already completed - call HandleAnyCompletion directly
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Call, handleAnyCompletion);
        il.Emit(OpCodes.Br, afterTaskSetupLabel);

        il.MarkLabel(notCompletedLabel);
        // Not completed - use ContinueWith with cancellation token support
        // This ensures continuations can be cancelled when the first promise fulfills,
        // preventing orphaned tasks from keeping the process alive.

        // task.ContinueWith(action, state, cancellationToken, options, scheduler)
        // Load task
        il.Emit(OpCodes.Ldloc, taskLocal);

        // Create Action<Task<object?>, object?> delegate pointing to handleAnyCompletion (which is the shim)
        // For static method: ldnull, ldftn method, newobj Action::.ctor(object, IntPtr)
        il.Emit(OpCodes.Ldnull);  // null target for static method
        il.Emit(OpCodes.Ldftn, handleAnyCompletion);  // handleAnyCompletion is actually the shim
        var actionType = _types.ActionTaskOfObjectAndObject;
        var actionCtor = actionType.GetConstructor([_types.Object, _types.IntPtr])!;
        il.Emit(OpCodes.Newobj, actionCtor);

        // Load state (already a reference type, no boxing needed)
        il.Emit(OpCodes.Ldloc, stateLocal);

        // Load state.Cts.Token for cancellation support
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldfld, anyState.CtsField);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.CancellationTokenSource, "Token"));

        // Load TaskContinuationOptions.ExecuteSynchronously
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);

        // Load TaskScheduler.Default
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(_types.TaskScheduler, "Default"));

        // Call ContinueWith(Action<Task<TResult>, object?>, object?, CancellationToken, TaskContinuationOptions, TaskScheduler)
        var continueWithMethod = _types.GetMethod(_types.TaskOfObject, "ContinueWith",
            [_types.ActionTaskOfObjectAndObject, _types.Object, _types.CancellationToken, _types.TaskContinuationOptions, _types.TaskScheduler]);
        il.Emit(OpCodes.Callvirt, continueWithMethod);
        il.Emit(OpCodes.Pop);  // Continuation task is tracked via cancellation token, safe to discard

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
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.TaskCompletionSourceOfObject, "Task"));
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter"));

        var awaiterLocal = il.DeclareLocal(sm.AwaiterType);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, awaiterLocal);
        il.Emit(OpCodes.Stfld, sm.AwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, _types.GetPropertyGetter(sm.AwaiterType, "IsCompleted"));
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
        var awaitMethod = _types.GetGenericMethod(sm.BuilderType, "AwaitUnsafeOnCompleted")
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
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(sm.AwaiterType, "GetResult"));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Set state to -2 and SetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetResult", [_types.Object]));
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== Exception handler ==========
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exceptionLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(sm.BuilderType, "SetException", [_types.Exception]));
        il.Emit(OpCodes.Leave, returnLabel);

        il.EndExceptionBlock();

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}

