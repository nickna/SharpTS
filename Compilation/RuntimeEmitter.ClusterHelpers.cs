using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits cluster module types ($ClusterContext, $ClusterWorker, $ClusterManager) as pure IL.
/// No reflection to SharpTS.dll — fully standalone emitted types following the $HttpServer pattern.
///
/// Compiled-mode limitations:
/// - isPrimary/isWorker/isMaster, setupPrimary, settings, workers dict, event methods all work
/// - fork() throws InvalidOperationException (same as Worker) because compiled modules use shared
///   static fields that can't be safely re-initialized per-thread (async/[ThreadStatic] conflict)
/// </summary>
public partial class RuntimeEmitter
{
    // Field builders for $ClusterWorker (needed across emit methods)
    private FieldBuilder _cwIdField = null!;
    private FieldBuilder _cwThreadField = null!;
    private FieldBuilder _cwP2wQueueField = null!;
    private FieldBuilder _cwW2pQueueField = null!;
    private FieldBuilder _cwCtsField = null!;
    private FieldBuilder _cwIsRunningField = null!;
    private FieldBuilder _cwIsDeadField = null!;
    private FieldBuilder _cwIsConnectedField = null!;
    private FieldBuilder _cwExitedAfterDisconnectField = null!;
    private FieldBuilder _cwNextIdField = null!;

    // Field builders for $ClusterManager
    private FieldBuilder _cmWorkersField = null!;
    private FieldBuilder _cmSettingsField = null!;

    // Resolved generic types
    private Type _blockingCollectionOfObject = null!;
    private Type _concurrentDictOfDoubleObject = null!;
    private ConstructorInfo _blockingCollectionCtor = null!;
    private ConstructorInfo _concurrentDictCtor = null!;

    /// <summary>
    /// Emits all cluster types into the compiled assembly module.
    /// Called from RuntimeEmitter.cs after EventEmitter emission.
    /// </summary>
    internal void EmitClusterTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Resolve generic types once
        _blockingCollectionOfObject = typeof(BlockingCollection<>).MakeGenericType(typeof(object));
        _blockingCollectionCtor = _blockingCollectionOfObject.GetConstructor(Type.EmptyTypes)!;
        _concurrentDictOfDoubleObject = typeof(ConcurrentDictionary<,>).MakeGenericType(typeof(double), typeof(object));
        _concurrentDictCtor = _concurrentDictOfDoubleObject.GetConstructor(Type.EmptyTypes)!;

