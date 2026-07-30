using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// TLS module support for compiled TypeScript: tls.createServer, tls.connect, etc.
/// Uses emitted $TlsSocket and $TlsServer types for standalone DLL support (no SharpTS.dll dependency).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all tls module methods.
    /// </summary>
    private void EmitTlsModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitTlsCreateServer(typeBuilder, runtime);
        EmitTlsConnect(typeBuilder, runtime);
        EmitTlsCreateSocket(typeBuilder, runtime);
        EmitTlsCreateSecureContext(typeBuilder, runtime);
        EmitTlsCheckServerIdentity(typeBuilder, runtime);
        EmitTlsGetCiphers(typeBuilder, runtime);
        EmitTlsRootCertificates(typeBuilder, runtime);
        EmitTlsGetDefaultMinVersion(typeBuilder, runtime);
        EmitTlsGetDefaultMaxVersion(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object TlsCreateServer(object? options, object? callback)
    /// Creates a new $TlsServer instance (pure IL, no SharpTS.dll dependency).
    /// </summary>
    private void EmitTlsCreateServer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsCreateServer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.TlsCreateServer = method;
        runtime.RegisterBuiltInModuleMethod("tls", "createServer", method);
        runtime.RegisterBuiltInModuleMethod("tls", "Server", method); // alias

        var il = method.GetILGenerator();

        // new $TlsServer(options, callback)
        il.Emit(OpCodes.Ldarg_0); // options
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Newobj, runtime.TlsServerCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsConnect(object? portOrOptions, object? hostOrCallback, object? optionsOrNull, object? callbackOrNull)
    /// Creates a new $TlsSocket. Parses port/host/options, registers the callback as a
    /// 'secureConnect' listener, then runs the pure-BCL SslStream handshake on a ThreadPool
    /// thread via $TlsConnectClosure (no SharpTS.dll dependency).
    /// NOTE: The TlsConnect body is deferred to EmitTlsConnectBody (Phase 2) since it
    /// depends on the $TlsConnectClosure type defined after EmitRuntimeClass.
    /// </summary>
    private MethodBuilder? _tlsConnectMethod;

    private void EmitTlsConnect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsConnect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.TlsConnect = method;
        runtime.RegisterBuiltInModuleMethod("tls", "connect", method);
        _tlsConnectMethod = method;
        // Body emitted in EmitTlsConnectBody after $TlsConnectClosure is defined
    }

    /// <summary>
    /// Phase 2: Emits the body of TlsConnect after $TlsConnectClosure is available.
    /// </summary>
    internal void EmitTlsConnectBody(EmittedRuntime runtime)
    {
        var il = _tlsConnectMethod!.GetILGenerator();

        // Parse port from arg0 (double → int)
        var portLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, portLocal);
        var portDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, portDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);
        il.MarkLabel(portDone);

        // Parse host from arg1 (string), default "127.0.0.1"
        var hostLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, "127.0.0.1");
        il.Emit(OpCodes.Stloc, hostLocal);
        var hostDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, hostDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, hostLocal);
        il.MarkLabel(hostDone);

        // Find options dict in args
        var optionsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optionsLocal);
        for (int argIdx = 2; argIdx >= 0; argIdx--)
        {
            var skipOpt = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brfalse, skipOpt);
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, optionsLocal);
            il.MarkLabel(skipOpt);
        }

        // Find callback in args
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);
        for (int argIdx = 3; argIdx >= 1; argIdx--)
        {
            var skipCb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, skipCb);
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Stloc, callbackLocal);
            il.MarkLabel(skipCb);
        }

        // tls.connect(options) form: when port/host weren't given positionally, read them from
        // the options dict (options.port / options.host). Mirrors interp Connect's options branch.
        var optTryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")!;
        var optTmpLocal = il.DeclareLocal(_types.Object);
        var optParseDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, optParseDone);
        // if (port == 0 && options["port"] is double) port = (int)d
        var skipOptPort = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Brtrue, skipOptPort);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldloca, optTmpLocal);
        il.Emit(OpCodes.Callvirt, optTryGet);
        il.Emit(OpCodes.Brfalse, skipOptPort);
        il.Emit(OpCodes.Ldloc, optTmpLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, skipOptPort);
        il.Emit(OpCodes.Ldloc, optTmpLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);
        il.MarkLabel(skipOptPort);
        // if (options["host"] is string) host = it
        var skipOptHost = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "host");
        il.Emit(OpCodes.Ldloca, optTmpLocal);
        il.Emit(OpCodes.Callvirt, optTryGet);
        il.Emit(OpCodes.Brfalse, skipOptHost);
        il.Emit(OpCodes.Ldloc, optTmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, skipOptHost);
        il.Emit(OpCodes.Ldloc, optTmpLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, hostLocal);
        il.MarkLabel(skipOptHost);
        il.MarkLabel(optParseDone);

        // Check rejectUnauthorized
        var rejectLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, rejectLocal);
        var tempLocal = il.DeclareLocal(_types.Object);
        var rejectDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, rejectDone);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "rejectUnauthorized");
        il.Emit(OpCodes.Ldloca, tempLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")!);
        il.Emit(OpCodes.Brfalse, rejectDone);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, rejectDone);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stloc, rejectLocal);
        il.MarkLabel(rejectDone);

        // Extract ALPN protocols
        var alpnLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, alpnLocal);
        var alpnDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, alpnDone);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldstr, "ALPNProtocols");
        il.Emit(OpCodes.Ldloca, tempLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue")!);
        il.Emit(OpCodes.Brfalse, alpnDone);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, alpnDone);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("OfType")!, _types.String));
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(System.Linq.Enumerable).GetMethod("ToArray")!, _types.String));
        il.Emit(OpCodes.Stloc, alpnLocal);
        il.MarkLabel(alpnDone);

        // Create socket
        var socketLocal = il.DeclareLocal(runtime.TlsSocketType);
        il.Emit(OpCodes.Newobj, runtime.TlsSocketCtor);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Stfld, _tlsSocketServernameField);

        // Register the connect callback as a 'secureConnect' listener (Node semantics):
        // the OK closure emits 'secureConnect' once the handshake completes.
        var noConnectCb = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noConnectCb);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldstr, "secureConnect");
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOn);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noConnectCb);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Create $TlsConnectClosure and queue on ThreadPool
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Ldloc, hostLocal);
        il.Emit(OpCodes.Ldloc, rejectLocal);
        il.Emit(OpCodes.Ldloc, alpnLocal);
        il.Emit(OpCodes.Newobj, _tlsConnectClosureCtor);
        var closureLocal = il.DeclareLocal(_tlsConnectClosureType);
        il.Emit(OpCodes.Stloc, closureLocal);

        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldftn, _tlsConnectClosureConnect);
        il.Emit(OpCodes.Newobj, typeof(System.Threading.WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(System.Threading.ThreadPool).GetMethod("QueueUserWorkItem", [typeof(System.Threading.WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsCreateSocket()
    /// Creates a new $TlsSocket instance directly (used by tls.TLSSocket() constructor call).
    /// </summary>
    private void EmitTlsCreateSocket(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsCreateSocket",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.TlsCreateSocket = method;
        runtime.RegisterBuiltInModuleMethod("tls", "TLSSocket", method);

        var il = method.GetILGenerator();

        // new $TlsSocket()
        il.Emit(OpCodes.Newobj, runtime.TlsSocketCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsCreateSecureContext(object? options)
    /// Returns a SecureContext dictionary holding the parsed cert/key/ca/minVersion/maxVersion,
    /// which createServer/connect accept via options.secureContext. Mirrors interp
    /// TlsModuleInterpreter.CreateSecureContext.
    /// </summary>
    private void EmitTlsCreateSecureContext(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsCreateSecureContext",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.TlsCreateSecureContext = method;
        runtime.RegisterBuiltInModuleMethod("tls", "createSecureContext", method);

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var tryGet = dictType.GetMethod("TryGetValue")!;
        var setItem = _types.GetMethod(dictType, "set_Item", _types.String, _types.Object);

        // var ctx = new Dictionary<string,object?>();
        var ctxLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(dictType));
        il.Emit(OpCodes.Stloc, ctxLocal);

        // if (options is Dictionary<string,object?> opts) copy known keys
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, done);
        var optsLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Stloc, optsLocal);
        var tmpLocal = il.DeclareLocal(_types.Object);
        foreach (var key in new[] { "cert", "key", "ca", "minVersion", "maxVersion" })
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, optsLocal);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldloca, tmpLocal);
            il.Emit(OpCodes.Callvirt, tryGet);
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldloc, ctxLocal);
            il.Emit(OpCodes.Ldstr, key);
            il.Emit(OpCodes.Ldloc, tmpLocal);
            il.Emit(OpCodes.Callvirt, setItem);
            il.MarkLabel(skip);
        }
        il.MarkLabel(done);

        il.Emit(OpCodes.Ldloc, ctxLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsGetDefaultMinVersion()
    /// Returns "TLSv1.2" - no reflection needed.
    /// </summary>
    private void EmitTlsGetDefaultMinVersion(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsGetDefaultMinVersion",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            []
        );
        runtime.TlsGetDefaultMinVersion = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "TLSv1.2");
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object TlsGetDefaultMaxVersion()
    /// Returns "TLSv1.3" - no reflection needed.
    /// </summary>
    private void EmitTlsGetDefaultMaxVersion(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TlsGetDefaultMaxVersion",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            []
        );
        runtime.TlsGetDefaultMaxVersion = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "TLSv1.3");
        il.Emit(OpCodes.Ret);
    }
}
