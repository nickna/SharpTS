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
    public required FieldBuilder ConstructorField { get; init; }
    public FieldBuilder? CapabilityField { get; init; }
    public FieldBuilder? StablePrimitiveField { get; init; }
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
    public required FieldBuilder ConstructorField { get; init; }
    public required FieldBuilder CapabilityField { get; init; }
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
    public required FieldBuilder JobAwaiterField { get; init; }     // forced Promise-job boundary
    public required FieldBuilder FlattenAwaiterField { get; init; } // TaskAwaiter<object?> for flattening
    public required FieldBuilder ValueField { get; init; }          // intermediate value
    public required FieldBuilder ExceptionField { get; init; }      // stored exception
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Holds information about the fulfillment-only Promise.then state machine used
/// when the receiver and primitive callback result are statically stable.
/// </summary>
internal class PrimitivePromiseThenStateMachine
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder BuilderField { get; init; }
    public required FieldBuilder PromiseField { get; init; }
    public required FieldBuilder OnFulfilledField { get; init; }
    public required FieldBuilder PromiseAwaiterField { get; init; }
    public required FieldBuilder JobAwaiterField { get; init; }
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
}

/// <summary>
/// Holds the emitted carrier used to fuse a proven-linear primitive Promise
/// chain while retaining one observable FIFO job per reaction.
/// </summary>
internal class PrimitivePromiseChainClass
{
    public required TypeBuilder Type { get; init; }
    public required Type TableType { get; init; }
    public required FieldBuilder TableField { get; init; }
    public required ConstructorBuilder Constructor { get; init; }
    public required MethodBuilder AppendMethod { get; init; }
    public required MethodBuilder TaskGetter { get; init; }
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
    public required FieldBuilder JobAwaiterField { get; init; }     // forced Promise-job boundary
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
    public required FieldBuilder ConstructorField { get; init; }     // C parameter
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
    public required FieldBuilder CtsField { get; init; }              // CancellationTokenSource
    public required ConstructorBuilder Constructor { get; init; }
    public MethodBuilder? HandleCompletionMethod { get; set; }        // HandleAnyCompletion method
    public MethodBuilder? HandleCompletionShim { get; set; }          // Shim for ContinueWith
}

