using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Promise executor constructor support: new Promise((resolve, reject) => { ... })
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $PromiseResolveCallback and $PromiseRejectCallback types.
    /// Must be called before EmitInvokeValue so the callback types are available for dispatch.
    /// </summary>
    private void EmitPromiseCallbackTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitPromiseResolveCallbackType(moduleBuilder, runtime);
        EmitPromiseRejectCallbackType(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits the PromiseFromExecutor method that creates promises from executor functions.
    /// Must be called after EmitInvokeValue since it depends on runtime.InvokeValue.
    /// </summary>
    private void EmitPromiseExecutorSupport(TypeBuilder runtimeType, EmittedRuntime runtime, ModuleBuilder moduleBuilder)
    {
        // Emit the PromiseFromExecutor method
        EmitPromiseFromExecutorMethod(runtimeType, runtime, runtime.RequirePromise().ResolveCallbackType, runtime.RequirePromise().RejectCallbackType);
        EmitPromiseFromDirectExecutorMethod(runtimeType, runtime,
            runtime.RequirePromise().ResolveCallbackType, runtime.RequirePromise().RejectCallbackType);

        // Promise-subclass support (#242): receiver unwrapping + derived-result wrapping
        EmitUnwrapPromiseReceiverMethod(runtimeType, runtime);

        // NewPromiseCapabilityResult was declared by EmitPromiseMethods. Its body
        // is filled by EmitPromiseCapabilitySupport after ConstructDynamicValue.

        EmitObservePromiseConstructorMethod(runtimeType, runtime);
        EmitPrepareDerivedPromiseCapabilityMethod(runtime);
        EmitWrapDerivedPromiseResultMethod(runtimeType, runtime);
    }

    /// <summary>
    /// Emits ObservePromiseConstructor(object receiver) -> void. The own
    /// constructor getter is part of SpeciesConstructor and must run before
    /// PerformPromiseThen schedules a reaction. Keeping this separate from
    /// result wrapping preserves that ordering even though the result Task does
    /// not exist until after PromiseThen/PromiseCatch/PromiseFinally starts.
    /// </summary>
    private void EmitObservePromiseConstructorMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ObservePromiseConstructor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]);
        runtime.RequirePromise().ObservePromiseConstructorMethod = method;

        var il = method.GetILGenerator();
        var getterLocal = il.DeclareLocal(_types.Object);
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, doneLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldloca, getterLocal);
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.TryGetGetter);
        il.Emit(OpCodes.Brfalse, doneLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, getterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits NormalizePromiseList(object iterable, object constructor, object capability,
    /// int kind, bool stablePrimitive) -> object:
    /// materializes supported finite iterables and returns a list normalized through constructor C's
    /// current <c>resolve</c> method. The built-in path unwraps
    /// $Promise elements (including #242 subclasses) to their backing Task;
    /// when user code has replaced <c>Promise.resolve</c>, that callable is
    /// invoked once for each value with C as its receiver, as required by
    /// PerformPromiseAll/Race/AllSettled/Any. Resolve is captured before the
    /// iterable is acquired, matching the observable spec ordering.
    /// </summary>
    internal void EmitNormalizePromiseList(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitPromiseCombinatorResultAdoption(runtime);

        // PerformPromiseAll/Race/AllSettled/Any invoke nextPromise.then
        // synchronously while the iterator record is still open. Adapt that
        // observable invocation to the host Task representation only after
        // Get(nextPromise, "then") and Call have both completed successfully.
        // An abrupt getter/call must escape this helper so the incremental
        // iterator loop below can perform IteratorClose immediately.
        var invokeThenMethod = runtimeType.DefineMethod(
            "InvokePromiseCombinatorThen",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );
        {
            var thenIl = invokeThenMethod.GetILGenerator();
            var tcsLocal = thenIl.DeclareLocal(_types.TaskCompletionSourceOfObject);
            var settledLocal = thenIl.DeclareLocal(_types.Object);
            var resolveLocal = thenIl.DeclareLocal(runtime.RequirePromise().ResolveCallbackType);
            var rejectLocal = thenIl.DeclareLocal(runtime.RequirePromise().RejectCallbackType);

            thenIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(
                _types.TaskCompletionSourceOfObject));
            thenIl.Emit(OpCodes.Stloc, tcsLocal);

            thenIl.Emit(OpCodes.Newobj,
                typeof(System.Runtime.CompilerServices.StrongBox<bool>)
                    .GetConstructor(Type.EmptyTypes)!);
            thenIl.Emit(OpCodes.Stloc, settledLocal);

            thenIl.Emit(OpCodes.Ldloc, tcsLocal);
            thenIl.Emit(OpCodes.Ldloc, settledLocal);
            thenIl.Emit(OpCodes.Newobj, runtime.RequirePromise().ResolveCallbackCtor);
            thenIl.Emit(OpCodes.Stloc, resolveLocal);

            thenIl.Emit(OpCodes.Ldloc, tcsLocal);
            thenIl.Emit(OpCodes.Ldloc, settledLocal);
            thenIl.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectCallbackCtor);
            thenIl.Emit(OpCodes.Stloc, rejectLocal);

            thenIl.Emit(OpCodes.Ldarg_0);
            thenIl.Emit(OpCodes.Ldarg_1);
            thenIl.Emit(OpCodes.Ldc_I4_2);
            thenIl.Emit(OpCodes.Newarr, _types.Object);
            thenIl.Emit(OpCodes.Dup);
            thenIl.Emit(OpCodes.Ldc_I4_0);
            var useGeneratedResolveLabel = thenIl.DefineLabel();
            var haveResolveLabel = thenIl.DefineLabel();
            thenIl.Emit(OpCodes.Ldarg_2);
            thenIl.Emit(OpCodes.Brfalse, useGeneratedResolveLabel);
            thenIl.Emit(OpCodes.Ldarg_2);
            thenIl.Emit(OpCodes.Br, haveResolveLabel);
            thenIl.MarkLabel(useGeneratedResolveLabel);
            thenIl.Emit(OpCodes.Ldloc, resolveLocal);
            thenIl.MarkLabel(haveResolveLabel);
            thenIl.Emit(OpCodes.Stelem_Ref);
            thenIl.Emit(OpCodes.Dup);
            thenIl.Emit(OpCodes.Ldc_I4_1);
            var useGeneratedRejectLabel = thenIl.DefineLabel();
            var haveRejectLabel = thenIl.DefineLabel();
            thenIl.Emit(OpCodes.Ldarg_3);
            thenIl.Emit(OpCodes.Brfalse, useGeneratedRejectLabel);
            thenIl.Emit(OpCodes.Ldarg_3);
            thenIl.Emit(OpCodes.Br, haveRejectLabel);
            thenIl.MarkLabel(useGeneratedRejectLabel);
            thenIl.Emit(OpCodes.Ldloc, rejectLocal);
            thenIl.MarkLabel(haveRejectLabel);
            thenIl.Emit(OpCodes.Stelem_Ref);
            thenIl.Emit(OpCodes.Call, runtime.Invocation.Method);
            thenIl.Emit(OpCodes.Pop);

            thenIl.Emit(OpCodes.Ldloc, tcsLocal);
            thenIl.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            thenIl.Emit(OpCodes.Ret);
        }

        // IteratorClose(iterator, throwCompletion) for the narrow abrupt path
        // owned by Promise combinators. GetMethod(iterator, "return") can
        // replace the original completion (for example when return is not
        // callable), while an exception thrown by the return call itself does
        // not replace an already-throwing completion.
        var closeIteratorMethod = runtimeType.DefineMethod(
            "ClosePromiseIteratorOnAbrupt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Exception]
        );
        {
            var closeIl = closeIteratorMethod.GetILGenerator();
            var returnMethodLocal = closeIl.DeclareLocal(_types.Object);
            var finishCloseLabel = closeIl.DefineLabel();
            var noReturnMethodLabel = closeIl.DefineLabel();
            var returnCallableLabel = closeIl.DefineLabel();

            // For a throw completion, IteratorClose performs GetMethod/call
            // for their side effects but the original completion wins over
            // every abrupt close result (§7.4.11 step 5).
            closeIl.BeginExceptionBlock();
            closeIl.Emit(OpCodes.Ldarg_0);
            closeIl.Emit(OpCodes.Ldstr, "return");
            closeIl.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            closeIl.Emit(OpCodes.Stloc, returnMethodLocal);

            closeIl.Emit(OpCodes.Ldloc, returnMethodLocal);
            closeIl.Emit(OpCodes.Brfalse, noReturnMethodLabel);
            closeIl.Emit(OpCodes.Ldloc, returnMethodLocal);
            closeIl.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
            closeIl.Emit(OpCodes.Brtrue, noReturnMethodLabel);

            closeIl.Emit(OpCodes.Ldloc, returnMethodLocal);
            closeIl.Emit(OpCodes.Call, runtime.Operators.TypeOf);
            closeIl.Emit(OpCodes.Ldstr, "function");
            closeIl.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", _types.String, _types.String));
            closeIl.Emit(OpCodes.Brtrue, returnCallableLabel);
            GuestErrorEmitter.ThrowTypeError(closeIl, runtime,
                "Iterator return method is not callable");

            closeIl.MarkLabel(returnCallableLabel);
            closeIl.Emit(OpCodes.Ldarg_0);
            closeIl.Emit(OpCodes.Ldloc, returnMethodLocal);
            closeIl.Emit(OpCodes.Ldc_I4_0);
            closeIl.Emit(OpCodes.Newarr, _types.Object);
            closeIl.Emit(OpCodes.Call, runtime.Invocation.Method);
            closeIl.Emit(OpCodes.Pop);
            closeIl.Emit(OpCodes.Leave, finishCloseLabel);
            closeIl.MarkLabel(noReturnMethodLabel);
            closeIl.Emit(OpCodes.Leave, finishCloseLabel);
            closeIl.BeginCatchBlock(_types.Exception);
            closeIl.Emit(OpCodes.Pop);
            closeIl.Emit(OpCodes.Leave, finishCloseLabel);
            closeIl.EndExceptionBlock();

            closeIl.MarkLabel(finishCloseLabel);
            closeIl.Emit(OpCodes.Ldarg_1);
            closeIl.Emit(OpCodes.Throw);
        }

        var method = runtime.RequirePromise().NormalizePromiseListMethod;

        var il = method.GetILGenerator();
        var listType = _types.ListOfObject;
        var listLocal = il.DeclareLocal(listType);
        var resultLocal = il.DeclareLocal(listType);
        var taskArrayLocal = il.DeclareLocal(typeof(Task<object?>[]));
        var indexLocal = il.DeclareLocal(_types.Int32);
        var elementLocal = il.DeclareLocal(_types.Object);
        var resolvedElementLocal = il.DeclareLocal(_types.Object);
        var resolveFunctionLocal = il.DeclareLocal(_types.Object);
        var thenFunctionLocal = il.DeclareLocal(_types.Object);
        var resolveDescriptorLocal = il.DeclareLocal(runtime.DescriptorStorage.DescriptorType);
        var invokeResolveLocal = il.DeclareLocal(_types.Boolean);
        var constructorTypeLocal = il.DeclareLocal(_types.Type);
        var iteratorFunctionLocal = il.DeclareLocal(_types.Object);
        var iteratorLocal = il.DeclareLocal(_types.Object);
        var iteratorWrapperLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var iteratorTypeLocal = il.DeclareLocal(_types.String);
        var exceptionLocal = il.DeclareLocal(_types.Exception);

        // Capture an observable C.resolve before iterator acquisition. The
        // intrinsic/inherited %Promise%.resolve path remains the compiler's
        // raw Task-preserving fast path; calling its value-form wrapper here
        // would re-wrap native tasks and lose rejection identity. Own expando
        // overrides and non-Promise constructors (including declared statics)
        // are invoked per element.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "resolve");
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, resolveDescriptorLocal);
        var captureResolveLabel = il.DefineLabel();
        var resolveCaptureDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resolveDescriptorLocal);
        il.Emit(OpCodes.Brtrue, captureResolveLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, constructorTypeLocal);
        il.Emit(OpCodes.Ldloc, constructorTypeLocal);
        il.Emit(OpCodes.Brfalse, captureResolveLabel);
        il.Emit(OpCodes.Ldloc, constructorTypeLocal);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Beq, resolveCaptureDoneLabel);
        il.Emit(OpCodes.Ldtoken, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldloc, constructorTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.Type, "IsAssignableFrom", _types.Type));
        il.Emit(OpCodes.Brtrue, resolveCaptureDoneLabel);

        il.MarkLabel(captureResolveLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "resolve");
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, resolveFunctionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, invokeResolveLocal);
        il.MarkLabel(resolveCaptureDoneLabel);

        // GetPromiseResolve requires the captured resolve value to be
        // callable before iterator acquisition. InvokeMethodValue's permissive
        // fallback is used by other dynamic call sites, so enforce the Promise
        // combinator contract here and let the outer state-machine catch turn
        // the guest TypeError into a rejection.
        var resolveCallableLabel = il.DefineLabel();
        var resolveCheckedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, invokeResolveLocal);
        il.Emit(OpCodes.Brfalse, resolveCheckedLabel);
        il.Emit(OpCodes.Ldloc, resolveFunctionLocal);
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, resolveCallableLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime,
            "Promise constructor resolve is not callable");
        il.MarkLabel(resolveCallableLabel);
        il.MarkLabel(resolveCheckedLabel);

        // Arrays use the existing dense-list path and strings are safely finite
        // built-in iterables. General custom iterators use an incremental loop
        // below so next(), C.resolve(), and thenable adoption stay interleaved.
        var materializeLabel = il.DefineLabel();
        var customIteratorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Brtrue, materializeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, materializeLabel);
        il.Emit(OpCodes.Br, customIteratorLabel);
        il.MarkLabel(materializeLabel);

        // Materialize through the shared iterator-protocol bridge. Lists retain
        // their fast path and strings expand to their code-unit elements.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Iterator);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeClass.Type);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, runtime.IteratorCollection.ToList);
        il.Emit(OpCodes.Stloc, listLocal);

        // The overwhelmingly common Promise.all case is a dense intrinsic
        // array whose elements are already native promises. Preserve the own
        // `then` checks required by PerformPromiseAll, but keep the proven
        // tasks in their final array shape so the state machine can hand them
        // straight to Task.WhenAll without two intermediate List copies.
        var ordinaryListNormalizationLabel = il.DefineLabel();
        var fastTaskScanLoopLabel = il.DefineLabel();
        var fastTaskScanTSPromiseLabel = il.DefineLabel();
        var fastTaskScanStoreLabel = il.DefineLabel();
        var fastTaskScanDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Bne_Un, ordinaryListNormalizationLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, ordinaryListNormalizationLabel);
        il.Emit(OpCodes.Ldloc, invokeResolveLocal);
        il.Emit(OpCodes.Brtrue, ordinaryListNormalizationLabel);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Newarr, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskArrayLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(fastTaskScanLoopLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, taskArrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, fastTaskScanDoneLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Brfalse, fastTaskScanTSPromiseLabel);
        il.Emit(OpCodes.Ldarg_S, 4);
        il.Emit(OpCodes.Brtrue, fastTaskScanStoreLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, ordinaryListNormalizationLabel);
        il.Emit(OpCodes.Br, fastTaskScanStoreLabel);

        il.MarkLabel(fastTaskScanTSPromiseLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Brfalse, ordinaryListNormalizationLabel);
        var stableTSPromiseTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_S, 4);
        il.Emit(OpCodes.Brtrue, stableTSPromiseTaskLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, ordinaryListNormalizationLabel);
        il.MarkLabel(stableTSPromiseTaskLabel);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Castclass, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequirePromise().TaskGetter);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);

        il.MarkLabel(fastTaskScanStoreLabel);
        il.Emit(OpCodes.Ldloc, taskArrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, fastTaskScanLoopLabel);

        il.MarkLabel(fastTaskScanDoneLabel);
        il.Emit(OpCodes.Ldloc, taskArrayLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(ordinaryListNormalizationLabel);

        // var result = new List<object?>(); for each element: $Promise → .Task
        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        var useBuiltInResolveLabel = il.DefineLabel();
        var haveResolvedElementLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, invokeResolveLocal);
        il.Emit(OpCodes.Brfalse, useBuiltInResolveLabel);
        EmitInvokeCapturedResolve(il);
        il.Emit(OpCodes.Br, haveResolvedElementLabel);
        il.MarkLabel(useBuiltInResolveLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.MarkLabel(haveResolvedElementLabel);

        // PerformPromiseAll/Race/AllSettled/Any invokes `then` for every
        // resolved element. Preserve the established synchronous adoption
        // path for ordinary thenables and intrinsic Tasks, but do not let the
        // host Task fast path hide an own observable `then` override.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        EmitNormalizeResolvedPromiseElement(il);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Custom iterables must not be pre-materialized: a resolve/then abrupt
        // completion can terminate an otherwise-infinite iterator and requires
        // IteratorClose before the combinator rejects.
        il.MarkLabel(customIteratorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Iterator);
        il.Emit(OpCodes.Call, runtime.IteratorProtocol.Function);
        il.Emit(OpCodes.Stloc, iteratorFunctionLocal);

        var haveIteratorFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iteratorFunctionLocal);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brfalse, haveIteratorFunctionLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Value is not iterable");
        il.MarkLabel(haveIteratorFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iteratorFunctionLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Stloc, iteratorLocal);

        // GetIterator requires @@iterator to return an Object. TypeOf(null) is
        // "object", so retain explicit null/undefined checks before the broad
        // object/function test.
        var iteratorObjectOkLabel = il.DefineLabel();
        var iteratorTypeErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Brfalse, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, iteratorTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
        il.Emit(OpCodes.Stloc, iteratorTypeLocal);
        il.Emit(OpCodes.Ldloc, iteratorTypeLocal);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, iteratorObjectOkLabel);
        il.Emit(OpCodes.Ldloc, iteratorTypeLocal);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, iteratorObjectOkLabel);
        il.Emit(OpCodes.Br, iteratorTypeErrorLabel);
        il.MarkLabel(iteratorTypeErrorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Iterator method must return an object");
        il.MarkLabel(iteratorObjectOkLabel);

        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeClass.Type);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Newobj, runtime.IteratorWrappers.Ctor);
        il.Emit(OpCodes.Stloc, iteratorWrapperLocal);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(listType, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        var customLoopLabel = il.DefineLabel();
        var customLoopDoneLabel = il.DefineLabel();
        var customElementAddedLabel = il.DefineLabel();
        il.MarkLabel(customLoopLabel);
        il.Emit(OpCodes.Ldloc, iteratorWrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, customLoopDoneLabel);
        il.Emit(OpCodes.Ldloc, iteratorWrapperLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(
            _types.IEnumeratorOfObject, "Current").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // IteratorClose applies only after IteratorStep/IteratorValue succeeded.
        // Errors while reading next/done/value therefore remain outside this
        // exception region, matching the iterator-record [[Done]] rules.
        il.BeginExceptionBlock();
        var customUseBuiltInResolveLabel = il.DefineLabel();
        var customHaveResolvedElementLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, invokeResolveLocal);
        il.Emit(OpCodes.Brfalse, customUseBuiltInResolveLabel);
        EmitInvokeCapturedResolve(il);
        il.Emit(OpCodes.Br, customHaveResolvedElementLabel);
        il.MarkLabel(customUseBuiltInResolveLabel);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stloc, resolvedElementLocal);
        il.MarkLabel(customHaveResolvedElementLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, resolvedElementLocal);
        EmitNormalizeResolvedPromiseElement(il);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(listType, "Add", _types.Object));
        il.Emit(OpCodes.Leave, customElementAddedLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, closeIteratorMethod);
        il.Emit(OpCodes.Leave, customElementAddedLabel); // unreachable, keeps the handler well-formed
        il.EndExceptionBlock();

        il.MarkLabel(customElementAddedLabel);
        il.Emit(OpCodes.Br, customLoopLabel);

        il.MarkLabel(customLoopDoneLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        void EmitInvokeCapturedResolve(ILGenerator targetIl)
        {
            targetIl.Emit(OpCodes.Ldarg_1);
            targetIl.Emit(OpCodes.Ldloc, resolveFunctionLocal);
            targetIl.Emit(OpCodes.Ldc_I4_1);
            targetIl.Emit(OpCodes.Newarr, _types.Object);
            targetIl.Emit(OpCodes.Dup);
            targetIl.Emit(OpCodes.Ldc_I4_0);
            targetIl.Emit(OpCodes.Ldloc, elementLocal);
            targetIl.Emit(OpCodes.Stelem_Ref);
            targetIl.Emit(OpCodes.Call, runtime.Invocation.Method);
            targetIl.Emit(OpCodes.Stloc, resolvedElementLocal);
        }

        void EmitNormalizeResolvedPromiseElement(ILGenerator targetIl)
        {
            // Stack on entry: [..., resultList, resolvedElement]. Spill the
            // value and discard the receiver; every branch reloads resultLocal
            // so it leaves [resultList, task] for the caller's List.Add.
            targetIl.Emit(OpCodes.Stloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Pop);
            var ordinaryCapabilityLabel = targetIl.DefineLabel();
            var invokeObservableThenLabel = targetIl.DefineLabel();
            var useOrdinaryCoercionLabel = targetIl.DefineLabel();
            var normalizedLabel = targetIl.DefineLabel();

            // A directly wired intrinsic race capability still receives
            // nextPromise (the result of %Promise%.resolve), not the raw
            // iterable value. Preserve native Promise identity/overrides, but
            // wrap primitives and foreign thenables before invoking `then`.
            var capabilityResolutionReadyLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Brfalse, capabilityResolutionReadyLabel);
            targetIl.Emit(OpCodes.Ldloc, invokeResolveLocal);
            targetIl.Emit(OpCodes.Brtrue, capabilityResolutionReadyLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Isinst, _types.TaskOfObject);
            targetIl.Emit(OpCodes.Brtrue, capabilityResolutionReadyLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
            targetIl.Emit(OpCodes.Brtrue, capabilityResolutionReadyLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().ResolveValueMethod);
            targetIl.Emit(OpCodes.Stloc, resolvedElementLocal);
            targetIl.MarkLabel(capabilityResolutionReadyLabel);

            // Promise.race with a custom constructor must pass the SAME result
            // capability resolve/reject functions to every nextPromise.then.
            // Converting each element to a host Task would substitute fresh
            // resolving callbacks and violate observable callback identity.
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Brfalse, ordinaryCapabilityLabel);

            // Promise.any likewise passes its result capability's Resolve
            // directly, while each Reject element remains independently
            // guarded and feeds the host aggregation task.
            var raceCapabilityLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Ldarg_3);
            targetIl.Emit(OpCodes.Ldc_I4_2);
            targetIl.Emit(OpCodes.Bne_Un, raceCapabilityLabel);

            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            targetIl.Emit(OpCodes.Stloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Call, runtime.Operators.TypeOf);
            targetIl.Emit(OpCodes.Ldstr, "function");
            targetIl.Emit(OpCodes.Call, _types.StringOpEquality);
            var anyThenCallableLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Brtrue, anyThenCallableLabel);
            GuestErrorEmitter.ThrowTypeError(targetIl, runtime,
                "Promise.any resolved element then is not callable");
            targetIl.MarkLabel(anyThenCallableLabel);
            targetIl.Emit(OpCodes.Ldloc, resultLocal);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().GetPromiseCapabilityResolveMethod);
            targetIl.Emit(OpCodes.Ldnull);
            targetIl.Emit(OpCodes.Call, invokeThenMethod);
            targetIl.Emit(OpCodes.Br, normalizedLabel);

            targetIl.MarkLabel(raceCapabilityLabel);

            // Promise.all uses a fresh resolve-element function for each
            // nextPromise, but passes the SAME result capability reject
            // function to every then invocation.
            var raceDirectCapabilityLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Ldarg_3);
            targetIl.Emit(OpCodes.Ldc_I4_3);
            targetIl.Emit(OpCodes.Bne_Un, raceDirectCapabilityLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            targetIl.Emit(OpCodes.Stloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Call, runtime.Operators.TypeOf);
            targetIl.Emit(OpCodes.Ldstr, "function");
            targetIl.Emit(OpCodes.Call, _types.StringOpEquality);
            var allThenCallableLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Brtrue, allThenCallableLabel);
            GuestErrorEmitter.ThrowTypeError(targetIl, runtime,
                "Promise.all resolved element then is not callable");
            targetIl.MarkLabel(allThenCallableLabel);
            targetIl.Emit(OpCodes.Ldloc, resultLocal);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldnull);
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().GetPromiseCapabilityRejectMethod);
            targetIl.Emit(OpCodes.Call, invokeThenMethod);
            targetIl.Emit(OpCodes.Br, normalizedLabel);

            targetIl.MarkLabel(raceDirectCapabilityLabel);

            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            targetIl.Emit(OpCodes.Stloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Call, runtime.Operators.TypeOf);
            targetIl.Emit(OpCodes.Ldstr, "function");
            targetIl.Emit(OpCodes.Call, _types.StringOpEquality);
            var directThenCallableLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Brtrue, directThenCallableLabel);
            GuestErrorEmitter.ThrowTypeError(targetIl, runtime,
                "Promise.race resolved element then is not callable");
            targetIl.MarkLabel(directThenCallableLabel);

            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldc_I4_2);
            targetIl.Emit(OpCodes.Newarr, _types.Object);
            targetIl.Emit(OpCodes.Dup);
            targetIl.Emit(OpCodes.Ldc_I4_0);
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().GetPromiseCapabilityResolveMethod);
            targetIl.Emit(OpCodes.Stelem_Ref);
            targetIl.Emit(OpCodes.Dup);
            targetIl.Emit(OpCodes.Ldc_I4_1);
            targetIl.Emit(OpCodes.Ldarg_2);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().GetPromiseCapabilityRejectMethod);
            targetIl.Emit(OpCodes.Stelem_Ref);
            targetIl.Emit(OpCodes.Call, runtime.Invocation.Method);
            targetIl.Emit(OpCodes.Pop);

            // The custom capability has been wired directly. Keep the host
            // race task pending forever; its only role is to preserve the
            // existing wrapper signature while the capability itself settles.
            targetIl.Emit(OpCodes.Ldloc, resultLocal);
            targetIl.Emit(OpCodes.Newobj,
                _types.GetDefaultConstructor(_types.TaskCompletionSourceOfObject));
            targetIl.Emit(OpCodes.Callvirt,
                _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            targetIl.Emit(OpCodes.Br, normalizedLabel);

            targetIl.MarkLabel(ordinaryCapabilityLabel);
            targetIl.Emit(OpCodes.Ldloc, resultLocal);

            // A custom C.resolve result is already nextPromise, so Perform*
            // must Invoke its then method directly. The intrinsic fast path
            // also returns an existing native Promise unchanged; if that
            // promise has an own observable then override, invoke it directly
            // rather than feeding it through PromiseResolveValue (which would
            // turn a synchronous abrupt completion into a rejected Task and
            // allow an infinite iterator to keep running).
            targetIl.Emit(OpCodes.Ldloc, invokeResolveLocal);
            targetIl.Emit(OpCodes.Brtrue, invokeObservableThenLabel);

            var checkNativePromiseObjectLabel = targetIl.DefineLabel();
            var ordinaryResolutionLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Isinst, _types.TaskOfObject);
            targetIl.Emit(OpCodes.Brfalse, checkNativePromiseObjectLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
            targetIl.Emit(OpCodes.Brtrue, invokeObservableThenLabel);
            targetIl.Emit(OpCodes.Br, useOrdinaryCoercionLabel);

            targetIl.MarkLabel(checkNativePromiseObjectLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
            targetIl.Emit(OpCodes.Brfalse, ordinaryResolutionLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
            targetIl.Emit(OpCodes.Brtrue, invokeObservableThenLabel);
            targetIl.Emit(OpCodes.Br, useOrdinaryCoercionLabel);

            targetIl.MarkLabel(ordinaryResolutionLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.DescriptorStorage.GetPropertyDescriptor);
            targetIl.Emit(OpCodes.Brfalse, useOrdinaryCoercionLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().ResolveValueMethod);
            targetIl.Emit(OpCodes.Br, normalizedLabel);
            targetIl.MarkLabel(useOrdinaryCoercionLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Call, runtime.RequirePromise().CoerceAwaitableToTaskMethod);
            targetIl.Emit(OpCodes.Br, normalizedLabel);

            targetIl.MarkLabel(invokeObservableThenLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldstr, "then");
            targetIl.Emit(OpCodes.Call, runtime.ObjectRead.Property);
            targetIl.Emit(OpCodes.Stloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Call, runtime.Operators.TypeOf);
            targetIl.Emit(OpCodes.Ldstr, "function");
            targetIl.Emit(OpCodes.Call, _types.StringOpEquality);
            var observableThenCallableLabel = targetIl.DefineLabel();
            targetIl.Emit(OpCodes.Brtrue, observableThenCallableLabel);
            GuestErrorEmitter.ThrowTypeError(targetIl, runtime,
                "Promise combinator resolved element then is not callable");
            targetIl.MarkLabel(observableThenCallableLabel);
            targetIl.Emit(OpCodes.Ldloc, resolvedElementLocal);
            targetIl.Emit(OpCodes.Ldloc, thenFunctionLocal);
            targetIl.Emit(OpCodes.Ldnull);
            targetIl.Emit(OpCodes.Ldnull);
            targetIl.Emit(OpCodes.Call, invokeThenMethod);
            targetIl.MarkLabel(normalizedLabel);
        }

    }

    /// <summary>
    /// Promise.all/allSettled settle their capability through its resolve
    /// function. In particular, the newly-created result array can inherit a
    /// callable or poisoned <c>then</c> from Array.prototype. Async method
    /// builders do not assimilate an object result, so bridge the state-machine
    /// task through the emitted Promise resolving functions.
    /// </summary>
    private void EmitPromiseCombinatorResultAdoption(EmittedRuntime runtime)
    {
        var settle = runtime.RequirePromise().SettlePromiseCombinatorResultMethod;
        {
            var il = settle.GetILGenerator();
            var callbacksLocal = il.DeclareLocal(_types.ObjectArray);
            var exceptionLocal = il.DeclareLocal(_types.Exception);
            var successLabel = il.DefineLabel();
            var haveExceptionLabel = il.DefineLabel();
            var invokeRejectLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.ObjectArray);
            il.Emit(OpCodes.Stloc, callbacksLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.Task, "IsCompletedSuccessfully").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, successLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.Task, "Exception").GetGetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, haveExceptionLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "Promise combinator task was canceled");
            il.Emit(OpCodes.Newobj, typeof(TaskCanceledException)
                .GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Br, invokeRejectLabel);

            il.MarkLabel(haveExceptionLabel);
            il.Emit(OpCodes.Stloc, exceptionLocal);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.Exception, "InnerException").GetGetMethod()!);
            il.Emit(OpCodes.Dup);
            var haveInnerLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, haveInnerLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, exceptionLocal);
            il.MarkLabel(haveInnerLabel);

            il.MarkLabel(invokeRejectLabel);
            il.Emit(OpCodes.Call, runtime.Errors.WrapException);
            var reasonLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, reasonLocal);
            il.Emit(OpCodes.Ldloc, callbacksLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, runtime.RequirePromise().RejectCallbackType);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, reasonLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.RequirePromise().RejectCallbackInvoke);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(successLabel);
            il.Emit(OpCodes.Ldloc, callbacksLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, runtime.RequirePromise().ResolveCallbackType);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.TaskOfObject, "Result").GetGetMethod()!);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.RequirePromise().ResolveCallbackInvoke);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ret);
        }

        var adopt = runtime.RequirePromise().AdoptPromiseCombinatorResultMethod;
        {
            var il = adopt.GetILGenerator();
            var tcsLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);
            var settledLocal = il.DeclareLocal(_types.Object);
            var resolveLocal = il.DeclareLocal(runtime.RequirePromise().ResolveCallbackType);
            var rejectLocal = il.DeclareLocal(runtime.RequirePromise().RejectCallbackType);
            var callbacksLocal = il.DeclareLocal(_types.ObjectArray);
            var scheduleLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            // This facade is an internal result-adoption bridge, not a Promise
            // reaction job. Its completion must remain inline with the source
            // combinator task: Promise.allKeyed/allSettledKeyed attach their
            // allocation-free result mapper with ExecuteSynchronously, and a
            // RunContinuationsAsynchronously facade would override that option.
            // Under thread-pool pressure the emitted event loop could then drain
            // before the mapper (and the guest .then/$DONE chain) became visible.
            // PromiseThen still owns scheduling of observable guest reactions.
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(
                _types.TaskCompletionSourceOfObject));
            il.Emit(OpCodes.Stloc, tcsLocal);
            il.Emit(OpCodes.Newobj,
                typeof(System.Runtime.CompilerServices.StrongBox<bool>)
                    .GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, settledLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, settledLocal);
            il.Emit(OpCodes.Newobj, runtime.RequirePromise().ResolveCallbackCtor);
            il.Emit(OpCodes.Stloc, resolveLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, settledLocal);
            il.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectCallbackCtor);
            il.Emit(OpCodes.Stloc, rejectLocal);

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
            il.Emit(OpCodes.Stloc, callbacksLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.Task, "IsCompleted").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, scheduleLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, callbacksLocal);
            il.Emit(OpCodes.Call, settle);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(scheduleLabel);
            var continuationType = _types.ActionTaskOfObjectAndObject;
            var continuationCtor = _types.GetConstructor(
                continuationType, _types.Object, _types.IntPtr);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, settle);
            il.Emit(OpCodes.Newobj, continuationCtor);
            il.Emit(OpCodes.Ldloc, callbacksLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(
                _types.CancellationToken, "None").GetGetMethod()!);
            il.Emit(OpCodes.Ldc_I4,
                (int)TaskContinuationOptions.ExecuteSynchronously);
            il.Emit(OpCodes.Call, _types.GetProperty(
                _types.TaskScheduler, "Default").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskOfObject, "ContinueWith",
                [continuationType, _types.Object,
                    _types.CancellationToken, _types.TaskContinuationOptions,
                    _types.TaskScheduler]));
            il.Emit(OpCodes.Pop);

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(
                _types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits UnwrapPromiseReceiver(object receiver) -> Task&lt;object?&gt;:
    /// $Promise instances (including #242 Promise subclasses) yield their
    /// wrapped task; anything else is cast to Task&lt;object?&gt; (matching the
    /// previous inline Castclass that broke for $Promise receivers).
    /// </summary>
    private void EmitUnwrapPromiseReceiverMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "UnwrapPromiseReceiver",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]
        );
        runtime.RequirePromise().UnwrapPromiseReceiverMethod = method;

        var il = method.GetILGenerator();
        var notPromiseObjLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Brfalse, notPromiseObjLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequirePromise().TaskGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPromiseObjLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits PrepareDerivedPromiseCapability(object receiver) -> object.
    /// Observes the constructor and resolves species before a reaction is
    /// registered, returning null for the intrinsic path or an already-validated
    /// capability holder for every other constructor, including Promise subclasses.
    /// </summary>
    /// <remarks>
    /// Generic Promise subclasses (#351): static <c>@@species</c> descriptors are
    /// stored under the open generic definition (MyP`1) while the receiver's
    /// runtime type is closed (MyP&lt;object&gt;). SymbolRegistryKey reconciles the
    /// storage owner; accessors receive the actual closed constructor. A species
    /// naming a generic subclass is closed via SymbolClosedOwner before the
    /// shared capability protocol constructs it and validates its callbacks.
    /// </remarks>
    private void EmitPrepareDerivedPromiseCapabilityMethod(EmittedRuntime runtime)
    {
        var il = runtime.RequirePromise().PrepareDerivedPromiseCapabilityMethod.GetILGenerator();
        var intrinsic = il.DefineLabel();
        var nominal = il.DefineLabel();
        var haveConstructor = il.DefineLabel();
        var haveType = il.DefineLabel();
        var general = il.DefineLabel();
        var receiverType = il.DeclareLocal(_types.Type);
        var constructor = il.DeclareLocal(_types.Object);
        var species = il.DeclareLocal(_types.Object);
        var speciesType = il.DeclareLocal(_types.Type);
        var definitionInstance = il.DeclareLocal(runtime.ClassDefinitions.InstanceInterface);
        var definition = il.DeclareLocal(runtime.ClassDefinitions.Type);

        // Preserve the existing constructor observation and receiver admission.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.RequirePromise().ObservePromiseConstructorMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Brfalse, intrinsic);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, receiverType);
        il.Emit(OpCodes.Ldloc, receiverType);
        il.Emit(OpCodes.Ldtoken, runtime.RequirePromise().Type);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Beq, intrinsic);

        // A nominal child may inherit an expression parent's definition marker.
        // Only the actual matching template identifies the evaluated constructor.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ClassDefinitions.InstanceInterface);
        il.Emit(OpCodes.Stloc, definitionInstance);
        il.Emit(OpCodes.Ldloc, definitionInstance);
        il.Emit(OpCodes.Brfalse, nominal);
        il.Emit(OpCodes.Ldloc, definitionInstance);
        il.Emit(OpCodes.Callvirt, runtime.ClassDefinitions.GetDefinition);
        il.Emit(OpCodes.Isinst, runtime.ClassDefinitions.Type);
        il.Emit(OpCodes.Stloc, definition);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Brfalse, nominal);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Ldfld, runtime.ClassDefinitions.Template);
        il.Emit(OpCodes.Ldloc, receiverType);
        il.Emit(OpCodes.Bne_Un, nominal);
        il.Emit(OpCodes.Ldloc, definition);
        il.Emit(OpCodes.Stloc, constructor);
        il.Emit(OpCodes.Br, haveConstructor);
        il.MarkLabel(nominal);
        // CLR instantiations share the source constructor's guest descriptors
        // and mutable parent. External/non-generic owners pass through unchanged.
        il.Emit(OpCodes.Ldloc, receiverType);
        il.Emit(OpCodes.Call, runtime.SymbolAccessors.RegistryKey);
        il.Emit(OpCodes.Stloc, constructor);
        il.MarkLabel(haveConstructor);

        // Resolve the selected live guest chain, preserving the original
        // constructor as accessor receiver. An absent species uses %Promise%.
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Species);
        il.Emit(OpCodes.Ldloc, constructor);
        il.Emit(OpCodes.Call, runtime.Reflect.Get);
        il.Emit(OpCodes.Stloc, species);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, speciesType);
        il.Emit(OpCodes.Ldloc, speciesType);
        il.Emit(OpCodes.Brtrue, haveType);
        EmitSpeciesValueRouting(il, runtime, species, intrinsic, general);

        il.MarkLabel(haveType);
        // Retain the established closure and capability rules for Type species.
        il.Emit(OpCodes.Ldloc, speciesType);
        il.Emit(OpCodes.Call, _nativeOwnerClosure?.SymbolOwner ?? runtime.SymbolAccessors.ClosedOwner);
        il.Emit(OpCodes.Stloc, speciesType);
        il.Emit(OpCodes.Ldloc, speciesType);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Beq, intrinsic);
        il.Emit(OpCodes.Ldloc, speciesType);
        il.Emit(OpCodes.Call, runtime.RequirePromise().PreparePromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(general);
        il.Emit(OpCodes.Ldloc, species);
        il.Emit(OpCodes.Call, runtime.RequirePromise().PreparePromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(intrinsic);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Adopts a reaction task into the capability prepared before registration.</summary>
    private void EmitWrapDerivedPromiseResultMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "WrapDerivedPromiseResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.TaskOfObject, _types.Object]);
        runtime.RequirePromise().WrapDerivedPromiseResultMethod = method;

        var il = method.GetILGenerator();
        var intrinsicLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, intrinsicLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.RequirePromise().AdoptPromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(intrinsicLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the §7.3.22 steps 5-7 branch for a resolved <c>@@species</c> value
    /// already known not to be a <see cref="Type"/> (so it is neither a class nor
    /// <c>%Promise%</c>): <c>undefined</c>/<c>null</c> falls back to <c>%Promise%</c>
    /// (<paramref name="promiseFallbackLabel"/>); any other value is routed with
    /// its raw form preserved to the general NewPromiseCapability path
    /// (<paramref name="generalFromValueLabel"/>), which constructs a callable
    /// species or throws <c>TypeError</c> for a non-constructor (#390).
    /// </summary>
    private void EmitSpeciesValueRouting(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder speciesValLocal,
        Label promiseFallbackLabel, Label generalFromValueLabel)
    {
        il.Emit(OpCodes.Ldloc, speciesValLocal);
        il.Emit(OpCodes.Brfalse, promiseFallbackLabel);    // null → %Promise%
        il.Emit(OpCodes.Ldloc, speciesValLocal);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, promiseFallbackLabel);     // undefined → %Promise%
        il.Emit(OpCodes.Br, generalFromValueLabel);
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
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PromiseResolveCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);
        var rootField = typeBuilder.DefineField("_root", _types.TaskOfObject, FieldAttributes.Private);
        var sharedSettledType = typeof(System.Runtime.CompilerServices.StrongBox<bool>);
        var sharedSettledValue = sharedSettledType.GetField("Value")!;

        // Transfers an adopted Promise/thenable task's settlement to this
        // resolving function's target capability.
        var settleFromTaskMethod = typeBuilder.DefineMethod(
            "SettleFromTask",
            MethodAttributes.Private,
            _types.Void,
            [_types.TaskOfObject]);
        {
            var il = settleFromTaskMethod.GetILGenerator();
            var faultedLabel = il.DefineLabel();
            var canceledLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsFaulted").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, faultedLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, canceledLabel);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskOfObject, "Result").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod("TrySetResult")!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(faultedLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "Exception").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod(
                "TrySetException", [_types.Exception])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, doneLabel);

            il.MarkLabel(canceledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, tcsField);
            il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod(
                "TrySetCanceled", Type.EmptyTypes)!);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(doneLabel);
            il.Emit(OpCodes.Ret);
        }

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
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            // this._tcs = tcs
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, tcsField);
            // this._lock = lockObj
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, lockField);
            // Existing callers own the capability represented by tcs.Task.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task").GetGetMethod()!);
            il.Emit(OpCodes.Stfld, rootField);
            // this._settled = false
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, settledField);
            il.Emit(OpCodes.Ret);
        }

        // Recursive thenable jobs keep the original public capability while
        // retaining their own target TCS and once-only resolving pair.
        var rootedCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.TaskCompletionSourceOfObject, _types.Object, _types.TaskOfObject]);
        {
            var il = rootedCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, ctor);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Stfld, rootField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(object?[] args) method - compatible with TSFunction invocation
        var invokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [typeof(object[])]
        );
        runtime.NativeCallables?.Register(typeBuilder, invokeMethod);
        {
            var il = invokeMethod.GetILGenerator();
            var alreadySettledLabel = il.DefineLabel();
            var endLockLabel = il.DefineLabel();
            var notTaskLabel = il.DefineLabel();
            var endLabel = il.DefineLabel();
            var selfResolutionLabel = il.DefineLabel();

            var valueLocal = il.DeclareLocal(_types.Object);
            var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
            var adoptedTaskLocal = il.DeclareLocal(_types.TaskOfObject);

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
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Castclass, sharedSettledType);
            il.Emit(OpCodes.Ldfld, sharedSettledValue);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Castclass, sharedSettledType);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, sharedSettledValue);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // Check the original public task before any observable `then` Get.
            // Only exact task identity counts: a distinct wrapper or foreign
            // promise retains its own resolving functions and ownership.
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rootField);
            il.Emit(OpCodes.Beq, selfResolutionLabel);

            // ECMAScript Promise Resolve Functions only perform thenable
            // assimilation for Objects (callable functions included). Primitive
            // resolutions can fulfill the target capability directly. Routing a
            // number/string/boolean/null through PromiseResolveValue needlessly
            // performed a dynamic `then` lookup, allocated an intermediate TCS,
            // and transferred its already-completed result back into this TCS.
            // Worker fan-in resolves one primitive promise per worker, making
            // that generic path a material part of every round trip.
            var resolveObjectValueLabel = il.DefineLabel();
            var resolvePrimitiveValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Brfalse, resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brtrue, resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brtrue, resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, _types.Boolean);
            il.Emit(OpCodes.Brtrue, resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
            il.Emit(OpCodes.Brtrue, resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Br, resolveObjectValueLabel);

            il.MarkLabel(resolvePrimitiveValueLabel);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskCompletionSourceOfObject, "TrySetResult", _types.Object));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, endLabel);

            il.MarkLabel(resolveObjectValueLabel);

            // Promise Resolve Functions adopt promises and thenables instead of
            // fulfilling with their host Task representation as a plain value.
            // Native promises/tasks retain their backing task. For every other
            // value use PromiseResolveValue: it performs the observable `then`
            // Get inside its own try/catch and turns an abrupt getter into a
            // rejected task. Calling CoerceAwaitableToTask directly here let the
            // getter escape from resolve(), so resolve(poisonedThen) returned by
            // throwing and the executor catch observed the wrong completion.
            var resolveOrdinaryValueLabel = il.DefineLabel();
            var haveAdoptedTaskLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, _types.TaskOfObject);
            il.Emit(OpCodes.Brfalse, resolveOrdinaryValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, runtime.RequirePromise().CoerceAwaitableToTaskMethod);
            il.Emit(OpCodes.Br, haveAdoptedTaskLabel);
            il.MarkLabel(resolveOrdinaryValueLabel);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rootField);
            il.Emit(OpCodes.Call, runtime.RequirePromise().ResolveValueWithRootMethod);
            il.MarkLabel(haveAdoptedTaskLabel);
            il.Emit(OpCodes.Stloc, adoptedTaskLocal);
            // Resolving a promise with itself rejects with TypeError rather than
            // installing a continuation cycle that can never settle.
            var notSelfResolutionLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, adoptedTaskLocal);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!);
            il.Emit(OpCodes.Bne_Un, notSelfResolutionLabel);
            il.MarkLabel(selfResolutionLabel);
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldstr, "Chaining cycle detected for promise");
            il.Emit(OpCodes.Newobj, runtime.Errors.TypeErrorConstructor);
            il.Emit(OpCodes.Call, runtime.Errors.CreateException);
            il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>).GetMethod(
                "TrySetException", [_types.Exception])!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(notSelfResolutionLabel);

            // Plain resolutions normalize to an already-completed task. Settle
            // those immediately so short event-loop runs do not exit with a
            // queued continuation still outstanding. Only genuinely pending
            // Promise/thenable adoption needs scheduler handoff.
            var schedulePendingLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, adoptedTaskLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCompleted").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, schedulePendingLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, adoptedTaskLocal);
            il.Emit(OpCodes.Call, settleFromTaskMethod);
            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(schedulePendingLabel);

            // Settlement transfer runs no guest code; complete it atomically
            // with the adopted task so the event-loop drain cannot finish in
            // the gap before a separately queued scheduler callback. Promise
            // reactions remain jobs owned by PromiseThen's state machine.
            var continuationType = typeof(Action<Task<object?>>);
            il.Emit(OpCodes.Ldloc, adoptedTaskLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, settleFromTaskMethod);
            il.Emit(OpCodes.Newobj, continuationType.GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(
                _types.TaskOfObject, "ContinueWith", [continuationType, typeof(TaskContinuationOptions)])!);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            // Promise resolving functions are ECMAScript built-ins. Their
            // return value is always undefined, including repeated calls after
            // the promise has already settled (§27.2.1.3.2/.1).
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.RequirePromise().ResolveCallbackType = typeBuilder;
        runtime.RequirePromise().ResolveCallbackCtor = ctor;
        runtime.RequirePromise().ResolveCallbackWithRootCtor = rootedCtor;
        runtime.RequirePromise().ResolveCallbackInvoke = invokeMethod;
        return typeBuilder;
    }

    /// <summary>
    /// Emits the $PromiseRejectCallback type with similar structure to resolve callback.
    /// </summary>
    private TypeBuilder EmitPromiseRejectCallbackType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PromiseRejectCallback",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object
        );

        // Fields
        var tcsField = typeBuilder.DefineField("_tcs", typeof(TaskCompletionSource<object?>), FieldAttributes.Private);
        var lockField = typeBuilder.DefineField("_lock", _types.Object, FieldAttributes.Private);
        var settledField = typeBuilder.DefineField("_settled", typeof(bool), FieldAttributes.Private);
        var sharedSettledType = typeof(System.Runtime.CompilerServices.StrongBox<bool>);
        var sharedSettledValue = sharedSettledType.GetField("Value")!;

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TaskCompletionSource<object?>), _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
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
        runtime.NativeCallables?.Register(typeBuilder, invokeMethod);
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
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Castclass, sharedSettledType);
            il.Emit(OpCodes.Ldfld, sharedSettledValue);
            il.Emit(OpCodes.Brtrue, alreadySettledLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Castclass, sharedSettledType);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, sharedSettledValue);
            il.Emit(OpCodes.Leave, endLockLabel);

            il.MarkLabel(alreadySettledLabel);
            il.Emit(OpCodes.Leave, endLabel);

            il.BeginFinallyBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, lockField);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(Monitor), "Exit", _types.Object));
            il.EndExceptionBlock();

            il.MarkLabel(endLockLabel);

            // tcs.TrySetException(new $PromiseRejectedException(reason)) —
            // the carrier whose Reason the then/catch state machines extract,
            // so `new Promise((res, rej) => rej(err)).catch(e => ...)` hands
            // the guest value through unchanged (mirrors the interpreter's
            // SharpTSPromiseRejectedException; #232 reason-preservation).
            il.Emit(OpCodes.Ldloc, tcsLocal);
            il.Emit(OpCodes.Ldloc, reasonLocal);
            il.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectedExceptionCtor);
            var trySetException = typeof(TaskCompletionSource<object?>).GetMethod("TrySetException", [typeof(Exception)])!;
            il.Emit(OpCodes.Callvirt, trySetException);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(endLabel);
            il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.RequirePromise().RejectCallbackType = typeBuilder;
        runtime.RequirePromise().RejectCallbackCtor = ctor;
        runtime.RequirePromise().RejectCallbackInvoke = invokeMethod;
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
        runtime.RequirePromise().FromExecutor = method;

        var il = method.GetILGenerator();

        // Local variables
        var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
        var lockLocal = il.DeclareLocal(_types.Object);
        var resolveLocal = il.DeclareLocal(resolveCallbackType);
        var rejectLocal = il.DeclareLocal(rejectCallbackType);
        var argsLocal = il.DeclareLocal(typeof(object[]));

        // Task adoption (#242): a raw Task<object?> in place of an executor is
        // adopted as the promise's task directly. Promise-subclass constructors
        // chain through here (super(executor) → PromiseFromExecutor → base
        // $Promise ctor), so passing a task to that same constructor is the
        // derived-promise construction path used by inherited statics
        // (MyPromise.resolve) and subclass-typed then/catch/finally results.
        var notTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTaskLabel);

        // Executor validation precedes the rejection-catching invocation block:
        // construction must throw synchronously for a noncallable executor.
        var callableExecutor = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, callableExecutor);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException,
            runtime.Errors.TypeErrorConstructor, "Promise executor is not callable");
        il.MarkLabel(callableExecutor);

        // TaskCompletionSource<object?> tcs = new TaskCompletionSource<object?>();
        var tcsCtor = typeof(TaskCompletionSource<object?>).GetConstructor([])!;
        il.Emit(OpCodes.Newobj, tcsCtor);
        il.Emit(OpCodes.Stloc, tcsLocal);

        // object lockObj = new object();
        il.Emit(OpCodes.Newobj, typeof(System.Runtime.CompilerServices.StrongBox<bool>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, lockLocal);

        // var resolveCallback = new $PromiseResolveCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.RequirePromise().ResolveCallbackCtor);
        il.Emit(OpCodes.Stloc, resolveLocal);

        // var rejectCallback = new $PromiseRejectCallback(tcs, lockObj);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectCallbackCtor);
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

        // try { InvokeMethodValue(undefined, executor, args); }
        // catch (Exception ex) { tcs.TrySetException(ex); }
        var exLocal = il.DeclareLocal(_types.Exception);
        var endTryLabel = il.DefineLabel();

        il.BeginExceptionBlock();

        // Promise executors are called with undefined as their thisArgument.
        // Preserve the sentinel so strict functions observe undefined while
        // sloppy functions still coerce it to globalThis in their body.
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ldarg_0);  // executor
        il.Emit(OpCodes.Ldloc, argsLocal);  // args
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Pop);  // Discard executor return value

        il.Emit(OpCodes.Leave, endTryLabel);

        // catch (Exception ex)
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);

        // Call the capability's reject function instead of writing the TCS
        // directly. Resolve may already have claimed the shared settled flag
        // while adopting a pending thenable; an executor throw after that must
        // be ignored by reject (CreateResolvingFunctions single-settlement).
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Call, runtime.Errors.WrapException);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.RequirePromise().RejectCallbackInvoke);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Leave, endTryLabel);

        il.EndExceptionBlock();
        il.MarkLabel(endTryLabel);

        // Always return this capability's own task. Returning the task adopted
        // from a synchronous resolve aliases distinct Promise instances when
        // Task.FromResult reuses a cached task (notably resolve() / null), so
        // an expando such as p1.then = custom leaks onto p2. The continuation
        // above still mirrors pending adopted settlement into this facade.
        il.Emit(OpCodes.Ldloc, tcsLocal);
        var taskProperty = typeof(TaskCompletionSource<object?>).GetProperty("Task")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, taskProperty);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the compiler-only Promise constructor fast path for an inline
    /// executor arrow with the exact CLR signature object(object, object).
    /// The generic entry point above must accept every callable shape, so it
    /// wraps the arrow in $TSFunction, builds an object[] and routes through
    /// InvokeMethodValue. At an inline-arrow construction site none of those
    /// dynamic artifacts are observable: the Promise constructor invokes the
    /// executor exactly once and does not expose the function value. Bind the
    /// emitted arrow body to a typed delegate and invoke it directly while
    /// retaining the same resolving functions, first-settlement guard, throw
    /// handling, and per-Promise Task identity as the general path.
    /// </summary>
    private void EmitPromiseFromDirectExecutorMethod(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        TypeBuilder resolveCallbackType,
        TypeBuilder rejectCallbackType)
    {
        var executorType = typeof(Func<object, object, object>);
        var method = runtimeType.DefineMethod(
            "PromiseFromDirectExecutor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [executorType]);
        runtime.RequirePromise().FromDirectExecutor = method;

        var il = method.GetILGenerator();
        var tcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object?>));
        var settledLocal = il.DeclareLocal(_types.Object);
        var resolveLocal = il.DeclareLocal(resolveCallbackType);
        var rejectLocal = il.DeclareLocal(rejectCallbackType);
        var exLocal = il.DeclareLocal(_types.Exception);

        il.Emit(OpCodes.Newobj, typeof(TaskCompletionSource<object?>)
            .GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcsLocal);

        il.Emit(OpCodes.Newobj,
            typeof(System.Runtime.CompilerServices.StrongBox<bool>)
                .GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, settledLocal);

        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, settledLocal);
        il.Emit(OpCodes.Newobj, runtime.RequirePromise().ResolveCallbackCtor);
        il.Emit(OpCodes.Stloc, resolveLocal);

        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, settledLocal);
        il.Emit(OpCodes.Newobj, runtime.RequirePromise().RejectCallbackCtor);
        il.Emit(OpCodes.Stloc, rejectLocal);

        var completedLabel = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resolveLocal);
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Callvirt, executorType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, completedLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Call, runtime.Errors.WrapException);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.RequirePromise().RejectCallbackInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, completedLabel);
        il.EndExceptionBlock();

        il.MarkLabel(completedLabel);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object?>)
            .GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }
}
