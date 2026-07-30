using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $NetServer class extending $EventEmitter for standalone TCP/IPC server support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSNetServer
///
/// Two-phase architecture:
///   Phase 1a (EmitTSNetServerPhase1): Defines type, fields, constructor (with body),
///       and method STUBS (no bodies). Closure types defined between phases reference
///       the MethodBuilders/FieldBuilders from this phase.
///   Phase 2 (EmitTSNetServerPhase2): Emits all method bodies + calls CreateType().
/// </summary>
public partial class RuntimeEmitter
{
    // Field builders for $NetServer
    private TypeBuilder _netServerTypeBuilder = null!;
    private FieldBuilder _netServerListenerField = null!;
    private FieldBuilder _netServerIsListeningField = null!;
    private FieldBuilder _netServerCtsField = null!;
    private FieldBuilder _netServerConnectionListenerField = null!;
    private FieldBuilder _netServerPortField = null!;
    private FieldBuilder _netServerHostField = null!;
    private FieldBuilder _netServerMaxConnectionsField = null!;
    private FieldBuilder _netServerConnectionsField = null!;
    private FieldBuilder _netServerIsIpcField = null!;
    private FieldBuilder _netServerPipePathField = null!;
    private FieldBuilder _netServerUnixSocketField = null!;
    private FieldBuilder _netServerPipeReadyField = null!;
    // createServer(options) settings applied to accepted sockets (#1068); -1 = unset
    private FieldBuilder _netServerSocketHwmField = null!;
    // createServer({blockList}) — a $BlockList checked per accepted connection (#1069)
    private FieldBuilder _netServerBlockListField = null!;
    // createServer({allowHalfOpen}) — applied to accepted sockets (#1070)
    private FieldBuilder _netServerSocketAllowHalfOpenField = null!;

    // Method builders (defined in Phase 1a, bodies emitted in Phase 2)
    private MethodBuilder _netServerListenMethod = null!;
    private MethodBuilder _netServerCloseMethod = null!;
    private MethodBuilder _netServerAddressMethod = null!;
    private MethodBuilder _netServerGetConnectionsMethod = null!;
    private MethodBuilder _netServerGetMemberMethod = null!;
    private MethodBuilder _netServerSetMemberMethod = null!;

    // Closure constructors/run methods (set by the closure emitter between phases)
    internal ConstructorBuilder _tcpAcceptClosureCtor = null!;
    internal MethodBuilder _tcpAcceptClosureRun = null!;
    internal ConstructorBuilder _ipcAcceptClosureCtor = null!;
    internal MethodBuilder _ipcAcceptClosureRun = null!;

    /// <summary>
    /// Phase 1a: Defines the $NetServer type, fields, constructor (with body),
    /// and method STUBS (no bodies). Must be called BEFORE closure types are defined
    /// and BEFORE EmitRuntimeClass so NetCreateServer can use the constructor.
    /// </summary>
    private void EmitTSNetServerPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$NetServer",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        _netServerTypeBuilder = typeBuilder;
        _ = typeBuilder;

        // ── Fields ──
        _netServerListenerField = typeBuilder.DefineField("_listener", typeof(TcpListener), FieldAttributes.Private);
        _netServerIsListeningField = typeBuilder.DefineField("_isListening", _types.Boolean, FieldAttributes.Private);
        _ = _netServerIsListeningField;
        _netServerCtsField = typeBuilder.DefineField("_cts", typeof(CancellationTokenSource), FieldAttributes.Private);
        _netServerConnectionListenerField = typeBuilder.DefineField("_connectionListener", _types.Object, FieldAttributes.Assembly);
        _netServerPortField = typeBuilder.DefineField("_port", _types.Int32, FieldAttributes.Private);
        _netServerHostField = typeBuilder.DefineField("_host", _types.String, FieldAttributes.Private);
        // Assembly: the TCP accept closure enforces maxConnections + 'drop' (#1070)
        _netServerMaxConnectionsField = typeBuilder.DefineField("_maxConnections", _types.Int32, FieldAttributes.Assembly);
        _netServerConnectionsField = typeBuilder.DefineField("_connections", _types.ListOfObject, FieldAttributes.Assembly);
        _netServerIsIpcField = typeBuilder.DefineField("_isIpc", _types.Boolean, FieldAttributes.Private);
        _netServerPipePathField = typeBuilder.DefineField("_pipePath", _types.String, FieldAttributes.Private);
        _netServerUnixSocketField = typeBuilder.DefineField("_unixSocket", typeof(Socket), FieldAttributes.Private);
        _netServerPipeReadyField = typeBuilder.DefineField("_pipeReady", typeof(System.Threading.ManualResetEventSlim), FieldAttributes.Private);
        _netServerSocketHwmField = typeBuilder.DefineField("_socketHwm", _types.Int32, FieldAttributes.Assembly);
        _netServerBlockListField = typeBuilder.DefineField("_blockList", _types.Object, FieldAttributes.Assembly);
        _netServerSocketAllowHalfOpenField = typeBuilder.DefineField("_socketAllowHalfOpen", _types.Boolean, FieldAttributes.Assembly);

