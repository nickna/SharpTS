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
        EmitNetCreateSocket(typeBuilder, runtime);
        EmitNetCreateBlockList(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object NetCreateBlockList() — creates the opaque
    /// native handle used by the TypeScript BlockList facade.
    /// </summary>
    private void EmitNetCreateBlockList(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateBlockList",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.NetCreateBlockList = method;
        runtime.RegisterBuiltInModuleMethod("primitive:net", "createBlockList", method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, runtime.BlockListCtor!);
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
        runtime.RegisterBuiltInModuleMethod("primitive:net", "createServer", method);

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
        runtime.RegisterBuiltInModuleMethod("primitive:net", "createConnection", method);

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
    /// Creates an unconnected native Socket and applies constructor options.
    /// The public callable/newable Socket export lives in stdlib/node/net.ts.
    /// </summary>
    private void EmitNetCreateSocket(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NetCreateSocket",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NetCreateSocket = method;
        runtime.RegisterBuiltInModuleMethod("primitive:net", "createSocket", method);

        var il = method.GetILGenerator();
        var socketLocal = il.DeclareLocal(runtime.NetSocketType);
        il.Emit(OpCodes.Newobj, runtime.NetSocketCtor);
        il.Emit(OpCodes.Stloc, socketLocal);

        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, done);

        var valueLocal = il.DeclareLocal(_types.Object);
        var applyHwm = il.DefineLabel();
        var tryPlainHwm = il.DefineLabel();
        var noHwm = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "writableHighWaterMark");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tryPlainHwm);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, applyHwm);

        il.MarkLabel(tryPlainHwm);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "highWaterMark");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noHwm);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, noHwm);

        il.MarkLabel(applyHwm);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stfld, _netSocketWritableHwmField);
        il.MarkLabel(noHwm);

        var noAllowHalfOpen = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "allowHalfOpen");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, noAllowHalfOpen);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, noAllowHalfOpen);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stfld, _netSocketAllowHalfOpenField);
        il.MarkLabel(noAllowHalfOpen);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Ret);
    }

}
