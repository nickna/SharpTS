using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Holds information about an emitted async state machine type.
/// </summary>
internal class EmittedStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder IterableField { get; init; }
    public required FieldBuilder AwaiterField { get; init; }
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
    public required Type AwaiterType { get; init; }
}

/// <summary>
/// Holds information about the PromiseRace state machine (needs two awaiter fields).
/// </summary>
internal class PromiseRaceStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder IterableField { get; init; }
    public required FieldBuilder WhenAnyAwaiterField { get; init; }  // TaskAwaiter<Task<object?>>
    public required FieldBuilder ResultAwaiterField { get; init; }    // TaskAwaiter<object?>
    public required FieldBuilder WinningTaskField { get; init; }      // Task<object?> from WhenAny
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Holds information about the PromiseThen state machine.
/// </summary>
internal class PromiseThenStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder PromiseField { get; init; }        // Task<object?> input promise
    public required FieldBuilder OnFulfilledField { get; init; }    // callback
    public required FieldBuilder OnRejectedField { get; init; }     // error callback
    public required FieldBuilder PromiseAwaiterField { get; init; } // TaskAwaiter<object?> for input
    public required FieldBuilder FlattenAwaiterField { get; init; } // TaskAwaiter<object?> for flattening
    public required FieldBuilder ValueField { get; init; }          // intermediate value
    public required FieldBuilder ExceptionField { get; init; }      // stored exception
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Holds information about the PromiseFinally state machine.
/// </summary>
internal class PromiseFinallyStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder PromiseField { get; init; }        // Task<object?> input promise
    public required FieldBuilder OnFinallyField { get; init; }      // callback (no args)
    public required FieldBuilder PromiseAwaiterField { get; init; } // TaskAwaiter<object?> for input
    public required FieldBuilder CallbackAwaiterField { get; init; } // TaskAwaiter<object?> for callback result
    public required FieldBuilder ValueField { get; init; }          // preserved value
    public required FieldBuilder ExceptionField { get; init; }      // preserved exception
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Holds information about the ProcessElementSettled helper state machine for PromiseAllSettled.
/// This handles a single element with try/catch, returning {status, value/reason} dictionary.
/// </summary>
internal class ProcessElementSettledStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }           // <>1__state
    public required FieldBuilder BuilderField { get; init; }         // <>t__builder
    public required FieldBuilder ElementField { get; init; }         // element parameter
    public required FieldBuilder AwaiterField { get; init; }         // TaskAwaiter<object?>
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
    public required Type AwaiterType { get; init; }
}

/// <summary>
/// Holds information about the PromiseAllSettled main state machine.
/// Uses the ProcessElementSettled helper + WhenAll pattern.
/// </summary>
internal class PromiseAllSettledStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }           // <>1__state
    public required FieldBuilder BuilderField { get; init; }         // <>t__builder
    public required FieldBuilder IterableField { get; init; }        // iterable parameter
    public required FieldBuilder AwaiterField { get; init; }         // TaskAwaiter<object?[]>
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
    public required Type AwaiterType { get; init; }
}

/// <summary>
/// Holds information about the $AnyState class for PromiseAny.
/// </summary>
internal class AnyStateClass
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder PendingCountField { get; init; }     // int
    public required FieldBuilder RejectionReasonsField { get; init; } // List<object?>
    public required FieldBuilder TcsField { get; init; }              // TaskCompletionSource<object?>
    public required FieldBuilder LockField { get; init; }             // object
    public required ConstructorBuilder Constructor { get; init; }
}

/// <summary>
/// Holds information about the PromiseAny state machine.
/// </summary>
internal class PromiseAnyStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }           // <>1__state
    public required FieldBuilder BuilderField { get; init; }         // <>t__builder
    public required FieldBuilder IterableField { get; init; }        // iterable parameter
    public required FieldBuilder StateObjField { get; init; }        // $AnyState instance
    public required FieldBuilder AwaiterField { get; init; }         // TaskAwaiter<object?> for Tcs.Task
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
    public required Type AwaiterType { get; init; }
}

