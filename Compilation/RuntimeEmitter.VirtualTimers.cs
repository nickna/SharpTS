using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Virtual timer infrastructure for compiled assemblies.
/// Implements JavaScript-like single-threaded timer semantics by processing
/// timer callbacks on the main thread during event loop iterations and Date.now() calls.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $VirtualTimer class that holds timer callback information.
    /// </summary>
    private void EmitVirtualTimerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$VirtualTimer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.VirtualTimerType = typeBuilder;

        // Fields
        var callbackField = typeBuilder.DefineField("Callback", _types.Object, FieldAttributes.Public);
        var argsField = typeBuilder.DefineField("Args", _types.ObjectArray, FieldAttributes.Public);
        var scheduledTimeField = typeBuilder.DefineField("ScheduledTime", _types.Int64, FieldAttributes.Public);
        var isCancelledField = typeBuilder.DefineField("IsCancelled", _types.Boolean, FieldAttributes.Public);
        var isIntervalField = typeBuilder.DefineField("IsInterval", _types.Boolean, FieldAttributes.Public);
        var intervalMsField = typeBuilder.DefineField("IntervalMs", _types.Int32, FieldAttributes.Public);
        var hasRefField = typeBuilder.DefineField("HasRef", _types.Boolean, FieldAttributes.Public);

        runtime.VirtualTimerCallback = callbackField;
        runtime.VirtualTimerArgs = argsField;
        runtime.VirtualTimerScheduledTime = scheduledTimeField;
        runtime.VirtualTimerIsCancelled = isCancelledField;
        runtime.VirtualTimerIsInterval = isIntervalField;
        runtime.VirtualTimerIntervalMs = intervalMsField;
        runtime.VirtualTimerHasRef = hasRefField;

        // Default constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.VirtualTimerCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the static timer queue and related infrastructure.
    /// </summary>
    private void EmitTimerQueueInfrastructure(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Static field: List<$VirtualTimer> _timerQueue
        var listType = _types.MakeGenericType(_types.ListOpen, runtime.VirtualTimerType);
        var timerQueueField = runtimeType.DefineField(
            "_timerQueue",
            listType,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = timerQueueField;

        // Static field: long _timerStartTicks (for high-resolution timing)
        var startTicksField = runtimeType.DefineField(
            "_timerStartTicks",
            _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = startTicksField;

        // Static field: bool _timerInitialized
        var initializedField = runtimeType.DefineField(
            "_timerInitialized",
            _types.Boolean,
            FieldAttributes.Private | FieldAttributes.Static
        );
        _ = initializedField;

        // Emit helper methods
        EmitEnsureTimerInitialized(runtimeType, runtime, timerQueueField, startTicksField, initializedField);
        EmitGetCurrentTimeMs(runtimeType, runtime, startTicksField, initializedField);
        EmitProcessPendingTimers(runtimeType, runtime, timerQueueField);
        EmitAddVirtualTimer(runtimeType, runtime, timerQueueField);
    }

    /// <summary>
    /// Emits: private static void EnsureTimerInitialized()
    /// </summary>
    private void EmitEnsureTimerInitialized(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        FieldBuilder timerQueueField,
        FieldBuilder startTicksField,
        FieldBuilder initializedField)
    {
        var method = runtimeType.DefineMethod(
            "EnsureTimerInitialized",
            MethodAttributes.Private | MethodAttributes.Static,
            null,
            Type.EmptyTypes
        );
        runtime.EnsureTimerInitialized = method;

        var il = method.GetILGenerator();
        var alreadyInitializedLabel = il.DefineLabel();

        // if (_timerInitialized) return;
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue_S, alreadyInitializedLabel);

        // _timerQueue = new List<$VirtualTimer>();
        var listType = _types.MakeGenericType(_types.ListOpen, runtime.VirtualTimerType);
        // Use TypeBuilder.GetConstructor for generic types containing TypeBuilder
        var listOpenCtor = _types.GetConstructor(_types.ListOpen, Type.EmptyTypes)!;
        var listCtor = EmitterTypeHelpers.ResolveConstructor(listType, listOpenCtor);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stsfld, timerQueueField);

        // _timerStartTicks = Stopwatch.GetTimestamp();
        il.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        il.Emit(OpCodes.Stsfld, startTicksField);

        // _timerInitialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        il.MarkLabel(alreadyInitializedLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static long GetCurrentTimeMs()
    /// Returns milliseconds since timer initialization (for scheduling).
    /// </summary>
    private void EmitGetCurrentTimeMs(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        FieldBuilder startTicksField,
        FieldBuilder initializedField)
    {
        var method = runtimeType.DefineMethod(
            "GetCurrentTimeMs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int64,
            Type.EmptyTypes
        );
        runtime.GetCurrentTimeMs = method;

        var il = method.GetILGenerator();

        // EnsureTimerInitialized();
        il.Emit(OpCodes.Call, runtime.EnsureTimerInitialized);

        // return (Stopwatch.GetTimestamp() - _timerStartTicks) * 1000 / Stopwatch.Frequency;
        il.Emit(OpCodes.Call, _types.StopwatchGetTimestamp);
        il.Emit(OpCodes.Ldsfld, startTicksField);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I8, 1000L);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldsfld, typeof(System.Diagnostics.Stopwatch).GetField("Frequency")!);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static int ProcessPendingTimers()
    /// Checks and executes any timers that are due.
    /// Returns the number of milliseconds until the next timer fires, or -1 if no timers exist.
    /// This return value is used by EventLoop.Run() to set its wait timeout precisely.
    /// </summary>
    private void EmitProcessPendingTimers(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        FieldBuilder timerQueueField)
    {
        var method = runtimeType.DefineMethod(
            "ProcessPendingTimers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.ProcessPendingTimers = method;

        var il = method.GetILGenerator();
        var listType = _types.MakeGenericType(_types.ListOpen, runtime.VirtualTimerType);

        // Get generic methods for List<$VirtualTimer> using TypeBuilder.GetMethod
        var listOpenCountGetter = _types.GetProperty(_types.ListOpen, "Count")!.GetGetMethod()!;
        var countGetter = EmitterTypeHelpers.ResolveMethod(listType, listOpenCountGetter);
        var listOpenGetItem = _types.GetMethod(_types.ListOpen, "get_Item")!;
        var getItem = EmitterTypeHelpers.ResolveMethod(listType, listOpenGetItem);
        var listOpenRemoveAt = _types.GetMethod(_types.ListOpen, "RemoveAt")!;
        var removeAt = EmitterTypeHelpers.ResolveMethod(listType, listOpenRemoveAt);

        // Process microtasks first - they always run before any macrotask (timers)
        // This ensures correct JavaScript event loop semantics
        il.Emit(OpCodes.Call, runtime.ProcessMicrotasks);

        // EnsureTimerInitialized();
        il.Emit(OpCodes.Call, runtime.EnsureTimerInitialized);

        // long currentTime = GetCurrentTimeMs();
        var currentTimeLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Call, runtime.GetCurrentTimeMs);
        il.Emit(OpCodes.Stloc, currentTimeLocal);

        // long minNextTime = long.MaxValue (track earliest future timer)
        var minNextTimeLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldc_I8, long.MaxValue);
        il.Emit(OpCodes.Stloc, minNextTimeLocal);

        // Process timers in a loop (need to handle removals and intervals)
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        var iLocal = il.DeclareLocal(_types.Int32);
        var timerLocal = il.DeclareLocal(runtime.VirtualTimerType);
        var countLocal = il.DeclareLocal(_types.Int32);

        // i = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStartLabel);

        // count = _timerQueue.Count;
        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Stloc, countLocal);

        // if (i >= count) break;
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // timer = _timerQueue[i];
        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, getItem);
        il.Emit(OpCodes.Stloc, timerLocal);

        // if (timer.IsCancelled) { _timerQueue.RemoveAt(i); if (timer.HasRef) EventLoop.Unref(); continue; }
        var notCancelledLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerIsCancelled);
        il.Emit(OpCodes.Brfalse_S, notCancelledLabel);

        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, removeAt);

        // if (timer.HasRef) { timer.HasRef = false; EventLoop.GetInstance().Unref(); }
        var skipCancelUnref = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerHasRef);
        il.Emit(OpCodes.Brfalse, skipCancelUnref);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerHasRef);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);
        il.MarkLabel(skipCancelUnref);

        il.Emit(OpCodes.Br, loopStartLabel); // Don't increment i, continue from same index

        il.MarkLabel(notCancelledLabel);

        // if (timer.ScheduledTime > currentTime) { track min, i++; continue; }
        var isDueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Ldloc, currentTimeLocal);
        il.Emit(OpCodes.Ble, isDueLabel);

        // Timer not due yet — track minimum ScheduledTime for return value
        // if (timer.ScheduledTime < minNextTime) minNextTime = timer.ScheduledTime;
        var skipMinUpdate = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Ldloc, minNextTimeLocal);
        il.Emit(OpCodes.Bge, skipMinUpdate);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Stloc, minNextTimeLocal);
        il.MarkLabel(skipMinUpdate);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(isDueLabel);

        // Timer is due - execute callback
        // try { timer.Callback.Invoke(timer.Args); } catch { }
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerCallback);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerArgs);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop); // Discard result

        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop); // Discard exception
        il.EndExceptionBlock();

        // if (timer.IsInterval && !timer.IsCancelled) { reschedule } else { remove }
        var removeTimerLabel = il.DefineLabel();
        var afterHandleLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerIsInterval);
        il.Emit(OpCodes.Brfalse_S, removeTimerLabel);

        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerIsCancelled);
        il.Emit(OpCodes.Brtrue_S, removeTimerLabel);

        // Reschedule interval: timer.ScheduledTime = currentTime + timer.IntervalMs;
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldloc, currentTimeLocal);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerIntervalMs);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerScheduledTime);

        // Track rescheduled interval in minNextTime
        var skipIntervalMin = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Ldloc, minNextTimeLocal);
        il.Emit(OpCodes.Bge, skipIntervalMin);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Stloc, minNextTimeLocal);
        il.MarkLabel(skipIntervalMin);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, afterHandleLabel);

        // Remove non-interval timer
        il.MarkLabel(removeTimerLabel);
        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, removeAt);
        // Don't increment i

        // if (timer.HasRef) { timer.HasRef = false; EventLoop.GetInstance().Unref(); }
        var skipFireUnref = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerHasRef);
        il.Emit(OpCodes.Brfalse, skipFireUnref);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.VirtualTimerHasRef);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);
        il.MarkLabel(skipFireUnref);

        il.MarkLabel(afterHandleLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // Return: if minNextTime == long.MaxValue → -1 (no timers)
        //         else → max(0, (int)(minNextTime - currentTime))
        var hasTimers = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, minNextTimeLocal);
        il.Emit(OpCodes.Ldc_I8, long.MaxValue);
        il.Emit(OpCodes.Bne_Un, hasTimers);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasTimers);
        // Re-read current time for accurate delay (callbacks may have taken time)
        il.Emit(OpCodes.Call, runtime.GetCurrentTimeMs);
        il.Emit(OpCodes.Stloc, currentTimeLocal);
        // delay = (int)(minNextTime - currentTime)
        il.Emit(OpCodes.Ldloc, minNextTimeLocal);
        il.Emit(OpCodes.Ldloc, currentTimeLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_I4);
        // return Math.Max(0, delay)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AddVirtualTimer($VirtualTimer timer)
    /// Adds a timer to the queue, hooks ProcessPendingTimers into the event loop on first call,
    /// and wakes the event loop so it recalculates its wait timeout for the new timer.
    /// </summary>
    private void EmitAddVirtualTimer(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        FieldBuilder timerQueueField)
    {
        var method = runtimeType.DefineMethod(
            "AddVirtualTimer",
            MethodAttributes.Public | MethodAttributes.Static,
            null,
            [runtime.VirtualTimerType]
        );
        runtime.AddVirtualTimer = method;

        var il = method.GetILGenerator();
        var listType = _types.MakeGenericType(_types.ListOpen, runtime.VirtualTimerType);

        // Get generic Add method for List<$VirtualTimer> using TypeBuilder.GetMethod
        var listOpenAdd = _types.GetMethod(_types.ListOpen, "Add")!;
        var addMethod = EmitterTypeHelpers.ResolveMethod(listType, listOpenAdd);

        // EnsureTimerInitialized();
        il.Emit(OpCodes.Call, runtime.EnsureTimerInitialized);

        // _timerQueue.Add(timer);
        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, addMethod);

        // Lazily hook ProcessPendingTimers into the event loop:
        // if ($EventLoop._timerProcessor == null)
        //     $EventLoop._timerProcessor = new Func<int>(ProcessPendingTimers);
        var alreadyHooked = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.EventLoopTimerProcessorField);
        il.Emit(OpCodes.Brtrue, alreadyHooked);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, runtime.ProcessPendingTimers);
        il.Emit(OpCodes.Newobj, typeof(Func<int>).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Stsfld, runtime.EventLoopTimerProcessorField);
        il.MarkLabel(alreadyHooked);

        // Wake the event loop so it recalculates its wait timeout for the new timer
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopWake);

        il.Emit(OpCodes.Ret);
    }
}
