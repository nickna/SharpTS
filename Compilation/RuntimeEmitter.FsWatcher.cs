using System.IO;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits $FsWatcher type extending $EventEmitter for fs.watch() support.
/// Uses FileSystemWatcher + EventLoop for async event dispatch.
/// Pure IL — no reflection to SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    // $FsWatcher type and members
    private TypeBuilder _fsWatcherType = null!;
    private ConstructorBuilder _fsWatcherCtor = null!;
    private FieldBuilder _fsWatcherFswField = null!;
    private FieldBuilder _fsWatcherClosedField = null!;
    private MethodBuilder _fsWatcherCloseMethod = null!;
    private MethodBuilder _fsWatcherOnFsEvent = null!;

    // $FsWatchChangeClosure
    private TypeBuilder _fsWatchClosureType = null!;
    private ConstructorBuilder _fsWatchClosureCtor = null!;
    private FieldBuilder _fsWatchClosureWatcherField = null!;
    private FieldBuilder _fsWatchClosureEventTypeField = null!;
    private FieldBuilder _fsWatchClosureFilenameField = null!;
    private MethodBuilder _fsWatchClosureRun = null!;

    private void EmitFsWatcherClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // 1. Emit closure type first (needed by the event handler)
        EmitFsWatchChangeClosure(moduleBuilder, runtime);

        // 2. Define $FsWatcher : $EventEmitter
        _fsWatcherType = moduleBuilder.DefineType(
            "$FsWatcher",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType);

        _fsWatcherFswField = _fsWatcherType.DefineField("_watcher", typeof(FileSystemWatcher), FieldAttributes.Private);
        _fsWatcherClosedField = _fsWatcherType.DefineField("_closed", _types.Boolean, FieldAttributes.Private);

        EmitFsWatcherOnFsEvent(runtime);
        EmitFsWatcherConstructor(runtime);
        EmitFsWatcherClose(runtime);

        _ = _fsWatcherType;
        _ = _fsWatcherCtor;
        _ = _fsWatcherCloseMethod;

        _fsWatcherType.CreateType();
    }

    /// <summary>
    /// Emits the closure type that marshals FileSystemWatcher events to the EventLoop.
    /// </summary>
    private void EmitFsWatchChangeClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _fsWatchClosureType = moduleBuilder.DefineType(
            "$FsWatchChangeClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _fsWatchClosureWatcherField = _fsWatchClosureType.DefineField("_watcher", runtime.TSEventEmitterType, FieldAttributes.Public);
        _fsWatchClosureEventTypeField = _fsWatchClosureType.DefineField("_eventType", _types.String, FieldAttributes.Public);
        _fsWatchClosureFilenameField = _fsWatchClosureType.DefineField("_filename", _types.String, FieldAttributes.Public);

        // Constructor(watcher, eventType, filename)
        _fsWatchClosureCtor = _fsWatchClosureType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard,
            [runtime.TSEventEmitterType, _types.String, _types.String]);
        {
            var il = _fsWatchClosureCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, _fsWatchClosureWatcherField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, _fsWatchClosureEventTypeField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stfld, _fsWatchClosureFilenameField);
            il.Emit(OpCodes.Ret);
        }

        // Run(): void — calls watcher.Emit("change", [eventType, filename])
        _fsWatchClosureRun = _fsWatchClosureType.DefineMethod(
            "Run", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        {
            var il = _fsWatchClosureRun.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _fsWatchClosureWatcherField);
            il.Emit(OpCodes.Ldstr, "change");
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _fsWatchClosureEventTypeField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _fsWatchClosureFilenameField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop); // Emit returns bool
            il.Emit(OpCodes.Ret);
        }

        _fsWatchClosureType.CreateType();
    }

    /// <summary>
    /// Emits OnFsEvent(object sender, FileSystemEventArgs e): if not closed, schedule closure.
    /// </summary>
    private void EmitFsWatcherOnFsEvent(EmittedRuntime runtime)
    {
        _fsWatcherOnFsEvent = _fsWatcherType.DefineMethod(
            "OnFsEvent",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object, typeof(FileSystemEventArgs)]);

        var il = _fsWatcherOnFsEvent.GetILGenerator();

        // if (_closed) return
        var notClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, _fsWatcherClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notClosedLabel);

        // string eventType = "change"
        // string filename = e.Name ?? ""
        var filenameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(FileSystemEventArgs).GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var hasNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasNameLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(hasNameLabel);
        il.Emit(OpCodes.Stloc, filenameLocal);

        // EventLoop.GetInstance().Schedule(new Action(new $FsWatchChangeClosure(this, "change", filename).Run))
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldarg_0); // this (the $FsWatcher, which IS a $EventEmitter)
        il.Emit(OpCodes.Ldstr, "change");
        il.Emit(OpCodes.Ldloc, filenameLocal);
        il.Emit(OpCodes.Newobj, _fsWatchClosureCtor);
        il.Emit(OpCodes.Ldftn, _fsWatchClosureRun);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoopSchedule);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Constructor(string path): creates FileSystemWatcher, hooks events, calls Ref().
    /// </summary>
    private void EmitFsWatcherConstructor(EmittedRuntime runtime)
    {
        _fsWatcherCtor = _fsWatcherType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [_types.String]);

        var il = _fsWatcherCtor.GetILGenerator();

        // Call base $EventEmitter ctor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _closed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Stfld, _fsWatcherClosedField);

        // Determine dir and filter from path
        var dirLocal = il.DeclareLocal(_types.String);
        var filterLocal = il.DeclareLocal(_types.String);

        // if (File.Exists(path)) { dir = Path.GetDirectoryName(path); filter = Path.GetFileName(path); }
        // else { dir = path; filter = "*"; }
        var isDirLabel = il.DefineLabel();
        var afterPathLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Exists", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, isDirLabel);

        // File path
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetDirectoryName", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, dirLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetFileName", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, filterLocal);
        il.Emit(OpCodes.Br, afterPathLabel);

        // Directory path
        il.MarkLabel(isDirLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, dirLocal);
        il.Emit(OpCodes.Ldstr, "*");
        il.Emit(OpCodes.Stloc, filterLocal);

        il.MarkLabel(afterPathLabel);

        // _watcher = new FileSystemWatcher(dir, filter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dirLocal);
        il.Emit(OpCodes.Ldloc, filterLocal);
        il.Emit(OpCodes.Newobj, typeof(FileSystemWatcher).GetConstructor([typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stfld, _fsWatcherFswField);

        // Set NotifyFilter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fsWatcherFswField);
        il.Emit(OpCodes.Ldc_I4, (int)(NotifyFilters.FileName | NotifyFilters.DirectoryName |
                                       NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime));
        il.Emit(OpCodes.Callvirt, typeof(FileSystemWatcher).GetProperty("NotifyFilter")!.GetSetMethod()!);

        // Hook Changed event: _watcher.Changed += new FileSystemEventHandler(this.OnFsEvent)
        EmitHookFswEvent(il, runtime, "Changed");
        EmitHookFswEvent(il, runtime, "Created");
        EmitHookFswEvent(il, runtime, "Deleted");

        // EnableRaisingEvents = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fsWatcherFswField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(FileSystemWatcher).GetProperty("EnableRaisingEvents")!.GetSetMethod()!);

        // EventLoop.GetInstance().Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        il.Emit(OpCodes.Ret);
    }

    private void EmitHookFswEvent(ILGenerator il, EmittedRuntime runtime, string eventName)
    {
        // _watcher.add_{eventName}(new FileSystemEventHandler(this.OnFsEvent))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fsWatcherFswField);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldftn, _fsWatcherOnFsEvent);
        il.Emit(OpCodes.Newobj, typeof(FileSystemEventHandler).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, typeof(FileSystemWatcher).GetEvent(eventName)!.GetAddMethod()!);
    }

    /// <summary>
    /// Close(): disposes watcher, calls Unref().
    /// </summary>
    private void EmitFsWatcherClose(EmittedRuntime runtime)
    {
        _fsWatcherCloseMethod = _fsWatcherType.DefineMethod(
            "Close", MethodAttributes.Public, _types.Void, Type.EmptyTypes);

        var il = _fsWatcherCloseMethod.GetILGenerator();

        // if (_closed) return
        var notClosedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Ldfld, _fsWatcherClosedField);
        il.Emit(OpCodes.Brfalse, notClosedLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notClosedLabel);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Volatile);
        il.Emit(OpCodes.Stfld, _fsWatcherClosedField);

        // _watcher.EnableRaisingEvents = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fsWatcherFswField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(FileSystemWatcher).GetProperty("EnableRaisingEvents")!.GetSetMethod()!);

        // _watcher.Dispose()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _fsWatcherFswField);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // EventLoop.GetInstance().Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits $Runtime factory methods: FsWatch, FsWatchFile, FsUnwatchFile.
    /// </summary>
    private void EmitFsWatchFactories(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // Static registry for watchFile watchers
        _statWatcherRegistryField = runtimeType.DefineField(
            "_statWatchers",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private | FieldAttributes.Static);

        EmitFsWatchFactory(runtimeType, runtime);
        EmitFsWatchFileFactory(runtimeType, runtime);
        EmitFsUnwatchFileFactory(runtimeType, runtime);
    }

    /// <summary>
    /// FsWatch(path, options, callback) → $FsWatcher
    /// Creates watcher, optionally registers callback on "change" event.
    /// </summary>
    private void EmitFsWatchFactory(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "FsWatch",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]); // path, optionsOrCallback, callback
        runtime.FsWatch = method;

        var il = method.GetILGenerator();

        // Create $FsWatcher(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _fsWatcherCtor);
        var watcherLocal = il.DeclareLocal(_fsWatcherType);
        il.Emit(OpCodes.Stloc, watcherLocal);

        // Determine the callback: could be arg1 (if function) or arg2
        // If arg1 is a $TSFunction, use it as callback; else check arg2
        var noCallbackLabel = il.DefineLabel();
        var haveCallbackLabel = il.DefineLabel();
        var callbackLocal = il.DeclareLocal(_types.Object);

        // Check arg2 first (3-arg form: path, options, callback)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveCallbackLabel);
        il.Emit(OpCodes.Pop);

        // Check arg1 (2-arg form: path, callback)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveCallbackLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, noCallbackLabel);

        il.MarkLabel(haveCallbackLabel);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // watcher.On("change", callback)
        il.Emit(OpCodes.Ldloc, watcherLocal);
        il.Emit(OpCodes.Ldstr, "change");
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldloc, watcherLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// FsWatchFile(path, options, callback) → undefined
    /// Creates $StatWatcher, registers callback, stores in registry.
    /// </summary>
    private void EmitFsWatchFileFactory(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "FsWatchFile",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object, _types.Object]); // path, options, callback
        runtime.FsWatchFile = method;

        var il = method.GetILGenerator();

        // Extract interval from options (default 5007)
        var intervalLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4, 5007);
        il.Emit(OpCodes.Stloc, intervalLocal);

        // Check if arg1 is an options bag with "interval". Compiled object literals
        // are Dictionary<string, object?> — NOT $IHasFields (only class instances
        // implement that) — so both shapes must be handled or literal options bags
        // (the normal calling convention) are silently ignored.
        var afterOptionsLabel = il.DefineLabel();
        var haveValueLabel = il.DefineLabel();
        var notHasFieldsLabel = il.DefineLabel();
        var intervalObjLocal = il.DeclareLocal(_types.Object);

        // if (arg1 is $IHasFields hf) intervalObj = hf.GetProperty("interval")
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "interval");
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Stloc, intervalObjLocal);
        il.Emit(OpCodes.Br, haveValueLabel);

        // else if (arg1 is Dictionary<string, object> d) d.TryGetValue("interval", out intervalObj)
        il.MarkLabel(notHasFieldsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, afterOptionsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "interval");
        il.Emit(OpCodes.Ldloca, intervalObjLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")!);
        il.Emit(OpCodes.Pop);

        // if (intervalObj is double) interval = (int)(double)intervalObj
        il.MarkLabel(haveValueLabel);
        il.Emit(OpCodes.Ldloc, intervalObjLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, afterOptionsLabel);
        il.Emit(OpCodes.Ldloc, intervalObjLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, intervalLocal);

        il.MarkLabel(afterOptionsLabel);

        // Create $StatWatcher(path, interval)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, intervalLocal);
        il.Emit(OpCodes.Newobj, _statWatcherCtor);
        var watcherLocal = il.DeclareLocal(_statWatcherType);
        il.Emit(OpCodes.Stloc, watcherLocal);

        // Register callback on "change" event
        // callback is arg2 (3-arg: path, options, callback)
        // or arg1 if it's a TSFunction (2-arg: path, callback)
        var callbackLocal = il.DeclareLocal(_types.Object);
        var haveCallbackLabel = il.DefineLabel();
        var noCallbackLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveCallbackLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveCallbackLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, noCallbackLabel);

        il.MarkLabel(haveCallbackLabel);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, watcherLocal);
        il.Emit(OpCodes.Ldstr, "change");
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCallbackLabel);

        // Store in registry: _statWatchers[path] = watcher
        var registryOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _statWatcherRegistryField);
        il.Emit(OpCodes.Brtrue, registryOkLabel);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stsfld, _statWatcherRegistryField);
        il.MarkLabel(registryOkLabel);

        il.Emit(OpCodes.Ldsfld, _statWatcherRegistryField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetFullPath", [typeof(string)])!);
        il.Emit(OpCodes.Ldloc, watcherLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item")!);

        // Return undefined (null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// FsUnwatchFile(path) → undefined
    /// Finds watcher in registry, closes it, removes from registry.
    /// </summary>
    private void EmitFsUnwatchFileFactory(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "FsUnwatchFile",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.FsUnwatchFile = method;

        var il = method.GetILGenerator();

        // If registry is null, return
        var registryExistsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _statWatcherRegistryField);
        il.Emit(OpCodes.Brtrue, registryExistsLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(registryExistsLabel);

        // string fullPath = Path.GetFullPath(path)
        var fullPathLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Path).GetMethod("GetFullPath", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, fullPathLocal);

        // if (!_statWatchers.TryGetValue(fullPath, out var watcher)) return
        var watcherObjLocal = il.DeclareLocal(typeof(object));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _statWatcherRegistryField);
        il.Emit(OpCodes.Ldloc, fullPathLocal);
        il.Emit(OpCodes.Ldloca, watcherObjLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);

        // watcher.Close() — call via reflection on the emitted type
        il.Emit(OpCodes.Ldloc, watcherObjLocal);
        il.Emit(OpCodes.Castclass, _statWatcherType);
        il.Emit(OpCodes.Callvirt, _statWatcherCloseMethod);

        // Remove from registry
        il.Emit(OpCodes.Ldsfld, _statWatcherRegistryField);
        il.Emit(OpCodes.Ldloc, fullPathLocal);
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("Remove", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }
}
