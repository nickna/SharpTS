using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace SharpTS.Compilation;

/// <summary>
/// Emits $StatWatcher type extending $EventEmitter for fs.watchFile() support.
/// Uses Timer for polling + EventLoop for async event dispatch.
/// Pure IL — no reflection to SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    // $StatWatcher type and members
    private TypeBuilder _statWatcherType = null!;
    private ConstructorBuilder _statWatcherCtor = null!;
    private FieldBuilder _statWatcherTimerField = null!;
    private FieldBuilder _statWatcherClosedField = null!;
    private FieldBuilder _statWatcherFilenameField = null!;
    private FieldBuilder _statWatcherLastSizeField = null!;
    private FieldBuilder _statWatcherLastModifiedField = null!;
    private MethodBuilder _statWatcherCloseMethod = null!;
    private MethodBuilder _statWatcherPollCallback = null!;

    // $StatWatchPollClosure
    private TypeBuilder _statWatchPollClosureType = null!;
    private ConstructorBuilder _statWatchPollClosureCtor = null!;
    private FieldBuilder _statPollClosureWatcherField = null!;
    private FieldBuilder _statPollClosureCurrField = null!;
    private FieldBuilder _statPollClosurePrevField = null!;
    private MethodBuilder _statPollClosureRun = null!;

    // Static watcher registry for unwatchFile
    private FieldBuilder _statWatcherRegistryField = null!;

    private void EmitStatWatcherClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitStatWatchPollClosure(moduleBuilder, runtime);

        _statWatcherType = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$StatWatcher",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType);

        _statWatcherTimerField = _statWatcherType.DefineField("_timer", typeof(Timer), FieldAttributes.Private);
        _statWatcherClosedField = _statWatcherType.DefineField("_closed", _types.Boolean, FieldAttributes.Private);
        _statWatcherFilenameField = _statWatcherType.DefineField("_filename", _types.String, FieldAttributes.Private);
        _statWatcherLastSizeField = _statWatcherType.DefineField("_lastSize", typeof(long), FieldAttributes.Private);
        _statWatcherLastModifiedField = _statWatcherType.DefineField("_lastModified", typeof(long), FieldAttributes.Private);

        EmitStatWatcherPollCallback(runtime);
        EmitStatWatcherConstructor(runtime);
        EmitStatWatcherCloseMethod(runtime);

        _ = _statWatcherType;
        _ = _statWatcherCtor;
        _ = _statWatcherCloseMethod;

        _statWatcherType.CreateType();
    }

    private void EmitStatWatchPollClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _statWatchPollClosureType = moduleBuilder.DefineType(
            "$StatWatchPollClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _statPollClosureWatcherField = _statWatchPollClosureType.DefineField("_watcher", runtime.TSEventEmitterType, FieldAttributes.Public);
        _statPollClosureCurrField = _statWatchPollClosureType.DefineField("_curr", _types.Object, FieldAttributes.Public);
        _statPollClosurePrevField = _statWatchPollClosureType.DefineField("_prev", _types.Object, FieldAttributes.Public);

        _statWatchPollClosureCtor = _statWatchPollClosureType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [runtime.TSEventEmitterType, _types.Object, _types.Object]);
        {
            var il = _statWatchPollClosureCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, _statPollClosureWatcherField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, _statPollClosureCurrField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stfld, _statPollClosurePrevField);
            il.Emit(OpCodes.Ret);
        }

        // Run(): Emit("change", [curr, prev])
        _statPollClosureRun = _statWatchPollClosureType.DefineMethod(
            "Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        {
            var il = _statPollClosureRun.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _statPollClosureWatcherField);
            il.Emit(OpCodes.Ldstr, "change");
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _statPollClosureCurrField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _statPollClosurePrevField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        _statWatchPollClosureType.CreateType();
    }

    /// <summary>
    /// PollCallback(object? state): reads file info, compares, schedules event if changed.
    /// </summary>
    private void EmitStatWatcherPollCallback(EmittedRuntime runtime)
    {
        _statWatcherPollCallback = _statWatcherType.DefineMethod(
            "PollCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]); // TimerCallback signature: void(object? state)

        var il = _statWatcherPollCallback.GetILGenerator();

        // if (_closed) return
        var notClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, _statWatcherClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notClosedLabel);

        // try { FileInfo fi = new FileInfo(_filename); }
        il.BeginExceptionBlock();

        var fiLocal = il.DeclareLocal(typeof(FileInfo));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statWatcherFilenameField);
        il.Emit(OpCodes.Newobj, typeof(FileInfo).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, fiLocal);

        // long currentSize = fi.Length
        var currentSizeLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Ldloc, fiLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentSizeLocal);

        // long currentModified = fi.LastWriteTimeUtc.Ticks
        var currentModifiedLocal = il.DeclareLocal(typeof(long));
        il.Emit(OpCodes.Ldloc, fiLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("LastWriteTimeUtc")!.GetGetMethod()!);
        var dtLocal = il.DeclareLocal(typeof(DateTime));
        il.Emit(OpCodes.Stloc, dtLocal);
        il.Emit(OpCodes.Ldloca, dtLocal);
        il.Emit(OpCodes.Call, typeof(DateTime).GetProperty("Ticks")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentModifiedLocal);

        // if (currentSize == _lastSize && currentModified == _lastModified) return (no change)
        var changedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, currentSizeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statWatcherLastSizeField);
        il.Emit(OpCodes.Bne_Un, changedLabel);
        il.Emit(OpCodes.Ldloc, currentModifiedLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statWatcherLastModifiedField);
        il.Emit(OpCodes.Bne_Un, changedLabel);

        // No change — just leave
        var leaveLabel = il.DefineLabel();
        il.Emit(OpCodes.Leave, leaveLabel);

        il.MarkLabel(changedLabel);

        // Build prev stats: new $Stats(true, false, false, _lastSize, 0, 0, _lastModified/10000 - epoch, 0, 0)
        // Simplified: just pass size as the key differentiator
        var prevStatsLocal = il.DeclareLocal(_types.Object);
        EmitCreateStats(il, runtime, isFile: true,
            sizeEmitter: () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _statWatcherLastSizeField); il.Emit(OpCodes.Conv_R8); },
            mtimeMsEmitter: () => { EmitTicksToEpochMs(il, () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, _statWatcherLastModifiedField); }); });
        il.Emit(OpCodes.Stloc, prevStatsLocal);

        // Update stored values
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, currentSizeLocal);
        il.Emit(OpCodes.Stfld, _statWatcherLastSizeField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, currentModifiedLocal);
        il.Emit(OpCodes.Stfld, _statWatcherLastModifiedField);

        // Build curr stats
        var currStatsLocal = il.DeclareLocal(_types.Object);
        EmitCreateStats(il, runtime, isFile: true,
            sizeEmitter: () => { il.Emit(OpCodes.Ldloc, currentSizeLocal); il.Emit(OpCodes.Conv_R8); },
            mtimeMsEmitter: () => { EmitTicksToEpochMs(il, () => il.Emit(OpCodes.Ldloc, currentModifiedLocal)); });
        il.Emit(OpCodes.Stloc, currStatsLocal);

        // Schedule: EventLoop.GetInstance().Schedule(new Action(new $StatWatchPollClosure(this, curr, prev).Run))
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldarg_0); // this ($StatWatcher, which IS $EventEmitter)
        il.Emit(OpCodes.Ldloc, currStatsLocal);
        il.Emit(OpCodes.Ldloc, prevStatsLocal);
        il.Emit(OpCodes.Newobj, _statWatchPollClosureCtor);
        il.Emit(OpCodes.Ldftn, _statPollClosureRun);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        il.Emit(OpCodes.Leave, leaveLabel);

        // catch (Exception) — ignore errors during polling
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop); // discard exception
        il.Emit(OpCodes.Leave, leaveLabel);
        il.EndExceptionBlock();

        il.MarkLabel(leaveLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits new $Stats(isFile, isDir, isSymlink, size, mode, atimeMs, mtimeMs, ctimeMs, birthtimeMs)
    /// Leaves stats object on the stack.
    /// </summary>
    private void EmitCreateStats(ILGenerator il, EmittedRuntime runtime, bool isFile,
        Action sizeEmitter, Action mtimeMsEmitter)
    {
        il.Emit(isFile ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); // isFile
        il.Emit(isFile ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1); // isDir
        il.Emit(OpCodes.Ldc_I4_0); // isSymlink
        sizeEmitter(); // size (double)
        il.Emit(OpCodes.Ldc_R8, 0.0); // mode
        il.Emit(OpCodes.Ldc_R8, 0.0); // atimeMs
        mtimeMsEmitter(); // mtimeMs (double)
        il.Emit(OpCodes.Ldc_R8, 0.0); // ctimeMs
        il.Emit(OpCodes.Ldc_R8, 0.0); // birthtimeMs
        il.Emit(OpCodes.Newobj, runtime.StatsCtor);
    }

    /// <summary>
    /// Helper: converts ticks (long on stack from ticksEmitter) to epoch milliseconds (double on stack).
    /// </summary>
    private static void EmitTicksToEpochMs(ILGenerator il, Action ticksEmitter)
    {
        // (ticks - 621355968000000000L) / 10000.0
        ticksEmitter();
        il.Emit(OpCodes.Ldc_I8, 621355968000000000L); // Unix epoch in .NET ticks
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldc_R8, 10000.0);
        il.Emit(OpCodes.Div);
    }

    /// <summary>
    /// Constructor(path, intervalMs): captures initial stats, creates timer, Ref().
    /// </summary>
    private void EmitStatWatcherConstructor(EmittedRuntime runtime)
    {
        _statWatcherCtor = _statWatcherType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [_types.String, _types.Int32]);

        var il = _statWatcherCtor.GetILGenerator();

        // Call base $EventEmitter ctor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _closed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Stfld, _statWatcherClosedField);

        // _filename = Path.GetFullPath(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetFullPath", [typeof(string)])!);
        il.Emit(OpCodes.Stfld, _statWatcherFilenameField);

        // Capture initial stats
        il.BeginExceptionBlock();
        var fiLocal = il.DeclareLocal(typeof(FileInfo));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statWatcherFilenameField);
        il.Emit(OpCodes.Newobj, typeof(FileInfo).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Stloc, fiLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fiLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, _statWatcherLastSizeField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, fiLocal);
        il.Emit(OpCodes.Callvirt, typeof(FileInfo).GetProperty("LastWriteTimeUtc")!.GetGetMethod()!);
        var dtLocal = il.DeclareLocal(typeof(DateTime));
        il.Emit(OpCodes.Stloc, dtLocal);
        il.Emit(OpCodes.Ldloca, dtLocal);
        il.Emit(OpCodes.Call, typeof(DateTime).GetProperty("Ticks")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, _statWatcherLastModifiedField);

        var afterInitLabel = il.DefineLabel();
        il.Emit(OpCodes.Leave, afterInitLabel);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, afterInitLabel);
        il.EndExceptionBlock();
        il.MarkLabel(afterInitLabel);

        // _timer = new Timer(new TimerCallback(this.PollCallback), null, intervalMs, intervalMs)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldftn, _statWatcherPollCallback);
        il.Emit(OpCodes.Newobj, typeof(TimerCallback).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldnull); // state
        il.Emit(OpCodes.Ldarg_2); // dueTime = intervalMs
        il.Emit(OpCodes.Ldarg_2); // period = intervalMs
        il.Emit(OpCodes.Newobj, typeof(Timer).GetConstructor([typeof(TimerCallback), typeof(object), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Stfld, _statWatcherTimerField);

        // EventLoop.GetInstance().Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Close(): disposes timer, Unref().
    /// </summary>
    private void EmitStatWatcherCloseMethod(EmittedRuntime runtime)
    {
        _statWatcherCloseMethod = _statWatcherType.DefineMethod(
            "Close", MethodAttributes.Public, _types.Void, Type.EmptyTypes);

        var il = _statWatcherCloseMethod.GetILGenerator();

        var notClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, _statWatcherClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notClosedLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Stfld, _statWatcherClosedField);

        // _timer.Dispose()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _statWatcherTimerField);
        il.Emit(OpCodes.Callvirt, typeof(Timer).GetMethod("Dispose", Type.EmptyTypes)!);

        // EventLoop.GetInstance().Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        il.Emit(OpCodes.Ret);
    }
}
