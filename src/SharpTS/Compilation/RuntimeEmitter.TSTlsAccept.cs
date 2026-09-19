using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the TLS handshake infrastructure as pure-BCL IL (no SharpTS.dll dependency):
/// - $TlsAcceptClosure: dispatches a server-side 'secureConnection' + starts reading on the event loop
/// - $TlsConnectOkClosure / $TlsConnectErrClosure: client-side 'secureConnect' / 'error' dispatch
/// - $TlsConnectClosure: runs the client TCP connect + SslStream.AuthenticateAsClient on the ThreadPool
/// - $TlsServer._TlsAcceptWorker: blocking accept loop + SslStream.AuthenticateAsServer
///
/// SslStream is BCL, so the whole handshake compiles to standalone IL — matching how $NetServer/
/// $NetSocket emit TcpListener/TcpClient. The negotiated SslStream is retained on the $TlsSocket
/// (its base $NetSocket._stream + the TLS _sslStream field), so socket I/O and introspection work.
/// </summary>
public partial class RuntimeEmitter
{
    // The OR of the two enabled TLS protocol versions (1.2 | 1.3). 1.0/1.1 stay disabled.
    private static int EnabledTlsProtocols => (int)(SslProtocols.Tls12 | SslProtocols.Tls13);

    /// <summary>
    /// Emits the $TlsAcceptClosure class.
    /// Run(): invokes _server._callback([socket]), emits "secureConnection", then socket.StartReading().
    /// </summary>
    private TlsClosureMethods EmitTlsAcceptClosureClass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        EmittedTlsRuntime tls,
        TlsServerFields serverFields)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TlsAcceptClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var serverField = typeBuilder.DefineField("_server", tls.ServerCtor.DeclaringType!, FieldAttributes.Private);
        var socketField = typeBuilder.DefineField("_socket", tls.SocketType, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [tls.ServerCtor.DeclaringType!, tls.SocketType]
        );

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, serverField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, socketField);
        ctorIL.Emit(OpCodes.Ret);

        var run = typeBuilder.DefineMethod("Run", MethodAttributes.Public, typeof(void), Type.EmptyTypes);

        var il = run.GetILGenerator();

        // _server._connectionListener? — the $TlsServer stores it in _callback.
        var noCallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldfld, serverFields.Callback);
        il.Emit(OpCodes.Brfalse, noCallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldfld, serverFields.Callback);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noCallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldfld, serverFields.Callback);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCallback);

        // _server.Emit("secureConnection", [_socket])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldstr, "secureConnection");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // _socket.StartReading()  (inherited $NetSocket method — pumps 'data'/'end'/'close')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Callvirt, runtime.RequireNet().SocketStartReading);

        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();

        return new(ctor, run);
    }

    /// <summary>
    /// Emits $TlsAcceptErrorClosure: reports a failed server-side handshake as 'tlsClientError'
    /// on the event-loop thread. The accept worker itself runs on the ThreadPool and must not
    /// invoke TypeScript listeners directly.
    /// </summary>
    private TlsClosureMethods EmitTlsAcceptErrorClosureClass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        EmittedTlsRuntime tls)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TlsAcceptErrorClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var serverField = typeBuilder.DefineField("_server", tls.ServerCtor.DeclaringType!, FieldAttributes.Private);
        var msgField = typeBuilder.DefineField("_msg", _types.String, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [tls.ServerCtor.DeclaringType!, _types.String]
        );

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, serverField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, msgField);
        ctorIL.Emit(OpCodes.Ret);

        var run = typeBuilder.DefineMethod("Run", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = run.GetILGenerator();

        var errLocal = il.DeclareLocal(runtime.Errors.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, msgField);
        il.Emit(OpCodes.Newobj, runtime.Errors.MessageConstructor);
        il.Emit(OpCodes.Stloc, errLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverField);
        il.Emit(OpCodes.Ldstr, "tlsClientError");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();

        return new(ctor, run);
    }

    /// <summary>
    /// Emits $TlsConnectOkClosure: client-side success — emit 'secureConnect', start reading, release ref.
    /// </summary>
    private TlsClosureMethods EmitTlsConnectOkClosureClass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        EmittedTlsRuntime tls)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TlsConnectOkClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var socketField = typeBuilder.DefineField("_socket", tls.SocketType, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [tls.SocketType]);
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, socketField);
        ctorIL.Emit(OpCodes.Ret);

        var run = typeBuilder.DefineMethod("Run", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = run.GetILGenerator();

        // _socket.Emit("secureConnect", [])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldstr, "secureConnect");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // _socket.StartReading()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Callvirt, runtime.RequireNet().SocketStartReading);

        // EventLoop.Unref() — release the in-flight-connect ref taken in TlsConnect
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Unref);

        il.Emit(OpCodes.Ret);
        typeBuilder.CreateType();

        return new(ctor, run);
    }

    /// <summary>
    /// Emits $TlsConnectErrClosure: client-side failure — emit 'error' with the message, release ref.
    /// </summary>
    private TlsClosureMethods EmitTlsConnectErrClosureClass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        EmittedTlsRuntime tls)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TlsConnectErrClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var socketField = typeBuilder.DefineField("_socket", tls.SocketType, FieldAttributes.Private);
        var msgField = typeBuilder.DefineField("_msg", _types.String, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, [tls.SocketType, _types.String]);
        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, socketField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, msgField);
        ctorIL.Emit(OpCodes.Ret);

        var run = typeBuilder.DefineMethod("Run", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = run.GetILGenerator();

        // var err = new $Error(_msg)
        var errLocal = il.DeclareLocal(runtime.Errors.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, msgField);
        il.Emit(OpCodes.Newobj, runtime.Errors.MessageConstructor);
        il.Emit(OpCodes.Stloc, errLocal);

        // _socket.Emit("error", [err])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoop.Unref);

        il.Emit(OpCodes.Ret);
        typeBuilder.CreateType();

        return new(ctor, run);
    }

    /// <summary>
    /// Emits $TlsConnectClosure: runs the client TCP connect + SslStream.AuthenticateAsClient on the
    /// ThreadPool (pure-BCL), populates the $TlsSocket, then schedules the OK/Err closure.
    /// Fields: _socket, _port(int), _host, _reject(bool), _alpn(string[])
    /// </summary>
    private TlsConnectConstruction EmitTlsConnectClosureClass(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime,
        FieldBuilder netClientField,
        FieldBuilder netStreamField,
        EmittedTlsRuntime tls,
        TlsSocketFields socketFields,
        TlsSocketHelpers socketHelpers)
    {
        // The OK/Err dispatch closures are defined first (referenced from Connect()).
        var connectOk = EmitTlsConnectOkClosureClass(moduleBuilder, runtime, tls);
        var connectErr = EmitTlsConnectErrClosureClass(moduleBuilder, runtime, tls);

        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TlsConnectClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var socketField = typeBuilder.DefineField("_socket", tls.SocketType, FieldAttributes.Private);
        var portField = typeBuilder.DefineField("_port", _types.Int32, FieldAttributes.Private);
        var hostField = typeBuilder.DefineField("_host", _types.String, FieldAttributes.Private);
        var rejectField = typeBuilder.DefineField("_reject", _types.Boolean, FieldAttributes.Private);
        var alpnField = typeBuilder.DefineField("_alpn", typeof(string[]), FieldAttributes.Private);
        var policyErrorsField = typeBuilder.DefineField("_policyErrors", typeof(SslPolicyErrors), FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [tls.SocketType, _types.Int32, _types.String, _types.Boolean, typeof(string[])]
        );

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0); ctorIL.Emit(OpCodes.Ldarg_1); ctorIL.Emit(OpCodes.Stfld, socketField);
        ctorIL.Emit(OpCodes.Ldarg_0); ctorIL.Emit(OpCodes.Ldarg_2); ctorIL.Emit(OpCodes.Stfld, portField);
        ctorIL.Emit(OpCodes.Ldarg_0); ctorIL.Emit(OpCodes.Ldarg_3); ctorIL.Emit(OpCodes.Stfld, hostField);
        ctorIL.Emit(OpCodes.Ldarg_0); ctorIL.Emit(OpCodes.Ldarg, 4); ctorIL.Emit(OpCodes.Stfld, rejectField);
        ctorIL.Emit(OpCodes.Ldarg_0); ctorIL.Emit(OpCodes.Ldarg, 5); ctorIL.Emit(OpCodes.Stfld, alpnField);
        ctorIL.Emit(OpCodes.Ret);

        // bool _Validate(object sender, X509Certificate, X509Chain, SslPolicyErrors errors):
        //   _policyErrors = errors; return !_reject || errors == None;
        // An instance callback so the per-handshake chain result is captured (authorized/authError).
        var validate = typeBuilder.DefineMethod(
            "_Validate",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Object, typeof(X509Certificate), typeof(X509Chain), typeof(SslPolicyErrors)]
        );
        {
            var vil = validate.GetILGenerator();
            // _policyErrors = errors (arg4)
            vil.Emit(OpCodes.Ldarg_0);
            vil.Emit(OpCodes.Ldarg, 4);
            vil.Emit(OpCodes.Stfld, policyErrorsField);
            // return !_reject || errors == None
            var ret = vil.DefineLabel();
            var retTrue = vil.DefineLabel();
            vil.Emit(OpCodes.Ldarg_0);
            vil.Emit(OpCodes.Ldfld, rejectField);
            vil.Emit(OpCodes.Brfalse, retTrue);   // !_reject → true
            vil.Emit(OpCodes.Ldarg, 4);
            vil.Emit(OpCodes.Ldc_I4, (int)SslPolicyErrors.None);
            vil.Emit(OpCodes.Ceq);
            vil.Emit(OpCodes.Ret);
            vil.MarkLabel(retTrue);
            vil.Emit(OpCodes.Ldc_I4_1);
            vil.Emit(OpCodes.Ret);
        }

        // Connect(object state): void — runs on ThreadPool
        var connect = typeBuilder.DefineMethod(
            "Connect", MethodAttributes.Public, typeof(void), [_types.Object]);

        var il = connect.GetILGenerator();

        var tcpClientLocal = il.DeclareLocal(typeof(TcpClient));
        var sslStreamLocal = il.DeclareLocal(typeof(SslStream));
        var optsLocal = il.DeclareLocal(typeof(SslClientAuthenticationOptions));

        il.BeginExceptionBlock();

        // tcpClient = new TcpClient(); tcpClient.Connect(_host, _port);
        il.Emit(OpCodes.Newobj, typeof(TcpClient).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcpClientLocal);
        il.Emit(OpCodes.Ldloc, tcpClientLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hostField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, portField);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Connect", [_types.String, _types.Int32])!);

        // sslStream = new SslStream(ns, false, this._Validate)  — always observe the chain result;
        // _Validate enforces rejectUnauthorized and records _policyErrors for authorized/authError.
        il.Emit(OpCodes.Ldloc, tcpClientLocal);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, validate);
        il.Emit(OpCodes.Newobj, typeof(RemoteCertificateValidationCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Newobj, typeof(SslStream).GetConstructor(
            [typeof(System.IO.Stream), _types.Boolean, typeof(RemoteCertificateValidationCallback)])!);
        il.Emit(OpCodes.Stloc, sslStreamLocal);

        // opts = new SslClientAuthenticationOptions { TargetHost=_host, EnabledSslProtocols=1.2|1.3 }
        il.Emit(OpCodes.Newobj, typeof(SslClientAuthenticationOptions).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, optsLocal);
        il.Emit(OpCodes.Ldloc, optsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, hostField);
        il.Emit(OpCodes.Callvirt, typeof(SslClientAuthenticationOptions).GetProperty("TargetHost")!.GetSetMethod()!);
        il.Emit(OpCodes.Ldloc, optsLocal);
        il.Emit(OpCodes.Ldc_I4, EnabledTlsProtocols);
        il.Emit(OpCodes.Callvirt, typeof(SslClientAuthenticationOptions).GetProperty("EnabledSslProtocols")!.GetSetMethod()!);

        // if (_alpn != null) opts.ApplicationProtocols = _BuildAlpnList(_alpn)
        var noAlpn = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, alpnField);
        il.Emit(OpCodes.Brfalse, noAlpn);
        il.Emit(OpCodes.Ldloc, optsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, alpnField);
        il.Emit(OpCodes.Call, socketHelpers.BuildAlpnList);
        il.Emit(OpCodes.Callvirt, typeof(SslClientAuthenticationOptions).GetProperty("ApplicationProtocols")!.GetSetMethod()!);
        il.MarkLabel(noAlpn);

        // sslStream.AuthenticateAsClient(opts)
        il.Emit(OpCodes.Ldloc, sslStreamLocal);
        il.Emit(OpCodes.Ldloc, optsLocal);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetMethod("AuthenticateAsClient", [typeof(SslClientAuthenticationOptions)])!);

        // Populate the socket from the negotiated stream.
        EmitTlsPopulateSocket(il,
            loadSocket: () => { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, socketField); },
            loadClient: () => il.Emit(OpCodes.Ldloc, tcpClientLocal),
            loadSslStream: () => il.Emit(OpCodes.Ldloc, sslStreamLocal), netClientField, netStreamField, socketFields, socketHelpers);

        // _socket._authorized = (_policyErrors == None)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, policyErrorsField);
        il.Emit(OpCodes.Ldc_I4, (int)SslPolicyErrors.None);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Stfld, socketFields.Authorized);
        // if (_policyErrors != None) _socket._authError = _DescribePolicyErrors(_policyErrors)
        var authOk = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, policyErrorsField);
        il.Emit(OpCodes.Brfalse, authOk);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, policyErrorsField);
        il.Emit(OpCodes.Call, socketHelpers.DescribeErrors);
        il.Emit(OpCodes.Stfld, socketFields.AuthError);
        il.MarkLabel(authOk);

        // EventLoop.Schedule(new Action(new $TlsConnectOkClosure(_socket).Run))
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Newobj, connectOk.Constructor);
        il.Emit(OpCodes.Ldftn, connectOk.Run);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        var afterConnect = il.DefineLabel();
        il.Emit(OpCodes.Leave, afterConnect);

        // catch (Exception e) { EventLoop.Schedule(new $TlsConnectErrClosure(_socket, e.Message).Run); }
        il.BeginCatchBlock(_types.Exception);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, connectErr.Constructor);
        il.Emit(OpCodes.Ldftn, connectErr.Run);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Leave, afterConnect);
        il.EndExceptionBlock();

        il.MarkLabel(afterConnect);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();

        return new(ctor, connect);
    }

    /// <summary>
    /// Shared IL: populate a $TlsSocket from a negotiated TcpClient + SslStream.
    /// Sets base _client/_stream and the TLS _sslStream/_authorized/_peerCert/_alpnProtocol fields.
    /// </summary>
    private void EmitTlsPopulateSocket(
        ILGenerator il,
        Action loadSocket,
        Action loadClient,
        Action loadSslStream,
        FieldBuilder netClientField,
        FieldBuilder netStreamField,
        TlsSocketFields socketFields,
        TlsSocketHelpers socketHelpers)
    {
        // socket._client = tcpClient   (base $NetSocket field)
        loadSocket(); loadClient(); il.Emit(OpCodes.Stfld, netClientField);
        // socket._stream = sslStream   (base $NetSocket field — SslStream is a Stream)
        loadSocket(); loadSslStream(); il.Emit(OpCodes.Stfld, netStreamField);
        // socket._sslStream = sslStream
        loadSocket(); loadSslStream(); il.Emit(OpCodes.Stfld, socketFields.SslStream);
        // NOTE: _authorized/_authError are set by the caller (connect: chain policy; server: IsAuthenticated).
        // socket._peerCert = sslStream.RemoteCertificate as X509Certificate2
        loadSocket(); loadSslStream();
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("RemoteCertificate")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(X509Certificate2));
        il.Emit(OpCodes.Stfld, socketFields.PeerCert);
        // socket._alpnProtocol = _AlpnString(sslStream)
        loadSocket(); loadSslStream();
        il.Emit(OpCodes.Call, socketHelpers.AlpnString);
        il.Emit(OpCodes.Stfld, socketFields.AlpnProtocol);
    }

    /// <summary>
    /// Emits the _TlsAcceptWorker body on $TlsServer.
    /// Blocking loop: AcceptTcpClient → SslStream → AuthenticateAsServer → schedule accept closure.
    /// A per-client handshake failure is swallowed (the client is closed) and the loop continues,
    /// matching interp's tlsClientError-and-continue behavior; only a listener fault breaks the loop.
    /// </summary>
    private void EmitTlsServerAcceptWorkerBody(
        EmittedRuntime runtime,
        FieldBuilder netClientField,
        FieldBuilder netStreamField,
        EmittedTlsRuntime tls,
        TlsSocketFields socketFields,
        TlsSocketHelpers socketHelpers,
        TlsServerFields serverFields,
        MethodBuilder acceptWorker,
        TlsAcceptClosures acceptClosures)
    {
        var il = acceptWorker.GetILGenerator();

        var tcpClientLocal = il.DeclareLocal(typeof(TcpClient));
        var sslStreamLocal = il.DeclareLocal(typeof(SslStream));
        var authOptsLocal = il.DeclareLocal(typeof(SslServerAuthenticationOptions));
        var socketLocal = il.DeclareLocal(tls.SocketType);
        var handshakeExceptionLocal = il.DeclareLocal(_types.Exception);

        var loopTop = il.DefineLabel();
        var loopExit = il.DefineLabel();

        il.MarkLabel(loopTop);

        // if (!_isListening || _listener == null) break
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.IsListening);
        il.Emit(OpCodes.Brfalse, loopExit);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
        il.Emit(OpCodes.Brfalse, loopExit);

        // try { tcpClient = _listener.AcceptTcpClient(); } catch { break; }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Listener);
        il.Emit(OpCodes.Callvirt, typeof(System.Net.Sockets.TcpListener).GetMethod("AcceptTcpClient", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcpClientLocal);
        var acceptOk = il.DefineLabel();
        il.Emit(OpCodes.Leave, acceptOk);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, loopExit);
        il.EndExceptionBlock();
        il.MarkLabel(acceptOk);

        // try { handshake + create socket + schedule } catch { close client; continue }
        il.BeginExceptionBlock();

        // sslStream = new SslStream(tcpClient.GetStream(), false)
        il.Emit(OpCodes.Ldloc, tcpClientLocal);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("GetStream")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(SslStream).GetConstructor([typeof(System.IO.Stream), _types.Boolean])!);
        il.Emit(OpCodes.Stloc, sslStreamLocal);

        // authOpts = new SslServerAuthenticationOptions();
        il.Emit(OpCodes.Newobj, typeof(SslServerAuthenticationOptions).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, authOptsLocal);
        // authOpts.ServerCertificate = _LoadCert(_cert, _key)
        il.Emit(OpCodes.Ldloc, authOptsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Cert);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Key);
        il.Emit(OpCodes.Call, socketHelpers.LoadCert);
        il.Emit(OpCodes.Callvirt, typeof(SslServerAuthenticationOptions).GetProperty("ServerCertificate")!.GetSetMethod()!);
        // authOpts.ClientCertificateRequired = _requestCert
        il.Emit(OpCodes.Ldloc, authOptsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.RequestCert);
        il.Emit(OpCodes.Callvirt, typeof(SslServerAuthenticationOptions).GetProperty("ClientCertificateRequired")!.GetSetMethod()!);
        // authOpts.EnabledSslProtocols = 1.2|1.3
        il.Emit(OpCodes.Ldloc, authOptsLocal);
        il.Emit(OpCodes.Ldc_I4, EnabledTlsProtocols);
        il.Emit(OpCodes.Callvirt, typeof(SslServerAuthenticationOptions).GetProperty("EnabledSslProtocols")!.GetSetMethod()!);
        // if (_alpn != null) authOpts.ApplicationProtocols = _BuildAlpnList(_alpn)
        var noAlpn = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Alpn);
        il.Emit(OpCodes.Brfalse, noAlpn);
        il.Emit(OpCodes.Ldloc, authOptsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, serverFields.Alpn);
        il.Emit(OpCodes.Call, socketHelpers.BuildAlpnList);
        il.Emit(OpCodes.Callvirt, typeof(SslServerAuthenticationOptions).GetProperty("ApplicationProtocols")!.GetSetMethod()!);
        il.MarkLabel(noAlpn);

        // sslStream.AuthenticateAsServer(authOpts)
        il.Emit(OpCodes.Ldloc, sslStreamLocal);
        il.Emit(OpCodes.Ldloc, authOptsLocal);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetMethod("AuthenticateAsServer", [typeof(SslServerAuthenticationOptions)])!);

        // socket = new $TlsSocket(); populate
        il.Emit(OpCodes.Newobj, runtime.RequireTls().SocketCtor);
        il.Emit(OpCodes.Stloc, socketLocal);
        EmitTlsPopulateSocket(il,
            loadSocket: () => il.Emit(OpCodes.Ldloc, socketLocal),
            loadClient: () => il.Emit(OpCodes.Ldloc, tcpClientLocal),
            loadSslStream: () => il.Emit(OpCodes.Ldloc, sslStreamLocal), netClientField, netStreamField, socketFields, socketHelpers);
        // server-side: socket._authorized = sslStream.IsAuthenticated
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldloc, sslStreamLocal);
        il.Emit(OpCodes.Callvirt, typeof(SslStream).GetProperty("IsAuthenticated")!.GetGetMethod()!);
        il.Emit(OpCodes.Stfld, socketFields.Authorized);

        // EventLoop.Schedule(new Action(new $TlsAcceptClosure(this, socket).Run))
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Newobj, acceptClosures.Accept.Constructor);
        il.Emit(OpCodes.Ldftn, acceptClosures.Accept.Run);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);

        var handshakeOk = il.DefineLabel();
        il.Emit(OpCodes.Leave, handshakeOk);
        // catch (Exception ex) { close client; schedule 'tlsClientError'; } → continue loop
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, handshakeExceptionLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, tcpClientLocal);
        var noClient = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noClient);
        il.Emit(OpCodes.Ldloc, tcpClientLocal);
        il.Emit(OpCodes.Callvirt, typeof(TcpClient).GetMethod("Close")!);
        il.MarkLabel(noClient);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // EventLoop.Schedule(new Action(new $TlsAcceptErrorClosure(this, ex.Message).Run))
        il.Emit(OpCodes.Call, runtime.EventLoop.GetInstance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, handshakeExceptionLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, acceptClosures.Error.Constructor);
        il.Emit(OpCodes.Ldftn, acceptClosures.Error.Run);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoop.Schedule);
        il.Emit(OpCodes.Leave, handshakeOk);
        il.EndExceptionBlock();
        il.MarkLabel(handshakeOk);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(loopExit);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Finalizes the $TlsServer type after the accept worker body is emitted.
    /// </summary>
    private void EmitTlsServerFinalize(EmittedTlsRuntime tls)
    {
        ((TypeBuilder)tls.ServerCtor.DeclaringType!).CreateType();
    }
}