        // ── Constructor (with body) ──
        EmitNetServerCtor(typeBuilder, runtime);

        // ── Method stubs (no bodies — emitted in Phase 2) ──

        _netServerListenMethod = typeBuilder.DefineMethod(
            "Listen",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );
        _ = _netServerListenMethod;

        _netServerCloseMethod = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _ = _netServerCloseMethod;

        _netServerAddressMethod = typeBuilder.DefineMethod(
            "Address",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );
        _ = _netServerAddressMethod;

        _netServerGetConnectionsMethod = typeBuilder.DefineMethod(
            "GetConnections",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _ = _netServerGetConnectionsMethod;

        _netServerGetMemberMethod = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        _ = _netServerGetMemberMethod;

        _netServerSetMemberMethod = typeBuilder.DefineMethod(
            "SetMember",
            MethodAttributes.Public,
            typeof(void),
            [_types.String, _types.Object]
        );
        _ = _netServerSetMemberMethod;

        // NOTE: CreateType() is deferred to Phase 2
    }

    /// <summary>
    /// Phase 2: Emits all method bodies and finalizes the $NetServer type.
    /// Called after closure types have been defined between phases.
    /// </summary>
    private void EmitTSNetServerPhase2(EmittedRuntime runtime)
    {
        var typeBuilder = _netServerTypeBuilder;

        // Emit method bodies
        EmitNetServerListenBody(typeBuilder, runtime);
        EmitNetServerCloseBody(typeBuilder, runtime);
        EmitNetServerAddressBody(typeBuilder, runtime);
        EmitNetServerGetConnectionsBody(typeBuilder, runtime);
        EmitNetServerGetMemberBody(typeBuilder, runtime);
        EmitNetServerSetMemberBody(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    // ════════════════════════════════════════════════════════════════
    //  Constructor (body emitted in Phase 1a)
    // ════════════════════════════════════════════════════════════════

    private void EmitNetServerCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.NetServerCtor = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);
        // _connectionListener = callback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _netServerConnectionListenerField);
        // _host = "0.0.0.0"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Stfld, _netServerHostField);
        // _maxConnections = int.MaxValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stfld, _netServerMaxConnectionsField);
        // _connections = new List<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stfld, _netServerConnectionsField);
        // _socketHwm = -1 (unset — accepted sockets keep the 16 KiB default)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, _netServerSocketHwmField);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Method bodies (emitted in Phase 2)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Listen(object portOrOptions, object hostOrCallback, object backlogOrCallback, object callback)
    /// </summary>
    private void EmitNetServerListenBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerListenMethod.GetILGenerator();

        // if (_isListening) throw
        var notListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsListeningField);
        il.Emit(OpCodes.Brfalse, notListening);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Server is already listening");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notListening);

