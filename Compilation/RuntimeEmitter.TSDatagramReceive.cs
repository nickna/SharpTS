using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the dgram receive loop infrastructure:
/// - $DgramMessageClosure: Carries received data + socket ref, dispatches 'message' event on EventLoop
/// - _DgramReceiveWorker body: Blocking Receive loop on ThreadPool
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $DgramMessageClosure class used to schedule 'message' events
    /// on the EventLoop from the background receive thread.
    /// Fields: _socket ($DatagramSocket), _data (byte[]), _remoteEP (IPEndPoint)
    /// Run(): builds msg buffer + rinfo dict, calls _socket.Emit("message", [msg, rinfo])
    /// </summary>
    private void EmitDgramMessageClosureClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$DgramMessageClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields — use _dgramSocketTypeBuilder so Callvirt resolves Emit correctly
        var socketField = typeBuilder.DefineField("_socket", _dgramSocketTypeBuilder, FieldAttributes.Private);
        var dataField = typeBuilder.DefineField("_data", typeof(byte[]), FieldAttributes.Private);
        var addressField = typeBuilder.DefineField("_address", _types.String, FieldAttributes.Private);
        var familyField = typeBuilder.DefineField("_family", _types.String, FieldAttributes.Private);
        var portField = typeBuilder.DefineField("_port", _types.Double, FieldAttributes.Private);
        var sizeField = typeBuilder.DefineField("_size", _types.Double, FieldAttributes.Private);

        // Constructor: ($DatagramSocket socket, byte[] data, string address, string family, double port, double size)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_dgramSocketTypeBuilder, typeof(byte[]), _types.String, _types.String, _types.Double, _types.Double]
        );
        _dgramMessageClosureCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, socketField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, dataField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_3);
        ctorIL.Emit(OpCodes.Stfld, addressField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 4);
        ctorIL.Emit(OpCodes.Stfld, familyField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 5);
        ctorIL.Emit(OpCodes.Stfld, portField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg, 6);
        ctorIL.Emit(OpCodes.Stfld, sizeField);
        ctorIL.Emit(OpCodes.Ret);

        // Run(): void
        // Creates a Buffer from _data, builds rinfo dict, calls _socket.Emit("message", [msg, rinfo])
        var runMethod = typeBuilder.DefineMethod(
            "Run",
            MethodAttributes.Public,
            typeof(void),
            Type.EmptyTypes
        );
        _dgramMessageClosureRun = runMethod;

        var il = runMethod.GetILGenerator();

        // Build the msg as a $Buffer from the raw byte[] data
        var msgLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, dataField);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor); // new $Buffer(byte[] data)
        il.Emit(OpCodes.Stloc, msgLocal);

        // Build rinfo dict: { address, family, port, size }
        var dictType = _types.DictionaryStringObject;
        var dictCtor = _types.GetDefaultConstructor(dictType);
        var dictAdd = _types.GetMethod(dictType, "set_Item", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // ["address"] = _address
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "address");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, addressField);
        il.Emit(OpCodes.Callvirt, dictAdd);

        // ["family"] = _family
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "family");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, familyField);
        il.Emit(OpCodes.Callvirt, dictAdd);

        // ["port"] = _port (boxed double)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "port");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, portField);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, dictAdd);

        // ["size"] = _size (boxed double)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, sizeField);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, dictAdd);

        // Wrap in $Object via CreateObject
        il.Emit(OpCodes.Call, runtime.CreateObject);
        var rinfoLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, rinfoLocal);

        // _socket.Emit("message", new object[] { msg, rinfo })
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, socketField);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, msgLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, rinfoLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the _DgramReceiveWorker body:
    /// Async-capable loop that calls _client.ReceiveAsync(_receiveCts.Token) via
    /// ValueTaskAwaiter.GetResult(), then schedules a $DgramMessageClosure.Run on
    /// the EventLoop for each received packet.
    ///
    /// IMPORTANT: this MUST use the cancellable async overload, not the blocking
    /// Receive(ref EndPoint). On macOS/BSD, closing a socket from another thread
    /// does not reliably interrupt a blocked recvfrom() — the worker would park
    /// indefinitely in unmanaged code, blocking process shutdown. ReceiveAsync(token)
    /// supports cooperative cancellation at the .NET runtime layer, which works
    /// uniformly across Linux, Windows, and macOS.
    /// </summary>
    private void EmitDgramReceiveWorkerBody(EmittedRuntime runtime)
    {
        var il = _dgramReceiveWorkerMethod.GetILGenerator();

        var loopTop = il.DefineLabel();
        var loopExit = il.DefineLabel();
        var remoteEPLocal = il.DeclareLocal(typeof(IPEndPoint));
        var dataLocal = il.DeclareLocal(typeof(byte[]));
        var tokenLocal = il.DeclareLocal(typeof(System.Threading.CancellationToken));
        var vtLocal = il.DeclareLocal(typeof(System.Threading.Tasks.ValueTask<UdpReceiveResult>));
        var awaiterLocal = il.DeclareLocal(typeof(System.Runtime.CompilerServices.ValueTaskAwaiter<UdpReceiveResult>));
        var resultLocal = il.DeclareLocal(typeof(UdpReceiveResult));

        il.MarkLabel(loopTop);

        // if (_closed) break
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClosedField);
        il.Emit(OpCodes.Brtrue, loopExit);

        // if (_client == null) break
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Brfalse, loopExit);

        // try {
        il.BeginExceptionBlock();

        // token = (_receiveCts != null) ? _receiveCts.Token : default
        var ctsNonNull = il.DefineLabel();
        var tokenReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramReceiveCtsField);
        il.Emit(OpCodes.Brtrue, ctsNonNull);
        // default(CancellationToken)
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Initobj, typeof(System.Threading.CancellationToken));
        il.Emit(OpCodes.Br, tokenReady);
        il.MarkLabel(ctsNonNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramReceiveCtsField);
        il.Emit(OpCodes.Callvirt, typeof(System.Threading.CancellationTokenSource)
            .GetProperty("Token")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, tokenLocal);
        il.MarkLabel(tokenReady);

        // vt = _client.ReceiveAsync(token)   — ValueTask<UdpReceiveResult>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _dgramClientField);
        il.Emit(OpCodes.Ldloc, tokenLocal);
        il.Emit(OpCodes.Callvirt, typeof(UdpClient)
            .GetMethod("ReceiveAsync", [typeof(System.Threading.CancellationToken)])!);
        il.Emit(OpCodes.Stloc, vtLocal);

        // awaiter = vt.GetAwaiter()
        il.Emit(OpCodes.Ldloca, vtLocal);
        il.Emit(OpCodes.Call, typeof(System.Threading.Tasks.ValueTask<UdpReceiveResult>)
            .GetMethod("GetAwaiter", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, awaiterLocal);

        // result = awaiter.GetResult()  — blocks until receive completes,
        // throws OperationCanceledException if the token is cancelled.
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        il.Emit(OpCodes.Call, typeof(System.Runtime.CompilerServices.ValueTaskAwaiter<UdpReceiveResult>)
            .GetMethod("GetResult", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // data = result.Buffer
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, typeof(UdpReceiveResult).GetProperty("Buffer")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, dataLocal);

        // remoteEP = result.RemoteEndPoint
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, typeof(UdpReceiveResult).GetProperty("RemoteEndPoint")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, remoteEPLocal);

        // Extract remote endpoint info for the closure
        var addrStrLocal = il.DeclareLocal(_types.String);
        var familyStrLocal = il.DeclareLocal(_types.String);
        var portDblLocal = il.DeclareLocal(_types.Double);
        var sizeDblLocal = il.DeclareLocal(_types.Double);

        // address = remoteEP.Address.ToString()
        il.Emit(OpCodes.Ldloc, remoteEPLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Address")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, addrStrLocal);

        // family = remoteEP.AddressFamily == InterNetworkV6 ? "IPv6" : "IPv4"
        var isV6Label = il.DefineLabel();
        var familyDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, remoteEPLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("AddressFamily")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)AddressFamily.InterNetworkV6);
        il.Emit(OpCodes.Beq, isV6Label);
        il.Emit(OpCodes.Ldstr, "IPv4");
        il.Emit(OpCodes.Br, familyDoneLabel);
        il.MarkLabel(isV6Label);
        il.Emit(OpCodes.Ldstr, "IPv6");
        il.MarkLabel(familyDoneLabel);
        il.Emit(OpCodes.Stloc, familyStrLocal);

        // port = (double)remoteEP.Port
        il.Emit(OpCodes.Ldloc, remoteEPLocal);
        il.Emit(OpCodes.Callvirt, typeof(IPEndPoint).GetProperty("Port")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, portDblLocal);

        // size = (double)data.Length
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, sizeDblLocal);

        // Schedule on EventLoop: EventLoop.Schedule(new Action(new $DgramMessageClosure(...).Run))
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldarg_0); // socket (this)
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldloc, addrStrLocal);
        il.Emit(OpCodes.Ldloc, familyStrLocal);
        il.Emit(OpCodes.Ldloc, portDblLocal);
        il.Emit(OpCodes.Ldloc, sizeDblLocal);
        il.Emit(OpCodes.Newobj, _dgramMessageClosureCtor);
        il.Emit(OpCodes.Ldftn, _dgramMessageClosureRun);
        il.Emit(OpCodes.Newobj, typeof(Action).GetConstructor([_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, runtime.EventLoopSchedule);

        var afterReceive = il.DefineLabel();
        il.Emit(OpCodes.Leave, afterReceive);

        // catch (Exception) { break }
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, loopExit);
        il.EndExceptionBlock();

        il.MarkLabel(afterReceive);
        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(loopExit);
        il.Emit(OpCodes.Ret);
    }
}
