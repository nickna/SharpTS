using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits 7 closure types for thread-safe event dispatch in the compiled net/http modules.
/// Each closure is a sealed class with fields, a constructor, and a void Run() method.
/// These closures are scheduled on the event loop from background accept/read workers
/// so that event handlers always execute on the main thread.
/// </summary>
public partial class RuntimeEmitter
{
    // HTTP accept closure fields (socket/server closure fields declared in TSNetSocket.cs and TSNetServer.cs)
    internal ConstructorBuilder _httpAcceptClosureCtor = null!;
    internal MethodBuilder _httpAcceptClosureRun = null!;

    // IPC write closure fields

    private void EmitNetClosureTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitTcpAcceptClosure(moduleBuilder, runtime);
        EmitIpcAcceptClosure(moduleBuilder, runtime);
        EmitSocketReadDataClosure(moduleBuilder, runtime);
        EmitSocketReadEndClosure(moduleBuilder, runtime);
        EmitSocketConnectOkClosure(moduleBuilder, runtime);
        EmitSocketConnectErrClosure(moduleBuilder, runtime);
        // $HttpAcceptClosure references $HttpServer, which is gated on UsesHttp.
        if (_features.UsesHttp)
            EmitHttpAcceptClosure(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits $TcpAcceptClosure: wraps a TcpClient accepted by $NetServer for main-thread dispatch.
    /// Run() creates a $NetSocket, adds it to connections, fires connectionListener + "connection" event,
    /// then starts reading.
    /// </summary>
    private void EmitTcpAcceptClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$TcpAcceptClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var serverField = typeBuilder.DefineField("_server", _netServerTypeBuilder, FieldAttributes.Private);
        var clientField = typeBuilder.DefineField("_client", typeof(TcpClient), FieldAttributes.Private);

        // Constructor: (server, client)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netServerTypeBuilder, typeof(TcpClient)]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, serverField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, clientField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();
            var socketLocal = il.DeclareLocal(_netSocketTypeBuilder); // local 0: $NetSocket

            // var socket = new $NetSocket(_client)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, clientField);
            il.Emit(OpCodes.Newobj, runtime.NetSocketCtorTcpClient);
            il.Emit(OpCodes.Stloc, socketLocal);

            EmitApplyServerSocketOptions(il, serverField, socketLocal);

            // _server._connections.Add(socket)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldfld, _netServerConnectionsField);
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

            // if (_server._connectionListener != null) invoke with [socket]
            var noListener = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldfld, _netServerConnectionListenerField);
            il.Emit(OpCodes.Brfalse, noListener);

