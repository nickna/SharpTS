using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Virtual timer infrastructure for compiled assemblies.
/// Implements JavaScript-like single-threaded timer semantics by processing
/// timer callbacks on the main thread during Date.now() calls.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $VirtualTimer class that holds timer callback information.
    /// </summary>
    private void EmitVirtualTimerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$VirtualTimer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            _types.Object
        );
        runtime.VirtualTimerType = typeBuilder;

        // Fields
        var callbackField = typeBuilder.DefineField("Callback", runtime.TSFunctionType, FieldAttributes.Public);
        var argsField = typeBuilder.DefineField("Args", _types.ObjectArray, FieldAttributes.Public);
        var scheduledTimeField = typeBuilder.DefineField("ScheduledTime", _types.Int64, FieldAttributes.Public);
        var isCancelledField = typeBuilder.DefineField("IsCancelled", _types.Boolean, FieldAttributes.Public);
        var isIntervalField = typeBuilder.DefineField("IsInterval", _types.Boolean, FieldAttributes.Public);
        var intervalMsField = typeBuilder.DefineField("IntervalMs", _types.Int32, FieldAttributes.Public);

        runtime.VirtualTimerCallback = callbackField;
        runtime.VirtualTimerArgs = argsField;
        runtime.VirtualTimerScheduledTime = scheduledTimeField;
        runtime.VirtualTimerIsCancelled = isCancelledField;
        runtime.VirtualTimerIsInterval = isIntervalField;
        runtime.VirtualTimerIntervalMs = intervalMsField;

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
        runtime.TimerQueue = timerQueueField;

        // Static field: long _timerStartTicks (for high-resolution timing)
        var startTicksField = runtimeType.DefineField(
            "_timerStartTicks",
            _types.Int64,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.TimerStartTicks = startTicksField;

        // Static field: bool _timerInitialized
        var initializedField = runtimeType.DefineField(
            "_timerInitialized",
            _types.Boolean,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.TimerInitialized = initializedField;

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
        var listOpenCtor = _types.ListOpen.GetConstructor(Type.EmptyTypes)!;
        var listCtor = TypeBuilder.GetConstructor(listType, listOpenCtor);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stsfld, timerQueueField);

        // _timerStartTicks = Stopwatch.GetTimestamp();
        il.Emit(OpCodes.Call, typeof(System.Diagnostics.Stopwatch).GetMethod("GetTimestamp")!);
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
        il.Emit(OpCodes.Call, typeof(System.Diagnostics.Stopwatch).GetMethod("GetTimestamp")!);
        il.Emit(OpCodes.Ldsfld, startTicksField);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I8, 1000L);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldsfld, typeof(System.Diagnostics.Stopwatch).GetField("Frequency")!);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void ProcessPendingTimers()
    /// Checks and executes any timers that are due.
    /// </summary>
    private void EmitProcessPendingTimers(
        TypeBuilder runtimeType,
        EmittedRuntime runtime,
        FieldBuilder timerQueueField)
    {
        var method = runtimeType.DefineMethod(
            "ProcessPendingTimers",
            MethodAttributes.Public | MethodAttributes.Static,
            null,
            Type.EmptyTypes
        );
        runtime.ProcessPendingTimers = method;

        var il = method.GetILGenerator();
        var listType = _types.MakeGenericType(_types.ListOpen, runtime.VirtualTimerType);

        // Get generic methods for List<$VirtualTimer> using TypeBuilder.GetMethod
        var listOpenCountGetter = _types.ListOpen.GetProperty("Count")!.GetGetMethod()!;
        var countGetter = TypeBuilder.GetMethod(listType, listOpenCountGetter);
        var listOpenGetItem = _types.ListOpen.GetMethod("get_Item")!;
        var getItem = TypeBuilder.GetMethod(listType, listOpenGetItem);
        var listOpenRemoveAt = _types.ListOpen.GetMethod("RemoveAt")!;
        var removeAt = TypeBuilder.GetMethod(listType, listOpenRemoveAt);

        // EnsureTimerInitialized();
        il.Emit(OpCodes.Call, runtime.EnsureTimerInitialized);

        // long currentTime = GetCurrentTimeMs();
        var currentTimeLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Call, runtime.GetCurrentTimeMs);
        il.Emit(OpCodes.Stloc, currentTimeLocal);

        // Process timers in a loop (need to handle removals and intervals)
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

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

        // if (timer.IsCancelled) { _timerQueue.RemoveAt(i); continue; }
        var notCancelledLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerIsCancelled);
        il.Emit(OpCodes.Brfalse_S, notCancelledLabel);

        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, removeAt);
        il.Emit(OpCodes.Br, loopStartLabel); // Don't increment i, continue from same index

        il.MarkLabel(notCancelledLabel);

        // if (timer.ScheduledTime > currentTime) { i++; continue; }
        var isDueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerScheduledTime);
        il.Emit(OpCodes.Ldloc, currentTimeLocal);
        il.Emit(OpCodes.Ble, isDueLabel);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(isDueLabel);

        // Timer is due - execute callback
        // try { timer.Callback.Invoke(timer.Args); } catch { }
        var tryStart = il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerCallback);
        il.Emit(OpCodes.Ldloc, timerLocal);
        il.Emit(OpCodes.Ldfld, runtime.VirtualTimerArgs);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
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

        il.MarkLabel(afterHandleLabel);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void AddVirtualTimer($VirtualTimer timer)
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
        var listOpenAdd = _types.ListOpen.GetMethod("Add")!;
        var addMethod = TypeBuilder.GetMethod(listType, listOpenAdd);

        // EnsureTimerInitialized();
        il.Emit(OpCodes.Call, runtime.EnsureTimerInitialized);

        // _timerQueue.Add(timer);
        il.Emit(OpCodes.Ldsfld, timerQueueField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, addMethod);

        il.Emit(OpCodes.Ret);
    }
}
