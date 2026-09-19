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
    /// <summary>
    /// Phase 1a: Defines the $NetServer type, fields, constructor (with body),
    /// and method STUBS (no bodies). Must be called BEFORE closure types are defined
    /// and BEFORE EmitRuntimeClass so NetCreateServer can use the constructor.
    /// </summary>
    private NetServerConstruction EmitTSNetServerPhase1(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$NetServer",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.EventEmitter.Type
        );
        runtime.RequireNet().ServerType = typeBuilder;
        _ = typeBuilder;

        // ── Fields ──
        var listenerField = typeBuilder.DefineField("_listener", typeof(TcpListener), FieldAttributes.Private);
        var isListeningField = typeBuilder.DefineField("_isListening", _types.Boolean, FieldAttributes.Private);
        var ctsField = typeBuilder.DefineField("_cts", typeof(CancellationTokenSource), FieldAttributes.Private);
        var connectionListenerField = typeBuilder.DefineField("_connectionListener", _types.Object, FieldAttributes.Assembly);
        var portField = typeBuilder.DefineField("_port", _types.Int32, FieldAttributes.Private);
        var hostField = typeBuilder.DefineField("_host", _types.String, FieldAttributes.Private);
        // Assembly: the TCP accept closure enforces maxConnections + 'drop' (#1070)
        var maxConnectionsField = typeBuilder.DefineField("_maxConnections", _types.Int32, FieldAttributes.Assembly);
        var connectionsField = typeBuilder.DefineField("_connections", _types.ListOfObject, FieldAttributes.Assembly);
        var isIpcField = typeBuilder.DefineField("_isIpc", _types.Boolean, FieldAttributes.Private);
        var pipePathField = typeBuilder.DefineField("_pipePath", _types.String, FieldAttributes.Private);
        var unixSocketField = typeBuilder.DefineField("_unixSocket", typeof(Socket), FieldAttributes.Private);
        var pipeReadyField = typeBuilder.DefineField("_pipeReady", typeof(System.Threading.ManualResetEventSlim), FieldAttributes.Private);
        var socketHwmField = typeBuilder.DefineField("_socketHwm", _types.Int32, FieldAttributes.Assembly);
        var blockListField = typeBuilder.DefineField("_blockList", _types.Object, FieldAttributes.Assembly);
        var socketAllowHalfOpenField = typeBuilder.DefineField("_socketAllowHalfOpen", _types.Boolean, FieldAttributes.Assembly);

        // ── Constructor (with body) ──
        var serverFields = new NetServerFields(
            listenerField,
            isListeningField,
            ctsField,
            connectionListenerField,
            portField,
            hostField,
            maxConnectionsField,
            connectionsField,
            isIpcField,
            pipePathField,
            unixSocketField,
            pipeReadyField,
            socketHwmField,
            blockListField,
            socketAllowHalfOpenField);
        EmitNetServerCtor(typeBuilder, runtime, serverFields);

        // ── Method stubs (no bodies — emitted in Phase 2) ──

        var listenMethod = typeBuilder.DefineMethod(
            "Listen",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );

        var closeMethod = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var addressMethod = typeBuilder.DefineMethod(
            "Address",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var getConnectionsMethod = typeBuilder.DefineMethod(
            "GetConnections",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var getMemberMethod = typeBuilder.DefineMethod(
            "GetMember",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );

        var setMemberMethod = typeBuilder.DefineMethod(
            "SetMember",
            MethodAttributes.Public,
            typeof(void),
            [_types.String, _types.Object]
        );

        // NOTE: CreateType() is deferred to Phase 2
        return new(serverFields, new NetServerMethods(
                listenMethod,
                closeMethod,
                addressMethod,
                getConnectionsMethod,
                getMemberMethod,
                setMemberMethod));
    }

    /// <summary>
    /// Phase 2: Emits all method bodies and finalizes the $NetServer type.
    /// Called after closure types have been defined between phases.
    /// </summary>
    private void EmitTSNetServerPhase2(
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods,
        NetServerClosures serverClosures)
    {
        var typeBuilder = runtime.RequireNet().ServerType;

        // Emit method bodies
        EmitNetServerListenBody(typeBuilder, runtime, serverFields, serverMethods, serverClosures);
        EmitNetServerCloseBody(typeBuilder, runtime, serverFields, serverMethods);
        EmitNetServerAddressBody(typeBuilder, runtime, serverFields, serverMethods);
        EmitNetServerGetConnectionsBody(typeBuilder, runtime, serverFields, serverMethods);
        EmitNetServerGetMemberBody(typeBuilder, runtime, serverFields, serverMethods);
        EmitNetServerSetMemberBody(typeBuilder, runtime, serverFields, serverMethods);

        typeBuilder.CreateType();
    }

    // ════════════════════════════════════════════════════════════════
    //  Constructor (body emitted in Phase 1a)
    // ════════════════════════════════════════════════════════════════

    private void EmitNetServerCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, NetServerFields serverFields)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.RequireNet().ServerCtor = ctor;

        var il = ctor.GetILGenerator();
        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Ctor);
        // _connectionListener = callback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, serverFields.ConnectionListener);
        // _host = "0.0.0.0"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Stfld, serverFields.Host);
        // _maxConnections = int.MaxValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stfld, serverFields.MaxConnections);
        // _connections = new List<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stfld, serverFields.Connections);
        // _socketHwm = -1 (unset — accepted sockets keep the 16 KiB default)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, serverFields.SocketHwm);
        il.Emit(OpCodes.Ret);
    }

    // ════════════════════════════════════════════════════════════════
    //  Method bodies (emitted in Phase 2)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emits body for: public object Listen(object portOrOptions, object hostOrCallback, object backlogOrCallback, object callback)
    /// </summary>
    private void EmitNetServerListenBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods,
        NetServerClosures serverClosures)
    {
        var il = serverMethods.Listen.GetILGenerator();

        // if (_isListening) throw
        var notListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsListening);
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
        il.Emit(OpCodes.Stfld, serverFields.Port);

        // Check arg2: string (host) or callable (callback)
        var arg2NotString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, arg2NotString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stfld, serverFields.Host);
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
        EmitDictExtractPort(il, 1, serverFields.Port);
        // Extract host from dict["host"]
        var hostLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Host);
        il.Emit(OpCodes.Stloc, hostLocal);
        EmitDictTryGetString(il, 1, "host", hostLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stfld, serverFields.Host);
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
        il.Emit(OpCodes.Stfld, serverFields.IsIpc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Stfld, serverFields.PipePath);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, serverFields.Cts);

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
        il.Emit(OpCodes.Stfld, serverFields.UnixSocket);

        // _unixSocket.Bind(new UnixDomainSocketEndPoint(path))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
        il.Emit(OpCodes.Ldloc, ipcPathLocal);
        il.Emit(OpCodes.Newobj, typeof(UnixDomainSocketEndPoint).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Bind", [typeof(EndPoint)])!);

        // _unixSocket.Listen(511)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
        il.Emit(OpCodes.Ldc_I4, 511);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Listen", [_types.Int32])!);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, serverFields.IsListening);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);

        // Start Unix IPC accept worker on ThreadPool BEFORE callback
        // (callback may trigger client connect; accept worker must be ready)
        EmitIpcAcceptWorkerUnixStart(il, runtime, serverFields, serverClosures);

        // Emit 'listening' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
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
        il.Emit(OpCodes.Stfld, serverFields.IsListening);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);

        // _pipeReady = new ManualResetEventSlim(false)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.ManualResetEventSlim).GetConstructor([_types.Boolean])!);
        il.Emit(OpCodes.Stfld, serverFields.PipeReady);

        // Start Windows IPC accept worker on ThreadPool
        EmitIpcAcceptWorkerWindowsStart(il, runtime, serverFields, serverClosures);

        // _pipeReady.Wait(5000) — block until first pipe is listening
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.PipeReady);
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
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
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
        il.Emit(OpCodes.Ldfld, serverFields.Host);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", [_types.String])!);
        il.Emit(OpCodes.Brtrue, anyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Host);
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
        il.Emit(OpCodes.Ldfld, serverFields.Host);
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
        il.Emit(OpCodes.Ldfld, serverFields.Port);
        il.Emit(OpCodes.Newobj, typeof(TcpListener).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Stfld, serverFields.Listener);

        // try { _listener.Start() } catch { return this }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
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
        il.Emit(OpCodes.Ldfld, serverFields.Port);
        il.Emit(OpCodes.Brtrue, portNotZero);

        // _port = ((IPEndPoint)_listener.LocalEndpoint).Port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
        il.Emit(OpCodes.Callvirt, typeof(TcpListener).GetProperty("LocalEndpoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(IPEndPoint));
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, serverFields.Port);

        il.MarkLabel(portNotZero);

        // _isListening = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, serverFields.IsListening);

        // _cts = new CancellationTokenSource()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, serverFields.Cts);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Ref);

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
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // Start TCP accept worker on ThreadPool using $TcpAcceptClosure
        EmitTcpAcceptWorkerStart(il, runtime, serverFields, serverClosures);

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: ThreadPool.QueueUserWorkItem(_ => new $TcpAcceptClosure(this, _listener.AcceptTcpClient()).Run())
    /// The closure accept loop is in $TcpAcceptClosure.Run(), which checks _isListening,
    /// calls AcceptTcpClient, creates $NetSocket, schedules connection handling via EventLoop.
    /// </summary>
    private void EmitTcpAcceptWorkerStart(
        ILGenerator callerIl,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerClosures serverClosures)
    {
        // Emit a private _TcpAcceptWorker(object state) method that creates and runs the closure
        var acceptWorker = runtime.RequireNet().ServerType.DefineMethod(
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
            wil.Emit(OpCodes.Ldfld, serverFields.IsListening);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // try { client = _listener.AcceptTcpClient() } catch { break }
            wil.BeginExceptionBlock();
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.Listener);
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
            wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, clientLocal);
            wil.Emit(OpCodes.Newobj, serverClosures.TcpAccept.Constructor);
            wil.Emit(OpCodes.Ldftn, serverClosures.TcpAccept.Run);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

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
    private void EmitIpcAcceptWorkerUnixStart(
        ILGenerator callerIl,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerClosures serverClosures)
    {
        var acceptWorker = runtime.RequireNet().ServerType.DefineMethod(
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
            wil.Emit(OpCodes.Ldfld, serverFields.IsListening);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // Check _unixSocket != null
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
            wil.Emit(OpCodes.Brfalse, loopExit);

            // try { clientSocket = _unixSocket.AcceptAsync().GetAwaiter().GetResult() } catch { break }
            // Use async path — synchronous Socket.Accept may hang on macOS for Unix domain sockets
            wil.BeginExceptionBlock();
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
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
            wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, streamLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.PipePath);
            wil.Emit(OpCodes.Newobj, serverClosures.IpcAccept.Constructor);
            wil.Emit(OpCodes.Ldftn, serverClosures.IpcAccept.Run);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

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
    private void EmitIpcAcceptWorkerWindowsStart(
        ILGenerator callerIl,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerClosures serverClosures)
    {
        var acceptWorker = runtime.RequireNet().ServerType.DefineMethod(
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
            wil.Emit(OpCodes.Ldfld, serverFields.PipePath);
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
            wil.Emit(OpCodes.Ldfld, serverFields.Cts);
            wil.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetProperty("Token")!.GetGetMethod()!);
            wil.Emit(OpCodes.Stloc, tokenLocal);

            // first = true
            wil.Emit(OpCodes.Ldc_I4_1);
            wil.Emit(OpCodes.Stloc, firstLocal);

            wil.MarkLabel(loopTop);

            // Check _isListening
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.IsListening);
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
            wil.Emit(OpCodes.Ldfld, serverFields.PipeReady);
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
            wil.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldloc, pipeLocal);
            wil.Emit(OpCodes.Ldarg_0);
            wil.Emit(OpCodes.Ldfld, serverFields.PipePath);
            wil.Emit(OpCodes.Newobj, serverClosures.IpcAccept.Constructor);
            wil.Emit(OpCodes.Ldftn, serverClosures.IpcAccept.Run);
            wil.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
            wil.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

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
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, notTs);
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Br, skip);

        il.MarkLabel(notTs);
        // Check BoundTSFunction
        il.Emit(OpCodes.Ldarg, argIdx);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindings.BoundType);
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
    private void EmitNetServerCloseBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods)
    {
        var il = serverMethods.Close.GetILGenerator();

        // if (!_isListening) return this
        var isListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsListening);
        il.Emit(OpCodes.Brtrue, isListening);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListening);

        // _cts?.Cancel()
        il.BeginExceptionBlock();
        var noCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Cts);
        il.Emit(OpCodes.Brfalse, noCts);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Cts);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCts);

        // Branch: IPC vs TCP cleanup
        var notIpcClose = il.DefineLabel();
        var cleanupDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsIpc);
        il.Emit(OpCodes.Brfalse, notIpcClose);

        // ── IPC cleanup ──
        // Close _unixSocket if set
        var noUnixSock = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
        il.Emit(OpCodes.Brfalse, noUnixSock);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.UnixSocket);
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
        il.Emit(OpCodes.Ldfld, serverFields.PipePath);
        il.Emit(OpCodes.Brfalse, noPipePath);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.PipePath);
        il.Emit(OpCodes.Call, typeof(File).GetMethod("Exists", [_types.String])!);
        il.Emit(OpCodes.Brfalse, noPipePath);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.PipePath);
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
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
        il.Emit(OpCodes.Brfalse, noListener);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
        il.Emit(OpCodes.Callvirt, typeof(TcpListener).GetMethod("Stop")!);
        il.MarkLabel(noListener);

        il.MarkLabel(cleanupDone);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // _isListening = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, serverFields.IsListening);

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Unref);

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
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
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
    private void EmitNetServerAddressBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods)
    {
        var il = serverMethods.Address.GetILGenerator();

        // if (!_isListening) return null
        var isListening = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsListening);
        il.Emit(OpCodes.Brtrue, isListening);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isListening);

        // if (_isIpc) return _pipePath (Node.js returns the pipe path as a string)
        var notIpcAddr = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsIpc);
        il.Emit(OpCodes.Brfalse, notIpcAddr);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.PipePath);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notIpcAddr);

        // Return { address, family, port }
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Host);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Port);
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
    private void EmitNetServerGetConnectionsBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods)
    {
        var il = serverMethods.GetConnections.GetILGenerator();

        // if (callback is TSFunction) callback.Invoke([null, connections.Count])
        var noCb = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noCb);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Connections);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
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
    private void EmitNetServerGetMemberBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods)
    {
        var il = serverMethods.GetMember.GetILGenerator();

        var listeningLabel = il.DefineLabel();
        var maxConnLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        EmitStringCheck(il, 1, "listening", listeningLabel);
        EmitStringCheck(il, 1, "maxConnections", maxConnLabel);
        il.Emit(OpCodes.Br, defaultLabel);

        il.MarkLabel(listeningLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsListening);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(maxConnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.MaxConnections);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits body for: public void SetMember(string name, object value)
    /// </summary>
    private void EmitNetServerSetMemberBody(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        NetServerFields serverFields,
        NetServerMethods serverMethods)
    {
        var il = serverMethods.SetMember.GetILGenerator();

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
        il.Emit(OpCodes.Stfld, serverFields.MaxConnections);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}
