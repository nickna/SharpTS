using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $EventLoop singleton class for compiled-mode event loop support.
/// Keeps the process alive while active handles exist and dispatches callbacks
/// scheduled from I/O threads back to the main thread.
/// </summary>
public partial class RuntimeEmitter
{
    // Field builders for $EventLoop
    private FieldBuilder _eventLoopActiveHandlesField = null!;
    private FieldBuilder _eventLoopQueueField = null!;
    private FieldBuilder _eventLoopWakeField = null!;
    private FieldBuilder _eventLoopTimerProcessorField = null!;

    /// <summary>
    /// Emits the $EventLoop singleton class.
    /// Must be called before $NetServer/$NetSocket/$HttpServer so they can call Ref/Unref/Schedule.
    /// </summary>
    private void EmitTSEventLoopClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$EventLoop",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.EventLoopType = typeBuilder;

        // Static field: private static $EventLoop _instance
        var instanceField = typeBuilder.DefineField(
            "_instance",
            typeBuilder,
            FieldAttributes.Private | FieldAttributes.Static
        );
        runtime.EventLoopInstanceField = instanceField;

        // Instance fields
        _eventLoopActiveHandlesField = typeBuilder.DefineField(
            "_activeHandles", _types.Int32, FieldAttributes.Private);
        _eventLoopQueueField = typeBuilder.DefineField(
            "_queue", typeof(ConcurrentQueue<Action>), FieldAttributes.Private);
        _eventLoopWakeField = typeBuilder.DefineField(
            "_wake", typeof(ManualResetEventSlim), FieldAttributes.Private);

        // Static field: Func<int> _timerProcessor — set by timer infrastructure to ProcessPendingTimers.
        // Returns ms until next timer is due, or -1 if no timers.
        // Decouples emission order ($EventLoop is emitted before $Runtime where timers live).
        _eventLoopTimerProcessorField = typeBuilder.DefineField(
            "_timerProcessor", typeof(Func<int>), FieldAttributes.Public | FieldAttributes.Static);
        runtime.EventLoopTimerProcessorField = _eventLoopTimerProcessorField;