            EmitDgramCallbackInvocation(il, runtime,
                () =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, serverField);
                    il.Emit(OpCodes.Ldfld, _netServerConnectionListenerField);
                },
                1,
                (il2) =>
                {
                    il2.Emit(OpCodes.Ldc_I4_1);
                    il2.Emit(OpCodes.Newarr, _types.Object);
                    il2.Emit(OpCodes.Dup);
                    il2.Emit(OpCodes.Ldc_I4_0);
                    il2.Emit(OpCodes.Ldloc, socketLocal);
                    il2.Emit(OpCodes.Stelem_Ref);
                });

            il.MarkLabel(noListener);

            // _server.Emit("connection", new object[] { socket })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldstr, "connection");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // socket.StartReading()
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Callvirt, runtime.NetSocketStartReading);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _tcpAcceptClosureCtor = ctor;
        _tcpAcceptClosureRun = run;
    }

    /// <summary>
    /// Emits $IpcAcceptClosure: wraps a Stream accepted by $NetServer for IPC (named pipe / unix socket).
    /// Same as TcpAcceptClosure but constructs $NetSocket with (Stream, pipePath).
    /// </summary>
    private void EmitIpcAcceptClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$IpcAcceptClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var serverField = typeBuilder.DefineField("_server", _netServerTypeBuilder, FieldAttributes.Private);
        var streamField = typeBuilder.DefineField("_stream", typeof(Stream), FieldAttributes.Private);
        var pipePathField = typeBuilder.DefineField("_pipePath", _types.String, FieldAttributes.Private);

        // Constructor: (server, stream, pipePath)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netServerTypeBuilder, typeof(Stream), _types.String]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, serverField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, streamField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Stfld, pipePathField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();
            var socketLocal = il.DeclareLocal(_netSocketTypeBuilder);

            // var socket = new $NetSocket(_stream, _pipePath)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, streamField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, pipePathField);
            il.Emit(OpCodes.Newobj, runtime.NetSocketCtorStream);
            il.Emit(OpCodes.Stloc, socketLocal);

            EmitApplyServerSocketOptions(il, serverField, socketLocal);

            // _server._connections.Add(socket)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldfld, _netServerConnectionsField);
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

            // socket.StartReading() — must start BEFORE callbacks so reader is pending
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Callvirt, runtime.NetSocketStartReading);

            // socket._readReady?.Wait(5000) — wait for read worker to be ready
            var skipWait = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
            il.Emit(OpCodes.Brfalse, skipWait);
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
            il.Emit(OpCodes.Ldc_I4, 5000);
            il.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Wait", [typeof(int)])!);
            il.Emit(OpCodes.Pop); // Wait(int) returns bool
            il.MarkLabel(skipWait);

            // if (_server._connectionListener != null) invoke with [socket]
            var noListener = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldfld, _netServerConnectionListenerField);
            il.Emit(OpCodes.Brfalse, noListener);

            EmitDgramCallbackInvocation(il, runtime,
                () =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, serverField);
                    il.Emit(OpCodes.Ldfld, _netServerConnectionListenerField);
                },
                1,
                (il2) =>
                {
                    il2.Emit(OpCodes.Ldc_I4_1);
                    il2.Emit(OpCodes.Newarr, _types.Object);
                    il2.Emit(OpCodes.Dup);
                    il2.Emit(OpCodes.Ldc_I4_0);
                    il2.Emit(OpCodes.Ldloc, socketLocal);
                    il2.Emit(OpCodes.Stelem_Ref);
                });

            il.MarkLabel(noListener);

            // _server.Emit("connection", new object[] { socket })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldstr, "connection");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, socketLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _ipcAcceptClosureCtor = ctor;
        _ipcAcceptClosureRun = run;
    }

    /// <summary>
    /// Applies createServer(options) per-socket settings to a freshly accepted socket:
    /// if (server._socketHwm >= 0) socket._writableHwm = server._socketHwm (#1068).
    /// The closure's Run() has the server in a field and the socket in a local.
    /// </summary>
    private void EmitApplyServerSocketOptions(ILGenerator il, FieldBuilder serverField, LocalBuilder socketLocal)
    {
        var skipHwm = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldfld, _netServerSocketHwmField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, skipHwm);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldfld, _netServerSocketHwmField);
        il.Emit(OpCodes.Stfld, _netSocketWritableHwmField);
        il.MarkLabel(skipHwm);
    }

    /// <summary>
    /// Emits $SocketReadDataClosure: dispatches a "data" event with the read chunk on the main thread.
    /// </summary>
    private void EmitSocketReadDataClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$SocketReadDataClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var socketField = typeBuilder.DefineField("_socket", _netSocketTypeBuilder, FieldAttributes.Private);
        var chunkField = typeBuilder.DefineField("_chunk", _types.Object, FieldAttributes.Private);

        // Constructor: (socket, chunk)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netSocketTypeBuilder, _types.Object]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, socketField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, chunkField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();

            // _socket.Emit("data", new object[] { _chunk })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "data");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, chunkField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _socketReadDataClosureCtor = ctor;
        _socketReadDataClosureRun = run;
    }

    /// <summary>
    /// Emits $SocketReadEndClosure: fires "end" and "close" events when the read loop finishes,
    /// and unrefs the event loop if reading was started.
    /// </summary>
    private void EmitSocketReadEndClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$SocketReadEndClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var socketField = typeBuilder.DefineField("_socket", _netSocketTypeBuilder, FieldAttributes.Private);

        // Constructor: (socket)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netSocketTypeBuilder]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, socketField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();

            var skipEmit = il.DefineLabel();
            var checkReading = il.DefineLabel();

            // if (!_socket._destroyed) { emit "end" and "close" }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketDestroyedField);
            il.Emit(OpCodes.Brtrue, checkReading);

            // _socket.Emit("end", new object[0])
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "end");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // Skip 'close' if already emitted (e.g. an 'end' listener called
            // destroy(), whose Destroy body fired 'close' synchronously).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketCloseEmittedField);
            il.Emit(OpCodes.Brtrue, checkReading);

            // _socket._closeEmitted = true
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, _netSocketCloseEmittedField);

            // _socket.Emit("close", new object[] { false })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "close");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_0); // false
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // if (_socket._readingStarted) { _readingStarted = false; EventLoop.Unref() }
            il.MarkLabel(checkReading);
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketReadingStartedField);
            il.Emit(OpCodes.Brfalse, done);

            // _socket._readingStarted = false
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, _netSocketReadingStartedField);

            // EventLoop.Instance.Unref()
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);

            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _socketReadEndClosureCtor = ctor;
        _socketReadEndClosureRun = run;
    }

    /// <summary>
    /// Emits $SocketConnectOkClosure: fires "connect" event and starts reading on successful connection.
    /// </summary>
    private void EmitSocketConnectOkClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$SocketConnectOkClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var socketField = typeBuilder.DefineField("_socket", _netSocketTypeBuilder, FieldAttributes.Private);

        // Constructor: (socket)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netSocketTypeBuilder]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, socketField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();

            // IPC pipes need read-before-connect to avoid blocking writes
            var tcpPath = il.DefineLabel();
            var done = il.DefineLabel();

            // if (_socket._isIpc) goto ipcPath, else goto tcpPath
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketIsIpcField);
            il.Emit(OpCodes.Brfalse, tcpPath);

            // ── IPC path: StartReading → wait → emit connect ──
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Callvirt, runtime.NetSocketStartReading);

            // _socket._readReady?.Wait(5000)
            var skipIpcWait = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
            il.Emit(OpCodes.Brfalse, skipIpcWait);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldfld, _netSocketReadReadyField);
            il.Emit(OpCodes.Ldc_I4, 5000);
            il.Emit(OpCodes.Callvirt, typeof(System.Threading.ManualResetEventSlim).GetMethod("Wait", [typeof(int)])!);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(skipIpcWait);

            // _socket.Emit("connect", new object[0])
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "connect");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Br, done);

            // ── TCP path: emit connect → StartReading ──
            il.MarkLabel(tcpPath);

            // _socket.Emit("connect", new object[0])
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "connect");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // _socket.StartReading()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Callvirt, runtime.NetSocketStartReading);

            il.MarkLabel(done);

            // Release the in-flight-connect Ref taken in $TSNetSocket.Connect, after
            // the 'connect' event has been delivered (and reading started), so the
            // handle outlives delivery. Mirrors SharpTSSocket's interpreter.Unref().
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _socketConnectOkClosureCtor = ctor;
        _socketConnectOkClosureRun = run;
    }

    /// <summary>
    /// Emits $SocketConnectErrClosure: sets _connecting = false and fires "error" event with error dict.
    /// Error dict has { message, code, syscall } properties matching Node.js system errors.
    /// </summary>
    private void EmitSocketConnectErrClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$SocketConnectErrClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var socketField = typeBuilder.DefineField("_socket", _netSocketTypeBuilder, FieldAttributes.Private);
        var errorMsgField = typeBuilder.DefineField("_errorMsg", _types.String, FieldAttributes.Private);
        var errorCodeField = typeBuilder.DefineField("_errorCode", _types.String, FieldAttributes.Private);

        // Constructor: (socket, errorMsg, errorCode)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_netSocketTypeBuilder, _types.String, _types.String]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, socketField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, errorMsgField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Stfld, errorCodeField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();

            // _socket._connecting = false
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, _netSocketConnectingField);

            // var error = new $Error(_errorMsg); error.Code = _errorCode; error.Syscall = "connect";
            var errorLocal = il.DeclareLocal(runtime.TSErrorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, errorMsgField);
            il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
            il.Emit(OpCodes.Stloc, errorLocal);

            // error.Code = _errorCode
            il.Emit(OpCodes.Ldloc, errorLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, errorCodeField);
            il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeSetter);

            // error.Syscall = "connect"
            il.Emit(OpCodes.Ldloc, errorLocal);
            il.Emit(OpCodes.Ldstr, "connect");
            il.Emit(OpCodes.Callvirt, runtime.TSErrorSyscallSetter);

            // _socket.Emit("error", new object[] { error })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, socketField);
            il.Emit(OpCodes.Ldstr, "error");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, errorLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // Release the in-flight-connect Ref taken in $TSNetSocket.Connect, after
            // the 'error' event has been delivered. Mirrors SharpTSSocket's
            // interpreter.Unref() on the failure path.
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _socketConnectErrClosureCtor = ctor;
        _socketConnectErrClosureRun = run;
    }

    /// <summary>
    /// Emits $HttpAcceptClosure: wraps an HttpListenerContext accepted by $HttpServer.
    /// Run() creates $HttpRequest and $HttpResponse, fires "request" event, and invokes _callback if set.
    /// </summary>
    private void EmitHttpAcceptClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$HttpAcceptClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object)
        );

        var serverField = typeBuilder.DefineField("_server", runtime.TSHttpServerType, FieldAttributes.Private);
        var ctxField = typeBuilder.DefineField("_ctx", typeof(HttpListenerContext), FieldAttributes.Private);

        // Constructor: (server, ctx)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSHttpServerType, typeof(HttpListenerContext)]
        );
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, serverField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, ctxField);
            il.Emit(OpCodes.Ret);
        }

        // Run()
        var run = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        {
            var il = run.GetILGenerator();
            var reqLocal = il.DeclareLocal(runtime.TSHttpRequestType);
            var resLocal = il.DeclareLocal(runtime.TSHttpResponseType);

            // var req = new $HttpRequest(ctx.Request)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Callvirt, typeof(HttpListenerContext).GetProperty("Request")!.GetGetMethod()!);
            il.Emit(OpCodes.Newobj, runtime.TSHttpRequestCtor);
            il.Emit(OpCodes.Stloc, reqLocal);

            // var res = new $HttpResponse(ctx.Response)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, ctxField);
            il.Emit(OpCodes.Callvirt, typeof(HttpListenerContext).GetProperty("Response")!.GetGetMethod()!);
            il.Emit(OpCodes.Newobj, runtime.TSHttpResponseCtor);
            il.Emit(OpCodes.Stloc, resLocal);

            // _server.Emit("request", new object[] { req, res })
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldstr, "request");
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, reqLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldloc, resLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            // if (_server._callback != null) invoke with [req, res]
            var noCb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, serverField);
            il.Emit(OpCodes.Ldfld, _httpServerCallbackField);
            il.Emit(OpCodes.Brfalse, noCb);

            EmitDgramCallbackInvocation(il, runtime,
                () =>
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, serverField);
                    il.Emit(OpCodes.Ldfld, _httpServerCallbackField);
                },
                2,
                (il2) =>
                {
                    il2.Emit(OpCodes.Ldc_I4_2);
                    il2.Emit(OpCodes.Newarr, _types.Object);
                    il2.Emit(OpCodes.Dup);
                    il2.Emit(OpCodes.Ldc_I4_0);
                    il2.Emit(OpCodes.Ldloc, reqLocal);
                    il2.Emit(OpCodes.Stelem_Ref);
                    il2.Emit(OpCodes.Dup);
                    il2.Emit(OpCodes.Ldc_I4_1);
                    il2.Emit(OpCodes.Ldloc, resLocal);
                    il2.Emit(OpCodes.Stelem_Ref);
                });

            il.MarkLabel(noCb);

            // Read the request body and emit 'data' before 'end' (#1048 on-demand body),
            // so req.on('data') receives the posted payload as a $Buffer. Listeners were
            // attached synchronously by the handler above.
            var bodyBytesLocal = il.DeclareLocal(typeof(byte[]));
            il.BeginExceptionBlock();
            {
                var msLocal = il.DeclareLocal(typeof(System.IO.MemoryStream));
                il.Emit(OpCodes.Newobj, typeof(System.IO.MemoryStream).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Stloc, msLocal);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, ctxField);
                il.Emit(OpCodes.Callvirt, typeof(HttpListenerContext).GetProperty("Request")!.GetGetMethod()!);
                il.Emit(OpCodes.Callvirt, typeof(HttpListenerRequest).GetProperty("InputStream")!.GetGetMethod()!);
                il.Emit(OpCodes.Ldloc, msLocal);
                il.Emit(OpCodes.Callvirt, typeof(System.IO.Stream).GetMethod("CopyTo", [typeof(System.IO.Stream)])!);
                il.Emit(OpCodes.Ldloc, msLocal);
                il.Emit(OpCodes.Callvirt, typeof(System.IO.MemoryStream).GetMethod("ToArray", Type.EmptyTypes)!);
                il.Emit(OpCodes.Stloc, bodyBytesLocal);
            }
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Pop);
            il.EndExceptionBlock();

            // if (bytes != null && bytes.Length > 0) req.Emit("data", [ new $Buffer(bytes) ])
            var skipData = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, bodyBytesLocal);
            il.Emit(OpCodes.Brfalse, skipData);
            il.Emit(OpCodes.Ldloc, bodyBytesLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brfalse, skipData);
            il.Emit(OpCodes.Ldloc, reqLocal);
            il.Emit(OpCodes.Ldstr, "data");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, bodyBytesLocal);
            il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(skipData);

            // Emit 'end' event on request so req.on('end', ...) works
            il.Emit(OpCodes.Ldloc, reqLocal);
            il.Emit(OpCodes.Ldstr, "end");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
            il.Emit(OpCodes.Pop);

            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
        _httpAcceptClosureCtor = ctor;
        _httpAcceptClosureRun = run;
    }

}
