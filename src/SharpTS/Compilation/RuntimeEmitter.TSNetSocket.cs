using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $NetSocket class extending $EventEmitter for standalone TCP/IPC socket support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSSocket
///
/// Two-phase architecture:
///   Phase 1a (EmitTSNetSocketPhase1): Defines type, fields, constructors (with bodies),
///       and method STUBS (no bodies). Closure types defined between phases reference
///       the MethodBuilders/FieldBuilders from this phase.
///   Phase 2 (EmitTSNetSocketPhase2): Emits all method bodies + calls CreateType().
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Phase 1a: Defines the $NetSocket type, fields, constructors (with bodies),
    /// and method STUBS (no bodies). Must be called BEFORE closure types are defined
    /// and BEFORE EmitRuntimeClass so NetCreateConnection can use the constructor.
    /// </summary>
    private NetSocketConstruction EmitTSNetSocketPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$NetSocket",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.EventEmitter.Type
        );
        runtime.RequireNet().SocketType = typeBuilder;

        // ── Fields ──
        // Assembly (internal) rather than Private so the $TlsSocket subclass and the
        // TLS connect/accept workers (same emitted module) can assign the negotiated
        // TcpClient — TLSSocket extends net.Socket, mirroring interp SharpTSTlsSocket : SharpTSSocket.
        var clientField = typeBuilder.DefineField("_client", typeof(TcpClient), FieldAttributes.Assembly);
        var streamField = typeBuilder.DefineField("_stream", typeof(System.IO.Stream), FieldAttributes.Assembly);
        var connectingField = typeBuilder.DefineField("_connecting", _types.Boolean, FieldAttributes.Assembly);
        var destroyedField = typeBuilder.DefineField("_destroyed", _types.Boolean, FieldAttributes.Assembly);
        var closeEmittedField = typeBuilder.DefineField("_closeEmitted", _types.Boolean, FieldAttributes.Assembly);
        // _ended is Assembly: the read-end closure branches on it for half-close (#1070)
        var endedField = typeBuilder.DefineField("_ended", _types.Boolean, FieldAttributes.Assembly);
        var bytesReadField = typeBuilder.DefineField("_bytesRead", _types.Int32, FieldAttributes.Private);
        var bytesWrittenField = typeBuilder.DefineField("_bytesWritten", _types.Int32, FieldAttributes.Assembly);
        var encodingField = typeBuilder.DefineField("_encoding", _types.String, FieldAttributes.Private);
        var readingStartedField = typeBuilder.DefineField("_readingStarted", _types.Boolean, FieldAttributes.Assembly);
        var readCtsField = typeBuilder.DefineField("_readCts", typeof(CancellationTokenSource), FieldAttributes.Private);
        var isIpcField = typeBuilder.DefineField("_isIpc", _types.Boolean, FieldAttributes.Assembly);
        var readReadyField = typeBuilder.DefineField("_readReady", typeof(System.Threading.ManualResetEventSlim), FieldAttributes.Assembly);
        var pipePathField = typeBuilder.DefineField("_pipePath", _types.String, FieldAttributes.Private);
        var connectHostField = typeBuilder.DefineField("_connectHost", _types.String, FieldAttributes.Private);
        var connectPortField = typeBuilder.DefineField("_connectPort", _types.Int32, FieldAttributes.Private);

        // The queue and worker/shutdown flags are guarded by Monitor; writable
        // length is Interlocked, and pending callbacks/errors run on the event loop.
        // Write backpressure state (#1068). Queue/worker/shutdown/hwm fields are
        // Assembly: the server accept closures apply createServer options to
        // accepted sockets, and $SocketReadEndClosure coordinates the flush-aware
        // close through the worker flags.
        var writeQueueField = typeBuilder.DefineField("_writeQueue", typeof(Queue<object[]>), FieldAttributes.Assembly);
        var writeWorkerRunningField = typeBuilder.DefineField("_writeWorkerRunning", _types.Boolean, FieldAttributes.Assembly);
        var shutdownAfterFlushField = typeBuilder.DefineField("_shutdownAfterFlush", _types.Boolean, FieldAttributes.Assembly);
        var writableLengthField = typeBuilder.DefineField("_writableLength", _types.Int32, FieldAttributes.Private);
        var writableHwmField = typeBuilder.DefineField("_writableHwm", _types.Int32, FieldAttributes.Assembly);
        var needDrainField = typeBuilder.DefineField("_needDrain", _types.Boolean, FieldAttributes.Private);
        var pendingWriteCallbacksField = typeBuilder.DefineField("_pendingWriteCallbacks", typeof(Queue<object>), FieldAttributes.Private);
        var pendingWriteErrorField = typeBuilder.DefineField("_pendingWriteError", _types.String, FieldAttributes.Private);
        var pendingEndCallbackField = typeBuilder.DefineField("_pendingEndCallback", _types.Object, FieldAttributes.Private);

        // Half-close state (#1070) — Assembly: set/read by the accept and read-end closures.
        var allowHalfOpenField = typeBuilder.DefineField("_allowHalfOpen", _types.Boolean, FieldAttributes.Assembly);
        var endReceivedField = typeBuilder.DefineField("_endReceived", _types.Boolean, FieldAttributes.Assembly);
        var finishAfterEndField = typeBuilder.DefineField("_finishAfterEnd", _types.Boolean, FieldAttributes.Assembly);

        // ── Constructors (with bodies) ──

        var socketFields = new NetSocketFields(
            clientField,
            streamField,
            connectingField,
            destroyedField,
            closeEmittedField,
            endedField,
            bytesReadField,
            bytesWrittenField,
            encodingField,
            readingStartedField,
            readCtsField,
            isIpcField,
            pipePathField,
            readReadyField,
            connectHostField,
            connectPortField,
            writeQueueField,
            writeWorkerRunningField,
            shutdownAfterFlushField,
            writableLengthField,
            writableHwmField,
            needDrainField,
            pendingWriteCallbacksField,
            pendingWriteErrorField,
            pendingEndCallbackField,
            allowHalfOpenField,
            endReceivedField,
            finishAfterEndField);
        // Constructor 1: $NetSocket() — unconnected client socket
        EmitNetSocketDefaultCtor(typeBuilder, runtime, socketFields);

        // Constructor 2: $NetSocket(TcpClient) — server-accepted socket
        EmitNetSocketTcpClientCtor(typeBuilder, runtime, socketFields);

        // Constructor 3: $NetSocket(Stream, string) — IPC pipe socket
        EmitNetSocketStreamCtor(typeBuilder, runtime, socketFields);

        // ── Method stubs (no bodies — emitted in Phase 2) ──

        runtime.RequireNet().SocketStartReading = typeBuilder.DefineMethod(
            "StartReading",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );

        runtime.RequireNet().SocketConnect = typeBuilder.DefineMethod(
            "Connect",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        runtime.RequireNet().SocketWrite = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var endMethod = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var destroyMethod = typeBuilder.DefineMethod(
            "Destroy",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var setEncodingMethod = typeBuilder.DefineMethod(
            "SetEncoding",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        runtime.RequireNet().SocketGetMember = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );

        // Write-queue plumbing (#1068) — private helpers; bodies emitted in Phase 2.
        var enqueueWriteMethod = typeBuilder.DefineMethod(
            "_EnqueueWrite",
            MethodAttributes.Private,
            _types.Boolean,
            [_types.ByteArray, _types.Object]
        );
        var writeWorkerMethod = typeBuilder.DefineMethod(
            "_WriteWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );
        var flushTickMethod = typeBuilder.DefineMethod(
            "_FlushTick",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes
        );
        var fireWriteCallbacksMethod = typeBuilder.DefineMethod(
            "_FireWriteCallbacks",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes
        );
        var fireWriteErrorMethod = typeBuilder.DefineMethod(
            "_FireWriteError",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes
        );
        var fireEndCallbackMethod = typeBuilder.DefineMethod(
            "_FireEndCallback",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes
        );
        var shutdownWritableMethod = typeBuilder.DefineMethod(
            "_ShutdownWritable",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes
        );

        // NOTE: CreateType() is deferred to Phase 2
        return new(socketFields, new NetSocketMethods(
                endMethod,
                destroyMethod,
                setEncodingMethod,
                enqueueWriteMethod,
                writeWorkerMethod,
                flushTickMethod,
                fireWriteCallbacksMethod,
                fireWriteErrorMethod,
                fireEndCallbackMethod,
                shutdownWritableMethod));
    }

    /// <summary>
    /// Phase 2: Emits all method bodies and finalizes the $NetSocket type.
    /// Called after closure types have been defined between phases.
    /// </summary>
    private void EmitTSNetSocketPhase2(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods,
        NetSocketClosures socketClosures)
    {
        var typeBuilder = runtime.RequireNet().SocketType;

        // Emit method bodies
        EmitNetSocketStartReadingBody(typeBuilder, runtime, socketFields, socketClosures);
        EmitNetSocketConnectBody(typeBuilder, runtime, socketFields, socketClosures);
        EmitNetSocketWriteBody(typeBuilder, runtime, socketFields, socketMethods);
        EmitNetSocketEnqueueWriteBody(runtime, socketFields, socketMethods);
        EmitNetSocketWriteWorkerBody(runtime, socketFields, socketMethods);
        EmitNetSocketFlushTickBody(runtime, socketFields, socketMethods);
        EmitNetSocketFireWriteCallbacksBody(runtime, socketFields, socketMethods);
        EmitNetSocketFireWriteErrorBody(runtime, socketFields, socketMethods);
        EmitNetSocketFireEndCallbackBody(runtime, socketFields, socketMethods);
        EmitNetSocketShutdownWritableBody(runtime, socketFields, socketMethods);
        EmitNetSocketEndBody(typeBuilder, runtime, socketFields, socketMethods);
        EmitNetSocketDestroyBody(typeBuilder, runtime, socketFields, socketMethods);
        EmitNetSocketSetEncodingBody(typeBuilder, runtime, socketFields, socketMethods);
        EmitNetSocketGetMemberBody(typeBuilder, runtime, socketFields);

        typeBuilder.CreateType();
    }

    // ════════════════════════════════════════════════════════════════
    //  Constructors (bodies emitted in Phase 1a)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shared ctor tail: initializes the write-queue state (#1068).
    /// </summary>
    private void EmitNetSocketWriteStateInit(ILGenerator il, NetSocketFields socketFields)
    {
        // _writeQueue = new Queue<object[]>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Queue<object[]>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, socketFields.WriteQueue);
        // _pendingWriteCallbacks = new Queue<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Queue<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, socketFields.PendingWriteCallbacks);
        // _writableHwm = 16384 (Node's stream default)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Stfld, socketFields.WritableHwm);
    }

    private void EmitNetSocketDefaultCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.RequireNet().SocketCtor = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Ctor);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, socketFields.Encoding);
        EmitNetSocketWriteStateInit(il, socketFields);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNetSocketTcpClientCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TcpClient)]
        );
        runtime.RequireNet().SocketCtorTcpClient = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Ctor);
        // _client = client
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, socketFields.Client);
        // _stream = client.GetStream()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        il.Emit(OpCodes.Stfld, socketFields.Stream);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, socketFields.Encoding);
        EmitNetSocketWriteStateInit(il, socketFields);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNetSocketStreamCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(System.IO.Stream), _types.String]
        );
        runtime.RequireNet().SocketCtorStream = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Ctor);
        // _stream = arg1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, socketFields.Stream);
        // _isIpc = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.IsIpc);
        // _pipePath = arg2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, socketFields.PipePath);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, socketFields.Encoding);
        EmitNetSocketWriteStateInit(il, socketFields);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Method bodies (emitted in Phase 2)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Connect(object optionsOrPort, object hostOrCallback, object callback)
    /// Parses args and initiates async TCP or IPC connection.
    /// </summary>
    private void EmitNetSocketConnectBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketClosures socketClosures)
    {
        var il = runtime.RequireNet().SocketConnect.GetILGenerator();

        // Locals
        var portLocal = il.DeclareLocal(_types.Int32);      // port
        var hostLocal = il.DeclareLocal(_types.String);      // host
        var callbackLocal = il.DeclareLocal(_types.Object);  // callback
        var ipcPathLocal = il.DeclareLocal(_types.String);   // IPC path (null if TCP)

        // Default host = "localhost"
        il.Emit(OpCodes.Ldstr, "localhost");
        il.Emit(OpCodes.Stloc, hostLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, ipcPathLocal);

        // ── Parse first arg ──
        var notStringLabel = il.DefineLabel();
        var notDoubleLabel = il.DefineLabel();
        var parseDone = il.DefineLabel();

        // Check if arg1 is string (IPC path) — BEFORE double check
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);

        // ipcPath = (string)arg1
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, ipcPathLocal);
        // arg2 is callback
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        il.MarkLabel(notStringLabel);

        // Check if arg1 is double (port)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // port = (int)(double)arg1
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);

        // arg2 might be host (string) or callback
        var arg2NotString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, arg2NotString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, hostLocal);
        // arg3 is callback
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        il.MarkLabel(arg2NotString);
        // arg2 is callback
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        // First arg is dict (options object)
        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, parseDone);

        // Check "path" key BEFORE "port" key (IPC takes priority)
        EmitDictTryGetString(il, 1, "path", ipcPathLocal);

        // If ipcPath is still null, try to extract port
        var skipPort = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Brtrue, skipPort);

        // Extract port using TryGetValue
        {
            var valLocal = il.DeclareLocal(_types.Object);
            var noPort = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "port");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noPort);

            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, noPort);

            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, portLocal);

            il.MarkLabel(noPort);
        }

        il.MarkLabel(skipPort);

        // Extract host from dict["host"] if present
        EmitDictTryGetString(il, 1, "host", hostLocal);

        // Extract writableHighWaterMark / highWaterMark (stream.Duplex options that
        // Node's net.Socket honors) into _writableHwm (#1068)
        {
            var hwmValLocal = il.DeclareLocal(_types.Object);
            var applyHwm = il.DefineLabel();
            var tryPlainHwm = il.DefineLabel();
            var noHwm = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "writableHighWaterMark");
            il.Emit(OpCodes.Ldloca, hwmValLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, tryPlainHwm);
            il.Emit(OpCodes.Ldloc, hwmValLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brtrue, applyHwm);

            il.MarkLabel(tryPlainHwm);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "highWaterMark");
            il.Emit(OpCodes.Ldloca, hwmValLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noHwm);
            il.Emit(OpCodes.Ldloc, hwmValLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, noHwm);

            il.MarkLabel(applyHwm);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, hwmValLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stfld, socketFields.WritableHwm);
            il.MarkLabel(noHwm);

            // allowHalfOpen (bool) → _allowHalfOpen (#1070)
            var noAho = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "allowHalfOpen");
            il.Emit(OpCodes.Ldloca, hwmValLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noAho);
            il.Emit(OpCodes.Ldloc, hwmValLocal);
            il.Emit(OpCodes.Isinst, typeof(bool));
            il.Emit(OpCodes.Brfalse, noAho);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, hwmValLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.Emit(OpCodes.Stfld, socketFields.AllowHalfOpen);
            il.MarkLabel(noAho);
        }

        // arg2 is callback
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.MarkLabel(parseDone);

        // Register callback on 'connect' event if provided
        var noCallback = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noCallback);

        // this.On("connect", callback)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "connect");
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.On);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCallback);

        // _connecting = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.Connecting);

        // An in-flight connect is an active handle (Node semantics): keep the event
        // loop alive until the connect resolves, otherwise its 'connect'/'error'
        // continuation can be dropped if the loop's other handles (e.g. the peer
        // server) drain to zero before the thread-pool connect worker schedules its
        // closure. Mirrors SharpTSSocket.Connect's interpreter.Ref()/Unref() pair —
        // the emitted socket previously omitted it, leaving net.connect flaky under
        // load (the connect closure could miss the $EventLoop quiescence window).
        // Released exactly once inside the scheduled OK/ERR closure so the handle
        // outlives delivery. One Ref here balances exactly one closure (TCP and IPC
        // workers each schedule exactly one of OK/ERR).
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);

        // ── Branch: IPC vs TCP ──
        var ipcConnect = il.DefineLabel();
        var connectDone = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Brtrue, ipcConnect);

        // ── TCP path ──
        // _client = new TcpClient()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(TcpClient).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, socketFields.Client);

        // Store host/port for the worker
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stfld, socketFields.ConnectHost);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Stfld, socketFields.ConnectPort);

        // Emit error code helper (used by both TCP and IPC connect workers)
        var getSocketErrorCode = EmitGetSocketErrorCode(typeBuilder);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._ConnectWorker))
        var connectWorker = EmitNetSocketConnectWorker(typeBuilder, runtime, socketFields, socketClosures, getSocketErrorCode);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, connectWorker);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, connectDone);

        // ── IPC path ──
        il.MarkLabel(ipcConnect);

        // _isIpc = true; _pipePath = ipcPath
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.IsIpc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, socketFields.PipePath);

        // Store pipePath in _connectHost for the IPC worker to use
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, socketFields.ConnectHost);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._ConnectIpcWorker))
        var connectIpcWorker = EmitNetSocketConnectIpcWorker(typeBuilder, runtime, socketFields, socketClosures, getSocketErrorCode);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, connectIpcWorker);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(connectDone);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private void _ConnectWorker(object state)
    /// TCP connection on thread pool, uses $SocketConnectOkClosure / $SocketConnectErrClosure.
    /// </summary>
    private MethodBuilder EmitNetSocketConnectWorker(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketClosures socketClosures,
        MethodBuilder getSocketErrorCode)
    {
        var connectWorker = typeBuilder.DefineMethod(
            "_ConnectWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        var wil = connectWorker.GetILGenerator();

        wil.BeginExceptionBlock();

        // _client.Connect(_connectHost, _connectPort)
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.Client);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.ConnectHost);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.ConnectPort);
        wil.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Connect", [_types.String, _types.Int32])!);

        // _stream = _client.GetStream()
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.Client);
        wil.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        wil.Emit(OpCodes.Stfld, socketFields.Stream);

        // _connecting clears in the scheduled OK/ERR closure (loop thread) so
        // pending/readyState don't race the worker (Node semantics).

        // EventLoop.Schedule(new Action(new $SocketConnectOkClosure(this).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, socketClosures.ConnectOk.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ConnectOk.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        var leaveOk = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, leaveOk);

        wil.BeginCatchBlock(_types.Exception);
        // On error: schedule error event via closure (which clears _connecting)
        var exLocal = wil.DeclareLocal(_types.Exception);
        wil.Emit(OpCodes.Stloc, exLocal);

        // EventLoop.Schedule(new Action(new $SocketConnectErrClosure(this, ex.Message, GetSocketErrorCode(ex)).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Call, getSocketErrorCode);
        wil.Emit(OpCodes.Newobj, socketClosures.ConnectErr.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ConnectErr.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        wil.Emit(OpCodes.Leave, leaveOk);

        wil.EndExceptionBlock();

        wil.MarkLabel(leaveOk);
        wil.Emit(OpCodes.Ret);

        return connectWorker;
    }

    /// <summary>
    /// Emits: private void _ConnectIpcWorker(object state)
    /// IPC/named-pipe connection on thread pool.
    /// </summary>
    private MethodBuilder EmitNetSocketConnectIpcWorker(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketClosures socketClosures,
        MethodBuilder getSocketErrorCode)
    {
        var worker = typeBuilder.DefineMethod(
            "_ConnectIpcWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        // Also emit the static ConvertToWindowsPipeName / WindowsPipeExists helpers
        var convertMethod = EmitConvertToWindowsPipeName(typeBuilder);
        var pipeExistsMethod = EmitWindowsPipeExists(typeBuilder);

        var wil = worker.GetILGenerator();

        wil.BeginExceptionBlock();

        // Branch on OS: Windows uses NamedPipeClientStream, Unix uses Socket + UnixDomainSocketEndPoint
        var windowsPath = wil.DefineLabel();
        var connectDone = wil.DefineLabel();

        wil.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod("IsWindows")!);
        wil.Emit(OpCodes.Brtrue, windowsPath);

        // ── Unix path: Socket + UnixDomainSocketEndPoint + NetworkStream ──
        {
            // var unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
            var unixSocketLocal = wil.DeclareLocal(typeof(Socket));
            wil.Emit(OpCodes.Ldc_I4, (int)AddressFamily.Unix);
            wil.Emit(OpCodes.Ldc_I4, (int)SocketType.Stream);
            wil.Emit(OpCodes.Ldc_I4, (int)ProtocolType.Unspecified);
            wil.Emit(OpCodes.Newobj, typeof(Socket).GetConstructor([typeof(AddressFamily), typeof(SocketType), typeof(ProtocolType)])!);
            wil.Emit(OpCodes.Stloc, unixSocketLocal);

            // unixSocket.ConnectAsync(new UnixDomainSocketEndPoint(_connectHost)).GetAwaiter().GetResult()
            // Use async path — synchronous Socket.Connect may hang on macOS for Unix domain sockets
            wil.Emit(OpCodes.Ldloc, unixSocketLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, socketFields.ConnectHost);
            wil.Emit(OpCodes.Newobj, typeof(UnixDomainSocketEndPoint).GetConstructor([_types.String])!);
            wil.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("ConnectAsync", [typeof(EndPoint)])!);
            wil.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("GetAwaiter")!);
            var awaiterLocal = wil.DeclareLocal(typeof(TaskAwaiter));
            wil.Emit(OpCodes.Stloc, awaiterLocal);
            wil.Emit(OpCodes.Ldloca, awaiterLocal);
            wil.Emit(OpCodes.Call, typeof(TaskAwaiter).GetMethod("GetResult")!);

            // _stream = new NetworkStream(unixSocket, ownsSocket: true)
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, unixSocketLocal);
            wil.Emit(OpCodes.Ldc_I4_1); // ownsSocket = true
            wil.Emit(OpCodes.Newobj, typeof(NetworkStream).GetConstructor([typeof(Socket), _types.Boolean])!);
            wil.Emit(OpCodes.Stfld, socketFields.Stream);
        }

        wil.Emit(OpCodes.Br, connectDone);

        // ── Windows path: NamedPipeClientStream ──
        wil.MarkLabel(windowsPath);
        {
            // string pipeName = ConvertToWindowsPipeName(_connectHost)
            var pipeNameLocal = wil.DeclareLocal(_types.String);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, socketFields.ConnectHost);
            wil.Emit(OpCodes.Call, convertMethod);
            wil.Emit(OpCodes.Stloc, pipeNameLocal);

            // if (!WindowsPipeExists(pipeName))
            //     throw new FileNotFoundException("no such named pipe '" + _connectHost + "'");
            // Node raises ENOENT immediately for a missing pipe; the timed Connect below
            // cannot distinguish "missing" from "busy" (it retries CreateFile until the
            // timeout expires), so pre-check existence and keep the 5s budget for the
            // exists-but-busy case only. FileNotFoundException maps to ENOENT in
            // GetSocketErrorCode.
            var pipeExists = wil.DefineLabel();
            wil.Emit(OpCodes.Ldloc, pipeNameLocal);
            wil.Emit(OpCodes.Call, pipeExistsMethod);
            wil.Emit(OpCodes.Brtrue, pipeExists);
            wil.Emit(OpCodes.Ldstr, "no such named pipe '");
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, socketFields.ConnectHost);
            wil.Emit(OpCodes.Ldstr, "'");
            wil.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
            wil.Emit(OpCodes.Newobj, typeof(System.IO.FileNotFoundException).GetConstructor([_types.String])!);
            wil.Emit(OpCodes.Throw);
            wil.MarkLabel(pipeExists);

            // var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous)
            var pipeLocal = wil.DeclareLocal(typeof(NamedPipeClientStream));
            wil.Emit(OpCodes.Ldstr, ".");
            wil.Emit(OpCodes.Ldloc, pipeNameLocal);
            wil.Emit(OpCodes.Ldc_I4_3);  // PipeDirection.InOut = 3
            wil.Emit(OpCodes.Ldc_I4, (int)PipeOptions.Asynchronous);
            wil.Emit(OpCodes.Newobj, typeof(NamedPipeClientStream).GetConstructor([_types.String, _types.String, typeof(PipeDirection), typeof(PipeOptions)])!);
            wil.Emit(OpCodes.Stloc, pipeLocal);

            // pipeClient.Connect(5000) — blocking connect with timeout to avoid hanging forever
            wil.Emit(OpCodes.Ldloc, pipeLocal);
            wil.Emit(OpCodes.Ldc_I4, 5000);
            wil.Emit(OpCodes.Callvirt, typeof(NamedPipeClientStream).GetMethod("Connect", [_types.Int32])!);

            // _stream = pipeClient
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, pipeLocal);
            wil.Emit(OpCodes.Stfld, socketFields.Stream);
        }

        wil.MarkLabel(connectDone);

        // _connecting clears in the scheduled OK/ERR closure (loop thread)

        // EventLoop.Schedule(new Action(new $SocketConnectOkClosure(this).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, socketClosures.ConnectOk.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ConnectOk.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        var leaveOk = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, leaveOk);

        wil.BeginCatchBlock(_types.Exception);
        var exLocal = wil.DeclareLocal(_types.Exception);
        wil.Emit(OpCodes.Stloc, exLocal);

        // _connecting clears in the scheduled ERR closure (loop thread)

        // EventLoop.Schedule(new Action(new $SocketConnectErrClosure(this, ex.Message, GetSocketErrorCode(ex)).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Call, getSocketErrorCode);
        wil.Emit(OpCodes.Newobj, socketClosures.ConnectErr.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ConnectErr.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        wil.Emit(OpCodes.Leave, leaveOk);

        wil.EndExceptionBlock();

        wil.MarkLabel(leaveOk);
        wil.Emit(OpCodes.Ret);

        return worker;
    }

    /// <summary>
    /// Emits: public static string ConvertToWindowsPipeName(string path)
    /// Mirrors SharpTSSocket.ConvertToWindowsPipeName:
    ///   If path starts with "\\.\pipe\", return path.Substring(9).
    ///   Otherwise return Path.GetFileName(path).
    /// </summary>
    private MethodBuilder EmitConvertToWindowsPipeName(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ConvertToWindowsPipeName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );

        var il = method.GetILGenerator();

        // if (path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
        //     return path.Substring(9);
        var notPipePrefix = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, @"\\.\pipe\");
        il.Emit(OpCodes.Ldc_I4_5); // StringComparison.OrdinalIgnoreCase
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notPipePrefix);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 9); // length of "\\.\pipe\"
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPipePrefix);

        // return Path.GetFileName(path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(System.IO.Path).GetMethod("GetFileName", [_types.String])!);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits: public static bool WindowsPipeExists(string pipeName)
    /// Mirrors SharpTSSocket.WindowsPipeExists: enumerate \\.\pipe\ and compare full
    /// entry paths case-insensitively (enumeration is the safe probe; CreateFile-based
    /// checks can consume a pipe instance). Returns true on enumeration failure so the
    /// connect timeout still governs.
    /// </summary>
    private MethodBuilder EmitWindowsPipeExists(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "WindowsPipeExists",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Boolean);
        var end = il.DefineLabel();

        il.BeginExceptionBlock();

        // result = Directory.GetFiles(@"\\.\pipe\")
        //     .Contains(@"\\.\pipe\" + pipeName, StringComparer.OrdinalIgnoreCase)
        il.Emit(OpCodes.Ldstr, @"\\.\pipe\");
        il.Emit(OpCodes.Call, typeof(System.IO.Directory).GetMethod("GetFiles", [_types.String])!);
        il.Emit(OpCodes.Ldstr, @"\\.\pipe\");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Call, typeof(StringComparer).GetProperty("OrdinalIgnoreCase")!.GetGetMethod()!);
        var containsWithComparer = EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethods()
            .Single(m => m.Name == "Contains" && m.GetParameters().Length == 3), _types.String);
        il.Emit(OpCodes.Call, containsWithComparer);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, end);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, end);

        il.EndExceptionBlock();

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits: public static string GetSocketErrorCode(Exception ex)
    /// Maps .NET exceptions to Node.js error codes for socket operations.
    /// </summary>
    private MethodBuilder EmitGetSocketErrorCode(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "GetSocketErrorCode",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Exception]
        );

        var il = method.GetILGenerator();

        // Check if ex is SocketException
        var notSocket = il.DefineLabel();
        var checkFile = il.DefineLabel();
        var defaultCode = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(SocketException));
        var seLocal = il.DeclareLocal(typeof(SocketException));
        il.Emit(OpCodes.Stloc, seLocal);
        il.Emit(OpCodes.Ldloc, seLocal);
        il.Emit(OpCodes.Brfalse, notSocket);

        // switch (se.SocketErrorCode)
        il.Emit(OpCodes.Ldloc, seLocal);
        il.Emit(OpCodes.Callvirt, typeof(SocketException).GetProperty("SocketErrorCode")!.GetGetMethod()!);

        // ConnectionRefused
        var notRefused = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.ConnectionRefused);
        il.Emit(OpCodes.Bne_Un, notRefused);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "ECONNREFUSED");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notRefused);

        // AddressAlreadyInUse
        var notInUse = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.AddressAlreadyInUse);
        il.Emit(OpCodes.Bne_Un, notInUse);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "EADDRINUSE");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notInUse);

        // TimedOut
        var notTimeout = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)SocketError.TimedOut);
        il.Emit(OpCodes.Bne_Un, notTimeout);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "ETIMEDOUT");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTimeout);

        // Default for SocketException
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "ECONNREFUSED");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notSocket);

        // Check if ex is FileNotFoundException, DirectoryNotFoundException, or TimeoutException
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(FileNotFoundException));
        il.Emit(OpCodes.Brtrue, checkFile);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(DirectoryNotFoundException));
        il.Emit(OpCodes.Brtrue, checkFile);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(TimeoutException));
        il.Emit(OpCodes.Brfalse, defaultCode);

        il.MarkLabel(checkFile);
        il.Emit(OpCodes.Ldstr, "ENOENT");
        il.Emit(OpCodes.Ret);

        // Default
        il.MarkLabel(defaultCode);
        il.Emit(OpCodes.Ldstr, "ECONNREFUSED");
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits body for: public object Write(object data, object encodingOrCallback, object callback)
    /// </summary>
    private void EmitNetSocketWriteBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = runtime.RequireNet().SocketWrite.GetILGenerator();

        // if (_destroyed || _stream == null) return false
        var okLabel = il.DefineLabel();
        var retFalseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brtrue, retFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.MarkLabel(retFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(okLabel);

        // Convert data to bytes using _encoding
        // encoding = (arg2 is string) ? arg2 : _encoding
        var encLocal = il.DeclareLocal(_types.String);
        var notEncString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notEncString);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, encLocal);
        var encDone = il.DefineLabel();
        il.Emit(OpCodes.Br, encDone);
        il.MarkLabel(notEncString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Encoding);
        il.Emit(OpCodes.Stloc, encLocal);
        il.MarkLabel(encDone);

        // byte[] bytes = Encoding.UTF8.GetBytes(data.ToString())
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // callback = arg3 if callable, else arg2 if callable, else null.
        // The write callback fires post-flush on the event loop (Node semantics),
        // delivered via the write worker — not invoked synchronously here.
        var cbLocal = il.DeclareLocal(_types.Object);
        var useArg3 = il.DefineLabel();
        var useArg2 = il.DefineLabel();
        var cbDone = il.DefineLabel();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brtrue, useArg3);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
        il.Emit(OpCodes.Brtrue, useArg3);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brtrue, useArg2);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
        il.Emit(OpCodes.Brtrue, useArg2);
        il.Emit(OpCodes.Br, cbDone);
        il.MarkLabel(useArg3);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.Emit(OpCodes.Br, cbDone);
        il.MarkLabel(useArg2);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.MarkLabel(cbDone);

        // return _EnqueueWrite(bytes, callback) — the queue serializes TCP and IPC
        // writes alike (the old per-write IPC ThreadPool items could reorder) and
        // reports backpressure against _writableHwm.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Call, socketMethods.EnqueueWrite);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private bool _EnqueueWrite(byte[] bytes, object callback)
    /// Adds the chunk to the write queue, starts the single write worker if idle,
    /// and returns the backpressure verdict (false once buffered >= high-water mark).
    /// </summary>
    private void EmitNetSocketEnqueueWriteBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.EnqueueWrite.GetILGenerator();
        var newLenLocal = il.DeclareLocal(_types.Int32);
        var startWorkerLocal = il.DeclareLocal(_types.Boolean);

        // newLen = Interlocked.Add(ref _writableLength, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, socketFields.WritableLength);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Add", [_types.Int32.MakeByRefType(), _types.Int32])!);
        il.Emit(OpCodes.Stloc, newLenLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startWorkerLocal);

        // lock (_writeQueue) { _writeQueue.Enqueue([bytes, callback]); if (!_writeWorkerRunning) { _writeWorkerRunning = true; startWorker = true; } }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [_types.Object])!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object[]>).GetMethod("Enqueue", [typeof(object[])])!);

        var alreadyRunning = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteWorkerRunning);
        il.Emit(OpCodes.Brtrue, alreadyRunning);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.WriteWorkerRunning);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, startWorkerLocal);
        il.MarkLabel(alreadyRunning);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [_types.Object])!);

        // if (startWorker) { EventLoop.Ref(); ThreadPool.QueueUserWorkItem(new WaitCallback(this._WriteWorker)); }
        // The Ref keeps the loop alive until the flush lands; released in _FlushTick.
        var noStart = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startWorkerLocal);
        il.Emit(OpCodes.Brfalse, noStart);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, socketMethods.WriteWorker);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noStart);

        // if (newLen < _writableHwm) return true; _needDrain = true; return false
        var below = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, newLenLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WritableHwm);
        il.Emit(OpCodes.Blt, below);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.NeedDrain);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(below);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private void _WriteWorker(object state)
    /// Single-threaded flusher: dequeues chunks in order, writes them to _stream,
    /// schedules write callbacks / error delivery, and on queue-empty performs the
    /// deferred end() shutdown and schedules _FlushTick (drain check + loop Unref).
    /// </summary>
    private void EmitNetSocketWriteWorkerBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.WriteWorker.GetILGenerator();
        var itemLocal = il.DeclareLocal(typeof(object[]));
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        var cbLocal = il.DeclareLocal(_types.Object);
        var doShutdownLocal = il.DeclareLocal(_types.Boolean);
        var exitLocal = il.DeclareLocal(_types.Boolean);
        var okLocal = il.DeclareLocal(_types.Boolean);

        var loopTop = il.DefineLabel();
        var exitPath = il.DefineLabel();

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, itemLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, exitLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, doShutdownLocal);

        // lock (_writeQueue) { if (Count == 0) { _writeWorkerRunning = false; doShutdown = _shutdownAfterFlush; _shutdownAfterFlush = false; exit = true; } else item = Dequeue(); }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [_types.Object])!);

        var hasItem = il.DefineLabel();
        var lockDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object[]>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasItem);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, socketFields.WriteWorkerRunning);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ShutdownAfterFlush);
        il.Emit(OpCodes.Stloc, doShutdownLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, socketFields.ShutdownAfterFlush);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, exitLocal);
        il.Emit(OpCodes.Br, lockDone);
        il.MarkLabel(hasItem);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object[]>).GetMethod("Dequeue")!);
        il.Emit(OpCodes.Stloc, itemLocal);
        il.MarkLabel(lockDone);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, exitLocal);
        il.Emit(OpCodes.Brtrue, exitPath);

        // bytes = (byte[])item[0]; cb = item[1]
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, cbLocal);

        // ok = true; try { _stream.Write(bytes, 0, bytes.Length); _bytesWritten += bytes.Length; }
        // catch (Exception ex) { ok = false; _pendingWriteError = ex.Message; schedule _FireWriteError }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, okLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Write", [_types.ByteArray, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.BytesWritten);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, socketFields.BytesWritten);
        il.BeginCatchBlock(_types.Exception);
        {
            var exLocal = il.DeclareLocal(_types.Exception);
            il.Emit(OpCodes.Stloc, exLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, okLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, exLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!);
            il.Emit(OpCodes.Stfld, socketFields.PendingWriteError);
            il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, socketMethods.FireWriteError);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        }
        il.EndExceptionBlock();

        // Interlocked.Add(ref _writableLength, -bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, socketFields.WritableLength);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(Interlocked).GetMethod("Add", [_types.Int32.MakeByRefType(), _types.Int32])!);
        il.Emit(OpCodes.Pop);

        // if (ok && cb != null) { lock (_pendingWriteCallbacks) Enqueue(cb); schedule _FireWriteCallbacks }
        var skipCb = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, okLocal);
        il.Emit(OpCodes.Brfalse, skipCb);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Brfalse, skipCb);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [_types.Object])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object>).GetMethod("Enqueue", [_types.Object])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [_types.Object])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, socketMethods.FireWriteCallbacks);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        il.MarkLabel(skipCb);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(exitPath);
        // if (doShutdown) { schedule _FireEndCallback; _ShutdownWritable(); }
        // Schedule BEFORE the FIN goes out: an in-process peer's 'end' is enqueued
        // only after its read loop sees the FIN, so the end callback can never be
        // overtaken by it — Node's ordering, which shutdown-then-schedule left to a
        // thread-pool race (#1227; mirrors SharpTSSocket.End).
        var noShut = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, doShutdownLocal);
        il.Emit(OpCodes.Brfalse, noShut);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, socketMethods.FireEndCallback);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, socketMethods.ShutdownWritable);
        il.MarkLabel(noShut);

        // schedule _FlushTick — drain check + the worker's balancing Unref
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, socketMethods.FlushTick);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private void _FlushTick()
    /// Runs on the event loop after a write-worker generation exits: emits 'drain'
    /// if the buffer fully emptied while backpressured, then releases the worker's Ref.
    /// </summary>
    private void EmitNetSocketFlushTickBody(EmittedRuntime runtime, NetSocketFields socketFields, NetSocketMethods socketMethods)
    {
        var il = socketMethods.FlushTick.GetILGenerator();

        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brtrue, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WritableLength);
        il.Emit(OpCodes.Brtrue, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.NeedDrain);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, socketFields.NeedDrain);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "drain");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skip);

        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Unref);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: invokes the callable in <paramref name="cbLocal"/> ($TSFunction or
    /// $BoundTSFunction) with zero args; non-callables are ignored.
    /// </summary>
    private void EmitInvokeCallableLocal(ILGenerator il, EmittedRuntime runtime, LocalBuilder cbLocal)
    {
        var notTs = il.DefineLabel();
        var invoked = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, notTs);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, invoked);
        il.MarkLabel(notTs);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
        il.Emit(OpCodes.Brfalse, invoked);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Castclass, runtime.FunctionBindings.BoundType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.FunctionBindings.BoundInvoke);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(invoked);
    }

    /// <summary>
    /// Emits body for: private void _FireWriteCallbacks()
    /// Drains the pending write-callback queue on the event loop, in order.
    /// </summary>
    private void EmitNetSocketFireWriteCallbacksBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.FireWriteCallbacks.GetILGenerator();
        var cbLocal = il.DeclareLocal(_types.Object);
        var loopTop = il.DefineLabel();
        var done = il.DefineLabel();

        il.MarkLabel(loopTop);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, cbLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [_types.Object])!);
        var emptyQueue = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, emptyQueue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Callvirt, typeof(Queue<object>).GetMethod("Dequeue")!);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.MarkLabel(emptyQueue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteCallbacks);
        il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [_types.Object])!);

        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Brfalse, done);
        EmitInvokeCallableLocal(il, runtime, cbLocal);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private void _FireWriteError()
    /// Delivers a write failure as an 'error' event on the event loop (skipped once destroyed).
    /// </summary>
    private void EmitNetSocketFireWriteErrorBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.FireWriteError.GetILGenerator();
        var msgLocal = il.DeclareLocal(_types.String);
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingWriteError);
        il.Emit(OpCodes.Stloc, msgLocal);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, socketFields.PendingWriteError);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brtrue, done);

        // this.Emit("error", [new $Error(msg)])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Newobj, runtime.Errors.MessageConstructor);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private void _FireEndCallback()
    /// Invokes the end() callback exactly once. Enqueued on the loop BEFORE the
    /// shutdown sends the FIN (#1227), so it may run before _ShutdownWritable
    /// executes; the Destroy branch below closing the transport first is harmless —
    /// _ShutdownWritable catch-alls, and the branch only fires when the readable
    /// side already saw FIN (or the read-end path requested an auto-finish), where
    /// full teardown is the goal. Completes that close via Destroy (#1070).
    /// </summary>
    private void EmitNetSocketFireEndCallbackBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.FireEndCallback.GetILGenerator();
        var cbLocal = il.DeclareLocal(_types.Object);
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.PendingEndCallback);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, socketFields.PendingEndCallback);
        EmitInvokeCallableLocal(il, runtime, cbLocal);
        il.MarkLabel(done);

        // if (!_destroyed && (_finishAfterEnd || _endReceived)) { _finishAfterEnd = false; Destroy(null); }
        var skipDestroy = il.DefineLabel();
        var doDestroy = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brtrue, skipDestroy);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.FinishAfterEnd);
        il.Emit(OpCodes.Brtrue, doDestroy);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.EndReceived);
        il.Emit(OpCodes.Brfalse, skipDestroy);
        il.MarkLabel(doDestroy);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, socketFields.FinishAfterEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, socketMethods.Destroy);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipDestroy);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: private void _ShutdownWritable()
    /// TCP: half-close via Socket.Shutdown(Send). IPC: pipes can't half-close, so the
    /// stream is closed entirely (mirrors SharpTSSocket.ShutdownWritable).
    /// </summary>
    private void EmitNetSocketShutdownWritableBody(
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.ShutdownWritable.GetILGenerator();

        var tcpPath = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
        il.Emit(OpCodes.Brfalse, tcpPath);

        // IPC: try { _stream?.Close() } catch { } ; _stream = null
        il.BeginExceptionBlock();
        var noStream = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Brfalse, noStream);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Close")!);
        il.MarkLabel(noStream);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, socketFields.Stream);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(tcpPath);
        // try { _client?.Client?.Shutdown(SocketShutdown.Send) } catch { }
        il.BeginExceptionBlock();
        var noClient = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, noClient);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var noSocket = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noSocket);
        il.Emit(OpCodes.Ldc_I4_1); // SocketShutdown.Send = 1
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Shutdown", [typeof(SocketShutdown)])!);
        var shutDone = il.DefineLabel();
        il.Emit(OpCodes.Br, shutDone);
        il.MarkLabel(noSocket);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(shutDone);
        il.MarkLabel(noClient);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public object End(object dataOrCallback, object encodingOrCallback, object callback)
    /// </summary>
    private void EmitNetSocketEndBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.End.GetILGenerator();

        // if (_ended) return this
        var notEnded = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Ended);
        il.Emit(OpCodes.Brfalse, notEnded);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEnded);

        // Write final chunk if arg1 is not null and not callable
        var noFinalWrite = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noFinalWrite);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brtrue, noFinalWrite);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
        il.Emit(OpCodes.Brtrue, noFinalWrite);

        // Write(arg1, null, null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.RequireNet().SocketWrite);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noFinalWrite);

        // _ended = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.Ended);

        // _pendingEndCallback = first callable among arg1..arg3 (fired once after shutdown)
        {
            var cbLocal = il.DeclareLocal(_types.Object);
            var useArg1 = il.DefineLabel();
            var useArg2 = il.DefineLabel();
            var useArg3 = il.DefineLabel();
            var cbDone = il.DefineLabel();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, cbLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
            il.Emit(OpCodes.Brtrue, useArg1);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
            il.Emit(OpCodes.Brtrue, useArg1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
            il.Emit(OpCodes.Brtrue, useArg2);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
            il.Emit(OpCodes.Brtrue, useArg2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
            il.Emit(OpCodes.Brtrue, useArg3);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
            il.Emit(OpCodes.Brtrue, useArg3);
            il.Emit(OpCodes.Br, cbDone);
            il.MarkLabel(useArg1);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, cbLocal);
            il.Emit(OpCodes.Br, cbDone);
            il.MarkLabel(useArg2);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stloc, cbLocal);
            il.Emit(OpCodes.Br, cbDone);
            il.MarkLabel(useArg3);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Stloc, cbLocal);
            il.MarkLabel(cbDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, cbLocal);
            il.Emit(OpCodes.Stfld, socketFields.PendingEndCallback);
        }

        // end() must not truncate queued writes: if the write worker is active,
        // flag the shutdown for it to perform at queue-drain; otherwise shut down now.
        {
            var shutdownNowLocal = il.DeclareLocal(_types.Boolean);
            var workerActive = il.DefineLabel();
            var lockDone = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [_types.Object])!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.WriteWorkerRunning);
            il.Emit(OpCodes.Brtrue, workerActive);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, shutdownNowLocal);
            il.Emit(OpCodes.Br, lockDone);
            il.MarkLabel(workerActive);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, socketFields.ShutdownAfterFlush);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, shutdownNowLocal);
            il.MarkLabel(lockDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.WriteQueue);
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [_types.Object])!);

            // if (shutdownNow) { schedule _FireEndCallback; _ShutdownWritable(); }
            // Schedule BEFORE the FIN goes out so an in-process peer's 'end' — enqueued
            // only after its read loop sees the FIN — can never overtake the end
            // callback (#1227; mirrors SharpTSSocket.End and the worker-exit path).
            var noShutdownNow = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, shutdownNowLocal);
            il.Emit(OpCodes.Brfalse, noShutdownNow);
            il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, socketMethods.FireEndCallback);
            il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, socketMethods.ShutdownWritable);
            il.MarkLabel(noShutdownNow);
        }

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public object Destroy(object error)
    /// Fix: After _stream.Close(), sets _stream = null.
    /// </summary>
    private void EmitNetSocketDestroyBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.Destroy.GetILGenerator();

        // if (_destroyed) return this
        var notDestroyed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brfalse, notDestroyed);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDestroyed);

        // _destroyed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.Destroyed);

        // _readCts?.Cancel()
        il.BeginExceptionBlock();
        var noCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadCts);
        il.Emit(OpCodes.Brfalse, noCts);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadCts);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCts);

        // _stream?.Close(); _stream = null
        var noStream = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Brfalse, noStream);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Close")!);
        il.MarkLabel(noStream);
        // _stream = null (always, even if already null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, socketFields.Stream);

        // _client?.Close()
        var noClient2 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, noClient2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Close")!);
        il.MarkLabel(noClient2);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // Emit 'close' at most once per socket lifetime (Node semantics): the
        // read-loop end closure may already have fired it before destroy().
        // if (!_closeEmitted) { _closeEmitted = true; this.Emit("close", [error != null]) }
        var closeAlreadyEmitted = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.CloseEmitted);
        il.Emit(OpCodes.Brtrue, closeAlreadyEmitted);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.CloseEmitted);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq); // arg1 != null
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(closeAlreadyEmitted);

        // Unref event loop if reading was started
        var noUnref = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadingStarted);
        il.Emit(OpCodes.Brfalse, noUnref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, socketFields.ReadingStarted);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Unref);
        il.MarkLabel(noUnref);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public void StartReading()
    /// Starts async read loop on ThreadPool, schedules 'data'/'end' events via closures.
    /// </summary>
    private void EmitNetSocketStartReadingBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketClosures socketClosures)
    {
        var il = runtime.RequireNet().SocketStartReading.GetILGenerator();

        // if (_destroyed || _stream == null) return
        var okLabel = il.DefineLabel();
        var retLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Brtrue, retLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Stream);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(okLabel);

        // Cancel previous read if any
        var noPrevCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadCts);
        il.Emit(OpCodes.Brfalse, noPrevCts);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadCts);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.MarkLabel(noPrevCts);

        // _readCts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, socketFields.ReadCts);

        // _readReady = new ManualResetEventSlim(false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.ManualResetEventSlim).GetConstructor([_types.Boolean])!);
        il.Emit(OpCodes.Stfld, socketFields.ReadReady);

        // if (!_readingStarted) { _readingStarted = true; EventLoop.Ref(); }
        var alreadyReading = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.ReadingStarted);
        il.Emit(OpCodes.Brtrue, alreadyReading);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, socketFields.ReadingStarted);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);
        il.MarkLabel(alreadyReading);

        // Emit the read worker as a private instance method
        var readWorker = EmitNetSocketReadWorker(typeBuilder, runtime, socketFields, socketClosures);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._ReadWorker))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, readWorker);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private void _ReadWorker(object state)
    /// Read loop that uses $SocketReadDataClosure and $SocketReadEndClosure.
    /// </summary>
    private MethodBuilder EmitNetSocketReadWorker(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketClosures socketClosures)
    {
        var readWorker = typeBuilder.DefineMethod(
            "_ReadWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        var wil = readWorker.GetILGenerator();
        var bufferLocal = wil.DeclareLocal(_types.ByteArray);
        var bytesReadLocal = wil.DeclareLocal(_types.Int32);

        // buffer = new byte[65536]
        wil.Emit(OpCodes.Ldc_I4, 65536);
        wil.Emit(OpCodes.Newarr, typeof(byte));
        wil.Emit(OpCodes.Stloc, bufferLocal);

        // if (_readReady != null) _readReady.Set()
        var skipReadySignal = wil.DefineLabel();
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.ReadReady);
        wil.Emit(OpCodes.Brfalse, skipReadySignal);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.ReadReady);
        wil.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Set")!);
        wil.MarkLabel(skipReadySignal);

        // while loop
        var loopTop = wil.DefineLabel();
        var loopExit = wil.DefineLabel();

        wil.MarkLabel(loopTop);

        // Check _destroyed
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        wil.Emit(OpCodes.Brtrue, loopExit);

        // Check _stream != null
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.Stream);
        wil.Emit(OpCodes.Brfalse, loopExit);

        // try { bytesRead = _stream.Read(buffer, 0, 65536) } catch { break }
        wil.BeginExceptionBlock();
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.Stream);
        wil.Emit(OpCodes.Ldloc, bufferLocal);
        wil.Emit(OpCodes.Ldc_I4_0);
        wil.Emit(OpCodes.Ldc_I4, 65536);
        wil.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Read", [_types.ByteArray, _types.Int32, _types.Int32])!);
        wil.Emit(OpCodes.Stloc, bytesReadLocal);

        var afterRead = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, afterRead);

        wil.BeginCatchBlock(_types.Exception);
        wil.Emit(OpCodes.Pop);
        wil.Emit(OpCodes.Leave, loopExit);
        wil.EndExceptionBlock();

        wil.MarkLabel(afterRead);

        // if (bytesRead == 0) { schedule end event via closure; break }
        var notZero = wil.DefineLabel();
        wil.Emit(OpCodes.Ldloc, bytesReadLocal);
        wil.Emit(OpCodes.Brtrue, notZero);

        // EventLoop.Schedule(new Action(new $SocketReadEndClosure(this).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, socketClosures.ReadEnd.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ReadEnd.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        wil.Emit(OpCodes.Br, loopExit);

        wil.MarkLabel(notZero);

        // _bytesRead += bytesRead
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, socketFields.BytesRead);
        wil.Emit(OpCodes.Ldloc, bytesReadLocal);
        wil.Emit(OpCodes.Add);
        wil.Emit(OpCodes.Stfld, socketFields.BytesRead);

        // copy = new byte[bytesRead]; Array.Copy(buffer, copy, bytesRead)
        var copyLocal = wil.DeclareLocal(_types.ByteArray);
        wil.Emit(OpCodes.Ldloc, bytesReadLocal);
        wil.Emit(OpCodes.Newarr, typeof(byte));
        wil.Emit(OpCodes.Stloc, copyLocal);
        wil.Emit(OpCodes.Ldloc, bufferLocal);
        wil.Emit(OpCodes.Ldloc, copyLocal);
        wil.Emit(OpCodes.Ldloc, bytesReadLocal);
        wil.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), typeof(Array), _types.Int32])!);

        // chunk = Encoding.UTF8.GetString(copy)
        var chunkLocal = wil.DeclareLocal(_types.Object);
        wil.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        wil.Emit(OpCodes.Ldloc, copyLocal);
        wil.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", [_types.ByteArray])!);
        wil.Emit(OpCodes.Stloc, chunkLocal);

        // EventLoop.Schedule(new Action(new $SocketReadDataClosure(this, chunk).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, chunkLocal);
        wil.Emit(OpCodes.Newobj, socketClosures.ReadData.Constructor);
        wil.Emit(OpCodes.Ldftn, socketClosures.ReadData.Run);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        wil.Emit(OpCodes.Br, loopTop);

        wil.MarkLabel(loopExit);
        wil.Emit(OpCodes.Ret);

        return readWorker;
    }

    /// <summary>
    /// Emits body for: public object SetEncoding(object enc)
    /// </summary>
    private void EmitNetSocketSetEncodingBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetSocketFields socketFields,
        NetSocketMethods socketMethods)
    {
        var il = socketMethods.SetEncoding.GetILGenerator();

        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stfld, socketFields.Encoding);
        il.MarkLabel(notString);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public object GetMember(string name)
    /// Dispatches property/method access including new remoteFamily, localAddress, readyState.
    /// </summary>
    private void EmitNetSocketGetMemberBody(TypeBuilder typeBuilder, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var il = runtime.RequireNet().SocketGetMember.GetILGenerator();

        // Property dispatch labels
        var remoteAddressLabel = il.DefineLabel();
        var remotePortLabel = il.DefineLabel();
        var remoteFamilyLabel = il.DefineLabel();
        var localAddressLabel = il.DefineLabel();
        var localPortLabel = il.DefineLabel();
        var bytesReadLabel = il.DefineLabel();
        var bytesWrittenLabel = il.DefineLabel();
        var connectingLabel = il.DefineLabel();
        var destroyedLabel = il.DefineLabel();
        var readyStateLabel = il.DefineLabel();
        var writableLengthLabel = il.DefineLabel();
        var writableHwmLabel = il.DefineLabel();
        var writableNeedDrainLabel = il.DefineLabel();
        var localFamilyLabel = il.DefineLabel();
        var pendingLabel = il.DefineLabel();
        var allowHalfOpenLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "remoteAddress", remoteAddressLabel);
        EmitStringCheck(il, 1, "remotePort", remotePortLabel);
        EmitStringCheck(il, 1, "remoteFamily", remoteFamilyLabel);
        EmitStringCheck(il, 1, "localAddress", localAddressLabel);
        EmitStringCheck(il, 1, "localPort", localPortLabel);
        EmitStringCheck(il, 1, "bytesRead", bytesReadLabel);
        EmitStringCheck(il, 1, "bytesWritten", bytesWrittenLabel);
        EmitStringCheck(il, 1, "connecting", connectingLabel);
        EmitStringCheck(il, 1, "destroyed", destroyedLabel);
        EmitStringCheck(il, 1, "readyState", readyStateLabel);
        EmitStringCheck(il, 1, "writableLength", writableLengthLabel);
        EmitStringCheck(il, 1, "writableHighWaterMark", writableHwmLabel);
        EmitStringCheck(il, 1, "writableNeedDrain", writableNeedDrainLabel);
        EmitStringCheck(il, 1, "localFamily", localFamilyLabel);
        EmitStringCheck(il, 1, "pending", pendingLabel);
        EmitStringCheck(il, 1, "allowHalfOpen", allowHalfOpenLabel);

        // Fall through to default
        il.Emit(OpCodes.Br, defaultLabel);

        // ── localFamily ── (#1070)
        il.MarkLabel(localFamilyLabel);
        {
            var notIpcLf = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpcLf);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpcLf);
        }
        EmitGetEndpointFamily(il, runtime, "LocalEndPoint", socketFields);
        il.Emit(OpCodes.Ret);

        // ── pending ── (#1070): not yet connected — no stream, or connect in flight
        il.MarkLabel(pendingLabel);
        {
            var retTruePending = il.DefineLabel();
            var retFalsePending = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Connecting);
            il.Emit(OpCodes.Brtrue, retTruePending);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Stream);
            il.Emit(OpCodes.Brtrue, retFalsePending);
            il.MarkLabel(retTruePending);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(retFalsePending);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
        }

        // ── allowHalfOpen ── (#1070)
        il.MarkLabel(allowHalfOpenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.AllowHalfOpen);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── writableLength ──
        il.MarkLabel(writableLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WritableLength);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── writableHighWaterMark ──
        il.MarkLabel(writableHwmLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.WritableHwm);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── writableNeedDrain ──
        il.MarkLabel(writableNeedDrainLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.NeedDrain);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── remoteAddress ──
        il.MarkLabel(remoteAddressLabel);
        {
            // if (_isIpc) return null
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetRemoteEndpointString(il, runtime, "Address", socketFields);
        il.Emit(OpCodes.Ret);

        // ── remotePort ──
        il.MarkLabel(remotePortLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetRemoteEndpointPort(il, runtime, socketFields);
        il.Emit(OpCodes.Ret);

        // ── remoteFamily ──
        il.MarkLabel(remoteFamilyLabel);
        {
            // if (_isIpc) return "pipe"
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldstr, "pipe");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
            // For TCP: check if IPv6 else IPv4
            EmitGetRemoteEndpointFamily(il, runtime, socketFields);
            il.Emit(OpCodes.Ret);
        }

        // ── localAddress ──
        il.MarkLabel(localAddressLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetLocalEndpointString(il, runtime, "Address", socketFields);
        il.Emit(OpCodes.Ret);

        // ── localPort ──
        il.MarkLabel(localPortLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetLocalEndpointPort(il, runtime, socketFields);
        il.Emit(OpCodes.Ret);

        // ── bytesRead ──
        il.MarkLabel(bytesReadLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.BytesRead);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── bytesWritten ──
        il.MarkLabel(bytesWrittenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.BytesWritten);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── connecting ──
        il.MarkLabel(connectingLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Connecting);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── destroyed ──
        il.MarkLabel(destroyedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── readyState ──
        // "opening" if _connecting, "closed" if _destroyed,
        // "open" if _stream != null (simplified for IPC), _client?.Connected for TCP
        il.MarkLabel(readyStateLabel);
        {
            var notConnecting = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Connecting);
            il.Emit(OpCodes.Brfalse, notConnecting);
            il.Emit(OpCodes.Ldstr, "opening");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notConnecting);
            var notDestroyed2 = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Destroyed);
            il.Emit(OpCodes.Brfalse, notDestroyed2);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notDestroyed2);

            // if (_isIpc) return _stream != null ? "open" : "closed"
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.IsIpc);
            il.Emit(OpCodes.Brfalse, notIpc);

            var ipcStreamNull = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Stream);
            il.Emit(OpCodes.Brfalse, ipcStreamNull);
            il.Emit(OpCodes.Ldstr, "open");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(ipcStreamNull);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notIpc);

            // TCP: derived from lifecycle state (mirrors SharpTSSocket.GetReadyState —
            // TcpClient.Connected is racy around shutdown). Half-close states (#1070):
            // readOnly after end(), writeOnly after a received FIN.
            var closedRS = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Stream);
            il.Emit(OpCodes.Brfalse, closedRS);

            // if (_ended && _endReceived) return "closed"
            var notBothDone = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Ended);
            il.Emit(OpCodes.Brfalse, notBothDone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.EndReceived);
            il.Emit(OpCodes.Brtrue, closedRS);
            il.MarkLabel(notBothDone);

            // if (_ended) return "readOnly"
            var notReadOnly = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.Ended);
            il.Emit(OpCodes.Brfalse, notReadOnly);
            il.Emit(OpCodes.Ldstr, "readOnly");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notReadOnly);

            // if (_endReceived) return "writeOnly"
            var notWriteOnly = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketFields.EndReceived);
            il.Emit(OpCodes.Brfalse, notWriteOnly);
            il.Emit(OpCodes.Ldstr, "writeOnly");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notWriteOnly);

            il.Emit(OpCodes.Ldstr, "open");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(closedRS);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);
        }

        // ── default — return undefined ──
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Endpoint helpers (shared with GetMember)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Helper: If arg at argIndex equals value, branch to target.
    /// </summary>
    private void EmitStringCheck(ILGenerator il, int argIndex, string value, Label target)
    {
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Ldstr, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Helper: try to get a string from a dictionary at arg index, store in local.
    /// </summary>
    private void EmitDictTryGetString(ILGenerator il, int argIdx, string key, LocalBuilder target)
    {
        var skipLabel = il.DefineLabel();
        var valLocal = il.DeclareLocal(_types.Object);

        // dict.TryGetValue(key, out val)
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Ldloca, valLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // if (val is string s) target = s
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, target);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Helper: Emits IL to get a string property from the remote endpoint.
    /// </summary>
    private void EmitGetRemoteEndpointString(
        ILGenerator il,
        EmittedRuntime runtime,
        string property,
        NetSocketFields socketFields)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        // _client null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, done);

        // _client.Client null check
        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Brfalse, done);

        // RemoteEndPoint as IPEndPoint
        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("RemoteEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty(property)!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    private void EmitGetRemoteEndpointPort(ILGenerator il, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Brfalse, done);

        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("RemoteEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>
    /// Helper: returns "IPv6" or "IPv4" based on remote endpoint address family, or null.
    /// </summary>
    private void EmitGetRemoteEndpointFamily(ILGenerator il, EmittedRuntime runtime, NetSocketFields socketFields)
        => EmitGetEndpointFamily(il, runtime, "RemoteEndPoint", socketFields);

    /// <summary>
    /// Helper: pushes "IPv4"/"IPv6" (or null) for the given endpoint property
    /// ("RemoteEndPoint" or "LocalEndPoint").
    /// </summary>
    private void EmitGetEndpointFamily(
        ILGenerator il,
        EmittedRuntime runtime,
        string endpointProperty,
        NetSocketFields socketFields)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Brfalse, done);

        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty(endpointProperty)!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, done);

        // ep.AddressFamily == InterNetworkV6 ? "IPv6" : "IPv4"
        var isIpv4 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Net.Sockets.AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Bne_Un, isIpv4);
        il.Emit(OpCodes.Ldstr, "IPv6");
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(isIpv4);
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    /// <summary>
    /// Helper: Emits IL to get a string property from the local endpoint.
    /// </summary>
    private void EmitGetLocalEndpointString(ILGenerator il, EmittedRuntime runtime, string property, NetSocketFields socketFields)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Brfalse, done);

        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("LocalEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty(property)!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    private void EmitGetLocalEndpointPort(ILGenerator il, EmittedRuntime runtime, NetSocketFields socketFields)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketFields.Client);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Brfalse, done);

        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("LocalEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Brfalse, done);

        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }
}
