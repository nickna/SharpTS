using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Net module support for compiled TypeScript: net.createServer, net.createConnection, etc.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all net module methods.
    /// </summary>
    private void EmitNetModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitNetCreateServer(typeBuilder, runtime);
        EmitNetCreateConnection(typeBuilder, runtime);
        EmitNetIsIP(typeBuilder, runtime);
        EmitNetIsIPv4(typeBuilder, runtime);
        EmitNetIsIPv6(typeBuilder, runtime);
        EmitNetCreateBlockList(typeBuilder, runtime);
        EmitNetCreateSocketAddress(typeBuilder, runtime);
        EmitNetAutoSelectFamilyDefaults(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the autoSelectFamily default knobs (#1070). Connection establishment
    /// delegates to .NET's TcpClient (which already attempts every resolved address
    /// sequentially), so the knobs are API-compatibility state. Static fields use
    /// inverted defaults (disabled=false ⇒ true; timeout 0 ⇒ 250) so no type
    /// initializer is required.
    /// </summary>
    private void EmitNetAutoSelectFamilyDefaults(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var disabledField = typeBuilder.DefineField("_netAutoSelectFamilyDisabled", _types.Boolean,
            FieldAttributes.Private | FieldAttributes.Static);
        var timeoutField = typeBuilder.DefineField("_netAutoSelectFamilyTimeout", _types.Double,
            FieldAttributes.Private | FieldAttributes.Static);

        // getDefaultAutoSelectFamily(): return !_disabled
        {
            var method = typeBuilder.DefineMethod(
                "NetGetDefaultAutoSelectFamily",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                Type.EmptyTypes
            );
            runtime.NetGetDefaultAutoSelectFamily = method;
            runtime.RegisterBuiltInModuleMethod("net", "getDefaultAutoSelectFamily", method);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, disabledField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
        }

        // setDefaultAutoSelectFamily(v): if (v is bool) _disabled = !(bool)v
        {
            var method = typeBuilder.DefineMethod(
                "NetSetDefaultAutoSelectFamily",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]
            );
            runtime.NetSetDefaultAutoSelectFamily = method;
            runtime.RegisterBuiltInModuleMethod("net", "setDefaultAutoSelectFamily", method);
            var il = method.GetILGenerator();
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, typeof(bool));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Stsfld, disabledField);
            il.MarkLabel(skip);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // getDefaultAutoSelectFamilyAttemptTimeout(): return _timeout > 0 ? _timeout : 250
        {
            var method = typeBuilder.DefineMethod(
                "NetGetDefaultAutoSelectFamilyAttemptTimeout",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                Type.EmptyTypes
            );
            runtime.NetGetDefaultAutoSelectFamilyAttemptTimeout = method;
            runtime.RegisterBuiltInModuleMethod("net", "getDefaultAutoSelectFamilyAttemptTimeout", method);
            var il = method.GetILGenerator();
            var useDefault = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, timeoutField);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Ble_Un, useDefault);
            il.Emit(OpCodes.Ldsfld, timeoutField);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(useDefault);
            il.Emit(OpCodes.Ldc_R8, 250.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }

        // setDefaultAutoSelectFamilyAttemptTimeout(v): if (v is double && v > 0) _timeout = v
        {
            var method = typeBuilder.DefineMethod(
                "NetSetDefaultAutoSelectFamilyAttemptTimeout",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]
            );
            runtime.NetSetDefaultAutoSelectFamilyAttemptTimeout = method;
            runtime.RegisterBuiltInModuleMethod("net", "setDefaultAutoSelectFamilyAttemptTimeout", method);
            var il = method.GetILGenerator();
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Ble_Un, skip);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Stsfld, timeoutField);
            il.MarkLabel(skip);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits: public static object NetCreateBlockList(object? unused) — factory
    /// for the callable form / dynamic dispatch; `new net.BlockList()` compiles
    /// directly to the $BlockList constructor.
    /// </summary>
    private void EmitNetCreateBlockList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateBlockList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.RegisterBuiltInModuleMethod("net", "BlockList", method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, runtime.BlockListCtor!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetCreateSocketAddress(object? options)
    /// </summary>
    private void EmitNetCreateSocketAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateSocketAddress",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.RegisterBuiltInModuleMethod("net", "SocketAddress", method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.SocketAddressCtor!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetCreateServer(object? optionsOrCallback, object? callback)
    /// Creates a $NetServer directly (no reflection needed — standalone DLL support).
    /// Node signature: createServer([options][, connectionListener]) — an options dict
    /// as the first arg carries per-socket settings (highWaterMark) applied to
    /// accepted connections.
    /// </summary>
    private void EmitNetCreateServer(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateServer",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.NetCreateServer = method;
        runtime.RegisterBuiltInModuleMethod("net", "createServer", method);
        runtime.RegisterBuiltInModuleMethod("net", "Server", method); // alias

        var il = method.GetILGenerator();
        var serverLocal = il.DeclareLocal(_netServerTypeBuilder);
        var cbLocal = il.DeclareLocal(_types.Object);

        // callback = (arg0 is Dictionary) ? arg1 : arg0
        var optionsForm = il.DefineLabel();
        var createServer = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, optionsForm);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.Emit(OpCodes.Br, createServer);
        il.MarkLabel(optionsForm);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.MarkLabel(createServer);

        // server = new $NetServer(callback)
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Newobj, runtime.NetServerCtor);
        il.Emit(OpCodes.Stloc, serverLocal);

        // if (arg0 is Dictionary) parse per-socket options
        var noOptions = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, noOptions);
        {
            var valLocal = il.DeclareLocal(_types.Object);

            // highWaterMark (double) → server._socketHwm (#1068)
            var noHwm = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "highWaterMark");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noHwm);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, typeof(double));
            il.Emit(OpCodes.Brfalse, noHwm);
            il.Emit(OpCodes.Ldloc, serverLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stfld, _netServerSocketHwmField);
            il.MarkLabel(noHwm);

            // blockList ($BlockList) → server._blockList (#1069)
            var noBlockList = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "blockList");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noBlockList);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, _blockListTypeBuilder);
            il.Emit(OpCodes.Brfalse, noBlockList);
            il.Emit(OpCodes.Ldloc, serverLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Stfld, _netServerBlockListField);
            il.MarkLabel(noBlockList);

            // allowHalfOpen (bool) → server._socketAllowHalfOpen (#1070)
            var noAho = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "allowHalfOpen");
            il.Emit(OpCodes.Ldloca, valLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, noAho);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Isinst, typeof(bool));
            il.Emit(OpCodes.Brfalse, noAho);
            il.Emit(OpCodes.Ldloc, serverLocal);
            il.Emit(OpCodes.Ldloc, valLocal);
            il.Emit(OpCodes.Unbox_Any, _types.Boolean);
            il.Emit(OpCodes.Stfld, _netServerSocketAllowHalfOpenField);
            il.MarkLabel(noAho);
        }
        il.MarkLabel(noOptions);

        // return server
        il.Emit(OpCodes.Ldloc, serverLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetCreateConnection(object? options, object? hostOrCallback, object? callback)
    /// Creates a $NetSocket directly and calls Connect (no reflection needed).
    /// Node signature: connect(options|port|path[, host][, connectListener]) —
    /// the socket's Connect does the positional-arg parsing.
    /// </summary>
    private void EmitNetCreateConnection(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateConnection",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.NetCreateConnection = method;
        runtime.RegisterBuiltInModuleMethod("net", "createConnection", method);
        runtime.RegisterBuiltInModuleMethod("net", "connect", method); // alias

        var il = method.GetILGenerator();

        // var socket = new $NetSocket()
        var socketLocal = il.DeclareLocal(runtime.NetSocketType);
        il.Emit(OpCodes.Newobj, runtime.NetSocketCtor);
        il.Emit(OpCodes.Stloc, socketLocal);

        // socket.Connect(options, hostOrCallback, callback)
        var noOptions = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, noOptions);

        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldarg_0); // options/port/path
        il.Emit(OpCodes.Ldarg_1); // host or callback
        il.Emit(OpCodes.Ldarg_2); // callback
        il.Emit(OpCodes.Callvirt, runtime.NetSocketConnect);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noOptions);

        // return socket
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetIsIP(object? input)
    /// Returns 4 for IPv4, 6 for IPv6, 0 for invalid.
    /// </summary>
    private void EmitNetIsIP(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetIsIP",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NetIsIP = method;
        runtime.RegisterBuiltInModuleMethod("net", "isIP", method);

        var il = method.GetILGenerator();

        // if (input is not string) return 0.0
        var isStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isStringLabel);

        // IPAddress.TryParse(input as string, out addr)
        var addrLocal = il.DeclareLocal(typeof(IPAddress));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, addrLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);

        var validLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, validLabel);

        // Not valid
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);

        // Check address family
        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);

        var isV6Label = il.DefineLabel();
        il.Emit(OpCodes.Beq, isV6Label);

        // IPv4
        il.Emit(OpCodes.Ldc_R8, 4.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // IPv6
        il.MarkLabel(isV6Label);
        il.Emit(OpCodes.Ldc_R8, 6.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetIsIPv4(object? input)
    /// </summary>
    private void EmitNetIsIPv4(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetIsIPv4",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NetIsIPv4 = method;
        runtime.RegisterBuiltInModuleMethod("net", "isIPv4", method);

        var il = method.GetILGenerator();

        // if (input is not string) return false
        var isStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isStringLabel);

        var addrLocal = il.DeclareLocal(typeof(IPAddress));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, addrLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);

        var validLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, validLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);

        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetwork);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object NetIsIPv6(object? input)
    /// </summary>
    private void EmitNetIsIPv6(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetIsIPv6",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NetIsIPv6 = method;
        runtime.RegisterBuiltInModuleMethod("net", "isIPv6", method);

        var il = method.GetILGenerator();

        var isStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isStringLabel);

        var addrLocal = il.DeclareLocal(typeof(IPAddress));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, addrLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("TryParse", [_types.String, typeof(IPAddress).MakeByRefType()])!);

        var validLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, validLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(validLabel);

        il.Emit(OpCodes.Ldloc, addrLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }
}