internal class AnyElementStateClass
{
    public required TypeBuilder Type { get; init; }
    public required FieldBuilder StateField { get; init; }
    public required FieldBuilder IndexField { get; init; }
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
    public required FieldBuilder ConstructorField { get; init; }     // C parameter
    public required FieldBuilder CapabilityField { get; init; }      // prepared custom capability
    public required FieldBuilder StateObjField { get; init; }        // $AnyState instance
    public required FieldBuilder AwaiterField { get; init; }         // TaskAwaiter<object?> for Tcs.Task
    public required MethodBuilder MoveNextMethod { get; init; }
    public required Type BuilderType { get; init; }
    public required Type AwaiterType { get; init; }
}

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits Promise static methods with full async state machine support.
    /// These methods return Task&lt;object?&gt; and are awaited by the compiled code.
    /// State machines are emitted directly, eliminating the need for SharpTS.dll at runtime.
    /// </summary>
    private void EmitPromiseMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var taskType = _types.TaskOfObject;
        var moduleBuilder = (ModuleBuilder)typeBuilder.Module;
        var promiseJobAwaiterType = DefinePromiseJobAwaiter(moduleBuilder, runtime);
        EmitTrackTopLevelPromiseReaction(typeBuilder, runtime);

        // Static value-form methods need NewPromiseCapability before the
        // executor-support bodies are filled at the end of this emitter.
        // Predeclare the shared helper so those wrappers can reference it.
        runtime.NewPromiseCapabilityResultMethod ??= typeBuilder.DefineMethod(
            "NewPromiseCapabilityResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.TaskOfObject]);
        runtime.PreparePromiseCapabilityMethod ??= typeBuilder.DefineMethod(
            "PreparePromiseCapability",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.AdoptPromiseCapabilityMethod ??= typeBuilder.DefineMethod(
            "AdoptPromiseCapability",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.TaskOfObject]);
        runtime.AdoptCompletedPromiseCapabilityMethod ??= typeBuilder.DefineMethod(
            "AdoptCompletedPromiseCapability",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.TaskOfObject]);
        runtime.GetPromiseCapabilityResolveMethod ??= typeBuilder.DefineMethod(
            "GetPromiseCapabilityResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.GetPromiseCapabilityRejectMethod ??= typeBuilder.DefineMethod(
            "GetPromiseCapabilityReject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        // Promise.resolve(value?) - returns an existing intrinsic Promise only
        // when its observable constructor is %Promise%. An own constructor
        // override requires a fresh promise capability that adopts the input.
        var resolve = typeBuilder.DefineMethod(
            "PromiseResolve",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        runtime.PromiseResolve = resolve;
        {
            var il = resolve.GetILGenerator();
            var notTaskLabel = il.DefineLabel();
            var wrapTaskLabel = il.DefineLabel();
            var taskLocal = il.DeclareLocal(taskType);
            var tcsType = typeof(TaskCompletionSource<object?>);
            var tcsLocal = il.DeclareLocal(tcsType);
            var callbackLocal = il.DeclareLocal(runtime.PromiseResolveCallbackType);

            // Check if value is already a Task<object?>
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, taskType);
            il.Emit(OpCodes.Stloc, taskLocal);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Brfalse, notTaskLabel);

            // IsPromise(value) && value.constructor === %Promise%: preserve
            // identity. GetProperty observes own data/accessor overrides before
            // falling back to Promise.prototype.constructor.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, "constructor");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, wrapTaskLabel);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Ret);

            // The constructor differs: create a distinct intrinsic promise and
            // resolve it with the input promise. Reusing the resolving-function
            // implementation preserves adoption, rejection, and single-settle
            // behavior without introducing a second promise-resolution path.
            il.MarkLabel(wrapTaskLabel);
            il.Emit(OpCodes.Newobj, tcsType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Newobj, typeof(System.Runtime.CompilerServices.StrongBox<bool>)
                .GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Newobj, runtime.PromiseResolveCallbackCtor);
            il.Emit(OpCodes.Stloc, callbackLocal);
            il.Emit(OpCodes.Ldloc, callbackLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.PromiseResolveCallbackInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, tcsType.GetProperty("Task")!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);

            // Non-native values use the Promise Resolve Functions path: read
            // `then` synchronously and enqueue PromiseResolveThenableJob when
            // it is callable.
            il.MarkLabel(notTaskLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PromiseResolveValueMethod);
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
            // Create $PromiseRejectedException from reason (preserves original value in Reason property)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
            // Call Task.FromException<object?>(exception) - keep typeof() for arity-based generic lookup
            var fromException = EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
            il.Emit(OpCodes.Call, fromException);
            il.Emit(OpCodes.Ret);
        }

        // Value-form `Promise.resolve` / `Promise.reject` — wraps the direct
        // helpers above with ECMA-262 §27.2.5.1 step 2 `this`-is-Object check.
        // The direct PromiseResolve/PromiseReject keep their (object value)
        // signature so syntactic dispatch (`Promise.resolve(x)`) doesn't pay
        // the extra check. Value-form (`let r = Promise.resolve; r.call(this, x)`)
        // routes through these wrappers via the $TSFunction __this param.
        var resolveStatic = typeBuilder.DefineMethod(
            "PromiseResolveStatic",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        resolveStatic.DefineParameter(1, ParameterAttributes.None, "__this");
        resolveStatic.DefineParameter(2, ParameterAttributes.None, "value");
        runtime.PromiseResolveStatic = resolveStatic;
        {
            var il = resolveStatic.GetILGenerator();
            EmitPromiseStaticThisObjectCheck(il, runtime,
                "Promise.resolve called on non-Object");
            var intrinsicLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.Type);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Beq, intrinsicLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PreparePromiseCapabilityMethod);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ResolvePreparedPromiseCapabilityMethod);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(intrinsicLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, resolve);
            il.Emit(OpCodes.Ret);
        }

        var rejectStatic = typeBuilder.DefineMethod(
            "PromiseRejectStatic",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        rejectStatic.DefineParameter(1, ParameterAttributes.None, "__this");
        rejectStatic.DefineParameter(2, ParameterAttributes.None, "reason");
        runtime.PromiseRejectStatic = rejectStatic;
        {
            var il = rejectStatic.GetILGenerator();
            EmitPromiseStaticThisObjectCheck(il, runtime,
                "Promise.reject called on non-Object");
            EmitPromiseStaticCapabilityResult(il, runtime, reject);
        }

        // Promise.withResolvers() - returns object? ($Object with {promise, resolve, reject})
        // Unlike other Promise statics, withResolvers is synchronous and returns a plain object.
        var withResolvers = typeBuilder.DefineMethod(
            "PromiseWithResolvers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.PromiseWithResolvers = withResolvers;
        {
            var il = withResolvers.GetILGenerator();

            // var tcs = new TaskCompletionSource<object?>()
            var tcsType = typeof(TaskCompletionSource<object?>);
            var tcsLocal = il.DeclareLocal(tcsType);
            il.Emit(OpCodes.Newobj, tcsType.GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, tcsLocal);

            // var promise = tcs.Task (this is Task<object?>, which is our promise representation)
            var promiseLocal = il.DeclareLocal(taskType);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, tcsType.GetProperty("Task")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, promiseLocal);

            // Build resolve callback: a $TSFunction that calls tcs.TrySetResult
            // We need to emit a closure class for this
            // Instead, use a simple approach: emit static methods that take tcs as captured state

            // Create the resolve function using a lambda-like approach
            // We'll create an inner class to hold the TCS reference
            var resolveClosureType = moduleBuilder.DefineType(
                "$PromiseResolverClosure",
                TypeAttributes.NotPublic | TypeAttributes.Sealed,
                typeof(object)
            );
            var resolveClosureTcsField = resolveClosureType.DefineField("_tcs", tcsType, FieldAttributes.Public);
            var resolveClosureCtor = resolveClosureType.DefineConstructor(
                MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            {
                var ctorIl = resolveClosureCtor.GetILGenerator();
                ctorIl.Emit(OpCodes.Ldarg_0);
                ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
                ctorIl.Emit(OpCodes.Ret);
            }
            var resolveMethod = resolveClosureType.DefineMethod(
                "Resolve",
                MethodAttributes.Public,
                typeof(object),
                [typeof(object)]
            );
            {
                var rIl = resolveMethod.GetILGenerator();
                // tcs.TrySetResult(value)  — value is arg1 directly
                rIl.Emit(OpCodes.Ldarg_0);
                rIl.Emit(OpCodes.Ldfld, resolveClosureTcsField);
                rIl.Emit(OpCodes.Ldarg_1);
                rIl.Emit(OpCodes.Callvirt, tcsType.GetMethod("TrySetResult", [typeof(object)])!);
                rIl.Emit(OpCodes.Pop);

                rIl.Emit(OpCodes.Ldnull);
                rIl.Emit(OpCodes.Ret);
            }

            // Create reject closure
            var rejectMethod = resolveClosureType.DefineMethod(
                "Reject",
                MethodAttributes.Public,
                typeof(object),
                [typeof(object)]
            );
            {
                var rIl = rejectMethod.GetILGenerator();
                // reason is arg1 directly

                // tcs.TrySetException(new Exception(reason?.ToString() ?? "Promise rejected"))
                rIl.Emit(OpCodes.Ldarg_0);
                rIl.Emit(OpCodes.Ldfld, resolveClosureTcsField);

                // Build exception message
                var reasonNullLabel = rIl.DefineLabel();
                var afterReasonLabel = rIl.DefineLabel();
                rIl.Emit(OpCodes.Ldarg_1);
                rIl.Emit(OpCodes.Brfalse, reasonNullLabel);
                rIl.Emit(OpCodes.Ldarg_1);
                rIl.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
                rIl.Emit(OpCodes.Br, afterReasonLabel);
                rIl.MarkLabel(reasonNullLabel);
                rIl.Emit(OpCodes.Ldstr, "Promise rejected");
                rIl.MarkLabel(afterReasonLabel);

                rIl.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String]));
                rIl.Emit(OpCodes.Callvirt, tcsType.GetMethod("TrySetException", [typeof(Exception)])!);
                rIl.Emit(OpCodes.Pop);

                rIl.Emit(OpCodes.Ldnull);
                rIl.Emit(OpCodes.Ret);
            }

            resolveClosureType.CreateType();

            // Now emit the main body of PromiseWithResolvers
            // var closure = new $PromiseResolverClosure()
            var closureLocal = il.DeclareLocal(resolveClosureType);
            il.Emit(OpCodes.Newobj, resolveClosureCtor);
            il.Emit(OpCodes.Stloc, closureLocal);

            // closure._tcs = tcs
            il.Emit(OpCodes.Ldloc, closureLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Stfld, resolveClosureTcsField);

            // var resolveFunc = new $TSFunction(closure, resolveMethod, "resolve", 1)
            var resolveFuncLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, closureLocal);
            il.Emit(OpCodes.Castclass, typeof(object));
            il.Emit(OpCodes.Ldtoken, resolveMethod);
            il.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            il.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
            il.Emit(OpCodes.Ldstr, "resolve");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, resolveFuncLocal);

            // var rejectFunc = new $TSFunction(closure, rejectMethod, "reject", 1)
            var rejectFuncLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, closureLocal);
            il.Emit(OpCodes.Castclass, typeof(object));
            il.Emit(OpCodes.Ldtoken, rejectMethod);
            il.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            il.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
            il.Emit(OpCodes.Ldstr, "reject");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, rejectFuncLocal);

            // Build result object: { promise, resolve, reject }
            var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object?>));
            il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object?>).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, dictLocal);

            // dict["promise"] = promiseTask (Task<object?> IS our promise)
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "promise");
            il.Emit(OpCodes.Ldloc, promiseLocal);
            il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item", [typeof(string), typeof(object)])!);

            // dict["resolve"] = resolveFunc
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "resolve");
            il.Emit(OpCodes.Ldloc, resolveFuncLocal);
            il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item", [typeof(string), typeof(object)])!);

            // dict["reject"] = rejectFunc
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "reject");
            il.Emit(OpCodes.Ldloc, rejectFuncLocal);
            il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("set_Item", [typeof(string), typeof(object)])!);

            // var result = new $Object(dict)
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);

            // return result directly (not wrapped in Task - withResolvers is synchronous)
            il.Emit(OpCodes.Ret);
        }

        // Predeclare the promise/thenable adoption helper. Its body is emitted
        // later with the capability support, but combinator normalization must
        // be able to reference it now.
        runtime.CoerceAwaitableToTaskMethod ??= typeBuilder.DefineMethod(
            "CoerceAwaitableToTask",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]);

        // Reserve NormalizePromiseList before the combinators so their state
        // machines can reference it. Its body is emitted after the iterator
        // protocol helpers and $IteratorWrapper exist.
        runtime.NormalizePromiseListMethod = typeBuilder.DefineMethod(
            "NormalizePromiseList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Int32, _types.Boolean]);
        runtime.AdoptPromiseCombinatorResultMethod = typeBuilder.DefineMethod(
            "AdoptPromiseCombinatorResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.TaskOfObject]);
        runtime.SettlePromiseCombinatorResultMethod = typeBuilder.DefineMethod(
            "SettlePromiseCombinatorResult",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.TaskOfObject, _types.Object]);

        // Promise.all(iterable) - async state machine using Task.WhenAll
        var promiseAllSM = DefinePromiseAllStateMachine(moduleBuilder);
        var all = typeBuilder.DefineMethod(
            "PromiseAll",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.PromiseAll = all;
        EmitPromiseAllWrapper(all.GetILGenerator(), promiseAllSM, runtime, stablePrimitive: false);

        var allPrimitive = typeBuilder.DefineMethod(
            "PromiseAllPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.PromiseAllPrimitive = allPrimitive;
        EmitPromiseAllWrapper(allPrimitive.GetILGenerator(), promiseAllSM, runtime, stablePrimitive: true);
        EmitPromiseAllMoveNext(promiseAllSM, runtime);
        promiseAllSM.Type.CreateType();

        // Promise.race(iterable) - async state machine using Task.WhenAny
        var promiseRaceSM = DefinePromiseRaceStateMachine(moduleBuilder);
        var race = typeBuilder.DefineMethod(
            "PromiseRace",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.PromiseRace = race;
        EmitPromiseRaceWrapper(race.GetILGenerator(), promiseRaceSM, runtime);
        EmitPromiseRaceMoveNext(promiseRaceSM, runtime);
        promiseRaceSM.Type.CreateType();

        // First emit the ProcessElementSettled helper for PromiseAllSettled
        var processElementSettledSM = DefineProcessElementSettledStateMachine(moduleBuilder);
        var processElementSettled = typeBuilder.DefineMethod(
            "ProcessElementSettled",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object]
        );
        EmitProcessElementSettledWrapper(processElementSettled.GetILGenerator(), processElementSettledSM);
        EmitProcessElementSettledMoveNext(processElementSettledSM, runtime);
        processElementSettledSM.Type.CreateType();

        // Promise.allSettled(iterable) - async state machine using helper + WhenAll
        var promiseAllSettledSM = DefinePromiseAllSettledStateMachine(moduleBuilder);
        var allSettled = typeBuilder.DefineMethod(
            "PromiseAllSettled",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object, _types.Object]
        );
        runtime.PromiseAllSettled = allSettled;
        EmitPromiseAllSettledWrapper(allSettled.GetILGenerator(), promiseAllSettledSM, processElementSettled, runtime);
        EmitPromiseAllSettledMoveNext(promiseAllSettledSM, processElementSettled, runtime);
        promiseAllSettledSM.Type.CreateType();

        // Await Dictionary proposal combinators. Their shells are declared
        // here with the other Promise statics; bodies are emitted later, once
        // own-key, descriptor, symbol, and prototype helpers are available.
        runtime.PromiseAllKeyed = typeBuilder.DefineMethod(
            "PromiseAllKeyed", MethodAttributes.Public | MethodAttributes.Static,
            taskType, [_types.Object]);
        runtime.PromiseAllSettledKeyed = typeBuilder.DefineMethod(
            "PromiseAllSettledKeyed", MethodAttributes.Public | MethodAttributes.Static,
            taskType, [_types.Object]);
        runtime.PromiseKeyedMapResult = typeBuilder.DefineMethod(
            "PromiseKeyedMapResult", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [taskType, _types.Object]);

        // Promise.any(iterable) - pure IL implementation with state machine
        // Define the $AnyState class and helper methods
        var anyState = DefineAnyStateClass(moduleBuilder);
        var anyElementState = DefineAnyElementStateClass(moduleBuilder, anyState);

        // Define HandleAnyCompletion(Task<object?>, $AnyState) method on runtime type
        var handleAnyCompletion = typeBuilder.DefineMethod(
            "HandleAnyCompletion",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(Task<object?>), anyElementState.Type]
        );
        anyState.HandleCompletionMethod = handleAnyCompletion;
        EmitHandleAnyCompletion(handleAnyCompletion.GetILGenerator(), anyState,
            anyElementState, runtime);

        // Define HandleAnyCompletionShim(Task<object?>, object?) - casts and calls the real method
        var handleAnyCompletionShim = typeBuilder.DefineMethod(
            "HandleAnyCompletionShim",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(Task<object?>), typeof(object)]
        );
        anyState.HandleCompletionShim = handleAnyCompletionShim;
        EmitHandleAnyCompletionShim(handleAnyCompletionShim.GetILGenerator(),
            anyElementState, handleAnyCompletion);

        // Create the $AnyState type
        anyState.Type.CreateType();
        anyElementState.Type.CreateType();

        // Define Promise.any wrapper and state machine
        var promiseAnySM = DefinePromiseAnyStateMachine(moduleBuilder, anyState);
        var any = typeBuilder.DefineMethod(
            "PromiseAny",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.PromiseAny = any;
        EmitPromiseAnyWrapper(any.GetILGenerator(), promiseAnySM, runtime);
        EmitPromiseAnyMoveNext(promiseAnySM, anyState, anyElementState,
            handleAnyCompletionShim, runtime);
        promiseAnySM.Type.CreateType();

        // Value-form wrappers for all/race/allSettled/any — validate `this` is
        // Object per ECMA-262 §27.2.4 step 1 ("Let C be the this value.").
        // Tests like `Promise.race.call(undefined, [...])` rely on this throw.
        void EmitAllRaceVariantStaticWrapper(string name, string jsName, MethodBuilder target, Action<MethodBuilder> assign)
        {
            var sw = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);
            sw.DefineParameter(1, ParameterAttributes.None, "__this");
            sw.DefineParameter(2, ParameterAttributes.None, "iterable");
            assign(sw);
            var il = sw.GetILGenerator();
            EmitPromiseStaticThisObjectCheck(il, runtime,
                $"Promise.{jsName} called on non-Object");
            EmitPromiseStaticCapabilityResult(il, runtime, target,
                passConstructorToIntrinsic: jsName is "all" or "race" or "allSettled" or "any",
                passCapabilityToIntrinsic: jsName is "all" or "race" or "any",
                prepareIntrinsicCapability: jsName == "race");
        }
        EmitAllRaceVariantStaticWrapper("PromiseAllStatic", "all", all, m => runtime.PromiseAllStatic = m);
        EmitAllRaceVariantStaticWrapper("PromiseAllKeyedStatic", "allKeyed", runtime.PromiseAllKeyed, m => runtime.PromiseAllKeyedStatic = m);
        EmitAllRaceVariantStaticWrapper("PromiseRaceStatic", "race", race, m => runtime.PromiseRaceStatic = m);
        EmitAllRaceVariantStaticWrapper("PromiseAllSettledStatic", "allSettled", allSettled, m => runtime.PromiseAllSettledStatic = m);
        EmitAllRaceVariantStaticWrapper("PromiseAllSettledKeyedStatic", "allSettledKeyed", runtime.PromiseAllSettledKeyed, m => runtime.PromiseAllSettledKeyedStatic = m);
        EmitAllRaceVariantStaticWrapper("PromiseAnyStatic", "any", any, m => runtime.PromiseAnyStatic = m);

        // Callback invocation helpers must be emitted first (used by then/finally)
        EmitCallbackHelpers(typeBuilder, runtime);

        // Promise.prototype.then - async state machine with callback invocation
        var promiseThenSM = DefinePromiseThenStateMachine(
            moduleBuilder, runtime, promiseJobAwaiterType);
        var then = typeBuilder.DefineMethod(
            "PromiseThen",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, _types.Object, _types.Object]
        );
        runtime.PromiseThen = then;
        EmitPromiseThenWrapper(then.GetILGenerator(), promiseThenSM);
        EmitPromiseThenMoveNext(promiseThenSM, runtime, promiseJobAwaiterType);
        promiseThenSM.Type.CreateType();

        // Retain the small state machine as a defensive fallback for an input
        // that violates the completed intrinsic seed invariant. Proven-linear
        // chains use one fused carrier and one final Task instead.
        var primitivePromiseThenSM = DefinePrimitivePromiseThenStateMachine(
            moduleBuilder, promiseJobAwaiterType);
        var primitiveThenFallback = typeBuilder.DefineMethod(
            "PromiseThenPrimitiveFallback",
            MethodAttributes.Private | MethodAttributes.Static,
            taskType,
            [taskType, typeof(Func<double, double>)]
        );
        EmitPrimitivePromiseThenWrapper(
            primitiveThenFallback.GetILGenerator(), primitivePromiseThenSM);
        EmitPrimitivePromiseThenMoveNext(
            primitivePromiseThenSM, promiseJobAwaiterType);

        var primitiveChain = DefinePrimitivePromiseChainClass(
            moduleBuilder, typeBuilder, runtime);
        var primitiveThen = typeBuilder.DefineMethod(
            "PromiseThenPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, typeof(Func<double, double>)]
        );
        runtime.PromiseThenPrimitive = primitiveThen;
        EmitPrimitivePromiseChainAppend(
            primitiveThen.GetILGenerator(),
            primitiveChain,
            primitiveThenFallback);
        primitiveChain.Type.CreateType();
        primitivePromiseThenSM.Type.CreateType();

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
        var promiseFinallySM = DefinePromiseFinallyStateMachine(
            moduleBuilder, runtime, promiseJobAwaiterType);
        var finallyMethod = typeBuilder.DefineMethod(
            "PromiseFinally",
            MethodAttributes.Public | MethodAttributes.Static,
            taskType,
            [taskType, _types.Object]
        );
        runtime.PromiseFinally = finallyMethod;
        EmitPromiseFinallyWrapper(finallyMethod.GetILGenerator(), promiseFinallySM);
        EmitPromiseFinallyMoveNext(
            promiseFinallySM, runtime, promiseJobAwaiterType);
        promiseFinallySM.Type.CreateType();

        // PromiseFromExecutor - emitted in RuntimeEmitter.Promises.Executor.cs
        EmitPromiseExecutorSupport(typeBuilder, runtime, moduleBuilder);
    }

    /// <summary>
    /// Emits a lifetime-only tracker for SharpTS's standalone entry point. A
    /// discarded top-level then/catch/finally result must not be synchronously
    /// pumped between script statements, but a pending native source (for
    /// example timers/promises) must still keep the process alive until its
    /// reaction job can run.
    /// </summary>
    private void EmitTrackTopLevelPromiseReaction(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TrackTopLevelPromiseReaction",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.TaskOfObject]);
        runtime.TrackTopLevelPromiseReaction = method;

        var il = method.GetILGenerator();
        var done = il.DefineLabel();
        var eventLoopLocal = il.DeclareLocal(runtime.EventLoopType);
        var awaiterLocal = il.DeclareLocal(typeof(TaskAwaiter<object?>));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt,
            typeof(Task).GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, done);

        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Stloc, eventLoopLocal);
        il.Emit(OpCodes.Ldloc, eventLoopLocal);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        il.Emit(OpCodes.Ldloc, eventLoopLocal);
        il.Emit(OpCodes.Ldftn, runtime.EventLoopUnref);
        il.Emit(OpCodes.Newobj,
            typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call,
            typeof(TaskAwaiter<object?>).GetMethod("UnsafeOnCompleted")!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL that throws TypeError if arg0 (the `this`/C-constructor slot)
    /// is not an Object per ECMA-262 §27.2.5.1 step 2. Null, undefined,
    /// booleans, numbers, strings, symbols all fail.
    /// </summary>
    private void EmitPromiseStaticThisObjectCheck(ILGenerator il, EmittedRuntime runtime, string message)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        // null → throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        // $Undefined → throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        // primitive types → throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, throwLabel);
        // Promise static methods create a promise capability from C. Objects
        // that are callable but not constructable (notably eval and built-in
        // method wrappers) must fail synchronously at that step.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsConstructorMethod);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Br, okLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, message);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits the common tail for a value-form Promise static. The intrinsic
    /// Promise constructor returns the raw task; another constructor is routed
    /// through NewPromiseCapability so the observable result is an instance of
    /// that constructor rather than the compiler's Task representation.
    /// </summary>
    private void EmitPromiseStaticCapabilityResult(
        ILGenerator il, EmittedRuntime runtime, MethodInfo intrinsic,
        bool passConstructorToIntrinsic = false,
        bool passCapabilityToIntrinsic = false,
        bool prepareIntrinsicCapability = false)
    {
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var capabilityLocal = il.DeclareLocal(_types.Object);
        var customConstructorLabel = il.DefineLabel();
        var invokeIntrinsicLabel = il.DefineLabel();

        // NewPromiseCapability(C) precedes the Promise operation. In
        // particular, a custom constructor must receive and validate the
        // capability executor before Promise.all/any/etc. reads C.resolve or
        // begins iteration. The intrinsic %Promise% representation needs no
        // materialized holder and keeps its direct Task fast path.
        if (prepareIntrinsicCapability)
        {
            il.Emit(OpCodes.Br, customConstructorLabel);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.Type);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, customConstructorLabel);
            il.Emit(OpCodes.Br, invokeIntrinsicLabel);
        }

        il.MarkLabel(customConstructorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PreparePromiseCapabilityMethod);
        il.Emit(OpCodes.Stloc, capabilityLocal);

        il.MarkLabel(invokeIntrinsicLabel);
        il.Emit(OpCodes.Ldarg_1);
        if (passConstructorToIntrinsic)
            il.Emit(OpCodes.Ldarg_0);
        if (passCapabilityToIntrinsic)
            il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Call, intrinsic);
        il.Emit(OpCodes.Stloc, taskLocal);

        var returnIntrinsicLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Brfalse, returnIntrinsicLabel);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Call, runtime.AdoptCompletedPromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnIntrinsicLabel);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits callback invocation helpers that directly call $TSFunction.Invoke
    /// without using reflection. These are used by the emitted Promise methods.
    /// </summary>
    private void EmitCallbackHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // InvokeCallback(object? func, object? arg) -> object?
        // Handles both $TSFunction and $BoundTSFunction
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
            var isTSFunctionLabel = il.DefineLabel();
            var isBoundLabel = il.DefineLabel();
            var argsLocal = il.DeclareLocal(typeof(object[]));

            // if (func == null) goto nullLabel
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, nullLabel);

            // Create object[] { arg } and store in local
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Stloc, argsLocal);

            // Check if func is $TSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

            // Check if func is $BoundTSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Brtrue, isBoundLabel);

            // Unknown callable - use InvokeValue fallback
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeValue);
            il.Emit(OpCodes.Br, endLabel);

            // isTSFunctionLabel: call $TSFunction.InvokeWithThis(undefined, args).
            // Function expressions compile with __this as first parameter
            // (HasOwnThis=true). Plain Invoke(args) would map args[0] to __this
            // and pad the user's first param with null. InvokeWithThis prepends
            // the explicit undefined thisArg for __this so callbacks
            // see their declared params at the right indices. Arrow functions
            // are unaffected: InvokeWithThis's !expectsThis branch routes back
            // through Invoke(args) and sets the thread-local this.
            // Per ECMA-262 §27.2.1 PromiseReactionJob: thisArgument = undefined.
            il.MarkLabel(isTSFunctionLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Br, endLabel);

            // isBoundLabel: call $BoundTSFunction.Invoke
            il.MarkLabel(isBoundLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
            il.Emit(OpCodes.Br, endLabel);

            // nullLabel: return null
            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Ldnull);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ret);
        }

        // InvokeCallbackNoArgs(object? func) -> object?
        // Handles both $TSFunction and $BoundTSFunction
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
            var isTSFunctionLabel = il.DefineLabel();
            var isBoundLabel = il.DefineLabel();
            var argsLocal = il.DeclareLocal(typeof(object[]));

            // if (func == null) goto nullLabel
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse, nullLabel);

            // Create empty object[] and store in local
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Stloc, argsLocal);

            // Check if func is $TSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

            // Check if func is $BoundTSFunction
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Brtrue, isBoundLabel);

            // Unknown callable - use InvokeValue fallback
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeValue);
            il.Emit(OpCodes.Br, endLabel);

            // isTSFunctionLabel: call $TSFunction.InvokeWithThis(undefined, args).
            // Function expressions compile with __this as first parameter
            // (HasOwnThis=true). Plain Invoke(args) would map args[0] to __this
            // and pad the user's first param with null. InvokeWithThis prepends
            // the explicit undefined thisArg for __this so callbacks
            // see their declared params at the right indices. Arrow functions
            // are unaffected: InvokeWithThis's !expectsThis branch routes back
            // through Invoke(args) and sets the thread-local this.
            // Per ECMA-262 §27.2.1 PromiseReactionJob: thisArgument = undefined.
            il.MarkLabel(isTSFunctionLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Br, endLabel);

            // isBoundLabel: call $BoundTSFunction.Invoke
            il.MarkLabel(isBoundLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
            il.Emit(OpCodes.Br, endLabel);

            // nullLabel: return null
            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Ldnull);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ret);
        }
    }
}