        // Private constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        {
            var il = ctor.GetILGenerator();
            // base()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            // _activeHandles = 0
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, _eventLoopActiveHandlesField);
            // _queue = new ConcurrentQueue<Action>()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Newobj, typeof(ConcurrentQueue<Action>).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Stfld, _eventLoopQueueField);
            // _wake = new ManualResetEventSlim(false)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, typeof(ManualResetEventSlim).GetConstructor([_types.Boolean])!);
            il.Emit(OpCodes.Stfld, _eventLoopWakeField);
            il.Emit(OpCodes.Ret);
        }

        // GetInstance() — lazy singleton
        EmitEventLoopGetInstance(typeBuilder, runtime, instanceField, ctor);

        // Ref()
        EmitEventLoopRef(typeBuilder, runtime);

        // Unref()
        EmitEventLoopUnref(typeBuilder, runtime);

        // Schedule(Action)
        EmitEventLoopSchedule(typeBuilder, runtime);

        // Wake()
        EmitEventLoopWake(typeBuilder, runtime);

        // Run()
        EmitEventLoopRun(typeBuilder, runtime);

        // WaitForTask(Task)
        EmitEventLoopWaitForTask(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitEventLoopGetInstance(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder instanceField, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod(
            "GetInstance",
            MethodAttributes.Public | MethodAttributes.Static,
            typeBuilder,
            Type.EmptyTypes
        );
        runtime.EventLoopGetInstance = method;

        var il = method.GetILGenerator();

        // if (_instance != null) return _instance
        var createLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, instanceField);
        il.Emit(OpCodes.Brfalse, createLabel);
        il.Emit(OpCodes.Ldsfld, instanceField);
        il.Emit(OpCodes.Ret);

        // _instance = new $EventLoop()
        il.MarkLabel(createLabel);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stsfld, instanceField);
        il.Emit(OpCodes.Ldsfld, instanceField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitEventLoopRef(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Ref",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.EventLoopRef = method;

        var il = method.GetILGenerator();

        // Interlocked.Increment(ref _activeHandles)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _eventLoopActiveHandlesField);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Increment", [typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Pop); // discard return value
        il.Emit(OpCodes.Ret);
    }

    private void EmitEventLoopUnref(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Unref",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.EventLoopUnref = method;

        var il = method.GetILGenerator();

        // int val = Interlocked.Decrement(ref _activeHandles)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _eventLoopActiveHandlesField);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Decrement", [typeof(int).MakeByRefType()])!);

        // if (val <= 0) _wake.Set()
        var skipWake = il.DefineLabel();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, skipWake);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopWakeField);
        il.Emit(OpCodes.Callvirt, typeof(ManualResetEventSlim).GetMethod("Set")!);

        il.MarkLabel(skipWake);
        il.Emit(OpCodes.Ret);
    }

    private void EmitEventLoopSchedule(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Schedule",
            MethodAttributes.Public,
            typeof(void),
            [typeof(Action)]
        );
        runtime.EventLoopSchedule = method;

        var il = method.GetILGenerator();

        // _queue.Enqueue(action)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopQueueField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(ConcurrentQueue<Action>).GetMethod("Enqueue", [typeof(Action)])!);

        // _wake.Set()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopWakeField);
        il.Emit(OpCodes.Callvirt, typeof(ManualResetEventSlim).GetMethod("Set")!);

        il.Emit(OpCodes.Ret);
    }

    private void EmitEventLoopWake(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Wake",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.EventLoopWake = method;

        var il = method.GetILGenerator();

        // _wake.Set()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopWakeField);
        il.Emit(OpCodes.Callvirt, typeof(ManualResetEventSlim).GetMethod("Set")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the event loop Run() method with event-driven timer support.
    /// </summary>
    /// <remarks>
    /// <para>The loop processes I/O callbacks from the queue, then calls the timer processor
    /// delegate (if set) which fires any due timers and returns the ms until the next timer
    /// is due (-1 if no timers). The wait timeout is set to this delay, so the loop sleeps
    /// exactly until the next timer fires — no fixed polling interval.</para>
    /// <para>I/O events and new timer additions call Wake() or Schedule() which signal the
    /// ManualResetEventSlim, breaking out of the wait early when work arrives.</para>
    /// </remarks>
    private void EmitEventLoopRun(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.EventLoopRun = method;

        var il = method.GetILGenerator();
        var actionLocal = il.DeclareLocal(typeof(Action));
        var waitMsLocal = il.DeclareLocal(_types.Int32);

        // Define all labels upfront
        var loopTop = il.DefineLabel();
        var exitLoop = il.DefineLabel();
        var drainTop = il.DefineLabel();
        var drainEnd = il.DefineLabel();
        var waitLabel = il.DefineLabel();

        // while (true) {
        il.MarkLabel(loopTop);

        // Cooperative cancellation check — issue #74. Lets the Test262 runner
        // unwind a never-settling promise chain by flipping _cancelRequested.
        // At worst we wait the inner ManualResetEventSlim.Wait(100) period
        // before checking; that's the timeout resolution.
        if (runtime.CheckCancellationMethod != null)
            il.Emit(OpCodes.Call, runtime.CheckCancellationMethod);

        // waitMs = -1 (no timers by default)
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, waitMsLocal);

        // Inner drain loop: while (_queue.TryDequeue(out action)) { action.Invoke(); }
        il.MarkLabel(drainTop);
        // Also check cancellation inside the drain loop so a flood of
        // microtasks doesn't prevent timely cancellation.
        if (runtime.CheckCancellationMethod != null)
            il.Emit(OpCodes.Call, runtime.CheckCancellationMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopQueueField);
        il.Emit(OpCodes.Ldloca, actionLocal);
        il.Emit(OpCodes.Callvirt, typeof(ConcurrentQueue<Action>).GetMethod("TryDequeue", [typeof(Action).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, drainEnd);

        // try { action.Invoke(); } catch (Exception) { /* swallow */ }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, actionLocal);
        il.Emit(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")!);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // discard exception
        il.EndExceptionBlock();

        il.Emit(OpCodes.Br, drainTop);
        il.MarkLabel(drainEnd);

        // Process pending timers and get delay until next timer:
        // if (_timerProcessor != null) waitMs = _timerProcessor.Invoke();
        var skipTimers = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _eventLoopTimerProcessorField);
        il.Emit(OpCodes.Brfalse, skipTimers);
        il.Emit(OpCodes.Ldsfld, _eventLoopTimerProcessorField);
        il.Emit(OpCodes.Callvirt, typeof(Func<int>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Stloc, waitMsLocal);
        il.MarkLabel(skipTimers);

        // if (_activeHandles <= 0 && queue.IsEmpty) break
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopActiveHandlesField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, waitLabel); // still active, go wait

        // Check queue.IsEmpty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopQueueField);
        il.Emit(OpCodes.Callvirt, typeof(ConcurrentQueue<Action>).GetProperty("IsEmpty")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, exitLoop);

        il.MarkLabel(waitLabel);

        // If a timer is due NOW (waitMs == 0), skip wait and loop immediately
        il.Emit(OpCodes.Ldloc, waitMsLocal);
        il.Emit(OpCodes.Brfalse, loopTop);

        // Compute wait timeout:
        //   waitMs > 0  → sleep until next timer fires
        //   waitMs < 0  → no timers, use default 100ms poll for I/O
        var useTimerDelay = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, waitMsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, useTimerDelay);
        // No timers: use 100ms default
        il.Emit(OpCodes.Ldc_I4, 100);
        il.Emit(OpCodes.Stloc, waitMsLocal);
        il.MarkLabel(useTimerDelay);

        // _wake.Wait(waitMs)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopWakeField);
        il.Emit(OpCodes.Ldloc, waitMsLocal);
        il.Emit(OpCodes.Callvirt, typeof(ManualResetEventSlim).GetMethod("Wait", [_types.Int32])!);
        il.Emit(OpCodes.Pop); // discard bool return

        // _wake.Reset()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopWakeField);
        il.Emit(OpCodes.Callvirt, typeof(ManualResetEventSlim).GetMethod("Reset", Type.EmptyTypes)!);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(exitLoop);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>bool WaitForTask(Task task)</c> — the entry point's wait for a
    /// top-level promise/task value. Blocks while the task is pending AND the
    /// event loop has work that could still settle it (a scheduled timer, an
    /// active handle, or a queued callback), firing due timers and checking
    /// cancellation each iteration. Returns <c>true</c> when the task completed
    /// (caller observes the result / rethrows faults) or <c>false</c> when the
    /// process stayed quiescent for a full grace window — the task can never
    /// settle, so the caller skips it. Matches Node, where a forever-pending
    /// promise does not block process exit. Without this escape, Test262-style
    /// never-settling top-level promises (`new Promise(() => {})`) hang the
    /// program until an external watchdog kills it.
    /// </summary>
    private void EmitEventLoopWaitForTask(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "WaitForTask",
            MethodAttributes.Public,
            _types.Boolean,
            [typeof(Task)]
        );
        runtime.EventLoopWaitForTask = method;

        // Continuous quiescent wall-clock time before concluding the task can
        // never settle. Time-based, not iteration-based: a loaded thread pool
        // can delay a mid-flight continuation tens of ms with nothing visible
        // to the busy check, and Sleep(1) granularity differs by platform
        // (~15ms Windows, ~1ms Linux), so an iteration count was flaky on CI.
        const long QuiescentMsBeforeGiveUp = 250;

        var il = method.GetILGenerator();
        // Tick (Environment.TickCount64) when the loop last became quiescent;
        // -1 while busy.
        var quiescentStartLocal = il.DeclareLocal(typeof(long));
        var waitMsLocal = il.DeclareLocal(_types.Int32);

        var loopTop = il.DefineLabel();
        var notDone = il.DefineLabel();
        var skipTimers = il.DefineLabel();
        var busyLabel = il.DefineLabel();
        var sleepLabel = il.DefineLabel();

        var tickCount64Getter = typeof(Environment).GetProperty("TickCount64")!.GetGetMethod()!;

        // quiescentStart = -1
        il.Emit(OpCodes.Ldc_I8, -1L);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);

        il.MarkLabel(loopTop);

        // if (task.IsCompleted) return true
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Task).GetProperty("IsCompleted")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notDone);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDone);

        // Cooperative cancellation (issue #74) — without this, a pending task
        // makes the wait unkillable and the Test262 runner's cancel flag is
        // ignored until process teardown.
        if (runtime.CheckCancellationMethod != null)
            il.Emit(OpCodes.Call, runtime.CheckCancellationMethod);

        // waitMs = -1; if (_timerProcessor != null) waitMs = _timerProcessor.Invoke();
        // Invoke fires due timers (their callbacks may settle the task) and
        // returns ms until the next timer, or -1 when none are scheduled.
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, waitMsLocal);
        il.Emit(OpCodes.Ldsfld, _eventLoopTimerProcessorField);
        il.Emit(OpCodes.Brfalse, skipTimers);
        il.Emit(OpCodes.Ldsfld, _eventLoopTimerProcessorField);
        il.Emit(OpCodes.Callvirt, typeof(Func<int>).GetMethod("Invoke")!);
        il.Emit(OpCodes.Stloc, waitMsLocal);
        il.MarkLabel(skipTimers);

        // busy = waitMs >= 0 || _activeHandles > 0 || !_queue.IsEmpty
        il.Emit(OpCodes.Ldloc, waitMsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, busyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopActiveHandlesField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, busyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _eventLoopQueueField);
        il.Emit(OpCodes.Callvirt, typeof(ConcurrentQueue<Action>).GetProperty("IsEmpty")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, busyLabel);

        // idle:
        //   if (quiescentStart < 0) quiescentStart = TickCount64;
        //   else if (TickCount64 - quiescentStart >= grace) return false
        var startStreak = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, quiescentStartLocal);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Blt, startStreak);
        il.Emit(OpCodes.Call, tickCount64Getter);
        il.Emit(OpCodes.Ldloc, quiescentStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I8, QuiescentMsBeforeGiveUp);
        il.Emit(OpCodes.Blt, sleepLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(startStreak);
        il.Emit(OpCodes.Call, tickCount64Getter);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);
        il.Emit(OpCodes.Br, sleepLabel);

        // busy: reset the idle streak
        il.MarkLabel(busyLabel);
        il.Emit(OpCodes.Ldc_I8, -1L);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);

        il.MarkLabel(sleepLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(Thread).GetMethod("Sleep", [_types.Int32])!);
        il.Emit(OpCodes.Br, loopTop);
    }
}