        // Parse args: find port/path and callback
        var callbackLocal = il.DeclareLocal(_types.Object);    // local 0
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        var ipcPathLocal = il.DeclareLocal(_types.String);     // local 1
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, ipcPathLocal);

        // ── Check if arg1 is string (IPC path) ──
        var notString = il.DefineLabel();
        var parseDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);

        // ipcPath = (string)arg1
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, ipcPathLocal);

        // Find callback in remaining args
        EmitFindCallback(il, runtime, 2, callbackLocal);
        EmitFindCallback(il, runtime, 3, callbackLocal);
        EmitFindCallback(il, runtime, 4, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        il.MarkLabel(notString);

        // ── Check if arg1 is double (port) ──
        var notDouble = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDouble);

        // port = (int)(double)arg1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, _netServerPortField);

        // Check arg2: string (host) or callable (callback)
        var arg2NotString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, arg2NotString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stfld, _netServerHostField);
        // Find callback in remaining args
        EmitFindCallback(il, runtime, 3, callbackLocal);
        EmitFindCallback(il, runtime, 4, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        il.MarkLabel(arg2NotString);
        // arg2 might be callback
        EmitFindCallback(il, runtime, 2, callbackLocal);
        EmitFindCallback(il, runtime, 3, callbackLocal);
        EmitFindCallback(il, runtime, 4, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        // ── arg1 is dict (options) ──
        il.MarkLabel(notDouble);
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDict);

        // Extract path from dict["path"] first
        EmitDictTryGetString(il, 1, "path", ipcPathLocal);
        // Extract port from dict["port"]
        EmitDictExtractPort(il, 1, _netServerPortField);
        // Extract host from dict["host"]
        var hostLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerHostField);
        il.Emit(OpCodes.Stloc, hostLocal);
        EmitDictTryGetString(il, 1, "host", hostLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stfld, _netServerHostField);
        // arg2 is callback
        EmitFindCallback(il, runtime, 2, callbackLocal);
        il.Emit(OpCodes.Br, parseDone);

        il.MarkLabel(notDict);
        // Could be calling listen() with no args or listen(callback)
        EmitFindCallback(il, runtime, 1, callbackLocal);
        EmitFindCallback(il, runtime, 2, callbackLocal);

        il.MarkLabel(parseDone);

        // ── Check if IPC path was detected ──
        var notIpc = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Brfalse, notIpc);

        // Set _isIpc = true, _pipePath = path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netServerIsIpcField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, _netServerPipePathField);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _netServerCtsField);

        // Branch on OS: Windows vs Unix
        var isWindows = il.DefineLabel();
        var ipcListenDone = il.DefineLabel();

        il.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod("IsWindows")!);
        il.Emit(OpCodes.Brtrue, isWindows);

        // ── Unix IPC: create and bind Unix domain socket ──

        // Delete stale socket file
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Exists", [_types.String])!);
        var noStaleFile = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noStaleFile);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Delete", [_types.String])!);
        il.MarkLabel(noStaleFile);

        // _unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.Unix);
        il.Emit(OpCodes.Ldc_I4, (int)SocketType.Stream);
        il.Emit(OpCodes.Ldc_I4, (int)ProtocolType.Unspecified);
        il.Emit(OpCodes.Newobj, typeof(Socket).GetConstructor([typeof(AddressFamily), typeof(SocketType), typeof(ProtocolType)])!);
        il.Emit(OpCodes.Stfld, _netServerUnixSocketField);

        // _unixSocket.Bind(new UnixDomainSocketEndPoint(path))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Newobj, typeof(UnixDomainSocketEndPoint).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Bind", [typeof(EndPoint)])!);

        // _unixSocket.Listen(511)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
        il.Emit(OpCodes.Ldc_I4, 511);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Listen", [_types.Int32])!);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netServerIsListeningField);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Start Unix IPC accept worker on ThreadPool BEFORE callback
        // (callback may trigger client connect; accept worker must be ready)
        EmitIpcAcceptWorkerUnixStart(il, runtime);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Call callback if provided
        var noUnixCb = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noUnixCb);
        EmitDgramCallbackInvocation(il, runtime,
            () => il.Emit(OpCodes.Ldloc, callbackLocal), 0);
        il.MarkLabel(noUnixCb);

        il.Emit(OpCodes.Br, ipcListenDone);

        // ── Windows IPC ──
        il.MarkLabel(isWindows);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netServerIsListeningField);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // _pipeReady = new ManualResetEventSlim(false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.ManualResetEventSlim).GetConstructor([_types.Boolean])!);
        il.Emit(OpCodes.Stfld, _netServerPipeReadyField);

        // Start Windows IPC accept worker on ThreadPool
        EmitIpcAcceptWorkerWindowsStart(il, runtime);

        // _pipeReady.Wait(5000) — block until first pipe is listening
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPipeReadyField);
        il.Emit(OpCodes.Ldc_I4, 5000);
        il.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Wait", [_types.Int32])!);
        il.Emit(OpCodes.Pop); // Wait(int) returns bool

        // Call callback if provided (after worker queued so pipe may be ready)
        var noWinCb = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noWinCb);
        EmitDgramCallbackInvocation(il, runtime,
            () => il.Emit(OpCodes.Ldloc, callbackLocal), 0);
        il.MarkLabel(noWinCb);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(ipcListenDone);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // ── TCP path (not IPC) ──
        il.MarkLabel(notIpc);

        // Create TcpListener and start
        // IPAddress ipAddr = _host == "0.0.0.0" || _host == "::" ? IPAddress.Any : IPAddress.Loopback
        var ipAddrLocal = il.DeclareLocal(typeof(IPAddress));
        var notAny = il.DefineLabel();
        var ipDone = il.DefineLabel();
        var anyLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerHostField);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, anyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerHostField);
        il.Emit(OpCodes.Ldstr, "::");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, anyLabel);
        il.Emit(OpCodes.Br, notAny);

        il.MarkLabel(anyLabel);
        il.Emit(OpCodes.Ldsfld, typeof(IPAddress).GetField("Any")!);
        il.Emit(OpCodes.Stloc, ipAddrLocal);
        il.Emit(OpCodes.Br, ipDone);

        il.MarkLabel(notAny);
        // Try parse, fallback to Loopback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerHostField);
        il.Emit(OpCodes.Ldloca, ipAddrLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, ipDone);
        il.Emit(OpCodes.Ldsfld, typeof(IPAddress).GetField("Loopback")!);
        il.Emit(OpCodes.Stloc, ipAddrLocal);

        il.MarkLabel(ipDone);

        // _listener = new TcpListener(ipAddr, _port)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipAddrLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPortField);
        il.Emit(OpCodes.Newobj, typeof(TcpListener).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Stfld, _netServerListenerField);

        // try { _listener.Start() } catch { return this }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerListenerField);
        il.Emit(OpCodes.Callvirt, typeof(TcpListener).GetMethod("Start", Type.EmptyTypes)!);

        var startOk = il.DefineLabel();
        il.Emit(OpCodes.Leave, startOk);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, startOk);
        il.EndExceptionBlock();

        il.MarkLabel(startOk);

        // Update port if it was 0 (auto-assigned)
        var portNotZero = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPortField);
        il.Emit(OpCodes.Brtrue, portNotZero);

        // _port = ((IPEndPoint)_listener.LocalEndpoint).Port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerListenerField);
        il.Emit(OpCodes.Callvirt, typeof(TcpListener).GetProperty("LocalEndpoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(IPEndPoint));
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, _netServerPortField);

        il.MarkLabel(portNotZero);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _netServerIsListeningField);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _netServerCtsField);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Call callback if provided
        var noTcpCb = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noTcpCb);
        EmitDgramCallbackInvocation(il, runtime,
            () => il.Emit(OpCodes.Ldloc, callbackLocal), 0);
        il.MarkLabel(noTcpCb);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Start TCP accept worker on ThreadPool using $TcpAcceptClosure
        EmitTcpAcceptWorkerStart(il, runtime);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: ThreadPool.QueueUserWorkItem(_ => new $TcpAcceptClosure(this, _listener.AcceptTcpClient()).Run())
    /// The closure accept loop is in $TcpAcceptClosure.Run(), which checks _isListening,
    /// calls AcceptTcpClient, creates $NetSocket, schedules connection handling via EventLoop.
    /// </summary>
    private void EmitTcpAcceptWorkerStart(ILGenerator callerIl, EmittedRuntime runtime)
    {
        // Emit a private _TcpAcceptWorker(object state) method that creates and runs the closure
        var acceptWorker = _netServerTypeBuilder.DefineMethod(
            "_TcpAcceptWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        {
            var wil = acceptWorker.GetILGenerator();
            var clientLocal = wil.DeclareLocal(typeof(TcpClient));

            var loopTop = wil.DefineLabel();
            var loopExit = wil.DefineLabel();

            wil.MarkLabel(loopTop);

            // Check _isListening
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerIsListeningField);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // try { client = _listener.AcceptTcpClient() } catch { break }
            wil.BeginExceptionBlock();
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerListenerField);
            wil.Emit(OpCodes.Callvirt, typeof(TcpListener).GetMethod("AcceptTcpClient")!);
            wil.Emit(OpCodes.Stloc, clientLocal);

            var afterAccept = wil.DefineLabel();
            wil.Emit(OpCodes.Leave, afterAccept);

            wil.BeginCatchBlock(_types.Exception);
            wil.Emit(OpCodes.Pop);
            wil.Emit(OpCodes.Leave, loopExit);
            wil.EndExceptionBlock();

            wil.MarkLabel(afterAccept);

            // Schedule: EventLoop.Schedule(new $TcpAcceptClosure(this, client).Run)
            wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, clientLocal);
            wil.Emit(OpCodes.Newobj, _tcpAcceptClosureCtor);
            wil.Emit(OpCodes.Ldftn, _tcpAcceptClosureRun);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

            wil.Emit(OpCodes.Br, loopTop);

            wil.MarkLabel(loopExit);
            wil.Emit(OpCodes.Ret);
        }

        // In caller: ThreadPool.QueueUserWorkItem(new WaitCallback(this._TcpAcceptWorker))
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Ldftn, acceptWorker);
        callerIl.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        callerIl.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        callerIl.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits the Unix IPC accept worker start: ThreadPool.QueueUserWorkItem on a blocking Socket.Accept loop.
    /// </summary>
    private void EmitIpcAcceptWorkerUnixStart(ILGenerator callerIl, EmittedRuntime runtime)
    {
        var acceptWorker = _netServerTypeBuilder.DefineMethod(
            "_IpcAcceptWorkerUnix",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        {
            var wil = acceptWorker.GetILGenerator();
            var clientSocketLocal = wil.DeclareLocal(typeof(Socket));
            var streamLocal = wil.DeclareLocal(typeof(NetworkStream));

            var loopTop = wil.DefineLabel();
            var loopExit = wil.DefineLabel();

            wil.MarkLabel(loopTop);

            // Check _isListening
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerIsListeningField);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // Check _unixSocket != null
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // try { clientSocket = _unixSocket.AcceptAsync().GetAwaiter().GetResult() } catch { break }
            // Use async path — synchronous Socket.Accept may hang on macOS for Unix domain sockets
            wil.BeginExceptionBlock();
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
            wil.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("AcceptAsync", Type.EmptyTypes)!);
            wil.Emit(OpCodes.Callvirt, typeof(Task<Socket>).GetMethod("GetAwaiter")!);
            var acceptAwaiterLocal = wil.DeclareLocal(typeof(TaskAwaiter<Socket>));
            wil.Emit(OpCodes.Stloc, acceptAwaiterLocal);
            wil.Emit(OpCodes.Ldloca, acceptAwaiterLocal);
            wil.Emit(OpCodes.Call, typeof(TaskAwaiter<Socket>).GetMethod("GetResult")!);
            wil.Emit(OpCodes.Stloc, clientSocketLocal);

            var afterAccept = wil.DefineLabel();
            wil.Emit(OpCodes.Leave, afterAccept);

            wil.BeginCatchBlock(_types.Exception);
            wil.Emit(OpCodes.Pop);
            wil.Emit(OpCodes.Leave, loopExit);
            wil.EndExceptionBlock();

            wil.MarkLabel(afterAccept);

            // stream = new NetworkStream(clientSocket, ownsSocket: true)
            wil.Emit(OpCodes.Ldloc, clientSocketLocal);
            wil.Emit(OpCodes.Ldc_I4_1); // ownsSocket = true
            wil.Emit(OpCodes.Newobj, typeof(NetworkStream).GetConstructor([typeof(Socket), _types.Boolean])!);
            wil.Emit(OpCodes.Stloc, streamLocal);

            // Schedule: EventLoop.Schedule(new $IpcAcceptClosure(this, stream, pipePath).Run)
            wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, streamLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerPipePathField);
            wil.Emit(OpCodes.Newobj, _ipcAcceptClosureCtor);
            wil.Emit(OpCodes.Ldftn, _ipcAcceptClosureRun);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

            wil.Emit(OpCodes.Br, loopTop);

            wil.MarkLabel(loopExit);
            wil.Emit(OpCodes.Ret);
        }

        // In caller: ThreadPool.QueueUserWorkItem(new WaitCallback(this._IpcAcceptWorkerUnix))
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Ldftn, acceptWorker);
        callerIl.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        callerIl.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        callerIl.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits the Windows IPC accept worker start: ThreadPool.QueueUserWorkItem on a blocking
    /// NamedPipeServerStream.WaitForConnection loop.
    /// </summary>
    private void EmitIpcAcceptWorkerWindowsStart(ILGenerator callerIl, EmittedRuntime runtime)
    {
        var acceptWorker = _netServerTypeBuilder.DefineMethod(
            "_IpcAcceptWorkerWindows",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        {
            var wil = acceptWorker.GetILGenerator();
            var pipeNameLocal = wil.DeclareLocal(_types.String);      // local 0
            var pipeLocal = wil.DeclareLocal(typeof(NamedPipeServerStream)); // local 1
            var tokenLocal = wil.DeclareLocal(typeof(CancellationToken));    // local 2
            var taskLocal = wil.DeclareLocal(typeof(System.Threading.Tasks.Task)); // local 3
            var awaiterLocal = wil.DeclareLocal(typeof(System.Runtime.CompilerServices.TaskAwaiter)); // local 4
            var firstLocal = wil.DeclareLocal(_types.Boolean);        // local 5

            var loopTop = wil.DefineLabel();
            var loopExit = wil.DefineLabel();

            // Compute pipe name: inline the ConvertToWindowsPipeName logic
            // if path starts with "\\.\pipe\", strip prefix; else use Path.GetFileName()
            var pathLocal = wil.DeclareLocal(_types.String);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerPipePathField);
            wil.Emit(OpCodes.Stloc, pathLocal);

            var notPipePrefix = wil.DefineLabel();
            var pipeNameDone = wil.DefineLabel();

            wil.Emit(OpCodes.Ldloc, pathLocal);
            wil.Emit(OpCodes.Ldstr, @"\\.\pipe\");
            wil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
            wil.Emit(OpCodes.Brfalse, notPipePrefix);

            // Strip prefix: path.Substring(9)
            wil.Emit(OpCodes.Ldloc, pathLocal);
            wil.Emit(OpCodes.Ldc_I4, 9);
            wil.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
            wil.Emit(OpCodes.Stloc, pipeNameLocal);
            wil.Emit(OpCodes.Br, pipeNameDone);

            wil.MarkLabel(notPipePrefix);
            // Use Path.GetFileName(path)
            wil.Emit(OpCodes.Ldloc, pathLocal);
            wil.Emit(OpCodes.Call, typeof(System.IO.Path).GetMethod("GetFileName", [_types.String])!);
            wil.Emit(OpCodes.Stloc, pipeNameLocal);

            wil.MarkLabel(pipeNameDone);

            // Load cancellation token once before the loop
            // token = _cts.Token
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerCtsField);
            wil.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetProperty("Token")!.GetGetMethod()!);
            wil.Emit(OpCodes.Stloc, tokenLocal);

            // first = true
            wil.Emit(OpCodes.Ldc_I4_1);
            wil.Emit(OpCodes.Stloc, firstLocal);

            wil.MarkLabel(loopTop);

            // Check _isListening
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerIsListeningField);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // try {
            //   pipe = new NamedPipeServerStream(pipeName, InOut, MaxAllowed, Byte, Async)
            //   task = pipe.WaitForConnectionAsync(token)
            //   if (first) { first = false; _pipeReady.Set(); }
            //   task.GetAwaiter().GetResult()
            // } catch { break }
            wil.BeginExceptionBlock();

            // new NamedPipeServerStream(pipeName, InOut, MaxAllowed, Byte, Async)
            wil.Emit(OpCodes.Ldloc, pipeNameLocal);
            wil.Emit(OpCodes.Ldc_I4, (int)PipeDirection.InOut);
            wil.Emit(OpCodes.Ldc_I4, NamedPipeServerStream.MaxAllowedServerInstances);
            wil.Emit(OpCodes.Ldc_I4, (int)PipeTransmissionMode.Byte);
            wil.Emit(OpCodes.Ldc_I4, (int)PipeOptions.Asynchronous);
            wil.Emit(OpCodes.Newobj, typeof(NamedPipeServerStream).GetConstructor([
                _types.String, typeof(PipeDirection), _types.Int32,
                typeof(PipeTransmissionMode), typeof(PipeOptions)
            ])!);
            wil.Emit(OpCodes.Stloc, pipeLocal);

            // task = pipe.WaitForConnectionAsync(token)
            wil.Emit(OpCodes.Ldloc, pipeLocal);
            wil.Emit(OpCodes.Ldloc, tokenLocal);
            wil.Emit(OpCodes.Callvirt, typeof(NamedPipeServerStream).GetMethod("WaitForConnectionAsync", [typeof(CancellationToken)])!);
            wil.Emit(OpCodes.Stloc, taskLocal);

            // if (first) { first = false; _pipeReady.Set(); }
            var skipSignal = wil.DefineLabel();
            wil.Emit(OpCodes.Ldloc, firstLocal);
            wil.Emit(OpCodes.Brfalse, skipSignal);
            wil.Emit(OpCodes.Ldc_I4_0);
            wil.Emit(OpCodes.Stloc, firstLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerPipeReadyField);
            wil.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Set")!);
            wil.MarkLabel(skipSignal);

            // task.GetAwaiter().GetResult() — blocks until connection, but cancellable
            wil.Emit(OpCodes.Ldloc, taskLocal);
            wil.Emit(OpCodes.Callvirt, typeof(System.Threading.Tasks.Task).GetMethod("GetAwaiter")!);
            wil.Emit(OpCodes.Stloc, awaiterLocal);
            wil.Emit(OpCodes.Ldloca, awaiterLocal);
            wil.Emit(OpCodes.Call, typeof(System.Runtime.CompilerServices.TaskAwaiter).GetMethod("GetResult")!);

            var afterAccept = wil.DefineLabel();
            wil.Emit(OpCodes.Leave, afterAccept);

            wil.BeginCatchBlock(_types.Exception);
            wil.Emit(OpCodes.Pop);
            wil.Emit(OpCodes.Leave, loopExit);
            wil.EndExceptionBlock();

            wil.MarkLabel(afterAccept);

            // Schedule: EventLoop.Schedule(new $IpcAcceptClosure(this, pipe, pipePath).Run)
            wil.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, pipeLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, _netServerPipePathField);
            wil.Emit(OpCodes.Newobj, _ipcAcceptClosureCtor);
            wil.Emit(OpCodes.Ldftn, _ipcAcceptClosureRun);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoopSchedule);

            wil.Emit(OpCodes.Br, loopTop);

            wil.MarkLabel(loopExit);
            wil.Emit(OpCodes.Ret);
        }

        // In caller: ThreadPool.QueueUserWorkItem(new WaitCallback(this._IpcAcceptWorkerWindows))
        callerIl.Emit(OpCodes.Ldarg_0);
        callerIl.Emit(OpCodes.Ldftn, acceptWorker);
        callerIl.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        callerIl.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        callerIl.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Helper: If arg at argIdx is TSFunction or BoundTSFunction, store in target local (if target is null).
    /// </summary>
    private void EmitFindCallback(ILGenerator il, EmittedRuntime runtime, int argIdx, LocalBuilder target)
    {
        var skip = il.DefineLabel();
        // Skip if already found
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Brtrue, skip);

        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Brfalse, skip);

        // Check TSFunction
        var notTs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTs);
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Br, skip);

        il.MarkLabel(notTs);
        // Check BoundTSFunction
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Stloc, target);

        il.MarkLabel(skip);
    }

    /// <summary>
    /// Helper: Extract port from dict at argIdx, store in field.
    /// </summary>
    private void EmitDictExtractPort(ILGenerator il, int argIdx, FieldBuilder portField)
    {
        var skip = il.DefineLabel();
        var valLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldloca, valLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, skip);

        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, skip);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, portField);

        il.MarkLabel(skip);
    }

    // ════════════════════════════════════════════════════════════════
    //  Close
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Close(object callback)
    /// </summary>
    private void EmitNetServerCloseBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerCloseMethod.GetILGenerator();

        // if (!_isListening) return this
        var isListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsListeningField);
        il.Emit(OpCodes.Brtrue, isListening);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListening);

        // _cts?.Cancel()
        il.BeginExceptionBlock();
        var noCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerCtsField);
        il.Emit(OpCodes.Brfalse, noCts);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCts);

        // Branch: IPC vs TCP cleanup
        var notIpcClose = il.DefineLabel();
        var cleanupDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsIpcField);
        il.Emit(OpCodes.Brfalse, notIpcClose);

        // ── IPC cleanup ──
        // Close _unixSocket if set
        var noUnixSock = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
        il.Emit(OpCodes.Brfalse, noUnixSock);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerUnixSocketField);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Close", Type.EmptyTypes)!);
        var unixCloseOk = il.DefineLabel();
        il.Emit(OpCodes.Leave, unixCloseOk);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, unixCloseOk);
        il.EndExceptionBlock();
        il.MarkLabel(unixCloseOk);

        il.MarkLabel(noUnixSock);

        // Delete Unix socket file on non-Windows
        il.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod("IsWindows")!);
        var isWinCleanup = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, isWinCleanup);

        // if (_pipePath != null && File.Exists(_pipePath)) File.Delete(_pipePath)
        var noPipePath = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPipePathField);
        il.Emit(OpCodes.Brfalse, noPipePath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPipePathField);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Exists", [_types.String])!);
        il.Emit(OpCodes.Brfalse, noPipePath);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPipePathField);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Delete", [_types.String])!);
        var deleteOk = il.DefineLabel();
        il.Emit(OpCodes.Leave, deleteOk);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, deleteOk);
        il.EndExceptionBlock();
        il.MarkLabel(deleteOk);

        il.MarkLabel(noPipePath);
        il.MarkLabel(isWinCleanup);

        il.Emit(OpCodes.Br, cleanupDone);

        // ── TCP cleanup ──
        il.MarkLabel(notIpcClose);

        // _listener?.Stop()
        var noListener = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerListenerField);
        il.Emit(OpCodes.Brfalse, noListener);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerListenerField);
        il.Emit(OpCodes.Callvirt, typeof(TcpListener).GetMethod("Stop")!);
        il.MarkLabel(noListener);

        il.MarkLabel(cleanupDone);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // _isListening = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _netServerIsListeningField);

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        // Call callback
        var noCb = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCb);
        EmitDgramCallbackInvocation(il, runtime,
            () => il.Emit(OpCodes.Ldarg_1), 0);
        il.MarkLabel(noCb);

        // Emit 'close' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Address
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Address()
    /// </summary>
    private void EmitNetServerAddressBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerAddressMethod.GetILGenerator();

        // if (!_isListening) return null
        var isListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsListeningField);
        il.Emit(OpCodes.Brtrue, isListening);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListening);

        // if (_isIpc) return _pipePath (Node.js returns the pipe path as a string)
        var notIpcAddr = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsIpcField);
        il.Emit(OpCodes.Brfalse, notIpcAddr);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPipePathField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notIpcAddr);

        // Return { address, family, port }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerHostField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerPortField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  GetConnections
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object GetConnections(object callback)
    /// </summary>
    private void EmitNetServerGetConnectionsBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerGetConnectionsMethod.GetILGenerator();

        // if (callback is TSFunction) callback.Invoke([null, connections.Count])
        var noCb = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, noCb);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerConnectionsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCb);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  GetMember / SetMember
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object GetMember(string name)
    /// </summary>
    private void EmitNetServerGetMemberBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerGetMemberMethod.GetILGenerator();

        var listeningLabel = il.DefineLabel();
        var maxConnLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "listening", listeningLabel);
        EmitStringCheck(il, 1, "maxConnections", maxConnLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(listeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerIsListeningField);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(maxConnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _netServerMaxConnectionsField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public void SetMember(string name, object value)
    /// </summary>
    private void EmitNetServerSetMemberBody(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var il = _netServerSetMemberMethod.GetILGenerator();

        var maxConnLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "maxConnections", maxConnLabel);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(maxConnLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, endLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, _netServerMaxConnectionsField);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
