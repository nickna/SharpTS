using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Timer support for compiled TypeScript: setTimeout, clearTimeout, setInterval, clearInterval.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Implementation Notes:</strong>
/// Timers use <see cref="System.Threading.Tasks.Task.Delay"/> with <c>ContinueWith</c> to schedule
/// callbacks on the .NET thread pool. This provides non-blocking timer behavior without requiring
/// a dedicated event loop thread.
/// </para>
/// <para>
/// <strong>Important Behavioral Difference from Node.js:</strong>
/// Unlike Node.js, timers do NOT keep the process alive. When <c>Main()</c> returns, the process
/// exits regardless of pending timers. In Node.js, timers with <c>.ref()</c> (the default) keep
/// the event loop running until all referenced timers complete or are cleared.
/// </para>
/// <para>
/// This is a deliberate trade-off. Implementing Node.js-style event loop semantics would require:
/// <list type="bullet">
/// <item>A global timer registry tracking all active timers with <c>HasRef = true</c></item>
/// <item>Modified entry point that waits for all referenced timers before returning</item>
/// <item>Proper handling of <c>.ref()</c> and <c>.unref()</c> to add/remove from the registry</item>
/// </list>
/// For most compiled TypeScript use cases (CLI tools, services with their own lifecycle, scripts),
/// the current behavior is acceptable. Programs that need timer-based keepalive can use explicit
/// waiting mechanisms (busy-wait loops, <c>Thread.Sleep</c>, or async patterns).
/// </para>
/// </remarks>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $TimeoutClosure class for capturing callback, args, and cancellation token.
    /// This class is used to invoke the callback after the delay in setTimeout.
    /// </summary>
    private void EmitTimeoutClosureClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TimeoutClosure
        var typeBuilder = moduleBuilder.DefineType(
            "$TimeoutClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.TimeoutClosureType = typeBuilder;

        // Fields: Callback ($TSFunction), Args (object[]), Cts (CancellationTokenSource)
        var callbackField = typeBuilder.DefineField("Callback", runtime.TSFunctionType, FieldAttributes.Public);
        var argsField = typeBuilder.DefineField("Args", _types.ObjectArray, FieldAttributes.Public);
        var ctsField = typeBuilder.DefineField("Cts", _types.CancellationTokenSource, FieldAttributes.Public);

        runtime.TimeoutClosureCallback = callbackField;
        runtime.TimeoutClosureArgs = argsField;
        runtime.TimeoutClosureCts = ctsField;

        // Default constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TimeoutClosureCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Execute method: public void Execute(Task t)
        // This is called by ContinueWith after the delay completes
        var executeMethod = typeBuilder.DefineMethod(
            "Execute",
            MethodAttributes.Public,
            _types.Void,
            [_types.Task]
        );
        runtime.TimeoutClosureExecute = executeMethod;

        var il = executeMethod.GetILGenerator();
        var skipLabel = il.DefineLabel();

        // if (t.IsCanceled || Cts.IsCancellationRequested) return;
        // Check t.IsCanceled
        il.Emit(OpCodes.Ldarg_1); // t
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Check Cts.IsCancellationRequested
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Callback.Invoke(Args)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, callbackField);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, argsField);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop); // Discard return value

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $TSTimeout class for timer support in compiled assemblies.
    /// Provides unique ID generation, cancellation, and ref/unref behavior.
    /// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTimeout
    /// </summary>
    private void EmitTSTimeoutClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TSTimeout
        var typeBuilder = moduleBuilder.DefineType(
            "$TSTimeout",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.TSTimeoutType = typeBuilder;

        // Static field: private static int _nextId = 0
        var nextIdField = typeBuilder.DefineField(
            "_nextId",
            _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static
        );

        // Instance fields
        var idField = typeBuilder.DefineField("_id", _types.Int32, FieldAttributes.Private);
        var virtualTimerField = typeBuilder.DefineField("_virtualTimer", runtime.VirtualTimerType, FieldAttributes.Private);
        var hasRefField = typeBuilder.DefineField("_hasRef", _types.Boolean, FieldAttributes.Private);

        runtime.TSTimeoutVirtualTimerField = virtualTimerField;

        // Constructor: public $TSTimeout($VirtualTimer virtualTimer)
        EmitTSTimeoutConstructor(typeBuilder, runtime, nextIdField, idField, virtualTimerField, hasRefField);

        // Cancel method: public void Cancel()
        EmitTSTimeoutCancel(typeBuilder, runtime, virtualTimerField);

        // Ref method: public $TSTimeout Ref()
        EmitTSTimeoutRef(typeBuilder, runtime, hasRefField);

        // Unref method: public $TSTimeout Unref()
        EmitTSTimeoutUnref(typeBuilder, runtime, hasRefField);

        // HasRef property getter: public bool HasRef { get; }
        EmitTSTimeoutHasRefGetter(typeBuilder, runtime, hasRefField);

        // ToString override
        EmitTSTimeoutToString(typeBuilder, idField);

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitTSTimeoutConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder nextIdField, FieldBuilder idField, FieldBuilder virtualTimerField, FieldBuilder hasRefField)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.VirtualTimerType]
        );
        runtime.TSTimeoutCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));

        // _id = Interlocked.Increment(ref _nextId)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsflda, nextIdField);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Interlocked, "Increment", _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Stfld, idField);

        // _virtualTimer = virtualTimer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, virtualTimerField);

        // _hasRef = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, hasRefField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutCancel(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder virtualTimerField)
    {
        var method = typeBuilder.DefineMethod(
            "Cancel",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSTimeoutCancel = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // if (_virtualTimer == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, virtualTimerField);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // _virtualTimer.IsCancelled = true;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, virtualTimerField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerIsCancelled);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutRef(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "Ref",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSTimeoutRef = method;

        var il = method.GetILGenerator();

        // _hasRef = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, hasRefField);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutUnref(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "Unref",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.TSTimeoutUnref = method;

        var il = method.GetILGenerator();

        // _hasRef = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, hasRefField);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTimeoutHasRefGetter(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder hasRefField)
    {
        var method = typeBuilder.DefineMethod(
            "get_HasRef",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSTimeoutHasRefGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hasRefField);
        il.Emit(OpCodes.Ret);

        // Define property
        var property = typeBuilder.DefineProperty(
            "HasRef",
            PropertyAttributes.None,
            _types.Boolean,
            Type.EmptyTypes
        );
        property.SetGetMethod(method);
    }

    private void EmitTSTimeoutToString(TypeBuilder typeBuilder, FieldBuilder idField)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // return $"Timeout {{ _id: {_id} }}"
        il.Emit(OpCodes.Ldstr, "Timeout {{ _id: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, idField);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Ldstr, " }}");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String, _types.String));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $TSTimeout SetTimeout($TSFunction callback, double delay, object[] args)
    /// Creates a $VirtualTimer and adds it to the queue for single-threaded execution.
    /// Timers are processed when Date.now() is called (via ProcessPendingTimers).
    /// </summary>
    private void EmitSetTimeoutMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTimeout",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSTimeoutType,
            [runtime.TSFunctionType, _types.Double, _types.ObjectArray]
        );
        runtime.SetTimeout = method;

        var il = method.GetILGenerator();

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // delay
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // var virtualTimer = new $VirtualTimer();
        var virtualTimerLocal = il.DeclareLocal(runtime.VirtualTimerType);
        il.Emit(OpCodes.Newobj, runtime.VirtualTimerCtor);
        il.Emit(OpCodes.Stloc, virtualTimerLocal);

        // virtualTimer.Callback = callback (arg0)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldarg_0); // callback
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerCallback);

        // virtualTimer.Args = args (arg2)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldarg_2); // args
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerArgs);

        // virtualTimer.ScheduledTime = GetCurrentTimeMs() + delayMs
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Call, runtime.GetCurrentTimeMs);
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerScheduledTime);

        // virtualTimer.IsCancelled = false (default)
        // virtualTimer.IsInterval = false (default)
        // virtualTimer.IntervalMs = 0 (default)

        // AddVirtualTimer(virtualTimer)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Call, runtime.AddVirtualTimer);

        // var timeout = new $TSTimeout(virtualTimer);
        var timeoutLocal = il.DeclareLocal(runtime.TSTimeoutType);
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Newobj, runtime.TSTimeoutCtor);
        il.Emit(OpCodes.Stloc, timeoutLocal);

        // return timeout
        il.Emit(OpCodes.Ldloc, timeoutLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ClearTimeout(object handle)
    /// Cancels the timeout if handle is a $TSTimeout.
    /// </summary>
    private void EmitClearTimeoutMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearTimeout",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ClearTimeout = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // if (handle is $TSTimeout timeout) timeout.Cancel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSTimeoutType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Call Cancel on the timeout
        il.Emit(OpCodes.Callvirt, runtime.TSTimeoutCancel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Pop); // Remove null from stack
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $IntervalClosure class for capturing callback, args, delay, and cancellation token.
    /// This class is used to invoke the callback repeatedly in setInterval.
    /// Uses ContinueWith for non-overlapping execution.
    /// </summary>
    private void EmitIntervalClosureClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $IntervalClosure
        var typeBuilder = moduleBuilder.DefineType(
            "$IntervalClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.IntervalClosureType = typeBuilder;

        // Fields: Callback ($TSFunction), Args (object[]), Cts (CancellationTokenSource), DelayMs (int)
        var callbackField = typeBuilder.DefineField("Callback", runtime.TSFunctionType, FieldAttributes.Public);
        var argsField = typeBuilder.DefineField("Args", _types.ObjectArray, FieldAttributes.Public);
        var ctsField = typeBuilder.DefineField("Cts", _types.CancellationTokenSource, FieldAttributes.Public);
        var delayMsField = typeBuilder.DefineField("DelayMs", _types.Int32, FieldAttributes.Public);

        runtime.IntervalClosureCallback = callbackField;
        runtime.IntervalClosureArgs = argsField;
        runtime.IntervalClosureCts = ctsField;
        runtime.IntervalClosureDelayMs = delayMsField;

        // Default constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.IntervalClosureCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // ExecuteIteration method: public void ExecuteIteration(Task t)
        // This is called by ContinueWith after each delay completes
        var executeMethod = typeBuilder.DefineMethod(
            "ExecuteIteration",
            MethodAttributes.Public,
            _types.Void,
            [_types.Task]
        );
        runtime.IntervalClosureExecuteIteration = executeMethod;

        var il = executeMethod.GetILGenerator();
        var skipLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (t.IsCanceled || Cts.IsCancellationRequested) return;
        // Check t.IsCanceled
        il.Emit(OpCodes.Ldarg_1); // t
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Check Cts.IsCancellationRequested
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Callback.Invoke(Args)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, callbackField);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, argsField);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop); // Discard return value

        // Check cancellation again after callback
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Schedule next iteration: Task.Delay(DelayMs, Cts.Token).ContinueWith(this.ExecuteIteration)
        // Create Action<Task> delegate: new Action<Task>(this.ExecuteIteration)
        var actionLocal = il.DeclareLocal(_types.ActionOfTask);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldftn, executeMethod);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ActionOfTask, _types.Object, _types.IntPtr));
        il.Emit(OpCodes.Stloc, actionLocal);

        // Task.Delay(DelayMs, Cts.Token)
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, delayMsField);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldfld, ctsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.CancellationTokenSource, "Token").GetGetMethod()!);
        var taskDelayMethod = _types.GetMethod(_types.Task, "Delay", _types.Int32, _types.CancellationToken);
        il.Emit(OpCodes.Call, taskDelayMethod);

        // .ContinueWith(action)
        il.Emit(OpCodes.Ldloc, actionLocal);
        var continueWithMethod = _types.GetMethod(_types.Task, "ContinueWith", _types.ActionOfTask);
        il.Emit(OpCodes.Callvirt, continueWithMethod);
        il.Emit(OpCodes.Pop); // Discard the continuation task

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public static $TSTimeout SetInterval($TSFunction callback, double delay, object[] args)
    /// Creates a $VirtualTimer marked as interval and adds it to the queue.
    /// Timers are processed when Date.now() is called (via ProcessPendingTimers).
    /// </summary>
    private void EmitSetIntervalMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetInterval",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.TSTimeoutType,
            [runtime.TSFunctionType, _types.Double, _types.ObjectArray]
        );
        runtime.SetInterval = method;

        var il = method.GetILGenerator();

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // delay
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // var virtualTimer = new $VirtualTimer();
        var virtualTimerLocal = il.DeclareLocal(runtime.VirtualTimerType);
        il.Emit(OpCodes.Newobj, runtime.VirtualTimerCtor);
        il.Emit(OpCodes.Stloc, virtualTimerLocal);

        // virtualTimer.Callback = callback (arg0)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldarg_0); // callback
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerCallback);

        // virtualTimer.Args = args (arg2)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldarg_2); // args
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerArgs);

        // virtualTimer.ScheduledTime = GetCurrentTimeMs() + delayMs
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Call, runtime.GetCurrentTimeMs);
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerScheduledTime);

        // virtualTimer.IsCancelled = false (default)

        // virtualTimer.IsInterval = true
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerIsInterval);

        // virtualTimer.IntervalMs = delayMs
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerIntervalMs);

        // AddVirtualTimer(virtualTimer)
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Call, runtime.AddVirtualTimer);

        // var interval = new $TSTimeout(virtualTimer);
        var intervalLocal = il.DeclareLocal(runtime.TSTimeoutType);
        il.Emit(OpCodes.Ldloc, virtualTimerLocal);
        il.Emit(OpCodes.Newobj, runtime.TSTimeoutCtor);
        il.Emit(OpCodes.Stloc, intervalLocal);

        // return interval
        il.Emit(OpCodes.Ldloc, intervalLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ClearInterval(object handle)
    /// Cancels the interval if handle is a $TSTimeout.
    /// </summary>
    private void EmitClearIntervalMethod(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearInterval",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.ClearInterval = method;

        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // if (handle is $TSTimeout interval) interval.Cancel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSTimeoutType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // Call Cancel on the interval
        il.Emit(OpCodes.Callvirt, runtime.TSTimeoutCancel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Pop); // Remove null from stack
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits wrapper methods for timer functions that can be called via TSFunction invocation.
    /// These wrappers take object[] args and extract the callback, delay, and extra args.
    /// </summary>
    internal void EmitTimerModuleWrappers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        EmitSetTimeoutWrapper(runtimeType, runtime);
        EmitClearTimeoutWrapper(runtimeType, runtime);
        EmitSetIntervalWrapper(runtimeType, runtime);
        EmitClearIntervalWrapper(runtimeType, runtime);
        EmitSetImmediateWrapper(runtimeType, runtime);
        EmitClearImmediateWrapper(runtimeType, runtime);
    }

    /// <summary>
    /// Emits: object SetTimeoutWrapper(object[] args)
    /// Wrapper for setTimeout that can be invoked as a TSFunction.
    /// </summary>
    private void EmitSetTimeoutWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTimeoutWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var callbackLocal = il.DeclareLocal(runtime.TSFunctionType);
        var delayLocal = il.DeclareLocal(_types.Double);
        var extraArgsLocal = il.DeclareLocal(_types.ObjectArray);

        var hasCallbackLabel = il.DefineLabel();
        var hasDelayLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();

        // Extract callback from args[0]
        // if (args == null || args.Length == 0) callback = null; else callback = args[0] as $TSFunction;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, hasCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, hasCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br_S, hasDelayLabel);

        il.MarkLabel(hasCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Extract delay from args[1]
        il.MarkLabel(hasDelayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble_S, callLabel);

        // delay = Convert.ToDouble(args[1])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, _types.ConvertToDoubleFromObject);
        il.Emit(OpCodes.Stloc, delayLocal);
        il.Emit(OpCodes.Br_S, extractExtraArgs());

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, delayLocal);

        // Extract extra args (args[2..])
        Label extractExtraArgs()
        {
            var label = il.DefineLabel();
            il.MarkLabel(label);

            var hasExtraArgsLabel = il.DefineLabel();
            var noExtraArgsLabel = il.DefineLabel();
            var afterExtraArgsLabel = il.DefineLabel();

            // if (args.Length > 2) extraArgs = args[2..]; else extraArgs = new object[0];
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brfalse_S, noExtraArgsLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ble_S, noExtraArgsLabel);

            // Create array for extra args
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Stloc, extraArgsLocal);

            // Array.Copy(args, 2, extraArgs, 0, extraArgs.Length)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldloc, extraArgsLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, extraArgsLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);
            il.Emit(OpCodes.Br_S, afterExtraArgsLabel);

            il.MarkLabel(noExtraArgsLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Stloc, extraArgsLocal);

            il.MarkLabel(afterExtraArgsLabel);
            return label;
        }

        // Call SetTimeout(callback, delay, extraArgs)
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, delayLocal);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Call, runtime.SetTimeout);
        il.Emit(OpCodes.Ret);

        // Register the wrapper for the timers module
        runtime.RegisterBuiltInModuleMethod("timers", "setTimeout", method);
    }

    /// <summary>
    /// Emits: object ClearTimeoutWrapper(object[] args)
    /// </summary>
    private void EmitClearTimeoutWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearTimeoutWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var hasHandleLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();

        // Extract handle from args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, callLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ClearTimeout);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.ClearTimeout);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("timers", "clearTimeout", method);
    }

    /// <summary>
    /// Emits: object SetIntervalWrapper(object[] args)
    /// </summary>
    private void EmitSetIntervalWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetIntervalWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var callbackLocal = il.DeclareLocal(runtime.TSFunctionType);
        var delayLocal = il.DeclareLocal(_types.Double);
        var extraArgsLocal = il.DeclareLocal(_types.ObjectArray);

        var hasCallbackLabel = il.DefineLabel();
        var hasDelayLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();
        var afterExtraArgsLabel = il.DefineLabel();
        var noExtraArgsLabel = il.DefineLabel();

        // Extract callback from args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, hasCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, hasCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br_S, hasDelayLabel);

        il.MarkLabel(hasCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Extract delay from args[1]
        il.MarkLabel(hasDelayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble_S, callLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, _types.ConvertToDoubleFromObject);
        il.Emit(OpCodes.Stloc, delayLocal);

        // Extract extra args
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble_S, noExtraArgsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, extraArgsLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        il.Emit(OpCodes.Br_S, afterExtraArgsLabel);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, delayLocal);

        il.MarkLabel(noExtraArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, extraArgsLocal);

        il.MarkLabel(afterExtraArgsLabel);

        // Call SetInterval(callback, delay, extraArgs)
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, delayLocal);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Call, runtime.SetInterval);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("timers", "setInterval", method);
    }

    /// <summary>
    /// Emits: object ClearIntervalWrapper(object[] args)
    /// </summary>
    private void EmitClearIntervalWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearIntervalWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var callLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, callLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ClearInterval);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.ClearInterval);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("timers", "clearInterval", method);
    }

    /// <summary>
    /// Emits: object SetImmediateWrapper(object[] args)
    /// setImmediate is like setTimeout with 0 delay.
    /// </summary>
    private void EmitSetImmediateWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetImmediateWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var callbackLocal = il.DeclareLocal(runtime.TSFunctionType);
        var extraArgsLocal = il.DeclareLocal(_types.ObjectArray);

        var hasCallbackLabel = il.DefineLabel();
        var afterExtraArgsLabel = il.DefineLabel();
        var noExtraArgsLabel = il.DefineLabel();

        // Extract callback from args[0]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, hasCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, hasCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Extract extra args (args[1..]) - setImmediate has no delay parameter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble_S, noExtraArgsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, extraArgsLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        il.Emit(OpCodes.Br_S, afterExtraArgsLabel);

        il.MarkLabel(hasCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.MarkLabel(noExtraArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, extraArgsLocal);

        il.MarkLabel(afterExtraArgsLabel);

        // Call SetTimeout(callback, 0, extraArgs) - setImmediate is setTimeout with 0 delay
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ldloc, extraArgsLocal);
        il.Emit(OpCodes.Call, runtime.SetTimeout);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("timers", "setImmediate", method);
    }

    /// <summary>
    /// Emits: object ClearImmediateWrapper(object[] args)
    /// clearImmediate is the same as clearTimeout.
    /// </summary>
    private void EmitClearImmediateWrapper(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ClearImmediateWrapper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = method.GetILGenerator();

        var callLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble_S, callLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.ClearTimeout);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.ClearTimeout);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.RegisterBuiltInModuleMethod("timers", "clearImmediate", method);
    }
}
