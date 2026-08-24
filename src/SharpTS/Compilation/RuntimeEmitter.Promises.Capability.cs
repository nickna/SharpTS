using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the general NewPromiseCapability support (#349): the result of
/// <c>then</c>/<c>catch</c>/<c>finally</c> when <c>SpeciesConstructor</c> resolves
/// to a constructor that is <em>not</em> <c>%Promise%</c> or a guest
/// <c>class … extends Promise</c> (ECMA-262 §27.2.4.5 + §27.2.5.4 step 7). The
/// result is <c>new S((resolve, reject) =&gt; …)</c> with the captured capability
/// adopting the settled source task; <c>S</c> may be any guest class and the
/// returned object need not be a $Promise.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the <c>$PromiseCapability</c> holder type and fills the
    /// pre-declared <see cref="EmittedRuntime.NewPromiseCapabilityResultMethod"/>
    /// body. Must be called AFTER <c>EmitConstructDynamicValue</c> and after the
    /// $Runtime helpers it depends on (<c>InvokeValue</c>, <c>WrapException</c>,
    /// <c>ConstructDynamicValue</c>) are emitted, but while <c>$Runtime</c> is
    /// still open for new method bodies.
    /// </summary>
    internal void EmitPromiseCapabilitySupport(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitPromiseCapabilityType(moduleBuilder, runtime);
        EmitPreparePromiseCapabilityBody(runtime);
        EmitAdoptPromiseCapabilityBody(runtime);
        EmitAdoptCompletedPromiseCapabilityBody(runtime);
        EmitResolvePreparedPromiseCapabilityBody(runtime);
        EmitPromiseCapabilitySlotGetterBodies(runtime);
        EmitNewPromiseCapabilityResultBody(runtime);
        EmitCoerceAwaitableToTask(runtime);
    }

    /// <summary>
    /// Emits <c>CoerceAwaitableToTask(object value) -> Task&lt;object&gt;</c>: the
    /// await coercion for a value that reached the state machine's wrap-value
    /// path (already known not to be a $Promise or Task). An ordinary thenable
    /// (a value whose <c>then</c> member is callable, by <c>typeof</c>) is
    /// adopted — <c>then(resolve, reject)</c> settles a fresh capability whose
    /// task is awaited (ECMA-262 await → PromiseResolve, §27.2.1.3.2); anything
    /// else is wrapped with Task.FromResult (#349).
    /// </summary>
    private void EmitCoerceAwaitableToTask(EmittedRuntime runtime)
    {
        var method = runtime.CoerceAwaitableToTaskMethod;

        var il = method.GetILGenerator();
        var wrapLabel = il.DefineLabel();
        var tcsType = typeof(TaskCompletionSource<object?>);

        var thenLocal = il.DeclareLocal(_types.Object);
        var tcsLocal = il.DeclareLocal(tcsType);
        var lockLocal = il.DeclareLocal(_types.Object);

        // Normalize any CLR Task<T> returned from dynamic external interop.
        // Task<T> is invariant, so an `is Task<object>` check alone would wrap
        // Task<string> as a fulfilled ordinary value instead of awaiting it.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeManagedAwaitable);
        il.Emit(OpCodes.Starg, 0);

        var notTaskLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTaskLabel);

        var notPromiseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPromiseLabel);

        // if (value == null) goto wrap;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, wrapLabel);

        // then = GetProperty(value, "then");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, thenLocal);

        // if (TypeOf(then) != "function") goto wrap;
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, wrapLabel);

        // var tcs = new TaskCompletionSource<object?>(); var lockObj = new object();
        il.Emit(OpCodes.Newobj, tcsType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcsLocal);
        il.Emit(OpCodes.Newobj, typeof(System.Runtime.CompilerServices.StrongBox<bool>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, lockLocal);

        // try { InvokeMethodValue(value, then,
        //          new object[] { new $PromiseResolveCallback(tcs, lock),
        //                         new $PromiseRejectCallback(tcs, lock) }); }
        // catch (Exception e) { tcs.TrySetException(new $PromiseRejectedException(WrapException(e))); }
        var exLocal = il.DeclareLocal(_types.Exception);
        var endTryLabel = il.DefineLabel();
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);                       // receiver = value
        il.Emit(OpCodes.Ldloc, thenLocal);              // function = then
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseResolveCallbackCtor);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, lockLocal);
        il.Emit(OpCodes.Newobj, runtime.PromiseRejectCallbackCtor);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endTryLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Call, runtime.WrapException);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
        il.Emit(OpCodes.Callvirt, tcsType.GetMethod("TrySetException", [_types.Exception])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, endTryLabel);
        il.EndExceptionBlock();
        il.MarkLabel(endTryLabel);

        // return tcs.Task;
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, tcsType.GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // wrap: return Task.FromResult(value);
        il.MarkLabel(wrapLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, _types.Object));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the <c>$PromiseCapability</c> type: two object slots (Resolve,
    /// Reject), a <c>Capture(object[])</c> executor body (stored as the resolve/
    /// reject the species hands it), and a <c>Settle(Task&lt;object&gt;)</c>
    /// continuation that drives the captured callbacks when the source settles.
    /// </summary>
    private void EmitPromiseCapabilityType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PromiseCapability",
            TypeAttributes.Public | TypeAttributes.Sealed,
            _types.Object);

        var resolveField = typeBuilder.DefineField("Resolve", _types.Object, FieldAttributes.Public);
        var rejectField = typeBuilder.DefineField("Reject", _types.Object, FieldAttributes.Public);
        var instanceField = typeBuilder.DefineField("Promise", _types.Object, FieldAttributes.Public);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // object Capture(object[] args): reject a second invocation after either
        // slot acquired a non-undefined value, then capture the supplied slots.
        // This is GetCapabilitiesExecutor Functions §27.2.1.5.1, including the
        // deliberate allowance for a second call after (undefined, undefined).
        var capture = typeBuilder.DefineMethod(
            "Capture", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        {
            var il = capture.GetILGenerator();
            var checkRejectLabel = il.DefineLabel();
            var captureArgsLabel = il.DefineLabel();
            var noResolveLabel = il.DefineLabel();
            var noRejectLabel = il.DefineLabel();

            // if (Resolve is neither CLR-null nor JS undefined) throw TypeError.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resolveField);
            il.Emit(OpCodes.Brfalse, checkRejectLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resolveField);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, checkRejectLabel);
            il.Emit(OpCodes.Ldstr, "Promise capability executor was already invoked");
            GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);

            // if (Reject is neither CLR-null nor JS undefined) throw TypeError.
            il.MarkLabel(checkRejectLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            il.Emit(OpCodes.Brfalse, captureArgsLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, captureArgsLabel);
            il.Emit(OpCodes.Ldstr, "Promise capability executor was already invoked");
            GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);

            il.MarkLabel(captureArgsLabel);

            // if (args.Length > 0) this.Resolve = args[0];
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, noResolveLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stfld, resolveField);
            il.MarkLabel(noResolveLabel);

            // if (args.Length > 1) this.Reject = args[1];
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ble, noRejectLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stfld, rejectField);
            il.MarkLabel(noRejectLabel);

            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Ret);
        }

        // void Settle(Task<object> t): drives the captured capability from the
        // settled source. Faulted -> Reject(WrapException(t.Exception)); else ->
        // Resolve(t.Result). A missing (never-captured) callback is skipped so a
        // species that ignored the executor simply never settles instead of
        // throwing. Runs on the event-loop SynchronizationContext (the scheduler
        // passed to ContinueWith), so the guest callbacks resume on the loop
        // thread rather than the thread pool (#319/#320).
        var settle = typeBuilder.DefineMethod(
            "Settle", MethodAttributes.Public, typeof(void), [_types.TaskOfObject]);
        {
            var il = settle.GetILGenerator();
            var faultedLabel = il.DefineLabel();
            var doRejectLabel = il.DefineLabel();
            var doResolveLabel = il.DefineLabel();
            var retLabel = il.DefineLabel();
            var argLocal = il.DeclareLocal(_types.Object);

            // if (t.IsFaulted) goto faulted;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsFaulted").GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, faultedLabel);

            // arg = t.Result; callback = this.Resolve; (resolve path)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.TaskOfObject, "Result").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, argLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resolveField);
            il.Emit(OpCodes.Brfalse, retLabel);     // no resolve captured → skip
            il.Emit(OpCodes.Br, doResolveLabel);

            // faulted: arg = WrapException(t.Exception.InnerException); callback = this.Reject.
            // Task.Exception is an AggregateException; WrapException unwraps
            // TargetInvocationException and reads $PromiseRejectedException.Reason
            // but not AggregateException, so peel the first inner first.
            il.MarkLabel(faultedLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "Exception").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
            il.Emit(OpCodes.Call, runtime.WrapException);
            il.Emit(OpCodes.Stloc, argLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            il.Emit(OpCodes.Brfalse, retLabel);     // no reject captured → skip
            // fall through to doReject

            il.MarkLabel(doRejectLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, rejectField);
            EmitInvokeCapabilityCallback(il, runtime, argLocal);
            il.Emit(OpCodes.Br, retLabel);

            il.MarkLabel(doResolveLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, resolveField);
            EmitInvokeCapabilityCallback(il, runtime, argLocal);

            il.MarkLabel(retLabel);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        runtime.PromiseCapabilityType = typeBuilder;
        runtime.PromiseCapabilityCtor = ctor;
        runtime.PromiseCapabilityResolveField = resolveField;
        runtime.PromiseCapabilityRejectField = rejectField;
        runtime.PromiseCapabilityInstanceField = instanceField;
        runtime.PromiseCapabilityCaptureMethod = capture;
        runtime.PromiseCapabilitySettleMethod = settle;
    }

    /// <summary>
    /// Performs the synchronous portion of NewPromiseCapability: construct C
    /// with the capturing executor and require both captured callbacks to be
    /// callable. The returned holder is deliberately opaque to earlier-emitted
    /// Promise wrappers, allowing its generated type to remain a late-bound
    /// runtime implementation detail.
    /// </summary>
    private void EmitPreparePromiseCapabilityBody(EmittedRuntime runtime)
    {
        var il = runtime.PreparePromiseCapabilityMethod.GetILGenerator();
        var capabilityType = runtime.PromiseCapabilityType;
        var funcType = _types.FuncObjectArrayToObject;
        var capabilityLocal = il.DeclareLocal(capabilityType);
        var instanceLocal = il.DeclareLocal(_types.Object);
        var executorLocal = il.DeclareLocal(funcType);

        il.Emit(OpCodes.Newobj, runtime.PromiseCapabilityCtor);
        il.Emit(OpCodes.Stloc, capabilityLocal);

        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldftn, runtime.PromiseCapabilityCaptureMethod);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(funcType, [_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Stloc, executorLocal);

        // The intrinsic Promise constructor is represented by
        // Task<object>. It is not activatable through reflection; construct
        // its host promise capability through PromiseFromExecutor instead.
        var constructGeneralLabel = il.DefineLabel();
        var haveInstanceLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Bne_Un, constructGeneralLabel);
        il.Emit(OpCodes.Ldloc, executorLocal);
        il.Emit(OpCodes.Call, runtime.PromiseFromExecutor);
        il.Emit(OpCodes.Stloc, instanceLocal);
        il.Emit(OpCodes.Br, haveInstanceLabel);

        il.MarkLabel(constructGeneralLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, executorLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.ConstructDynamicValue);
        il.Emit(OpCodes.Stloc, instanceLocal);
        il.MarkLabel(haveInstanceLabel);

        EmitRequireCallableCapabilitySlot(il, runtime, capabilityLocal,
            runtime.PromiseCapabilityResolveField);
        EmitRequireCallableCapabilitySlot(il, runtime, capabilityLocal,
            runtime.PromiseCapabilityRejectField);

        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldloc, instanceLocal);
        il.Emit(OpCodes.Stfld, runtime.PromiseCapabilityInstanceField);
        il.Emit(OpCodes.Ldloc, instanceLocal);
        il.Emit(OpCodes.Call, runtime.MarkNonAutoAwaitPromiseMethod);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRequireCallableCapabilitySlot(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder capabilityLocal,
        FieldInfo slot)
    {
        var callableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, slot);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, callableLabel);
        il.Emit(OpCodes.Ldstr, "Promise resolve or reject function is not callable");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(callableLabel);
    }

    /// <summary>Adopts a task into a previously prepared capability.</summary>
    private void EmitAdoptPromiseCapabilityBody(EmittedRuntime runtime)
    {
        var il = runtime.AdoptPromiseCapabilityMethod.GetILGenerator();
        var capabilityType = runtime.PromiseCapabilityType;
        var actionType = typeof(Action<Task<object?>>);
        var schedulerType = typeof(TaskScheduler);
        var syncContextType = typeof(SynchronizationContext);
        var capabilityLocal = il.DeclareLocal(capabilityType);
        var schedulerLocal = il.DeclareLocal(schedulerType);
        var useDefaultLabel = il.DefineLabel();
        var haveSchedulerLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, capabilityType);
        il.Emit(OpCodes.Stloc, capabilityLocal);

        il.Emit(OpCodes.Call, syncContextType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, useDefaultLabel);
        il.Emit(OpCodes.Call, schedulerType.GetMethod("FromCurrentSynchronizationContext", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, haveSchedulerLabel);
        il.MarkLabel(useDefaultLabel);
        il.Emit(OpCodes.Call, schedulerType.GetProperty("Default")!.GetGetMethod()!);
        il.MarkLabel(haveSchedulerLabel);
        il.Emit(OpCodes.Stloc, schedulerLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldftn, runtime.PromiseCapabilitySettleMethod);
        il.Emit(OpCodes.Newobj, actionType.GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(CancellationToken).GetProperty("None")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Ldloc, schedulerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.TaskOfObject, "ContinueWith",
            [actionType, typeof(CancellationToken), typeof(TaskContinuationOptions), schedulerType])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, runtime.PromiseCapabilityInstanceField);
        il.Emit(OpCodes.Ret);
    }


    /// <summary>
    /// Settles an already-completed source inline, preserving the synchronous
    /// observability of custom Promise capability callbacks. Pending sources
    /// retain the normal event-loop continuation path.
    /// </summary>
    private void EmitAdoptCompletedPromiseCapabilityBody(EmittedRuntime runtime)
    {
        var il = runtime.AdoptCompletedPromiseCapabilityMethod.GetILGenerator();
        var schedulePendingLabel = il.DefineLabel();
        var settleFaultedLabel = il.DefineLabel();
        var returnPromiseLabel = il.DefineLabel();
        var capabilityType = runtime.PromiseCapabilityType;
        var capabilityLocal = il.DeclareLocal(capabilityType);
        var exceptionLocal = il.DeclareLocal(_types.Exception);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCompleted").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, schedulePendingLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, capabilityType);
        il.Emit(OpCodes.Stloc, capabilityLocal);

        // A faulted operation already carries the abrupt completion that must
        // be delivered to capability.[[Reject]]. Do not catch a throw from that
        // reject callback and attempt to call it a second time.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsFaulted").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, settleFaultedLabel);

        // For a fulfilled combinator, a throw from capability.[[Resolve]] is
        // itself the abrupt completion handled by IfAbruptRejectPromise. Turn
        // it into one call to capability.[[Reject]] before returning the
        // constructed promise. This is observable for empty Promise.all and
        // Promise.allSettled inputs whose custom resolve throws.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PromiseCapabilitySettleMethod);
        il.Emit(OpCodes.Leave, returnPromiseLabel);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, runtime.PromiseCapabilityRejectField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, runtime.WrapException);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, returnPromiseLabel);
        il.EndExceptionBlock();

        il.MarkLabel(settleFaultedLabel);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PromiseCapabilitySettleMethod);

        il.MarkLabel(returnPromiseLabel);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, runtime.PromiseCapabilityInstanceField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(schedulePendingLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AdoptPromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitResolvePreparedPromiseCapabilityBody(EmittedRuntime runtime)
    {
        var il = runtime.ResolvePreparedPromiseCapabilityMethod.GetILGenerator();
        var capabilityType = runtime.PromiseCapabilityType;
        var capabilityLocal = il.DeclareLocal(capabilityType);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, capabilityType);
        il.Emit(OpCodes.Stloc, capabilityLocal);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, runtime.PromiseCapabilityResolveField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, capabilityLocal);
        il.Emit(OpCodes.Ldfld, runtime.PromiseCapabilityInstanceField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitPromiseCapabilitySlotGetterBodies(EmittedRuntime runtime)
    {
        EmitGetter(runtime.GetPromiseCapabilityResolveMethod,
            runtime.PromiseCapabilityResolveField);
        EmitGetter(runtime.GetPromiseCapabilityRejectMethod,
            runtime.PromiseCapabilityRejectField);

        void EmitGetter(MethodBuilder method, FieldInfo field)
        {
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.PromiseCapabilityType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits, with the callback value already on the stack, the call
    /// <c>InvokeValue(callback, new object[] { arg })</c> and discards the
    /// result.
    /// </summary>
    private void EmitInvokeCapabilityCallback(ILGenerator il, EmittedRuntime runtime, LocalBuilder argLocal)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Fills the body of the pre-declared
    /// <c>NewPromiseCapabilityResult(Type species, Task&lt;object&gt; result)</c>:
    /// constructs <c>new species(executor)</c> through ConstructDynamicValue
    /// (Type → Activator), captures the resolve/reject, schedules adoption of
    /// <paramref name="result"/> onto the current SynchronizationContext, and
    /// returns the constructed object.
    /// </summary>
    private void EmitNewPromiseCapabilityResultBody(EmittedRuntime runtime)
    {
        var method = runtime.NewPromiseCapabilityResultMethod;
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PreparePromiseCapabilityMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.AdoptPromiseCapabilityMethod);
        il.Emit(OpCodes.Ret);
    }
}