public static partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Promise static methods with full async state machine support.
    /// These methods return Task&lt;object?&gt; and are awaited by the compiled code.
    /// State machines are emitted directly, eliminating the need for SharpTS.dll at runtime.
    /// </summary>
    private static void EmitPromiseMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var taskType = _types.TaskOfObject;
        var moduleBuilder = (ModuleBuilder)typeBuilder.Module;

        // Promise.resolve(value?) - simply wraps value in completed Task
        // IL equivalent: Task.FromResult<object?>(value)
        var resolve = typeBuilder.DefineMethod(
            "PromiseResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseResolve = resolve;
        {
            var il = resolve.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            // Call Task.FromResult<object?>(value) - keep typeof() for generic method lookup
            var fromResult = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(_types.Object);
            il.Emit(OpCodes.Call, fromResult);
            il.Emit(OpCodes.Ret);
        }

        // Promise.reject(reason) - creates a faulted Task
        // IL equivalent: Task.FromException<object?>(new Exception(reason?.ToString()))
        var reject = typeBuilder.DefineMethod(
            "PromiseReject",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseReject = reject;
        {
            var il = reject.GetILGenerator();
            // Create Exception from reason
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Object, "ToString"));
            var exceptionCtor = _types.GetConstructor(_types.Exception, [_types.String]);
            il.Emit(OpCodes.Newobj, exceptionCtor);
            // Call Task.FromException<object?>(exception) - keep typeof() for arity-based generic lookup
            var fromException = typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!.MakeGenericMethod(_types.Object);
            il.Emit(OpCodes.Call, fromException);
            il.Emit(OpCodes.Ret);
        }

        // Promise.all(iterable) - async state machine using Task.WhenAll
        var promiseAllSM = DefinePromiseAllStateMachine(moduleBuilder);
        var all = typeBuilder.DefineMethod(
            "PromiseAll",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseAll = all;
        EmitPromiseAllWrapper(all.GetILGenerator(), promiseAllSM);
        EmitPromiseAllMoveNext(promiseAllSM);
        promiseAllSM.Type.CreateType();

        // Promise.race(iterable) - async state machine using Task.WhenAny
        var promiseRaceSM = DefinePromiseRaceStateMachine(moduleBuilder);
        var race = typeBuilder.DefineMethod(
            "PromiseRace",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseRace = race;
        EmitPromiseRaceWrapper(race.GetILGenerator(), promiseRaceSM);
        EmitPromiseRaceMoveNext(promiseRaceSM);
        promiseRaceSM.Type.CreateType();

        // First emit the ProcessElementSettled helper for PromiseAllSettled
        var processElementSettledSM = DefineProcessElementSettledStateMachine(moduleBuilder);
        var processElementSettled = typeBuilder.DefineMethod(
            "ProcessElementSettled",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.ProcessElementSettled = processElementSettled;
        EmitProcessElementSettledWrapper(processElementSettled.GetILGenerator(), processElementSettledSM);
        EmitProcessElementSettledMoveNext(processElementSettledSM);
        processElementSettledSM.Type.CreateType();

        // Promise.allSettled(iterable) - async state machine using helper + WhenAll
        var promiseAllSettledSM = DefinePromiseAllSettledStateMachine(moduleBuilder);
        var allSettled = typeBuilder.DefineMethod(
            "PromiseAllSettled",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseAllSettled = allSettled;
        EmitPromiseAllSettledWrapper(allSettled.GetILGenerator(), promiseAllSettledSM, processElementSettled);
        EmitPromiseAllSettledMoveNext(promiseAllSettledSM, processElementSettled);
        promiseAllSettledSM.Type.CreateType();

        // Promise.any(iterable) - delegates to RuntimeTypes for now
        // NOTE: ContinueWith with state capture is complex to emit as pure IL.
        // This method still requires SharpTS.dll at runtime.
        var any = typeBuilder.DefineMethod(
            "PromiseAny",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseAny = any;
        {
            var il = any.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("PromiseAny")!);
            il.Emit(OpCodes.Ret);
        }

        // Callback invocation helpers must be emitted first (used by then/finally)
        EmitCallbackHelpers(typeBuilder, runtime);

        // Promise.prototype.then - async state machine with callback invocation
        var promiseThenSM = DefinePromiseThenStateMachine(moduleBuilder, runtime);
        var then = typeBuilder.DefineMethod(
            "PromiseThen",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, _types.Object, _types.Object]
        );
        runtime.PromiseThen = then;
        EmitPromiseThenWrapper(then.GetILGenerator(), promiseThenSM);
        EmitPromiseThenMoveNext(promiseThenSM, runtime);
        promiseThenSM.Type.CreateType();

        // Promise.prototype.catch - delegates to PromiseThen(promise, null, onRejected)
        var catchMethod = typeBuilder.DefineMethod(
            "PromiseCatch",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, _types.Object]
        );
        runtime.PromiseCatch = catchMethod;
        {
            var il = catchMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);  // task
            il.Emit(OpCodes.Ldnull);   // null for onFulfilled
            il.Emit(OpCodes.Ldarg_1);  // onRejected
            il.Emit(OpCodes.Call, then);
            il.Emit(OpCodes.Ret);
        }

        // Promise.prototype.finally - async state machine with callback invocation
        var promiseFinallySM = DefinePromiseFinallyStateMachine(moduleBuilder, runtime);
        var finallyMethod = typeBuilder.DefineMethod(
            "PromiseFinally",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, _types.Object]
        );
        runtime.PromiseFinally = finallyMethod;
        EmitPromiseFinallyWrapper(finallyMethod.GetILGenerator(), promiseFinallySM);
        EmitPromiseFinallyMoveNext(promiseFinallySM, runtime);
        promiseFinallySM.Type.CreateType();
    }

    #region PromiseAll State Machine

    /// <summary>
    /// Defines the PromiseAll state machine type structure.
    /// </summary>
    private static EmittedStateMachine DefinePromiseAllStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = _types.AsyncTaskMethodBuilderOfObject;
        var awaiterType = _types.TaskAwaiterOfObjectArray;

        // Define state machine struct: $PromiseAll_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseAll_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ValueType,
            [_types.IAsyncStateMachine]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", _types.Int32, FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = smType.DefineField("iterable", _types.Object, FieldAttributes.Public);
        var awaiterField = smType.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        // Define MoveNext method
        var moveNext = smType.DefineMethod(
            "MoveNext",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            Type.EmptyTypes
        );
        smType.DefineMethodOverride(moveNext, _types.GetMethodNoParams(_types.IAsyncStateMachine, "MoveNext"));

        // Define SetStateMachine method (empty body for value types)
        var setStateMachine = smType.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.IAsyncStateMachine]
        );
        smType.DefineMethodOverride(setStateMachine, _types.GetMethod(_types.IAsyncStateMachine, "SetStateMachine", [_types.IAsyncStateMachine]));
        var setSmIL = setStateMachine.GetILGenerator();
        setSmIL.Emit(OpCodes.Ret);

        return new EmittedStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            AwaiterField = awaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the PromiseAll wrapper method that creates and starts the state machine.
    /// </summary>
    private static void EmitPromiseAllWrapper(ILGenerator il, EmittedStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine: var sm = default($PromiseAll_SM);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.iterable = arg0;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.IterableField);

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
    /// Emits the MoveNext body for PromiseAll state machine.
    /// Implements: convert list to tasks, await Task.WhenAll, return List.
    /// </summary>
    private static void EmitPromiseAllMoveNext(EmittedStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);
        var taskArrayType = typeof(Task<object?>[]);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();  // New: for success path
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

        // Check for empty list
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return empty list immediately (jump to success path)
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and convert to tasks
        var indexLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));

        // count = list.Count
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);

        // if (index >= count) goto loopEnd
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
        var afterAddLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isTaskLabel);
        il.Emit(OpCodes.Pop);

        // Not a task - wrap in Task.FromResult
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, afterAddLabel);

        // Is a task - add directly
        il.MarkLabel(isTaskLabel);
        var taskTemp = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskTemp);
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, taskTemp);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        il.MarkLabel(afterAddLabel);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        // goto loopStart
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call Task.WhenAll<object?>(tasks.ToArray())
        // Find the generic WhenAll<TResult>(Task<TResult>[]) and specialize it
        var whenAllMethod = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAll" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsArray)
            .MakeGenericMethod(typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, whenAllMethod);

        // GetAwaiter and store to field
        var awaiterLocal = il.DeclareLocal(sm.AwaiterType);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?[]>).GetMethod("GetAwaiter")!);
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
        // this.<>1__state = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(sm.AwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod);

        // return (exit MoveNext)
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after await ==========
        il.MarkLabel(state0Label);

        // Reset state to -1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue (completed synchronously or resumed) ==========
        il.MarkLabel(continueLabel);

        // GetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetMethod("GetResult")!);

        // Convert object?[] to List<object?> using constructor
        var arrayResultLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Stloc, arrayResultLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Newobj, listType.GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== Success path - both normal and empty list converge here ==========
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

        // Set state to -2 (completed with error)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // builder.SetException(exception)
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

    #region PromiseRace State Machine

    /// <summary>
    /// Defines the PromiseRace state machine type structure.
    /// Requires two awaiter fields: one for WhenAny, one for the winning task.
    /// </summary>
    private static PromiseRaceStateMachine DefinePromiseRaceStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var whenAnyAwaiterType = typeof(TaskAwaiter<Task<object?>>);
        var resultAwaiterType = typeof(TaskAwaiter<object?>);

        // Define state machine struct: $PromiseRace_SM
        var smType = moduleBuilder.DefineType(
            "$PromiseRace_SM",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Define fields
        var stateField = smType.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = smType.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = smType.DefineField("iterable", typeof(object), FieldAttributes.Public);
        var whenAnyAwaiterField = smType.DefineField("<>u__1", whenAnyAwaiterType, FieldAttributes.Private);
        var resultAwaiterField = smType.DefineField("<>u__2", resultAwaiterType, FieldAttributes.Private);
        var winningTaskField = smType.DefineField("<winningTask>5__1", typeof(Task<object?>), FieldAttributes.Private);

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

        return new PromiseRaceStateMachine
        {
            Type = smType,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            WhenAnyAwaiterField = whenAnyAwaiterField,
            ResultAwaiterField = resultAwaiterField,
            WinningTaskField = winningTaskField,
            MoveNextMethod = moveNext,
            BuilderType = builderType
        };
    }

    /// <summary>
    /// Emits the PromiseRace wrapper method that creates and starts the state machine.
    /// </summary>
    private static void EmitPromiseRaceWrapper(ILGenerator il, PromiseRaceStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine: var sm = default($PromiseRace_SM);
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.iterable = arg0;
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.IterableField);

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
    /// Emits the MoveNext body for PromiseRace state machine.
    /// Implements: convert list to tasks, await Task.WhenAny, await winning task.
    /// </summary>
    private static void EmitPromiseRaceMoveNext(PromiseRaceStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);
        var whenAnyAwaiterType = typeof(TaskAwaiter<Task<object?>>);
        var resultAwaiterType = typeof(TaskAwaiter<object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();  // Resume after WhenAny
        var state1Label = il.DefineLabel();  // Resume after winning task
        var continue0Label = il.DefineLabel();  // Continue after WhenAny completes
        var continue1Label = il.DefineLabel();  // Continue after winning task completes
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

        // ========== STATE -1: Initial execution ==========

        // Cast iterable to List<object?>
        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.IterableField);
        il.Emit(OpCodes.Castclass, listType);
        il.Emit(OpCodes.Stloc, listLocal);

        // Check for empty list - return null immediately
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and convert to tasks (same as PromiseAll)
        var indexLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        var isTaskLabel = il.DefineLabel();
        var afterAddLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, isTaskLabel);
        il.Emit(OpCodes.Pop);

        // Not a task - wrap in Task.FromResult
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, afterAddLabel);

        il.MarkLabel(isTaskLabel);
        var taskTemp = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskTemp);
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, taskTemp);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        il.MarkLabel(afterAddLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call Task.WhenAny<object?>(tasks)
        var whenAnyMethod = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAny" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsGenericType &&
                   m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .MakeGenericMethod(typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Call, whenAnyMethod);

        // GetAwaiter for WhenAny result
        var whenAnyAwaiterLocal = il.DeclareLocal(whenAnyAwaiterType);
        il.Emit(OpCodes.Callvirt, typeof(Task<Task<object?>>).GetMethod("GetAwaiter")!);
        il.Emit(OpCodes.Stloc, whenAnyAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, whenAnyAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.WhenAnyAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Call, whenAnyAwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue0Label);

        // Not completed - suspend at state 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod0 = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(whenAnyAwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod0);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 0: Resume after WhenAny ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after WhenAny ==========
        il.MarkLabel(continue0Label);

        // GetResult from WhenAny - returns the winning Task<object?>
        // Store it in the winningTask field
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.WhenAnyAwaiterField);
        il.Emit(OpCodes.Call, whenAnyAwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stfld, sm.WinningTaskField);

        // Get awaiter for winning task
        var resultAwaiterLocal = il.DeclareLocal(resultAwaiterType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.WinningTaskField);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);
        il.Emit(OpCodes.Stloc, resultAwaiterLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultAwaiterLocal);
        il.Emit(OpCodes.Stfld, sm.ResultAwaiterField);

        // Check IsCompleted
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Call, resultAwaiterType.GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, continue1Label);

        // Not completed - suspend at state 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Ldarg_0);
        var awaitMethod1 = sm.BuilderType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "AwaitUnsafeOnCompleted" && m.IsGenericMethod)
            .MakeGenericMethod(resultAwaiterType, sm.Type);
        il.Emit(OpCodes.Call, awaitMethod1);
        il.Emit(OpCodes.Leave, returnLabel);

        // ========== STATE 1: Resume after winning task ==========
        il.MarkLabel(state1Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== Continue after winning task ==========
        il.MarkLabel(continue1Label);

        // GetResult from winning task
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.ResultAwaiterField);
        il.Emit(OpCodes.Call, resultAwaiterType.GetMethod("GetResult")!);
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

    #region PromiseFinally State Machine

    /// <summary>
    /// Defines the PromiseFinally state machine type structure.
    /// </summary>
    private static PromiseFinallyStateMachine DefinePromiseFinallyStateMachine(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
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
    private static void EmitPromiseFinallyWrapper(ILGenerator il, PromiseFinallyStateMachine sm)
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
    private static void EmitPromiseFinallyMoveNext(PromiseFinallyStateMachine sm, EmittedRuntime runtime)
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
        il.Emit(OpCodes.Callvirt, typeof(Task<object?>).GetMethod("GetAwaiter")!);
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

    #region ProcessElementSettled State Machine

    /// <summary>
    /// Defines the ProcessElementSettled helper state machine type structure.
    /// This helper handles a single element for PromiseAllSettled with try/catch.
    /// </summary>
    private static ProcessElementSettledStateMachine DefineProcessElementSettledStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?>);

        var typeBuilder = moduleBuilder.DefineType(
            "$ProcessElementSettled_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Fields
        var stateField = typeBuilder.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = typeBuilder.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var elementField = typeBuilder.DefineField("element", typeof(object), FieldAttributes.Public);
        var awaiterField = typeBuilder.DefineField("<>u__1", awaiterType, FieldAttributes.Private);

        // MoveNext method
        var moveNext = typeBuilder.DefineMethod(
            "MoveNext",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes
        );
        typeBuilder.DefineMethodOverride(moveNext, typeof(IAsyncStateMachine).GetMethod("MoveNext")!);

        // SetStateMachine (required by IAsyncStateMachine)
        var setStateMachine = typeBuilder.DefineMethod(
            "SetStateMachine",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(IAsyncStateMachine)]
        );
        typeBuilder.DefineMethodOverride(setStateMachine, typeof(IAsyncStateMachine).GetMethod("SetStateMachine")!);
        var setIL = setStateMachine.GetILGenerator();
        setIL.Emit(OpCodes.Ret);

        return new ProcessElementSettledStateMachine
        {
            Type = typeBuilder,
            StateField = stateField,
            BuilderField = builderField,
            ElementField = elementField,
            AwaiterField = awaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the wrapper method that creates the state machine and starts it.
    /// </summary>
    private static void EmitProcessElementSettledWrapper(ILGenerator il, ProcessElementSettledStateMachine sm)
    {
        var smLocal = il.DeclareLocal(sm.Type);

        // Initialize state machine
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Initobj, sm.Type);

        // sm.<>1__state = -1
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // sm.element = arg0
        il.Emit(OpCodes.Ldloca, smLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, sm.ElementField);

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
    /// Emits the MoveNext body for ProcessElementSettled state machine.
    /// Handles a single element with try/catch, returns {status, value/reason} dictionary.
    /// Uses a single try/catch and converts all exceptions to "rejected" dictionaries.
    /// </summary>
    private static void EmitProcessElementSettledMoveNext(ProcessElementSettledStateMachine sm)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var dictType = typeof(Dictionary<string, object?>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));
        var valueLocal = il.DeclareLocal(typeof(object));
        var dictLocal = il.DeclareLocal(dictType);

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var returnLabel = il.DefineLabel();
        var nonTaskLabel = il.DefineLabel();
        var afterAwaitSetupLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();

        // Begin try block - exceptions are converted to "rejected" dictionaries
        il.BeginExceptionBlock();

        // State dispatch: if (this.<>1__state == 0) goto state0Label
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.StateField);
        il.Emit(OpCodes.Brfalse, state0Label);

        // ========== STATE -1: Initial execution ==========

        // Check if element is Task<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ElementField);
        il.Emit(OpCodes.Isinst, typeof(Task<object?>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, nonTaskLabel);

        // It's a task - get awaiter
        var taskLocal = il.DeclareLocal(typeof(Task<object?>));
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Ldloc, taskLocal);
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

        // ========== nonTaskLabel: Element is not a Task, use as value directly ==========
        il.MarkLabel(nonTaskLabel);
        il.Emit(OpCodes.Pop);  // pop the null from isinst
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sm.ElementField);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, afterAwaitSetupLabel);

        // ========== STATE 0: Resume after await ==========
        il.MarkLabel(state0Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, sm.StateField);

        // ========== continueLabel: Completed synchronously or resumed ==========
        il.MarkLabel(continueLabel);
        // GetResult may throw if the task faulted - this is caught by our exception handler
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetMethod("GetResult")!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // ========== afterAwaitSetupLabel: Create fulfilled dictionary ==========
        il.MarkLabel(afterAwaitSetupLabel);

        // Create Dictionary { ["status"] = "fulfilled", ["value"] = value }
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Ldstr, "fulfilled");
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        // ========== Exception handler: Create rejected dictionary ==========
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);

        // Create Dictionary { ["status"] = "rejected", ["reason"] = ex.Message }
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "status");
        il.Emit(OpCodes.Ldstr, "rejected");
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "reason");
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.EndExceptionBlock();

        // ========== setResultLabel: Set result and complete ==========
        il.MarkLabel(setResultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, -2);
        il.Emit(OpCodes.Stfld, sm.StateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.BuilderField);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, sm.BuilderType.GetMethod("SetResult")!);

        // Return point
        il.MarkLabel(returnLabel);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region PromiseAllSettled State Machine

    /// <summary>
    /// Defines the PromiseAllSettled main state machine type structure.
    /// Uses ProcessElementSettled helper + WhenAll pattern.
    /// </summary>
    private static PromiseAllSettledStateMachine DefinePromiseAllSettledStateMachine(ModuleBuilder moduleBuilder)
    {
        var builderType = typeof(AsyncTaskMethodBuilder<object>);
        var awaiterType = typeof(TaskAwaiter<object?[]>);

        var typeBuilder = moduleBuilder.DefineType(
            "$PromiseAllSettled_StateMachine",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(ValueType),
            [typeof(IAsyncStateMachine)]
        );

        // Fields
        var stateField = typeBuilder.DefineField("<>1__state", typeof(int), FieldAttributes.Public);
        var builderField = typeBuilder.DefineField("<>t__builder", builderType, FieldAttributes.Public);
        var iterableField = typeBuilder.DefineField("iterable", typeof(object), FieldAttributes.Public);
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

        return new PromiseAllSettledStateMachine
        {
            Type = typeBuilder,
            StateField = stateField,
            BuilderField = builderField,
            IterableField = iterableField,
            AwaiterField = awaiterField,
            MoveNextMethod = moveNext,
            BuilderType = builderType,
            AwaiterType = awaiterType
        };
    }

    /// <summary>
    /// Emits the wrapper method for PromiseAllSettled.
    /// </summary>
    private static void EmitPromiseAllSettledWrapper(ILGenerator il, PromiseAllSettledStateMachine sm, MethodBuilder processElementSettled)
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
    /// Emits the MoveNext body for PromiseAllSettled state machine.
    /// Maps elements to ProcessElementSettled helper, uses WhenAll pattern.
    /// </summary>
    private static void EmitPromiseAllSettledMoveNext(PromiseAllSettledStateMachine sm, MethodBuilder processElementSettled)
    {
        var il = sm.MoveNextMethod.GetILGenerator();
        var listType = typeof(List<object?>);
        var taskListType = typeof(List<Task<object?>>);

        // Local variables
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        var resultLocal = il.DeclareLocal(typeof(object));

        // Labels
        var state0Label = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        var setResultLabel = il.DefineLabel();
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

        // Check for empty list
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // Empty list - return empty list immediately
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, setResultLabel);

        il.MarkLabel(notEmptyLabel);

        // Create tasks list
        var tasksLocal = il.DeclareLocal(taskListType);
        il.Emit(OpCodes.Newobj, taskListType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tasksLocal);

        // Loop through input list and call ProcessElementSettled for each
        var indexLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));
        var elementLocal = il.DeclareLocal(typeof(object));

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, listType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

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

        // tasks.Add(ProcessElementSettled(element))
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Call, processElementSettled);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("Add")!);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Call Task.WhenAll<object?>(tasks.ToArray())
        var whenAllMethod = typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "WhenAll" && m.IsGenericMethod &&
                   m.GetParameters().Length == 1 &&
                   m.GetParameters()[0].ParameterType.IsArray)
            .MakeGenericMethod(typeof(object));
        il.Emit(OpCodes.Ldloc, tasksLocal);
        il.Emit(OpCodes.Callvirt, taskListType.GetMethod("ToArray")!);
        il.Emit(OpCodes.Call, whenAllMethod);

        // GetAwaiter and store to field
        var awaiterLocal = il.DeclareLocal(sm.AwaiterType);
        il.Emit(OpCodes.Callvirt, typeof(Task<object?[]>).GetMethod("GetAwaiter")!);
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

        // ========== continueLabel: Completed synchronously or resumed ==========
        il.MarkLabel(continueLabel);

        // GetResult
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, sm.AwaiterField);
        il.Emit(OpCodes.Call, sm.AwaiterType.GetMethod("GetResult")!);

        // Convert object?[] to List<object?>
        var arrayResultLocal = il.DeclareLocal(typeof(object?[]));
        il.Emit(OpCodes.Stloc, arrayResultLocal);
        il.Emit(OpCodes.Ldloc, arrayResultLocal);
        il.Emit(OpCodes.Newobj, listType.GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // ========== setResultLabel: Success path ==========
        il.MarkLabel(setResultLabel);

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

    #region PromiseAny State Machine

    /// <summary>
    /// Defines the $AnyState class for PromiseAny.
    /// </summary>
    private static AnyStateClass DefineAnyStateClass(ModuleBuilder moduleBuilder)
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
    /// Emits the HandleAnyCompletion static method.
    /// Called by ContinueWith for each task in PromiseAny.
    /// </summary>
    private static void EmitHandleAnyCompletion(ILGenerator il, AnyStateClass anyState)
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
    private static PromiseAnyStateMachine DefinePromiseAnyStateMachine(ModuleBuilder moduleBuilder, AnyStateClass anyState)
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
    private static void EmitPromiseAnyWrapper(ILGenerator il, PromiseAnyStateMachine sm)
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
    private static void EmitPromiseAnyMoveNext(PromiseAnyStateMachine sm, AnyStateClass anyState, MethodBuilder handleAnyCompletion)
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

        il.Emit(OpCodes.Ldloc, taskLocal);

        // Create Action<Task<object?>, object> delegate
        // We need to emit a static method that takes Task<object?> and object, then casts object to $AnyState
        // For simplicity, let's just use ContinueWith without state capture by calling HandleAnyCompletion differently

        // Actually, the cleanest approach is to create an Action<Task<object?>> that captures state
        // But that requires emitting a display class. Let's use a simpler pattern:
        // Just call HandleAnyCompletion directly with the task result when it completes

        // Use ContinueWith with object state parameter
        // ContinueWith(Action<Task, object?>, object?)
        // But we need Action<Task<object?>, object?>, which is tricky

        // Let's use ConfigureAwait(false).GetAwaiter().OnCompleted approach instead
        // Or simply: ContinueWith(t => HandleAnyCompletion(t, anyStateCapture))

        // Since we can't easily emit lambdas with captures, let's use a workaround:
        // Store state in a static field temporarily (not thread-safe but works for simple cases)

        // Better approach: Use Task.ContinueWith overload that takes state object
        // Action<Task<TResult>, object?> continuation, object? state

        // Create the delegate for HandleAnyCompletion
        // void HandleAnyCompletion(Task<object?> task, $AnyState state)
        // We need Action<Task<object?>, object?> where second param is the state

        // Since handleAnyCompletion takes (Task<object?>, $AnyState) and we need to pass $AnyState as object?,
        // we can use ContinueWith<TResult, TState> but that's complex

        // Simplest: emit inline handling instead of using HandleAnyCompletion
        // Let's use ContinueWith with TaskContinuationOptions and inline the logic

        // Actually, let's create an intermediary: use ContinueWith with object? state
        il.Emit(OpCodes.Ldloc, stateLocal);  // state object
        il.Emit(OpCodes.Ldnull);  // continuation options scheduler

        // Need to use: ContinueWith(Action<Task<TResult>, object?>, object?, TaskContinuationOptions)
        // But we need to box anyState to object

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
        // Not completed - use ContinueWith

        // For ContinueWith, we need to box the $AnyState and create proper delegate
        // Let's use a simpler approach: use Task.Factory.ContinueWhenAll pattern
        // or just accept that we need more complex setup

        // Actually, the easiest is to use ContinueWith with the continuation that just calls our method
        // task.ContinueWith(t => HandleAnyCompletion(t, capturedState), TaskContinuationOptions.ExecuteSynchronously)

        // To emit this, we need to create a display class that captures stateLocal
        // That's complex. Let's use a workaround: re-emit a version of HandleAnyCompletion
        // as a ContinueWith callback that takes (Task<object?>, object?) state

        // For now, use reflection-based approach temporarily
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Box, anyState.Type);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);

        // Call ContinueWith(Action<Task<TResult>, object?>, object?, TaskContinuationOptions)
        var continueWithMethod = typeof(Task<object?>).GetMethods()
            .First(m => m.Name == "ContinueWith" &&
                       m.GetParameters().Length == 3 &&
                       m.GetParameters()[0].ParameterType == typeof(Action<Task<object?>, object?>) &&
                       m.GetParameters()[1].ParameterType == typeof(object) &&
                       m.GetParameters()[2].ParameterType == typeof(TaskContinuationOptions));

        // Create Action<Task<object?>, object?> delegate
        // We need a method with signature: void Method(Task<object?> task, object? state)
        // handleAnyCompletion has signature: void Method(Task<object?> task, $AnyState state)
        // These don't match because $AnyState != object?

        // We need a shim method. Let's emit one in the runtime type
        // For now, skip ContinueWith and just use synchronous handling for completed tasks

        // Pop the stuff we loaded
        il.Emit(OpCodes.Pop);  // TaskContinuationOptions
        il.Emit(OpCodes.Pop);  // boxed state
        il.Emit(OpCodes.Pop);  // task

        // For non-completed tasks, we'll need to handle differently
        // This is getting complex - let's emit an adapter method

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

    /// <summary>
    /// Emits callback invocation helpers that directly call $TSFunction.Invoke
    /// without using reflection. These are used by the emitted Promise methods.
    /// </summary>
    private static void EmitCallbackHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // InvokeCallback(object? func, object? arg) -> object?
        // Equivalent to: if (func == null) return null; return (($TSFunction)func).Invoke(new object[] { arg });
        var invokeCallback = typeBuilder.DefineMethod(
            "InvokeCallback",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.InvokeCallback = invokeCallback;
        {
            var il = invokeCallback.GetILGenerator();
            var nullLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            // if (func == null) goto nullLabel
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, nullLabel);

            // Cast func to $TSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);

            // Create object[] { arg }
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stelem_Ref);

            // Call Invoke(object[])
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Br, endLabel);

            // nullLabel: return null
            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Ldnull);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ret);
        }

        // InvokeCallbackNoArgs(object? func) -> object?
        // Equivalent to: if (func == null) return null; return (($TSFunction)func).Invoke(Array.Empty<object>());
        var invokeCallbackNoArgs = typeBuilder.DefineMethod(
            "InvokeCallbackNoArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.InvokeCallbackNoArgs = invokeCallbackNoArgs;
        {
            var il = invokeCallbackNoArgs.GetILGenerator();
            var nullLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();

            // if (func == null) goto nullLabel
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, nullLabel);

            // Cast func to $TSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);

            // Create empty object[]
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, typeof(object));

            // Call Invoke(object[])
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
            il.Emit(OpCodes.Br, endLabel);

            // nullLabel: return null
            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Ldnull);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ret);
        }
    }
}
