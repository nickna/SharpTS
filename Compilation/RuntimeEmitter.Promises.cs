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
