using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $DatagramSocket class extending $EventEmitter for standalone dgram support.
/// This replaces the reflection-based SharpTSDatagramSocket used via Activator.CreateInstance.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDatagramSocket
/// </summary>
public partial class RuntimeEmitter
{
    // Field builders for $DatagramSocket (used across method emitters)
    private TypeBuilder _dgramSocketTypeBuilder = null!;
    private FieldBuilder _dgramClientField = null!;
    private FieldBuilder _dgramFamilyField = null!;
    private FieldBuilder _dgramBoundField = null!;
    private FieldBuilder _dgramClosedField = null!;
    private FieldBuilder _dgramConnectedField = null!;
    private FieldBuilder _dgramConnectedAddressField = null!;
    private FieldBuilder _dgramConnectedPortField = null!;
    private FieldBuilder _dgramReceiveCtsField = null!;
    private FieldBuilder _dgramPendingCloseCallbackField = null!;
    private FieldBuilder _dgramPendingErrorField = null!;
    private MethodBuilder _dgramReceiveWorkerMethod = null!;
    private MethodBuilder _dgramEmitListeningMethod = null!;
    private MethodBuilder _dgramEmitCloseMethod = null!;
    private MethodBuilder _dgramEmitConnectMethod = null!;
    private MethodBuilder _dgramFireCloseCallbackMethod = null!;
    private MethodBuilder _dgramFireBindErrorMethod = null!;
    private TypeBuilder _dgramMessageClosureType = null!;
    private ConstructorBuilder _dgramMessageClosureCtor = null!;
    private MethodBuilder _dgramMessageClosureRun = null!;

    /// <summary>
    /// Phase 1: Defines the $DatagramSocket type, fields, constructor, and all sync methods.
    /// Must be called BEFORE EmitRuntimeClass so DgramCreateSocket can use the constructor.
    /// </summary>
    private void EmitDatagramSocketTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $DatagramSocket extends $EventEmitter
        var typeBuilder = moduleBuilder.DefineType(
            "$DatagramSocket",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType
        );
        _dgramSocketTypeBuilder = typeBuilder;

        // Fields
        _dgramClientField = typeBuilder.DefineField("_client", typeof(UdpClient), FieldAttributes.Private);
        _dgramFamilyField = typeBuilder.DefineField("_family", _types.Int32, FieldAttributes.Private);
        _dgramBoundField = typeBuilder.DefineField("_bound", _types.Boolean, FieldAttributes.Private);
        _dgramClosedField = typeBuilder.DefineField("_closed", _types.Boolean, FieldAttributes.Private);
        _dgramConnectedField = typeBuilder.DefineField("_connected", _types.Boolean, FieldAttributes.Private);
        _dgramConnectedAddressField = typeBuilder.DefineField("_connectedAddress", _types.String, FieldAttributes.Private);
        _dgramConnectedPortField = typeBuilder.DefineField("_connectedPort", _types.Int32, FieldAttributes.Private);
        _dgramReceiveCtsField = typeBuilder.DefineField("_receiveCts", typeof(CancellationTokenSource), FieldAttributes.Private);
        _dgramPendingCloseCallbackField = typeBuilder.DefineField("_pendingCloseCallback", _types.Object, FieldAttributes.Private);
        _dgramPendingErrorField = typeBuilder.DefineField("_pendingError", _types.Object, FieldAttributes.Private);

        // Constructor: public $DatagramSocket(object typeArg)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.DatagramSocketCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        // Call base constructor ($EventEmitter)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // Default _family = 2 (InterNetwork)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_2);
        ctorIL.Emit(OpCodes.Stfld, _dgramFamilyField);

