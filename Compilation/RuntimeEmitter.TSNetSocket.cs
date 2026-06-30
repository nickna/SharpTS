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
    // Field builders for $NetSocket
    private TypeBuilder _netSocketTypeBuilder = null!;
    private FieldBuilder _netSocketClientField = null!;
    private FieldBuilder _netSocketStreamField = null!;
    private FieldBuilder _netSocketConnectingField = null!;
    private FieldBuilder _netSocketDestroyedField = null!;
    private FieldBuilder _netSocketCloseEmittedField = null!;
    private FieldBuilder _netSocketEndedField = null!;
    private FieldBuilder _netSocketBytesReadField = null!;
    private FieldBuilder _netSocketBytesWrittenField = null!;
    private FieldBuilder _netSocketEncodingField = null!;
    private FieldBuilder _netSocketReadingStartedField = null!;
    private FieldBuilder _netSocketReadCtsField = null!;
    private FieldBuilder _netSocketIsIpcField = null!;
    private FieldBuilder _netSocketPipePathField = null!;
    private FieldBuilder _netSocketReadReadyField = null!;
    private FieldBuilder _netSocketConnectHostField = null!;
    private FieldBuilder _netSocketConnectPortField = null!;

    // Method builders for $NetSocket (defined in Phase 1a, bodies emitted in Phase 2)
    private MethodBuilder _netSocketConnectMethod = null!;
    private MethodBuilder _netSocketWriteMethod = null!;
    private MethodBuilder _netSocketEndMethod = null!;
    private MethodBuilder _netSocketDestroyMethod = null!;
    private MethodBuilder _netSocketStartReadingMethod = null!;
    private MethodBuilder _netSocketSetEncodingMethod = null!;
    private MethodBuilder _netSocketGetMemberMethod = null!;

    // Closure constructors/run methods (set by the closure emitter between phases)
    internal ConstructorBuilder _socketReadDataClosureCtor = null!;
    internal MethodBuilder _socketReadDataClosureRun = null!;
    internal ConstructorBuilder _socketReadEndClosureCtor = null!;
    internal MethodBuilder _socketReadEndClosureRun = null!;
    internal ConstructorBuilder _socketConnectOkClosureCtor = null!;
    internal MethodBuilder _socketConnectOkClosureRun = null!;
    internal ConstructorBuilder _socketConnectErrClosureCtor = null!;
    internal MethodBuilder _socketConnectErrClosureRun = null!;
    private MethodBuilder _getSocketErrorCodeMethod = null!;

    /// <summary>
    /// Phase 1a: Defines the $NetSocket type, fields, constructors (with bodies),
    /// and method STUBS (no bodies). Must be called BEFORE closure types are defined
    /// and BEFORE EmitRuntimeClass so NetCreateConnection can use the constructor.
    /// </summary>
    private void EmitTSNetSocketPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$NetSocket",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        _netSocketTypeBuilder = typeBuilder;
        runtime.NetSocketType = typeBuilder;

        // ── Fields ──
        // Assembly (internal) rather than Private so the $TlsSocket subclass and the
        // TLS connect/accept workers (same emitted module) can assign the negotiated
        // TcpClient — TLSSocket extends net.Socket, mirroring interp SharpTSTlsSocket : SharpTSSocket.
        _netSocketClientField = typeBuilder.DefineField("_client", typeof(TcpClient), FieldAttributes.Assembly);
        _netSocketStreamField = typeBuilder.DefineField("_stream", typeof(System.IO.Stream), FieldAttributes.Assembly);
        _netSocketConnectingField = typeBuilder.DefineField("_connecting", _types.Boolean, FieldAttributes.Assembly);
        _netSocketDestroyedField = typeBuilder.DefineField("_destroyed", _types.Boolean, FieldAttributes.Assembly);
        _netSocketCloseEmittedField = typeBuilder.DefineField("_closeEmitted", _types.Boolean, FieldAttributes.Assembly);
        _netSocketEndedField = typeBuilder.DefineField("_ended", _types.Boolean, FieldAttributes.Private);
        _netSocketBytesReadField = typeBuilder.DefineField("_bytesRead", _types.Int32, FieldAttributes.Private);
        _netSocketBytesWrittenField = typeBuilder.DefineField("_bytesWritten", _types.Int32, FieldAttributes.Assembly);
        _netSocketEncodingField = typeBuilder.DefineField("_encoding", _types.String, FieldAttributes.Private);
        _netSocketReadingStartedField = typeBuilder.DefineField("_readingStarted", _types.Boolean, FieldAttributes.Assembly);
        _netSocketReadCtsField = typeBuilder.DefineField("_readCts", typeof(CancellationTokenSource), FieldAttributes.Private);
        _netSocketIsIpcField = typeBuilder.DefineField("_isIpc", _types.Boolean, FieldAttributes.Assembly);
        _netSocketReadReadyField = typeBuilder.DefineField("_readReady", typeof(System.Threading.ManualResetEventSlim), FieldAttributes.Assembly);
        _netSocketPipePathField = typeBuilder.DefineField("_pipePath", _types.String, FieldAttributes.Private);
        _netSocketConnectHostField = typeBuilder.DefineField("_connectHost", _types.String, FieldAttributes.Private);
        _netSocketConnectPortField = typeBuilder.DefineField("_connectPort", _types.Int32, FieldAttributes.Private);

        // ── Constructors (with bodies) ──

        // Constructor 1: $NetSocket() — unconnected client socket
        EmitNetSocketDefaultCtor(typeBuilder, runtime);

        // Constructor 2: $NetSocket(TcpClient) — server-accepted socket
        EmitNetSocketTcpClientCtor(typeBuilder, runtime);

        // Constructor 3: $NetSocket(Stream, string) — IPC pipe socket
        EmitNetSocketStreamCtor(typeBuilder, runtime);

        // ── Method stubs (no bodies — emitted in Phase 2) ──

        _netSocketStartReadingMethod = typeBuilder.DefineMethod(
            "StartReading",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        runtime.NetSocketStartReading = _netSocketStartReadingMethod;

        _netSocketConnectMethod = typeBuilder.DefineMethod(
            "Connect",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.NetSocketConnect = _netSocketConnectMethod;

        _netSocketWriteMethod = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.NetSocketWrite = _netSocketWriteMethod;

        _netSocketEndMethod = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        _ = _netSocketEndMethod;

        _netSocketDestroyMethod = typeBuilder.DefineMethod(
            "Destroy",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _ = _netSocketDestroyMethod;

        _netSocketSetEncodingMethod = typeBuilder.DefineMethod(
            "SetEncoding",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _ = _netSocketSetEncodingMethod;

        _netSocketGetMemberMethod = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.NetSocketGetMember = _netSocketGetMemberMethod;

        // NOTE: CreateType() is deferred to Phase 2
    }

    /// <summary>
    /// Phase 2: Emits all method bodies and finalizes the $NetSocket type.
    /// Called after closure types have been defined between phases.
    /// </summary>
    private void EmitTSNetSocketPhase2(EmittedRuntime runtime)
    {
        var typeBuilder = _netSocketTypeBuilder;

        // Emit method bodies
        EmitNetSocketStartReadingBody(typeBuilder, runtime);
        EmitNetSocketConnectBody(typeBuilder, runtime);
        EmitNetSocketWriteBody(typeBuilder, runtime);
        EmitNetSocketEndBody(typeBuilder, runtime);
        EmitNetSocketDestroyBody(typeBuilder, runtime);
        EmitNetSocketSetEncodingBody(typeBuilder, runtime);
        EmitNetSocketGetMemberBody(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    // ════════════════════════════════════════════════════════════════
    //  Constructors (bodies emitted in Phase 1a)
    // ════════════════════════════════════════════════════════════════

    private void EmitNetSocketDefaultCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.NetSocketCtor = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, _netSocketEncodingField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNetSocketTcpClientCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(TcpClient)]
        );
        runtime.NetSocketCtorTcpClient = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        // _client = client
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _netSocketClientField);
        // _stream = client.GetStream()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        il.Emit(OpCodes.Stfld, _netSocketStreamField);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, _netSocketEncodingField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNetSocketStreamCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(System.IO.Stream), _types.String]
        );
        runtime.NetSocketCtorStream = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        // _stream = arg1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _netSocketStreamField);
        // _isIpc = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketIsIpcField);
        // _pipePath = arg2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stfld, _netSocketPipePathField);
        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, _netSocketEncodingField);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Method bodies (emitted in Phase 2)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Connect(object optionsOrPort, object hostOrCallback, object callback)
    /// Parses args and initiates async TCP or IPC connection.
    /// </summary>
    private void EmitNetSocketConnectBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketConnectMethod.GetILGenerator();

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
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
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
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCallback);

        // _connecting = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketConnectingField);

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
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // ── Branch: IPC vs TCP ──
        var ipcConnect = il.DefineLabel();
        var connectDone = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Brtrue, ipcConnect);

        // ── TCP path ──
        // _client = new TcpClient()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(TcpClient).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _netSocketClientField);

        // Store host/port for the worker
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stfld, _netSocketConnectHostField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Stfld, _netSocketConnectPortField);

        // Emit error code helper (used by both TCP and IPC connect workers)
        _getSocketErrorCodeMethod = EmitGetSocketErrorCode(typeBuilder);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._ConnectWorker))
        var connectWorker = EmitNetSocketConnectWorker(typeBuilder, runtime);
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
        il.Emit(OpCodes.Stfld, _netSocketIsIpcField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, _netSocketPipePathField);

        // Store pipePath in _connectHost for the IPC worker to use
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, _netSocketConnectHostField);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._ConnectIpcWorker))
        var connectIpcWorker = EmitNetSocketConnectIpcWorker(typeBuilder, runtime);
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
    private MethodBuilder EmitNetSocketConnectWorker(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        wil.Emit(OpCodes.Ldfld, _netSocketClientField);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketConnectHostField);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketConnectPortField);
        wil.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Connect", [_types.String, _types.Int32])!);

        // _stream = _client.GetStream()
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketClientField);
        wil.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        wil.Emit(OpCodes.Stfld, _netSocketStreamField);

        // _connecting = false
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldc_I4_0);
        wil.Emit(OpCodes.Stfld, _netSocketConnectingField);

        // EventLoop.Schedule(new Action(new $SocketConnectOkClosure(this).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, _socketConnectOkClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketConnectOkClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        var leaveOk = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, leaveOk);

        wil.BeginCatchBlock(_types.Exception);
        // On error: _connecting = false, schedule error event via closure
        var exLocal = wil.DeclareLocal(_types.Exception);
        wil.Emit(OpCodes.Stloc, exLocal);

        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldc_I4_0);
        wil.Emit(OpCodes.Stfld, _netSocketConnectingField);

        // EventLoop.Schedule(new Action(new $SocketConnectErrClosure(this, ex.Message, GetSocketErrorCode(ex)).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Callvirt, _types.Exception.GetProperty("Message")!.GetGetMethod()!);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Call, _getSocketErrorCodeMethod);
        wil.Emit(OpCodes.Newobj, _socketConnectErrClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketConnectErrClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

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
    private MethodBuilder EmitNetSocketConnectIpcWorker(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
            wil.Emit(OpCodes.Ldfld, _netSocketConnectHostField);
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
            wil.Emit(OpCodes.Stfld, _netSocketStreamField);
        }

        wil.Emit(OpCodes.Br, connectDone);

        // ── Windows path: NamedPipeClientStream ──
        wil.MarkLabel(windowsPath);
        {
            // string pipeName = ConvertToWindowsPipeName(_connectHost)
            var pipeNameLocal = wil.DeclareLocal(_types.String);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netSocketConnectHostField);
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
            wil.Emit(OpCodes.Ldfld, _netSocketConnectHostField);
            wil.Emit(OpCodes.Ldstr, "'");
            wil.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String])!);
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
            wil.Emit(OpCodes.Stfld, _netSocketStreamField);
        }

        wil.MarkLabel(connectDone);

        // _connecting = false
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldc_I4_0);
        wil.Emit(OpCodes.Stfld, _netSocketConnectingField);

        // EventLoop.Schedule(new Action(new $SocketConnectOkClosure(this).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, _socketConnectOkClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketConnectOkClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        var leaveOk = wil.DefineLabel();
        wil.Emit(OpCodes.Leave, leaveOk);

        wil.BeginCatchBlock(_types.Exception);
        var exLocal = wil.DeclareLocal(_types.Exception);
        wil.Emit(OpCodes.Stloc, exLocal);

        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldc_I4_0);
        wil.Emit(OpCodes.Stfld, _netSocketConnectingField);

        // EventLoop.Schedule(new Action(new $SocketConnectErrClosure(this, ex.Message, GetSocketErrorCode(ex)).Run))
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Callvirt, _types.Exception.GetProperty("Message")!.GetGetMethod()!);
        wil.Emit(OpCodes.Ldloc, exLocal);
        wil.Emit(OpCodes.Call, _getSocketErrorCodeMethod);
        wil.Emit(OpCodes.Newobj, _socketConnectErrClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketConnectErrClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notPipePrefix);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 9); // length of "\\.\pipe\"
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
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
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Call, typeof(StringComparer).GetProperty("OrdinalIgnoreCase")!.GetGetMethod()!);
        var containsWithComparer = typeof(System.Linq.Enumerable).GetMethods()
            .Single(m => m.Name == "Contains" && m.GetParameters().Length == 3)
            .MakeGenericMethod(_types.String);
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
    private void EmitNetSocketWriteBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketWriteMethod.GetILGenerator();

        // if (_destroyed || _stream == null) return false
        var okLabel = il.DefineLabel();
        var retFalseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
        il.Emit(OpCodes.Brtrue, retFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketStreamField);
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
        il.Emit(OpCodes.Ldfld, _netSocketEncodingField);
        il.Emit(OpCodes.Stloc, encLocal);
        il.MarkLabel(encDone);

        // byte[] bytes = Encoding.UTF8.GetBytes(data.ToString())
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // IPC: async write via ThreadPool to avoid blocking on InOut pipes
        var syncWrite = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
        il.Emit(OpCodes.Brfalse, syncWrite);

        // ThreadPool.QueueUserWorkItem(new WaitCallback(new $IpcWriteClosure(this, bytes).Run))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Newobj, _ipcWriteClosureCtor);
        il.Emit(OpCodes.Ldftn, _ipcWriteClosureRun);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(System.Threading.ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        // Invoke callbacks even for async write
        EmitNetSocketInvokeCallback(il, runtime, 3);
        EmitNetSocketInvokeCallback(il, runtime, 2);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(syncWrite);

        // try { _stream.Write(bytes, 0, bytes.Length); _bytesWritten += bytes.Length; } catch { }
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketStreamField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Write", [_types.ByteArray, _types.Int32, _types.Int32])!);

        // _bytesWritten += bytes.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketBytesWrittenField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _netSocketBytesWrittenField);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // Invoke callback if arg3 or arg2 is callable
        EmitNetSocketInvokeCallback(il, runtime, 3);
        EmitNetSocketInvokeCallback(il, runtime, 2);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: If arg at argIdx is TSFunction, invoke it with empty args.
    /// </summary>
    private void EmitNetSocketInvokeCallback(ILGenerator il, EmittedRuntime runtime, int argIdx)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, skip);

        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skip);
    }

    /// <summary>
    /// Emits body for: public object End(object dataOrCallback, object encodingOrCallback, object callback)
    /// </summary>
    private void EmitNetSocketEndBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketEndMethod.GetILGenerator();

        // if (_ended) return this
        var notEnded = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketEndedField);
        il.Emit(OpCodes.Brfalse, notEnded);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEnded);

        // Write final chunk if arg1 is not null and not callable
        var noFinalWrite = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noFinalWrite);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, noFinalWrite);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, noFinalWrite);

        // Write(arg1, null, null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.NetSocketWrite);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noFinalWrite);

        // _ended = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketEndedField);

        // try { _client?.Client?.Shutdown(SocketShutdown.Send) } catch { }
        il.BeginExceptionBlock();
        var noClient = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, noClient);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        var noSocket = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noSocket);
        il.Emit(OpCodes.Ldc_I4_1); // SocketShutdown.Send = 1
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Shutdown", [typeof(SocketShutdown)])!);
        var shutdownDone = il.DefineLabel();
        il.Emit(OpCodes.Br, shutdownDone);
        il.MarkLabel(noSocket);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(shutdownDone);
        il.MarkLabel(noClient);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public object Destroy(object error)
    /// Fix: After _stream.Close(), sets _stream = null.
    /// </summary>
    private void EmitNetSocketDestroyBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketDestroyMethod.GetILGenerator();

        // if (_destroyed) return this
        var notDestroyed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
        il.Emit(OpCodes.Brfalse, notDestroyed);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDestroyed);

        // _destroyed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketDestroyedField);

        // _readCts?.Cancel()
        il.BeginExceptionBlock();
        var noCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadCtsField);
        il.Emit(OpCodes.Brfalse, noCts);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCts);

        // _stream?.Close(); _stream = null
        var noStream = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketStreamField);
        il.Emit(OpCodes.Brfalse, noStream);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketStreamField);
        il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("Close")!);
        il.MarkLabel(noStream);
        // _stream = null (always, even if already null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _netSocketStreamField);

        // _client?.Close()
        var noClient2 = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, noClient2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
        il.Emit(OpCodes.Ldfld, _netSocketCloseEmittedField);
        il.Emit(OpCodes.Brtrue, closeAlreadyEmitted);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketCloseEmittedField);
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
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(closeAlreadyEmitted);

        // Unref event loop if reading was started
        var noUnref = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadingStartedField);
        il.Emit(OpCodes.Brfalse, noUnref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _netSocketReadingStartedField);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);
        il.MarkLabel(noUnref);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public void StartReading()
    /// Starts async read loop on ThreadPool, schedules 'data'/'end' events via closures.
    /// </summary>
    private void EmitNetSocketStartReadingBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketStartReadingMethod.GetILGenerator();

        // if (_destroyed || _stream == null) return
        var okLabel = il.DefineLabel();
        var retLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
        il.Emit(OpCodes.Brtrue, retLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketStreamField);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(okLabel);

        // Cancel previous read if any
        var noPrevCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadCtsField);
        il.Emit(OpCodes.Brfalse, noPrevCts);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.MarkLabel(noPrevCts);

        // _readCts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _netSocketReadCtsField);

        // _readReady = new ManualResetEventSlim(false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.ManualResetEventSlim).GetConstructor([_types.Boolean])!);
        il.Emit(OpCodes.Stfld, _netSocketReadReadyField);

        // if (!_readingStarted) { _readingStarted = true; EventLoop.Ref(); }
        var alreadyReading = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketReadingStartedField);
        il.Emit(OpCodes.Brtrue, alreadyReading);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netSocketReadingStartedField);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);
        il.MarkLabel(alreadyReading);

        // Emit the read worker as a private instance method
        var readWorker = EmitNetSocketReadWorker(typeBuilder, runtime);

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
    private MethodBuilder EmitNetSocketReadWorker(TypeBuilder typeBuilder, EmittedRuntime runtime)
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
        wil.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
        wil.Emit(OpCodes.Brfalse, skipReadySignal);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
        wil.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Set")!);
        wil.MarkLabel(skipReadySignal);

        // while loop
        var loopTop = wil.DefineLabel();
        var loopExit = wil.DefineLabel();

        wil.MarkLabel(loopTop);

        // Check _destroyed
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
        wil.Emit(OpCodes.Brtrue, loopExit);

        // Check _stream != null
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketStreamField);
        wil.Emit(OpCodes.Brfalse, loopExit);

        // try { bytesRead = _stream.Read(buffer, 0, 65536) } catch { break }
        wil.BeginExceptionBlock();
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketStreamField);
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
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Newobj, _socketReadEndClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketReadEndClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);
        wil.Emit(OpCodes.Br, loopExit);

        wil.MarkLabel(notZero);

        // _bytesRead += bytesRead
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldfld, _netSocketBytesReadField);
        wil.Emit(OpCodes.Ldloc, bytesReadLocal);
        wil.Emit(OpCodes.Add);
        wil.Emit(OpCodes.Stfld, _netSocketBytesReadField);

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
        wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        wil.Emit(OpCodes.Ldarg_0);
        wil.Emit(OpCodes.Ldloc, chunkLocal);
        wil.Emit(OpCodes.Newobj, _socketReadDataClosureCtor);
        wil.Emit(OpCodes.Ldftn, _socketReadDataClosureRun);
        wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        wil.Emit(OpCodes.Br, loopTop);

        wil.MarkLabel(loopExit);
        wil.Emit(OpCodes.Ret);

        return readWorker;
    }

    /// <summary>
    /// Emits body for: public object SetEncoding(object enc)
    /// </summary>
    private void EmitNetSocketSetEncodingBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketSetEncodingMethod.GetILGenerator();

        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stfld, _netSocketEncodingField);
        il.MarkLabel(notString);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public object GetMember(string name)
    /// Dispatches property/method access including new remoteFamily, localAddress, readyState.
    /// </summary>
    private void EmitNetSocketGetMemberBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netSocketGetMemberMethod.GetILGenerator();

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

        // Fall through to default
        il.Emit(OpCodes.Br, defaultLabel);

        // ── remoteAddress ──
        il.MarkLabel(remoteAddressLabel);
        {
            // if (_isIpc) return null
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetRemoteEndpointString(il, runtime, "Address");
        il.Emit(OpCodes.Ret);

        // ── remotePort ──
        il.MarkLabel(remotePortLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetRemoteEndpointPort(il, runtime);
        il.Emit(OpCodes.Ret);

        // ── remoteFamily ──
        il.MarkLabel(remoteFamilyLabel);
        {
            // if (_isIpc) return "pipe"
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldstr, "pipe");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
            // For TCP: check if IPv6 else IPv4
            EmitGetRemoteEndpointFamily(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // ── localAddress ──
        il.MarkLabel(localAddressLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetLocalEndpointString(il, runtime, "Address");
        il.Emit(OpCodes.Ret);

        // ── localPort ──
        il.MarkLabel(localPortLabel);
        {
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notIpc);
        }
        EmitGetLocalEndpointPort(il, runtime);
        il.Emit(OpCodes.Ret);

        // ── bytesRead ──
        il.MarkLabel(bytesReadLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketBytesReadField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── bytesWritten ──
        il.MarkLabel(bytesWrittenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketBytesWrittenField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // ── connecting ──
        il.MarkLabel(connectingLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketConnectingField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── destroyed ──
        il.MarkLabel(destroyedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // ── readyState ──
        // "opening" if _connecting, "closed" if _destroyed,
        // "open" if _stream != null (simplified for IPC), _client?.Connected for TCP
        il.MarkLabel(readyStateLabel);
        {
            var notConnecting = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketConnectingField);
            il.Emit(OpCodes.Brfalse, notConnecting);
            il.Emit(OpCodes.Ldstr, "opening");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notConnecting);
            var notDestroyed2 = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
            il.Emit(OpCodes.Brfalse, notDestroyed2);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notDestroyed2);

            // if (_isIpc) return _stream != null ? "open" : "closed"
            var notIpc = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, notIpc);

            var ipcStreamNull = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketStreamField);
            il.Emit(OpCodes.Brfalse, ipcStreamNull);
            il.Emit(OpCodes.Ldstr, "open");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(ipcStreamNull);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notIpc);

            // TCP: _client?.Connected ? "open" : "closed"
            var noClientRS = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketClientField);
            il.Emit(OpCodes.Brfalse, noClientRS);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _netSocketClientField);
            il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetProperty("Connected")!.GetGetMethod()!);
            var notConnectedRS = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notConnectedRS);
            il.Emit(OpCodes.Ldstr, "open");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notConnectedRS);
            il.MarkLabel(noClientRS);
            il.Emit(OpCodes.Ldstr, "closed");
            il.Emit(OpCodes.Ret);
        }

        // ── default — return undefined ──
        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
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
        il.Emit(OpCodes.Call, _types.String.GetMethod("Equals", [_types.String])!);
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
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
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
    private void EmitGetRemoteEndpointString(ILGenerator il, EmittedRuntime runtime, string property)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        // _client null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, done);

        // _client.Client null check
        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    private void EmitGetRemoteEndpointPort(ILGenerator il, EmittedRuntime runtime)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
    private void EmitGetRemoteEndpointFamily(ILGenerator il, EmittedRuntime runtime)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
    private void EmitGetLocalEndpointString(ILGenerator il, EmittedRuntime runtime, string property)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
    }

    private void EmitGetLocalEndpointPort(ILGenerator il, EmittedRuntime runtime)
    {
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
        il.Emit(OpCodes.Brfalse, done);

        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netSocketClientField);
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