        EmitClusterContextType(moduleBuilder, runtime);
        EmitClusterWorkerType(moduleBuilder, runtime);
        EmitClusterManagerType(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits $Runtime.ClusterIsPrimary() and ClusterIsWorker() convenience methods.
    /// Called from RuntimeEmitter.RuntimeClass.cs.
    /// </summary>
    internal void EmitClusterHelpers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // ClusterIsPrimary: return !$ClusterContext.IsWorker
        var isPrimary = runtimeType.DefineMethod("ClusterIsPrimary",
            MethodAttributes.Public | MethodAttributes.Static, _types.Boolean, Type.EmptyTypes);
        var il = isPrimary.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, runtime.ClusterContextIsWorkerField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
        runtime.ClusterIsPrimary = isPrimary;

        // ClusterIsWorker: return $ClusterContext.IsWorker
        var isWorker = runtimeType.DefineMethod("ClusterIsWorker",
            MethodAttributes.Public | MethodAttributes.Static, _types.Boolean, Type.EmptyTypes);
        il = isWorker.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, runtime.ClusterContextIsWorkerField);
        il.Emit(OpCodes.Ret);
        runtime.ClusterIsWorker = isWorker;
    }

    #region $ClusterContext

    /// <summary>
    /// Emits: public static class $ClusterContext with [ThreadStatic] fields.
    /// </summary>
    private void EmitClusterContextType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType("$ClusterContext",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        _ = typeBuilder;

        var tsAttr = new CustomAttributeBuilder(
            typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!, []);

        // [ThreadStatic] public static bool IsWorker
        var isWorker = typeBuilder.DefineField("IsWorker", _types.Boolean, FieldAttributes.Public | FieldAttributes.Static);
        isWorker.SetCustomAttribute(tsAttr);
        runtime.ClusterContextIsWorkerField = isWorker;

        // [ThreadStatic] public static double WorkerId
        var workerId = typeBuilder.DefineField("WorkerId", _types.Double, FieldAttributes.Public | FieldAttributes.Static);
        workerId.SetCustomAttribute(tsAttr);
        _ = workerId;

        // [ThreadStatic] public static object CurrentWorker
        var currentWorker = typeBuilder.DefineField("CurrentWorker", _types.Object, FieldAttributes.Public | FieldAttributes.Static);
        currentWorker.SetCustomAttribute(tsAttr);
        runtime.ClusterContextCurrentWorkerField = currentWorker;

        // [ThreadStatic] public static BlockingCollection<object> PrimaryToWorkerQueue
        var p2wQueue = typeBuilder.DefineField("PrimaryToWorkerQueue", _blockingCollectionOfObject, FieldAttributes.Public | FieldAttributes.Static);
        p2wQueue.SetCustomAttribute(tsAttr);
        _ = p2wQueue;

        // [ThreadStatic] public static BlockingCollection<object> WorkerToPrimaryQueue
        var w2pQueue = typeBuilder.DefineField("WorkerToPrimaryQueue", _blockingCollectionOfObject, FieldAttributes.Public | FieldAttributes.Static);
        w2pQueue.SetCustomAttribute(tsAttr);
        _ = w2pQueue;

        typeBuilder.CreateType();
    }

    #endregion

    #region $ClusterWorker

    /// <summary>
    /// Emits: public class $ClusterWorker : $EventEmitter
    /// Worker type with fields for thread, IPC queues, and lifecycle state.
    /// In compiled standalone mode, workers are not spawned (fork throws),
    /// but the type exists for type compatibility and property access.
    /// </summary>
    private void EmitClusterWorkerType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType("$ClusterWorker",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType);
        runtime.ClusterWorkerType = typeBuilder;

        // Static: next worker ID counter
        _cwNextIdField = typeBuilder.DefineField("_nextId", _types.Int32,
            FieldAttributes.Private | FieldAttributes.Static);

        // Instance fields
        _cwIdField = typeBuilder.DefineField("_id", _types.Double, FieldAttributes.Private);
        _cwThreadField = typeBuilder.DefineField("_thread", typeof(Thread), FieldAttributes.Private);
        _cwP2wQueueField = typeBuilder.DefineField("_p2wQueue", _blockingCollectionOfObject, FieldAttributes.Private);
        _cwW2pQueueField = typeBuilder.DefineField("_w2pQueue", _blockingCollectionOfObject, FieldAttributes.Private);
        _cwCtsField = typeBuilder.DefineField("_cts", typeof(CancellationTokenSource), FieldAttributes.Private);
        _cwIsRunningField = typeBuilder.DefineField("_isRunning", _types.Boolean, FieldAttributes.Private);
        _cwIsDeadField = typeBuilder.DefineField("_isDead", _types.Boolean, FieldAttributes.Private);
        _cwIsConnectedField = typeBuilder.DefineField("_isConnected", _types.Boolean, FieldAttributes.Private);
        _cwExitedAfterDisconnectField = typeBuilder.DefineField("_exitedAfterDisconnect", _types.Boolean, FieldAttributes.Private);

        // Constructor: just initializes fields, does NOT start a thread
        EmitClusterWorkerCtor(typeBuilder, runtime);

        // Instance methods
        EmitClusterWorkerSend(typeBuilder, runtime);
        EmitClusterWorkerDisconnect(typeBuilder, runtime);
        EmitClusterWorkerKill(typeBuilder, runtime);
        EmitClusterWorkerDeliverMessages(typeBuilder, runtime);
        EmitClusterWorkerGetMember(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Constructor: $ClusterWorker()
    /// Initializes ID, IPC queues, CTS, and state flags.
    /// </summary>
    private void EmitClusterWorkerCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public,
            CallingConventions.Standard, Type.EmptyTypes);
        _ = ctor;
        var il = ctor.GetILGenerator();

        // base() — call $EventEmitter ctor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _id = (double)Interlocked.Increment(ref _nextId)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsflda, _cwNextIdField);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Increment", [typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stfld, _cwIdField);

        // _p2wQueue = new BlockingCollection<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _blockingCollectionCtor);
        il.Emit(OpCodes.Stfld, _cwP2wQueueField);

        // _w2pQueue = new BlockingCollection<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _blockingCollectionCtor);
        il.Emit(OpCodes.Stfld, _cwW2pQueueField);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _cwCtsField);

        // _isConnected = true, _isRunning = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _cwIsConnectedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _cwIsRunningField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void Send(object message) — enqueue message to primary-to-worker queue.
    /// </summary>
    private void EmitClusterWorkerSend(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Send",
            MethodAttributes.Public, typeof(void), [_types.Object]);
        _ = method;
        var il = method.GetILGenerator();

        var retLabel = il.DefineLabel();
        var proceedLabel = il.DefineLabel();

        // if (_isDead) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsDeadField);
        il.Emit(OpCodes.Brtrue, retLabel);

        // if (!_isConnected) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsConnectedField);
        il.Emit(OpCodes.Brtrue, proceedLabel);
        il.Emit(OpCodes.Br, retLabel);

        il.MarkLabel(proceedLabel);

        // _p2wQueue.Add(message)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwP2wQueueField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _blockingCollectionOfObject.GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void Disconnect() — graceful disconnect.
    /// </summary>
    private void EmitClusterWorkerDisconnect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Disconnect",
            MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        runtime.ClusterWorkerDisconnect = method;
        var il = method.GetILGenerator();

        var retLabel = il.DefineLabel();
        var proceedLabel = il.DefineLabel();

        // if (!_isConnected) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsConnectedField);
        il.Emit(OpCodes.Brfalse, retLabel);

        // if (_isDead) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsDeadField);
        il.Emit(OpCodes.Brfalse, proceedLabel);
        il.Emit(OpCodes.Br, retLabel);

        il.MarkLabel(proceedLabel);

        // _isConnected = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _cwIsConnectedField);

        // _exitedAfterDisconnect = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _cwExitedAfterDisconnectField);

        // _cts.Cancel()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void Kill() — forced termination.
    /// </summary>
    private void EmitClusterWorkerKill(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Kill",
            MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        _ = method;
        var il = method.GetILGenerator();

        var retLabel = il.DefineLabel();
        var proceedLabel = il.DefineLabel();

        // if (_isDead) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsDeadField);
        il.Emit(OpCodes.Brfalse, proceedLabel);
        il.Emit(OpCodes.Br, retLabel);

        il.MarkLabel(proceedLabel);

        // _isConnected = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _cwIsConnectedField);

        // _cts.Cancel()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void DeliverMessages() — drain _w2pQueue and emit "message" events.
    /// </summary>
    private void EmitClusterWorkerDeliverMessages(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("DeliverMessages",
            MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        _ = method;
        var il = method.GetILGenerator();

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var msgLocal = il.DeclareLocal(_types.Object);

        il.MarkLabel(loopStart);

        // while (_w2pQueue.TryTake(out var msg))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwW2pQueueField);
        il.Emit(OpCodes.Ldloca, msgLocal);
        il.Emit(OpCodes.Callvirt, _blockingCollectionOfObject.GetMethod("TryTake", [typeof(object).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        // this.Emit("message", [msg])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop); // discard bool

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object GetMember(string name) — property access dispatch.
    /// </summary>
    private void EmitClusterWorkerGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetMember",
            MethodAttributes.Public, _types.Object, [_types.String]);
        _ = method;
        var il = method.GetILGenerator();

        var strEquals = _types.String.GetMethod("Equals", [_types.String])!;
        var idLabel = il.DefineLabel();
        var exitedLabel = il.DefineLabel();
        var isDeadLabel = il.DefineLabel();
        var isConnectedLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // "id"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "id");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, idLabel);

        // "exitedAfterDisconnect"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "exitedAfterDisconnect");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, exitedLabel);

        // "isDead"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isDead");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, isDeadLabel);

        // "isConnected"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isConnected");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, isConnectedLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // id → (double)_id
        il.MarkLabel(idLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIdField);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // exitedAfterDisconnect → (bool)
        il.MarkLabel(exitedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwExitedAfterDisconnectField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // isDead → (bool)
        il.MarkLabel(isDeadLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsDeadField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // isConnected → (bool)
        il.MarkLabel(isConnectedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cwIsConnectedField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // default → null
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    #endregion

    #region $ClusterManager

    /// <summary>
    /// Emits: public class $ClusterManager : $EventEmitter (singleton)
    /// </summary>
    private void EmitClusterManagerType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType("$ClusterManager",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType);
        _ = typeBuilder;

        // Static fields
        runtime.ClusterManagerInstanceField = typeBuilder.DefineField("Instance",
            typeBuilder, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly);
        _ = typeBuilder.DefineField("EntryPoint",
            typeof(Action), FieldAttributes.Public | FieldAttributes.Static);

        // Instance fields
        _cmWorkersField = typeBuilder.DefineField("_workers", _concurrentDictOfDoubleObject, FieldAttributes.Private);
        _cmSettingsField = typeBuilder.DefineField("_settings", _types.Object, FieldAttributes.Private);

        // Private constructor
        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Private,
            CallingConventions.Standard, Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Newobj, _concurrentDictCtor);
        ctorIl.Emit(OpCodes.Stfld, _cmWorkersField);
        ctorIl.Emit(OpCodes.Ret);

        // Static constructor: Instance = new $ClusterManager()
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIl = cctor.GetILGenerator();
        cctorIl.Emit(OpCodes.Newobj, ctor);
        cctorIl.Emit(OpCodes.Stsfld, runtime.ClusterManagerInstanceField);
        cctorIl.Emit(OpCodes.Ret);

        // Methods
        EmitClusterManagerFork(typeBuilder, runtime);
        EmitClusterManagerDisconnectAll(typeBuilder, runtime);
        EmitClusterManagerSetupPrimary(typeBuilder, runtime);
        EmitClusterManagerGetWorkersObject(typeBuilder, runtime);
        EmitClusterManagerGetSettings(typeBuilder, runtime);
        EmitClusterManagerRemoveWorker(typeBuilder, runtime);
        EmitClusterManagerEmitWorkerEvent(typeBuilder, runtime);
        EmitClusterManagerGetMember(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// object Fork(object env) — throws in compiled mode.
    /// Compiled modules use shared static fields that can't be safely re-initialized
    /// per-thread (async continuations can resume on different threads, breaking [ThreadStatic]).
    /// Same limitation as Worker constructor.
    /// </summary>
    private void EmitClusterManagerFork(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Fork",
            MethodAttributes.Public, _types.Object, [_types.Object]);
        runtime.ClusterManagerFork = method;
        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldstr, "cluster.fork() is not supported in standalone compiled mode. Use interpreter mode (dotnet run -- script.ts).");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// void DisconnectAll(object callback) — disconnect all workers.
    /// Uses ConcurrentDictionary.ToArray() to snapshot then iterate, avoiding
    /// complex IEnumerator generic dispatch on emitted types.
    /// </summary>
    private void EmitClusterManagerDisconnectAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("DisconnectAll",
            MethodAttributes.Public, typeof(void), [_types.Object]);
        runtime.ClusterManagerDisconnectAll = method;
        var il = method.GetILGenerator();

        var toArray = _concurrentDictOfDoubleObject.GetMethod("ToArray")!;
        var kvpType = typeof(KeyValuePair<,>).MakeGenericType(typeof(double), typeof(object));
        var arrayType = kvpType.MakeArrayType();
        var getValue = kvpType.GetProperty("Value")!.GetGetMethod()!;

        var arrayLocal = il.DeclareLocal(arrayType);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var array = _workers.ToArray()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cmWorkersField);
        il.Emit(OpCodes.Callvirt, toArray);
        il.Emit(OpCodes.Stloc, arrayLocal);

        // for (int i = 0; i < array.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // var worker = ($ClusterWorker)array[i].Value
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelema, kvpType);
        il.Emit(OpCodes.Call, getValue);
        il.Emit(OpCodes.Castclass, runtime.ClusterWorkerType);
        il.Emit(OpCodes.Callvirt, runtime.ClusterWorkerDisconnect);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void SetupPrimary(object settings) — store settings.
    /// </summary>
    private void EmitClusterManagerSetupPrimary(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetupPrimary",
            MethodAttributes.Public, typeof(void), [_types.Object]);
        runtime.ClusterManagerSetupPrimary = method;
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _cmSettingsField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object GetWorkersObject() — returns Dictionary with worker IDs as string keys.
    /// </summary>
    private void EmitClusterManagerGetWorkersObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetWorkersObject",
            MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        runtime.ClusterManagerGetWorkersObject = method;
        var il = method.GetILGenerator();

        var kvpType = typeof(KeyValuePair<,>).MakeGenericType(typeof(double), typeof(object));
        var toArray = _concurrentDictOfDoubleObject.GetMethod("ToArray")!;
        var getKey = kvpType.GetProperty("Key")!.GetGetMethod()!;
        var getValue = kvpType.GetProperty("Value")!.GetGetMethod()!;
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var arrayLocal = il.DeclareLocal(kvpType.MakeArrayType());
        var indexLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.Double);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var dict = new Dictionary<string, object>()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // var array = _workers.ToArray()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cmWorkersField);
        il.Emit(OpCodes.Callvirt, toArray);
        il.Emit(OpCodes.Stloc, arrayLocal);

        // for (int i = 0; i < array.Length; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // dict[array[i].Key.ToString("0")] = array[i].Value
        il.Emit(OpCodes.Ldloc, dictLocal);

        // Key as string
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelema, kvpType);
        il.Emit(OpCodes.Call, getKey);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloca, keyLocal);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", [typeof(string)])!);

        // Value
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelema, kvpType);
        il.Emit(OpCodes.Call, getValue);

        il.Emit(OpCodes.Callvirt, dictSetItem);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object GetSettings() — returns stored settings.
    /// </summary>
    private void EmitClusterManagerGetSettings(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetSettings",
            MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        runtime.ClusterManagerGetSettings = method;
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cmSettingsField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void RemoveWorker(double id) — remove from workers dict.
    /// </summary>
    private void EmitClusterManagerRemoveWorker(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("RemoveWorker",
            MethodAttributes.Public, typeof(void), [_types.Double]);
        _ = method;
        var il = method.GetILGenerator();

        var outLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cmWorkersField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, _concurrentDictOfDoubleObject.GetMethod("TryRemove",
            [typeof(double), typeof(object).MakeByRefType()])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// void EmitWorkerEvent(string eventName, object worker, double exitCode)
    /// </summary>
    private void EmitClusterManagerEmitWorkerEvent(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("EmitWorkerEvent",
            MethodAttributes.Public, typeof(void), [_types.String, _types.Object, _types.Double]);
        _ = method;
        var il = method.GetILGenerator();

        // this.Emit(eventName, [worker, exitCode])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// object GetMember(string name) — property access dispatch.
    /// </summary>
    private void EmitClusterManagerGetMember(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetMember",
            MethodAttributes.Public, _types.Object, [_types.String]);
        _ = method;
        var il = method.GetILGenerator();

        var strEquals = _types.String.GetMethod("Equals", [_types.String])!;
        var isPrimaryLabel = il.DefineLabel();
        var isWorkerLabel = il.DefineLabel();
        var workersLabel = il.DefineLabel();
        var settingsLabel = il.DefineLabel();
        var workerLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // "isPrimary" / "isMaster"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isPrimary");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, isPrimaryLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isMaster");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, isPrimaryLabel);

        // "isWorker"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isWorker");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, isWorkerLabel);

        // "workers"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "workers");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, workersLabel);

        // "settings"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "settings");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, settingsLabel);

        // "worker"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "worker");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brtrue, workerLabel);

        il.Emit(OpCodes.Br, defaultLabel);

        // isPrimary → !$ClusterContext.IsWorker
        il.MarkLabel(isPrimaryLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ClusterContextIsWorkerField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // isWorker → $ClusterContext.IsWorker
        il.MarkLabel(isWorkerLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ClusterContextIsWorkerField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // workers → GetWorkersObject()
        il.MarkLabel(workersLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.ClusterManagerGetWorkersObject);
        il.Emit(OpCodes.Ret);

        // settings → _settings
        il.MarkLabel(settingsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _cmSettingsField);
        il.Emit(OpCodes.Ret);

        // worker → $ClusterContext.CurrentWorker
        il.MarkLabel(workerLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ClusterContextCurrentWorkerField);
        il.Emit(OpCodes.Ret);

        // default → null
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    #endregion
}
