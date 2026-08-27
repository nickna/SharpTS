using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the compiled-mode equivalent of the interpreter's discarded-promise
    /// bookkeeping. A ConditionalWeakTable keeps promise identity without turning
    /// settled promises into process-lifetime roots; all guest events are marshalled
    /// back to the compiled event loop.
    /// </summary>
    private void EmitPromiseRejectionTracking(
        ModuleBuilder moduleBuilder,
        TypeBuilder runtimeType,
        EmittedRuntime runtime)
    {
        var taskType = _types.TaskOfObject;
        var trackerType = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$PromiseRejectionTracker",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var taskField = trackerType.DefineField(
            "Task", taskType, FieldAttributes.Private | FieldAttributes.InitOnly);
        var identityField = trackerType.DefineField(
            "Promise", _types.Object, FieldAttributes.Private | FieldAttributes.InitOnly);
        var handledField = trackerType.DefineField(
            "Handled", _types.Boolean, FieldAttributes.Public);
        var reportedField = trackerType.DefineField(
            "Reported", _types.Boolean, FieldAttributes.Public);

        var ctor = trackerType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [taskType, _types.Object]);
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, taskField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, identityField);
            il.Emit(OpCodes.Ret);
        }

        var report = trackerType.DefineMethod(
            "Report",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);
        var onCompleted = trackerType.DefineMethod(
            "OnCompleted",
            MethodAttributes.Public,
            _types.Void,
            [taskType]);
        var emitHandled = trackerType.DefineMethod(
            "EmitHandled",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);

        var cwtType = _types.MakeGenericType(
            typeof(ConditionalWeakTable<,>), _types.Object, trackerType);
        var trackersField = runtimeType.DefineField(
            "_promiseRejectionTrackers",
            cwtType,
            FieldAttributes.Private | FieldAttributes.Static);
        var cwtCtor = EmitterTypeHelpers.ResolveConstructor(
            cwtType,
            typeof(ConditionalWeakTable<,>).GetConstructor(Type.EmptyTypes)!);
        var cwtTryGet = EmitterTypeHelpers.ResolveMethod(
            cwtType,
            typeof(ConditionalWeakTable<,>).GetMethod("TryGetValue")!);
        var cwtAdd = EmitterTypeHelpers.ResolveMethod(
            cwtType,
            typeof(ConditionalWeakTable<,>).GetMethod("Add")!);

        EmitPromiseRejectionReport(
            report.GetILGenerator(), runtime, taskField, identityField,
            handledField, reportedField);
        EmitPromiseRejectionHandled(
            emitHandled.GetILGenerator(), runtime, identityField);
        EmitPromiseRejectionCompletion(
            onCompleted.GetILGenerator(), runtime, report);

        EmitObserveDiscardedPromiseResult(
            runtimeType, runtime, trackerType, ctor, report, onCompleted,
            trackersField, cwtCtor, cwtTryGet, cwtAdd);
        EmitNotifyPromiseRejectionHandler(
            runtimeType, runtime, trackerType, ctor, emitHandled,
            handledField, reportedField, trackersField,
            cwtCtor, cwtTryGet, cwtAdd);

        trackerType.CreateType();
    }

    private void EmitPromiseRejectionReport(
        ILGenerator il,
        EmittedRuntime runtime,
        FieldBuilder taskField,
        FieldBuilder identityField,
        FieldBuilder handledField,
        FieldBuilder reportedField)
    {
        var unref = il.DefineLabel();
        var taskType = _types.TaskOfObject;

        // Only faulted, still-unhandled tasks produce an event. The observing
        // path holds one event-loop ref until this check has run.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, taskField);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(taskType, "IsFaulted").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, unref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, handledField);
        il.Emit(OpCodes.Brtrue, unref);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, reportedField);

        // reason = WrapException(Task.Exception.InnerException)
        var reason = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, taskField);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(taskType, "Exception").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.Exception, "InnerException").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.WrapException);
        il.Emit(OpCodes.Stloc, reason);

        // process.emit('unhandledRejection', reason, promise)
        il.Emit(OpCodes.Call, runtime.GetProcessObject);
        il.Emit(OpCodes.Castclass, runtime.TSEventEmitterType);
        il.Emit(OpCodes.Ldstr, "unhandledRejection");
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, reason);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, identityField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(unref);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
        il.Emit(OpCodes.Ret);
    }

    private void EmitPromiseRejectionHandled(
        ILGenerator il,
        EmittedRuntime runtime,
        FieldBuilder identityField)
    {
        // process.emit('rejectionHandled', promise)
        il.Emit(OpCodes.Call, runtime.GetProcessObject);
        il.Emit(OpCodes.Castclass, runtime.TSEventEmitterType);
        il.Emit(OpCodes.Ldstr, "rejectionHandled");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, identityField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopUnref);
        il.Emit(OpCodes.Ret);
    }

    private void EmitPromiseRejectionCompletion(
        ILGenerator il,
        EmittedRuntime runtime,
        MethodBuilder report)
    {
        // Task continuations never invoke guest code on their worker thread.
        // They only enqueue the report action on the JS event loop.
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, report);
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(typeof(Action), [_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);
        il.Emit(OpCodes.Ret);
    }

    private void EmitObserveDiscardedPromiseResult(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        TypeBuilder trackerType,
        ConstructorBuilder trackerCtor,
        MethodBuilder report,
        MethodBuilder onCompleted,
        FieldBuilder trackersField,
        ConstructorInfo cwtCtor,
        MethodInfo cwtTryGet,
        MethodInfo cwtAdd)
    {
        var method = runtimeType.DefineMethod(
            "ObserveDiscardedPromiseResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]);
        runtime.ObserveDiscardedPromiseResult = method;

        var il = method.GetILGenerator();
        var task = il.DeclareLocal(_types.TaskOfObject);
        var wrapper = il.DeclareLocal(runtime.TSPromiseType);
        var tracker = il.DeclareLocal(trackerType);
        var haveTask = il.DefineLabel();
        var haveTable = il.DefineLabel();
        var addTracker = il.DefineLabel();
        var schedule = il.DefineLabel();
        var ret = il.DefineLabel();

        // Normalize raw Task<object> and $Promise values to the underlying task.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Brtrue, haveTask);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Stloc, wrapper);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Brfalse, ret);
        il.Emit(OpCodes.Ldloc, wrapper);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, task);
        il.MarkLabel(haveTask);

        // Lazily initialize because $Runtime's type initializer has already been
        // emitted by the time promise helpers are filled.
        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Brtrue, haveTable);
        il.Emit(OpCodes.Newobj, cwtCtor);
        il.Emit(OpCodes.Stsfld, trackersField);
        il.MarkLabel(haveTable);

        // Existing entries include promises whose rejection handler was attached
        // before their callback result became discarded.
        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldloca, tracker);
        il.Emit(OpCodes.Callvirt, cwtTryGet);
        il.Emit(OpCodes.Brfalse, addTracker);
        il.Emit(OpCodes.Br, ret);

        il.MarkLabel(addTracker);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, trackerCtor);
        il.Emit(OpCodes.Stloc, tracker);
        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Callvirt, cwtAdd);

        // Keep the loop alive until success/failure has been classified.
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);

        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.TaskOfObject, "IsCompleted").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, schedule);

        var continuationType = _types.MakeGenericType(
            typeof(Action<>), _types.TaskOfObject);
        var continuationCtor = _types.GetConstructor(
            continuationType, [_types.Object, _types.IntPtr])!;
        var continueWith = _types.GetMethod(
            _types.TaskOfObject,
            "ContinueWith",
            [continuationType, typeof(TaskContinuationOptions)])!;
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldftn, onCompleted);
        il.Emit(OpCodes.Newobj, continuationCtor);
        il.Emit(OpCodes.Ldc_I4,
            (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Callvirt, continueWith);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, ret);

        il.MarkLabel(schedule);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldftn, report);
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(typeof(Action), [_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNotifyPromiseRejectionHandler(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        TypeBuilder trackerType,
        ConstructorBuilder trackerCtor,
        MethodBuilder emitHandled,
        FieldBuilder handledField,
        FieldBuilder reportedField,
        FieldBuilder trackersField,
        ConstructorInfo cwtCtor,
        MethodInfo cwtTryGet,
        MethodInfo cwtAdd)
    {
        var method = runtimeType.DefineMethod(
            "NotifyPromiseRejectionHandler",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.TaskOfObject]);
        runtime.NotifyPromiseRejectionHandler = method;

        var il = method.GetILGenerator();
        var tracker = il.DeclareLocal(trackerType);
        var haveTable = il.DefineLabel();
        var haveTracker = il.DefineLabel();
        var ret = il.DefineLabel();

        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Brtrue, haveTable);
        il.Emit(OpCodes.Newobj, cwtCtor);
        il.Emit(OpCodes.Stsfld, trackersField);
        il.MarkLabel(haveTable);

        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, tracker);
        il.Emit(OpCodes.Callvirt, cwtTryGet);
        il.Emit(OpCodes.Brtrue, haveTracker);

        // Remember early handler attachment without observing/reporting the task.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, trackerCtor);
        il.Emit(OpCodes.Stloc, tracker);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, handledField);
        il.Emit(OpCodes.Ldsfld, trackersField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Callvirt, cwtAdd);
        il.Emit(OpCodes.Br, ret);

        il.MarkLabel(haveTracker);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldfld, handledField);
        il.Emit(OpCodes.Brtrue, ret);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, handledField);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldfld, reportedField);
        il.Emit(OpCodes.Brfalse, ret);

        // rejectionHandled is itself a later event-loop turn.
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopRef);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldloc, tracker);
        il.Emit(OpCodes.Ldftn, emitHandled);
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(typeof(Action), [_types.Object, _types.IntPtr])!);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopSchedule);

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ret);
    }
}