        // if (typeArg?.ToString() == "udp6") _family = 23
        var skipUdp6 = ctorIL.DefineLabel();
        var doneFamily = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Brfalse, skipUdp6);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        ctorIL.Emit(OpCodes.Ldstr, "udp6");
        ctorIL.Emit(OpCodes.Call, _types.String.GetMethod("Equals", [_types.String])!);
        ctorIL.Emit(OpCodes.Brfalse, skipUdp6);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4, 23); // InterNetworkV6
        ctorIL.Emit(OpCodes.Stfld, _dgramFamilyField);
        ctorIL.Emit(OpCodes.Br, doneFamily);

        ctorIL.MarkLabel(skipUdp6);
        ctorIL.MarkLabel(doneFamily);

        // _bound = false, _closed = false, _connected = false (default)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, _dgramBoundField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, _dgramClosedField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, _dgramConnectedField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldstr, "");
        ctorIL.Emit(OpCodes.Stfld, _dgramConnectedAddressField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stfld, _dgramConnectedPortField);

        ctorIL.Emit(OpCodes.Ret);

        // Define receive worker method stub (body emitted in Phase 2)
        // Must be defined before EmitDgramBind which references it.
        _dgramReceiveWorkerMethod = typeBuilder.DefineMethod(
            "_DgramReceiveWorker",
            MethodAttributes.Private,
            typeof(void),
            [_types.Object]
        );

        // Define event-emit helper instance methods used as Action delegate targets
        // for deferred event emission via EventLoop.Schedule. These bind 'this' implicitly.
        EmitDgramEventHelper(typeBuilder, runtime, "_EmitListening", "listening", out _dgramEmitListeningMethod);
        EmitDgramEventHelper(typeBuilder, runtime, "_EmitClose",     "close",     out _dgramEmitCloseMethod);
        EmitDgramEventHelper(typeBuilder, runtime, "_EmitConnect",   "connect",   out _dgramEmitConnectMethod);

        // Define helpers that read pending callback/error fields and dispatch.
        // Used as Action delegate targets to defer user close-callback and bind-error
        // emission to the event loop without needing a separate closure class.
        EmitDgramFireCloseCallbackHelper(typeBuilder, runtime, out _dgramFireCloseCallbackMethod);
        EmitDgramFireBindErrorHelper(typeBuilder, runtime, out _dgramFireBindErrorMethod);

        // Emit all methods (none need InvokeValue, so all go in Phase 1)
        EmitDgramBind(typeBuilder, runtime);
        EmitDgramClose(typeBuilder, runtime);
        EmitDgramAddress(typeBuilder, runtime);
        EmitDgramRemoteAddress(typeBuilder, runtime);
        EmitDgramConnect(typeBuilder, runtime);
        EmitDgramDisconnect(typeBuilder, runtime);
        EmitDgramSetBroadcast(typeBuilder, runtime);
        EmitDgramSetTTL(typeBuilder, runtime);
        EmitDgramSetMulticastTTL(typeBuilder, runtime);
        EmitDgramGetRecvBufferSize(typeBuilder, runtime);
        EmitDgramSetRecvBufferSize(typeBuilder, runtime);
        EmitDgramGetSendBufferSize(typeBuilder, runtime);
        EmitDgramSetSendBufferSize(typeBuilder, runtime);
        EmitDgramSend(typeBuilder, runtime);
        EmitDgramAddMembership(typeBuilder, runtime);
        EmitDgramDropMembership(typeBuilder, runtime);
        EmitDgramSourceSpecificMembership(typeBuilder, runtime, add: true);
        EmitDgramSourceSpecificMembership(typeBuilder, runtime, add: false);
        EmitDgramSetMulticastLoopback(typeBuilder, runtime);
        EmitDgramSetMulticastInterface(typeBuilder, runtime);
        EmitDgramRef(typeBuilder, runtime);
        EmitDgramUnref(typeBuilder, runtime);

        // NOTE: CreateType() is deferred to Phase 2
    }

    /// <summary>
    /// Phase 2: Finalizes the $DatagramSocket type after EmitRuntimeClass.
    /// </summary>
    private void EmitDatagramSocketFinalize(EmittedRuntime runtime)
    {
        _ = _dgramSocketTypeBuilder.CreateType()!;
    }

    /// <summary>
    /// Helper: emits IL to invoke a callback that may be TSFunction or BoundTSFunction.
    /// loadCallback is an action that pushes the callback object onto the stack.
    /// If argCount > 0, loadArgs pushes the args array onto the stack.
    /// </summary>
    private void EmitDgramCallbackInvocation(ILGenerator il, EmittedRuntime runtime,
        Action loadCallback, int argCount, Action<ILGenerator>? loadArgs = null)
    {
        var notTSFunc = il.DefineLabel();
        var notBound = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // Check TSFunction
        loadCallback();
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunc);

        loadCallback();
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        if (argCount == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
        }
        else
        {
            loadArgs?.Invoke(il);
        }
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, doneLabel);

        // Check BoundTSFunction
        il.MarkLabel(notTSFunc);
        loadCallback();
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBound);

        loadCallback();
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        if (argCount == 0)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
        }
        else
        {
            loadArgs?.Invoke(il);
        }
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notBound);
        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Helper: emits IL to emit an event on this (EventEmitter).
    /// </summary>
    private void EmitDgramEmitEvent(ILGenerator il, EmittedRuntime runtime, string eventName)
    {
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldstr, eventName);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop); // discard bool return
    }

    /// <summary>
    /// Helper: defines a private instance method that calls this.Emit(eventName, []).
    /// Used as the target of an Action delegate scheduled on the event loop, so that
    /// 'listening'/'close'/'connect' fire on a future tick instead of synchronously
    /// inside Bind/Close/Connect (matching the interpreter and Node.js semantics).
    /// </summary>
    private void EmitDgramEventHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        string eventName,
        out MethodBuilder methodBuilder)
    {
        methodBuilder = typeBuilder.DefineMethod(
            methodName,
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes);

        var il = methodBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldstr, eventName);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop); // discard bool
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits IL that schedules an Action(this.<paramref name="instanceMethod"/>) on $EventLoop.
    /// Stack on entry: empty. Stack on exit: empty.
    /// </summary>
    private void EmitDgramScheduleAction(
        ILGenerator il,
        EmittedRuntime runtime,
        MethodBuilder instanceMethod)
    {
        // EventLoop.GetInstance()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        // new Action(this, instanceMethod)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, instanceMethod);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        // EventLoop.Schedule(action)
        il.Emit(OpCodes.Call, runtime.EventLoopSchedule);
    }

    /// <summary>
    /// Helper: emits a private instance method `_FireCloseCallback()` that reads
    /// `_pendingCloseCallback` and invokes it as $TSFunction or $BoundTSFunction with
    /// no args. This is the deferred-callback mechanism for Close() — the field is
    /// set by Close() and the method is scheduled via EventLoop.Schedule(new Action(this._FireCloseCallback)).
    /// Close() is idempotent (`if (_closed) return null`), so the field is set at most once.
    /// </summary>
    private void EmitDgramFireCloseCallbackHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        out MethodBuilder methodBuilder)
    {
        methodBuilder = typeBuilder.DefineMethod(
            "_FireCloseCallback",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes);

        var il = methodBuilder.GetILGenerator();

        var notTSFunc = il.DefineLabel();
        var notBound = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // var cb = _pendingCloseCallback; if (cb == null) return;
        var cbLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramPendingCloseCallbackField);
        il.Emit(OpCodes.Stloc, cbLocal);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // _pendingCloseCallback = null  (release reference, single-shot semantics)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _dgramPendingCloseCallbackField);

        // if (cb is TSFunction) ((TSFunction)cb).Invoke(new object[0]);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunc);

        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, doneLabel);

        // else if (cb is BoundTSFunction) ((BoundTSFunction)cb).Invoke(new object[0]);
        il.MarkLabel(notTSFunc);
        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBound);

        il.Emit(OpCodes.Ldloc, cbLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notBound);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits a private instance method `_FireBindError()` that reads
    /// `_pendingError` and emits an 'error' event with it. Used as the deferred
    /// dispatch for Bind()'s catch block.
    /// </summary>
    private void EmitDgramFireBindErrorHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        out MethodBuilder methodBuilder)
    {
        methodBuilder = typeBuilder.DefineMethod(
            "_FireBindError",
            MethodAttributes.Private,
            typeof(void),
            Type.EmptyTypes);

        var il = methodBuilder.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // var err = _pendingError; if (err == null) return;
        var errLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramPendingErrorField);
        il.Emit(OpCodes.Stloc, errLocal);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // _pendingError = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _dgramPendingErrorField);

        // this.Emit("error", new object[] { err });
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits IL to check if a value is a callable (TSFunction or BoundTSFunction).
    /// Pushes true (1) or false (0) onto the stack.
    /// </summary>
    private void EmitDgramIsCallable(ILGenerator il, EmittedRuntime runtime, Action loadValue)
    {
        var isCallable = il.DefineLabel();
        var doneCheck = il.DefineLabel();

        loadValue();
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallable);

        loadValue();
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isCallable);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneCheck);

        il.MarkLabel(isCallable);
        il.Emit(OpCodes.Ldc_I4_1);
        il.MarkLabel(doneCheck);
    }

    /// <summary>
    /// Helper: emits IL to parse port from object (double → int, default to defaultPort).
    /// Result is stored in the returned local.
    /// </summary>
    private LocalBuilder EmitDgramParsePort(ILGenerator il, int argIndex, int defaultPort)
    {
        var portLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4, defaultPort);
        il.Emit(OpCodes.Stloc, portLocal);

        var portDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, portDone);

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, portLocal);

        il.MarkLabel(portDone);
        return portLocal;
    }

    /// <summary>
    /// Helper: emits IL to parse address from object (string, default depends on family).
    /// Result is stored in the returned local.
    /// </summary>
    private LocalBuilder EmitDgramParseAddress(ILGenerator il, int argIndex)
    {
        var addrLocal = il.DeclareLocal(_types.String);

        // Default address based on _family: if _family == 23 => "::" else "0.0.0.0"
        var isV6 = il.DefineLabel();
        var addrDefaultDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Beq, isV6);
        il.Emit(OpCodes.Ldstr, "0.0.0.0");
        il.Emit(OpCodes.Br, addrDefaultDone);
        il.MarkLabel(isV6);
        il.Emit(OpCodes.Ldstr, "::");
        il.MarkLabel(addrDefaultDone);
        il.Emit(OpCodes.Stloc, addrLocal);

        // If arg is string, override
        var addrDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, addrDone);

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, addrLocal);

        il.MarkLabel(addrDone);
        return addrLocal;
    }

    /// <summary>
    /// Helper: emits IL to create a UdpClient with the specified AddressFamily and store in _client.
    /// </summary>
    private void EmitDgramCreateClient(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Newobj, typeof(UdpClient).GetConstructor([typeof(AddressFamily)])!);
        il.Emit(OpCodes.Stfld, _dgramClientField);
    }

    /// <summary>
    /// Helper: emits IL to create a Dictionary&lt;string,object?&gt; with address info.
    /// Expects addressLocal (string), familyLocal (string), portLocal (int) to be set.
    /// </summary>
    private void EmitDgramCreateAddressDict(ILGenerator il, LocalBuilder addressLocal,
        LocalBuilder familyLocal, LocalBuilder portLocal)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["address"] = addressStr
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldloc, addressLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // dict["family"] = familyStr
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldloc, familyLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        // dict["port"] = (double)port
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("set_Item", [_types.String, _types.Object])!);

        il.Emit(OpCodes.Ldloc, dictLocal);
    }

    /// <summary>
    /// Helper: emits IL to compute family string from _family field.
    /// Returns a local containing "IPv4" or "IPv6".
    /// </summary>
    private LocalBuilder EmitDgramFamilyString(ILGenerator il)
    {
        var familyLocal = il.DeclareLocal(_types.String);
        var isV6Label = il.DefineLabel();
        var familyDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Beq, isV6Label);
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Br, familyDone);
        il.MarkLabel(isV6Label);
        il.Emit(OpCodes.Ldstr, "IPv6");
        il.MarkLabel(familyDone);
        il.Emit(OpCodes.Stloc, familyLocal);
        return familyLocal;
    }

    // ─────────────────────────────── Individual method emitters ───────────────────────────────

    /// <summary>
    /// Emits: public object Bind(object portArg, object addressArg, object callbackArg)
    /// Binds the socket to the given port/address, emits 'listening' event.
    /// </summary>
    private void EmitDgramBind(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Bind",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Detect callback in any of the 3 args
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Check arg3 first (callbackArg)
        var checkArg2 = il.DefineLabel();
        var checkArg1 = il.DefineLabel();
        var callbackFound = il.DefineLabel();

        EmitDgramIsCallable(il, runtime, () => il.Emit(OpCodes.Ldarg_3));
        il.Emit(OpCodes.Brfalse, checkArg2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, callbackFound);

        // Check arg2 (addressArg)
        il.MarkLabel(checkArg2);
        EmitDgramIsCallable(il, runtime, () => il.Emit(OpCodes.Ldarg_2));
        il.Emit(OpCodes.Brfalse, checkArg1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, callbackFound);

        // Check arg1 (portArg)
        il.MarkLabel(checkArg1);
        EmitDgramIsCallable(il, runtime, () => il.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Brfalse, callbackFound);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.MarkLabel(callbackFound);

        // Parse port (default 0)
        var portLocal = EmitDgramParsePort(il, 1, 0);

        // Parse address (default based on family)
        var addressLocal = EmitDgramParseAddress(il, 2);

        // If callback != null, register it as a one-time 'listening' listener.
        // This matches the interpreter's `Once("listening", callback)` and Node's
        // documented bind(port, addr, cb) behavior. The callback then fires as part of
        // the deferred 'listening' event below, after Bind() has returned to the caller.
        var noCallback = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, noCallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "listening");
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOnce);
        il.Emit(OpCodes.Pop); // discard returned this
        il.MarkLabel(noCallback);

        // Try/catch block for binding
        var tryStart = il.BeginExceptionBlock();

        // Create UdpClient if null
        var clientExists = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brtrue, clientExists);
        EmitDgramCreateClient(il);
        il.MarkLabel(clientExists);

        // _client.Client.Bind(new IPEndPoint(IPAddress.Parse(address), port))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, addressLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Newobj, typeof(IPEndPoint).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Bind", [typeof(EndPoint)])!);

        // _bound = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _dgramBoundField);

        // _receiveCts = new CancellationTokenSource()
        // Must be set before queueing the receive worker so the worker observes a non-null CTS.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(CancellationTokenSource).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _dgramReceiveCtsField);

        // EventLoop.Ref() to keep process alive while socket is bound
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Start receive loop on ThreadPool
        // ThreadPool.QueueUserWorkItem(new WaitCallback(this._DgramReceiveWorker))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, _dgramReceiveWorkerMethod);
        il.Emit(OpCodes.Newobj, typeof(WaitCallback).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(ThreadPool).GetMethod("QueueUserWorkItem", [typeof(WaitCallback)])!);
        il.Emit(OpCodes.Pop);

        // Schedule 'listening' event on the event loop (NOT synchronous).
        // Node guarantees bind() is async — listening fires on a future tick.
        EmitDgramScheduleAction(il, runtime, _dgramEmitListeningMethod);

        // Catch block: store the exception in _pendingError and schedule _FireBindError
        // on the event loop. This defers the 'error' event so user-attached error handlers
        // (which may be attached after Bind() returns) can still observe it.
        // On entry to a catch block, the exception is on the evaluation stack.
        il.BeginCatchBlock(typeof(Exception));
        var exLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLocal);     // exception → exLocal
        il.Emit(OpCodes.Ldarg_0);            // this
        il.Emit(OpCodes.Ldloc, exLocal);     // exception
        il.Emit(OpCodes.Stfld, _dgramPendingErrorField);  // this._pendingError = exception
        // EventLoop.Schedule(new Action(this._FireBindError))
        EmitDgramScheduleAction(il, runtime, _dgramFireBindErrorMethod);
        il.EndExceptionBlock();

        // return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Close(object callbackArg)
    /// </summary>
    private void EmitDgramClose(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if (_closed) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClosedField);
        il.Emit(OpCodes.Brtrue, returnNull);

        // _closed = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _dgramClosedField);

        // Cancel the receive worker BEFORE disposing the client. This is the cooperative
        // wakeup mechanism: ReceiveAsync(token) observes the cancellation at the runtime
        // layer (not the kernel), so it works on macOS/BSD where closing a socket from
        // another thread does not reliably interrupt a blocked recvfrom().
        var noCts = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramReceiveCtsField);
        il.Emit(OpCodes.Brfalse, noCts);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramReceiveCtsField);
        il.Emit(OpCodes.Callvirt, typeof(CancellationTokenSource).GetMethod("Cancel", Type.EmptyTypes)!);
        il.MarkLabel(noCts);

        // if _client != null: _client.Close(), _client.Dispose()
        var noClient = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, noClient);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Close", Type.EmptyTypes)!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Dispose", Type.EmptyTypes)!);

        // _client = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _dgramClientField);

        il.MarkLabel(noClient);

        // EventLoop.Unref() only if socket was bound (i.e., Ref() was called during bind)
        var skipUnref = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramBoundField);
        il.Emit(OpCodes.Brfalse, skipUnref);
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);
        il.MarkLabel(skipUnref);

        // Schedule the user close callback (if any) on the event loop, before the
        // 'close' event. The interpreter does the same: ScheduleTimer(callback) then
        // ScheduleTimer(EmitEvent("close")). FIFO ordering on the queue preserves this.
        // Close() is gated by `if (_closed) return null` so this runs at most once,
        // making the field-based handoff safe (no concurrent close calls).
        var noCallback = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noCallback);
        // _pendingCloseCallback = callback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _dgramPendingCloseCallbackField);
        // EventLoop.Schedule(new Action(this._FireCloseCallback))
        EmitDgramScheduleAction(il, runtime, _dgramFireCloseCallbackMethod);
        il.MarkLabel(noCallback);

        // Schedule 'close' event (deferred to a future tick)
        EmitDgramScheduleAction(il, runtime, _dgramEmitCloseMethod);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Address()
    /// Returns a Dictionary with address, family, port from the bound socket.
    /// </summary>
    private void EmitDgramAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Address",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var returnEmpty = il.DefineLabel();

        // if _client == null, return empty dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // var socket = _client.Client
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);

        // Check if socket is null
        var socketLocal = il.DeclareLocal(typeof(Socket));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, socketLocal);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // var ep = socket.LocalEndPoint as IPEndPoint
        il.Emit(OpCodes.Ldloc, socketLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("LocalEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, typeof(IPEndPoint));

        var epLocal = il.DeclareLocal(typeof(IPEndPoint));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, epLocal);
        il.Emit(OpCodes.Brfalse, returnEmpty);

        // Extract address string from endpoint
        var addrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Address")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, addrLocal);

        // Extract port from endpoint
        var portLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, epLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, portLocal);

        // Family string
        var familyLocal = EmitDgramFamilyString(il);

        // Create and return dictionary
        EmitDgramCreateAddressDict(il, addrLocal, familyLocal, portLocal);
        il.Emit(OpCodes.Ret);

        // Return empty dictionary
        il.MarkLabel(returnEmpty);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object RemoteAddress()
    /// Returns remote address info or throws if not connected.
    /// </summary>
    private void EmitDgramRemoteAddress(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RemoteAddress",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // if (!_connected) throw ERR_SOCKET_DGRAM_NOT_CONNECTED (#1071)
        var isConnected = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramConnectedField);
        il.Emit(OpCodes.Brtrue, isConnected);

        EmitDgramThrowCoded(il, runtime, "ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");

        il.MarkLabel(isConnected);

        // Build address dict from stored fields
        var addrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramConnectedAddressField);
        il.Emit(OpCodes.Stloc, addrLocal);

        var portLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramConnectedPortField);
        il.Emit(OpCodes.Stloc, portLocal);

        var familyLocal = EmitDgramFamilyString(il);

        EmitDgramCreateAddressDict(il, addrLocal, familyLocal, portLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Connect(object portArg, object addressArg, object callbackArg)
    /// </summary>
    private void EmitDgramConnect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Connect",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Parse port
        var portLocal = EmitDgramParsePort(il, 1, 0);

        // Parse address (default based on family)
        var addressLocal = EmitDgramParseAddress(il, 2);

        // If callback != null, register as one-time 'connect' listener (mirrors interpreter).
        var noCallback = il.DefineLabel();
        EmitDgramIsCallable(il, runtime, () => il.Emit(OpCodes.Ldarg_3));
        il.Emit(OpCodes.Brfalse, noCallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "connect");
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOnce);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noCallback);

        // Create UdpClient if null
        var clientExists = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brtrue, clientExists);
        EmitDgramCreateClient(il);
        il.MarkLabel(clientExists);

        // _client.Connect(IPAddress.Parse(address), port)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldloc, addressLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Newobj, typeof(IPEndPoint).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Connect", [typeof(IPEndPoint)])!);

        // _connected = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _dgramConnectedField);

        // _connectedAddress = address
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, addressLocal);
        il.Emit(OpCodes.Stfld, _dgramConnectedAddressField);

        // _connectedPort = port
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, portLocal);
        il.Emit(OpCodes.Stfld, _dgramConnectedPortField);

        // Schedule 'connect' event on the event loop. The Once-registered callback (if any)
        // fires as part of this event.
        EmitDgramScheduleAction(il, runtime, _dgramEmitConnectMethod);

        // return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Disconnect()
    /// </summary>
    private void EmitDgramDisconnect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Disconnect",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();

        // if (!_connected) throw ERR_SOCKET_DGRAM_NOT_CONNECTED (#1071)
        var isConnected = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramConnectedField);
        il.Emit(OpCodes.Brtrue, isConnected);

        EmitDgramThrowCoded(il, runtime, "ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");

        il.MarkLabel(isConnected);

        // Disconnect by connecting socket to Any:0 (may throw on macOS/BSD — swallow)
        // try { _client.Client.Connect(new IPEndPoint(IPAddress.Any, 0)) } catch { }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldsfld, typeof(IPAddress).GetField("Any")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, typeof(IPEndPoint).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("Connect", [typeof(EndPoint)])!);
        var afterDisconnect = il.DefineLabel();
        il.Emit(OpCodes.Leave, afterDisconnect);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop); // On macOS/BSD, connecting to Any:0 may throw
        il.Emit(OpCodes.Leave, afterDisconnect);
        il.EndExceptionBlock();
        il.MarkLabel(afterDisconnect);

        // _connected = false (always, regardless of socket-level result)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _dgramConnectedField);

        // _connectedAddress = ""
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stfld, _dgramConnectedAddressField);

        // _connectedPort = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _dgramConnectedPortField);

        // return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object SetBroadcast(object flagArg)
    /// </summary>
    private void EmitDgramSetBroadcast(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetBroadcast",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if _client == null, return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnNull);

        // Determine truthy: arg is bool true, or non-null non-false
        var setTrue = il.DefineLabel();
        var setFalse = il.DefineLabel();
        var setBroadcastDone = il.DefineLabel();

        // Check if arg is boxed bool
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, setTrue); // non-bool truthy check: if non-null, set true

        // It's a bool - unbox and use directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("EnableBroadcast")!.GetSetMethod()!);
        il.Emit(OpCodes.Br, returnNull);

        // Non-bool: if non-null, set true
        il.MarkLabel(setTrue);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, setFalse);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("EnableBroadcast")!.GetSetMethod()!);
        il.Emit(OpCodes.Br, returnNull);

        il.MarkLabel(setFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("EnableBroadcast")!.GetSetMethod()!);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object SetTTL(object ttlArg)
    /// </summary>
    private void EmitDgramSetTTL(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetTTL",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if _client == null, return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnNull);

        // if arg is not double, return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnNull);

        // _client.Ttl = (short)(double)arg
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I2);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Ttl")!.GetSetMethod()!);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object SetMulticastTTL(object ttlArg)
    /// </summary>
    private void EmitDgramSetMulticastTTL(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetMulticastTTL",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if _client == null, return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnNull);

        // if arg is not double, return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnNull);

        // _client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, (int)ttl)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionLevel.IP);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionName.MulticastTimeToLive);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("SetSocketOption", [typeof(SocketOptionLevel), typeof(SocketOptionName), _types.Int32])!);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetRecvBufferSize()
    /// </summary>
    private void EmitDgramGetRecvBufferSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetRecvBufferSize",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();

        // if (!_bound) throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramBoundField);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // return (double)_client.Client.ReceiveBufferSize
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("ReceiveBufferSize")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Not bound");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public object SetRecvBufferSize(object size)
    /// </summary>
    private void EmitDgramSetRecvBufferSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetRecvBufferSize",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if _client == null, return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnNull);

        // if arg is not double, return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnNull);

        // _client.Client.ReceiveBufferSize = (int)(double)size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("ReceiveBufferSize")!.GetSetMethod()!);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object GetSendBufferSize()
    /// </summary>
    private void EmitDgramGetSendBufferSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetSendBufferSize",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var throwLabel = il.DefineLabel();

        // if (!_bound) throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramBoundField);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // return (double)_client.Client.SendBufferSize
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("SendBufferSize")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: Not bound");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public object SetSendBufferSize(object size)
    /// </summary>
    private void EmitDgramSetSendBufferSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetSendBufferSize",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var returnNull = il.DefineLabel();

        // if _client == null, return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, returnNull);

        // if arg is not double, return null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnNull);

        // _client.Client.SendBufferSize = (int)(double)size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetProperty("SendBufferSize")!.GetSetMethod()!);

        il.MarkLabel(returnNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Send(object msg, object portOrCb, object addrOrCb, object cb1, object cb2, object cb3)
    /// Sends data via UDP. If connected, sends to connected endpoint; otherwise parses port/address.
    /// </summary>
    private void EmitDgramSend(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Send",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Convert msg to bytes:
        // - If msg is a $Buffer, use GetData() to get raw bytes
        // - Otherwise, use Encoding.UTF8.GetBytes(msg?.ToString() ?? "")
        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var bytesReadyLabel = il.DefineLabel();

        // Check if msg is $Buffer
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        var notBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBufferLabel);

        // Buffer path: bytes = ((TSBuffer)msg).GetData()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, bytesLocal);
        il.Emit(OpCodes.Br, bytesReadyLabel);

        // String path: bytes = Encoding.UTF8.GetBytes(msg?.ToString() ?? "")
        il.MarkLabel(notBufferLabel);
        var msgStrLocal = il.DeclareLocal(_types.String);
        var msgNullLabel = il.DefineLabel();
        var msgDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1); // msg
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, msgNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, msgDoneLabel);
        il.MarkLabel(msgNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(msgDoneLabel);
        il.Emit(OpCodes.Stloc, msgStrLocal);

        il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, msgStrLocal);
        il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", [_types.String])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        il.MarkLabel(bytesReadyLabel);

        // Create client if null
        var clientExists = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brtrue, clientExists);
        EmitDgramCreateClient(il);
        il.MarkLabel(clientExists);

        // Detect callback: scan args 6,5,4,3,2 for callable
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Check args from right to left for callback
        for (int argIdx = 6; argIdx >= 2; argIdx--)
        {
            var skipCb = il.DefineLabel();
            EmitDgramIsCallable(il, runtime, () => il.Emit(OpCodes.Ldarg, argIdx));
            il.Emit(OpCodes.Brfalse, skipCb);
            il.Emit(OpCodes.Ldarg, argIdx);
            il.Emit(OpCodes.Stloc, callbackLocal);
            il.MarkLabel(skipCb);
        }

        // Destination validation (#1071, Node semantics): a connected socket rejects
        // an explicit port; an unconnected socket requires one. Thrown synchronously.
        {
            var validationDone = il.DefineLabel();
            var notConnectedCheck = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _dgramConnectedField);
            il.Emit(OpCodes.Brfalse, notConnectedCheck);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brfalse, validationDone);
            EmitDgramThrowCoded(il, runtime, "ERR_SOCKET_DGRAM_IS_CONNECTED", "Already connected");
            il.MarkLabel(notConnectedCheck);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brtrue, validationDone);
            EmitDgramThrowCoded(il, runtime, "ERR_SOCKET_DGRAM_NOT_CONNECTED", "Not connected");
            il.MarkLabel(validationDone);
        }

        // Try/catch for send
        var tryStart = il.BeginExceptionBlock();

        // Branch: if _connected, send without endpoint; else parse port/addr
        var notConnected = il.DefineLabel();
        var sendDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramConnectedField);
        il.Emit(OpCodes.Brfalse, notConnected);

        // Connected: _client.Send(bytes, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Send", [typeof(byte[]), _types.Int32])!);
        il.Emit(OpCodes.Pop); // discard int return
        il.Emit(OpCodes.Br, sendDone);

        // Not connected: parse port from arg2 (portOrCb), address from arg3 (addrOrCb)
        il.MarkLabel(notConnected);

        // Parse port from arg2
        var sendPortLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, sendPortLocal);
        var portParsed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, portParsed);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, sendPortLocal);
        il.MarkLabel(portParsed);

        // Parse address from arg3
        var sendAddrLocal = il.DeclareLocal(_types.String);
        // Default address based on family
        var addrIsV6 = il.DefineLabel();
        var addrDefaultDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Beq, addrIsV6);
        il.Emit(OpCodes.Ldstr, "127.0.0.1");
        il.Emit(OpCodes.Br, addrDefaultDone);
        il.MarkLabel(addrIsV6);
        il.Emit(OpCodes.Ldstr, "::1");
        il.MarkLabel(addrDefaultDone);
        il.Emit(OpCodes.Stloc, sendAddrLocal);

        var addrParsed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, addrParsed);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, sendAddrLocal);
        il.MarkLabel(addrParsed);

        // _client.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(addr), port))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, sendAddrLocal);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Ldloc, sendPortLocal);
        il.Emit(OpCodes.Newobj, typeof(IPEndPoint).GetConstructor([typeof(IPAddress), _types.Int32])!);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("Send", [typeof(byte[]), _types.Int32, typeof(IPEndPoint)])!);
        il.Emit(OpCodes.Pop); // discard int return

        il.MarkLabel(sendDone);

        // Call callback if provided (no error)
        EmitDgramCallbackInvocation(il, runtime, () => il.Emit(OpCodes.Ldloc, callbackLocal), 0);

        // Catch: emit error event
        il.BeginCatchBlock(typeof(Exception));
        var sendExLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, sendExLocal);

        // Emit 'error' event with the exception
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, sendExLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.EndExceptionBlock();

        // return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: throws a guest error carrying a Node error code:
    /// throw CreateException(new $Error(message) { Code = code })
    /// </summary>
    private void EmitDgramThrowCoded(ILGenerator il, EmittedRuntime runtime, string code, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, code);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeSetter);
        il.Emit(OpCodes.Call, runtime.CreateException);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Helper: if (_client == null) _client = new UdpClient(family) — lazily creates
    /// the handle so pre-bind option setters work (mirrors SharpTSDatagramSocket.EnsureClient).
    /// </summary>
    private void EmitDgramEnsureClient(ILGenerator il)
    {
        var exists = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brtrue, exists);
        EmitDgramCreateClient(il);
        il.MarkLabel(exists);
    }

    /// <summary>
    /// Emits: public object AddMembership(object multicastAddr, object localAddr)
    /// UdpClient.JoinMulticastGroup with the optional local interface address (#1071 —
    /// previously a silent no-op stub, diverging from the interpreter).
    /// </summary>
    private void EmitDgramAddMembership(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "AddMembership",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var done = il.DefineLabel();

        // if (_client == null || multicastAddr is not string) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, done);

        var noLocal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noLocal);

        // _client.JoinMulticastGroup(Parse(mcast), Parse(local))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("JoinMulticastGroup", [typeof(IPAddress), typeof(IPAddress)])!);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(noLocal);
        // _client.JoinMulticastGroup(Parse(mcast))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("JoinMulticastGroup", [typeof(IPAddress)])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object DropMembership(object multicastAddr, object localAddr)
    /// UdpClient.DropMulticastGroup (#1071 — previously a silent no-op stub). The
    /// optional interface matters: Linux requires IP_DROP_MEMBERSHIP to match the
    /// join's (group, interface) tuple, so an interface-scoped join must be dropped
    /// with the same interface (Windows matches by group alone).
    /// </summary>
    private void EmitDgramDropMembership(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DropMembership",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, done);

        // Interface-scoped IPv4 drop: SetSocketOption(IP, DropMembership, new MulticastOption(mcast, local))
        var noLocal = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, noLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Beq, noLocal); // v6: interface form is an ifindex — fall through to the plain drop

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionLevel.IP);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionName.DropMembership);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Newobj, typeof(MulticastOption).GetConstructor([typeof(IPAddress), typeof(IPAddress)])!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("SetSocketOption", [typeof(SocketOptionLevel), typeof(SocketOptionName), _types.Object])!);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(noLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetMethod("DropMulticastGroup", [typeof(IPAddress)])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object AddSourceSpecificMembership/DropSourceSpecificMembership
    /// (sourceAddr, groupAddr[, iface]) — IPv4 ip_mreq_source via SetSocketOption;
    /// field order differs per platform (Windows: group,source,iface; Unix:
    /// group,iface,source). udp6 throws — no portable MCAST_JOIN_SOURCE_GROUP in .NET.
    /// Mirrors SharpTSDatagramSocket.SourceSpecificMembership (#1071).
    /// </summary>
    private void EmitDgramSourceSpecificMembership(TypeBuilder typeBuilder, EmittedRuntime runtime, bool add)
    {
        var method = typeBuilder.DefineMethod(
            add ? "AddSourceSpecificMembership" : "DropSourceSpecificMembership",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var mreqLocal = il.DeclareLocal(_types.ByteArray);
        var ifaceLocal = il.DeclareLocal(typeof(IPAddress));
        var argsOk = il.DefineLabel();
        var familyOk = il.DefineLabel();

        // if (arg1 is not string || arg2 is not string) throw ERR_INVALID_ARG_TYPE
        var badArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, badArgs);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, argsOk);
        il.MarkLabel(badArgs);
        EmitDgramThrowCoded(il, runtime, "ERR_INVALID_ARG_TYPE",
            "The \"sourceAddress\" and \"groupAddress\" arguments must be of type string");
        il.MarkLabel(argsOk);

        // if (_family == InterNetworkV6) throw — documented ceiling
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Bne_Un, familyOk);
        EmitDgramThrowCoded(il, runtime, "ERR_INVALID_ARG_VALUE",
            "Source-specific multicast is not supported for udp6 sockets on this runtime");
        il.MarkLabel(familyOk);

        EmitDgramEnsureClient(il);

        // mreq = new byte[12]
        il.Emit(OpCodes.Ldc_I4, 12);
        il.Emit(OpCodes.Newarr, typeof(byte));
        il.Emit(OpCodes.Stloc, mreqLocal);

        // Array.Copy(Parse(group).GetAddressBytes(), 0, mreq, 0, 4)
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, mreqLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        // iface = (arg3 is string) ? Parse(arg3) : IPAddress.Any
        var haveIface = il.DefineLabel();
        var ifaceDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, haveIface);
        il.Emit(OpCodes.Ldsfld, typeof(IPAddress).GetField("Any")!);
        il.Emit(OpCodes.Stloc, ifaceLocal);
        il.Emit(OpCodes.Br, ifaceDone);
        il.MarkLabel(haveIface);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Stloc, ifaceLocal);
        il.MarkLabel(ifaceDone);

        // Platform-dependent packing of source/interface at offsets 4/8
        var unixPack = il.DefineLabel();
        var packDone = il.DefineLabel();
        il.Emit(OpCodes.Call, typeof(OperatingSystem).GetMethod("IsWindows")!);
        il.Emit(OpCodes.Brfalse, unixPack);
        // Windows: source@4, iface@8
        EmitDgramCopyAddressBytes(il, mreqLocal, 4, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        });
        EmitDgramCopyAddressBytes(il, mreqLocal, 8, () => il.Emit(OpCodes.Ldloc, ifaceLocal));
        il.Emit(OpCodes.Br, packDone);
        il.MarkLabel(unixPack);
        // Unix: iface@4, source@8
        EmitDgramCopyAddressBytes(il, mreqLocal, 4, () => il.Emit(OpCodes.Ldloc, ifaceLocal));
        EmitDgramCopyAddressBytes(il, mreqLocal, 8, () =>
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        });
        il.MarkLabel(packDone);

        // _client.Client.SetSocketOption(IP, Add/DropSourceMembership, mreq)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionLevel.IP);
        il.Emit(OpCodes.Ldc_I4, (int)(add ? SocketOptionName.AddSourceMembership : SocketOptionName.DropSourceMembership));
        il.Emit(OpCodes.Ldloc, mreqLocal);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("SetSocketOption", [typeof(SocketOptionLevel), typeof(SocketOptionName), _types.ByteArray])!);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: Array.Copy(loadAddress().GetAddressBytes(), 0, mreq, offset, 4)
    /// </summary>
    private void EmitDgramCopyAddressBytes(ILGenerator il, LocalBuilder mreqLocal, int offset, Action loadAddress)
    {
        loadAddress();
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, mreqLocal);
        il.Emit(OpCodes.Ldc_I4, offset);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);
    }

    /// <summary>
    /// Emits: public object SetMulticastLoopback(object flag)
    /// UdpClient.MulticastLoopback picks the right option level from the family (#1071).
    /// </summary>
    private void EmitDgramSetMulticastLoopback(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetMulticastLoopback",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var flagLocal = il.DeclareLocal(_types.Boolean);

        EmitDgramEnsureClient(il);

        // flag = (arg is bool b && b) || (arg is double d && d != 0)
        var setTrue = il.DefineLabel();
        var setFalse = il.DefineLabel();
        var flagDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, setFalse);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, setTrue);
        il.Emit(OpCodes.Br, setFalse);
        il.MarkLabel(setTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, flagLocal);
        il.Emit(OpCodes.Br, flagDone);
        il.MarkLabel(setFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, flagLocal);
        il.MarkLabel(flagDone);

        // _client.MulticastLoopback = flag
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldloc, flagLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("MulticastLoopback")!.GetSetMethod()!);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object SetMulticastInterface(object iface)
    /// IPv4: interface address bytes; IPv6: scope id (#1071).
    /// </summary>
    private void EmitDgramSetMulticastInterface(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetMulticastInterface",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var ipLocal = il.DeclareLocal(typeof(IPAddress));
        var argOk = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, argOk);
        EmitDgramThrowCoded(il, runtime, "ERR_INVALID_ARG_TYPE",
            "The \"multicastInterface\" argument must be of type string");
        il.MarkLabel(argOk);

        EmitDgramEnsureClient(il);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, typeof(IPAddress).GetMethod("Parse", [_types.String])!);
        il.Emit(OpCodes.Stloc, ipLocal);

        var v6Path = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramFamilyField);
        il.Emit(OpCodes.Ldc_I4, 23);
        il.Emit(OpCodes.Beq, v6Path);

        // IPv4: SetSocketOption(IP, MulticastInterface, ip.GetAddressBytes())
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionLevel.IP);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionName.MulticastInterface);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetMethod("GetAddressBytes")!);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("SetSocketOption", [typeof(SocketOptionLevel), typeof(SocketOptionName), _types.ByteArray])!);
        il.Emit(OpCodes.Br, done);

        // IPv6: SetSocketOption(IPv6, MulticastInterface, (int)ip.ScopeId)
        il.MarkLabel(v6Path);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient).GetProperty("Client")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionLevel.IPv6);
        il.Emit(OpCodes.Ldc_I4, (int)SocketOptionName.MulticastInterface);
        il.Emit(OpCodes.Ldloc, ipLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPAddress).GetProperty("ScopeId")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, typeof(Socket).GetMethod("SetSocketOption", [typeof(SocketOptionLevel), typeof(SocketOptionName), _types.Int32])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Ref()
    /// No-op, returns this.
    /// </summary>
    private void EmitDgramRef(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Ref",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Unref()
    /// No-op, returns this.
    /// </summary>
    private void EmitDgramUnref(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Unref",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

}
