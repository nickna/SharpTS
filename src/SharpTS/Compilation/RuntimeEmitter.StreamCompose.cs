using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits compiled <c>stream.compose(...streams)</c> and <c>Duplex.from(source)</c> (#1028),
/// mirroring the interpreter (<see cref="SharpTS.Runtime.Types.SharpTSDuplexConstructor"/> /
/// <c>StreamModuleInterpreter.Compose</c>). All BCL-only / emitted-runtime — standalone preserved.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// public static object DuplexFrom(object[] args) — like ReadableFrom but yields a $Duplex
    /// whose readable side carries the iterable's items.
    /// </summary>
    private void EmitStreamDuplexFrom(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "DuplexFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]);
        nodeStreams.DuplexFrom = method;

        var il = method.GetILGenerator();
        var streamLocal = il.DeclareLocal(nodeStreams.DuplexType);
        il.Emit(OpCodes.Newobj, nodeStreams.DuplexCtor);
        il.Emit(OpCodes.Stloc, streamLocal);

        // SetObjectMode(true) — Duplex inherits TSReadableSetObjectMode.
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, nodeStreams.ReadableSetObjectMode);

        var iterableLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, iterableLocal);

        var notListLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        var countLocal = il.DeclareLocal(_types.Int32);
        var idxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var pushLoop = il.DefineLabel();
        var pushEnd = il.DefineLabel();
        il.MarkLabel(pushLoop);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, pushEnd);
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
        il.Emit(OpCodes.Callvirt, nodeStreams.ReadablePush);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, pushLoop);
        il.MarkLabel(pushEnd);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notListLabel);
        il.MarkLabel(doneLabel);

        // Push null for EOF.
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, nodeStreams.ReadablePush);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the <c>$StreamComposeBridge</c> closure class (fields: first stream, composed Duplex;
    /// methods ForwardWrite / PushData / PushEnd / EndFirst). Each method is wrapped in a $TSFunction
    /// by <see cref="EmitStreamComposeMethod"/>.
    /// </summary>
    private void EmitStreamComposeBridgeClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$StreamComposeBridge",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        runtime.RequireNodeStreams().ComposeBridgeType = typeBuilder;

        var firstField = typeBuilder.DefineField("_first", _types.Object, FieldAttributes.Private);
        var duplexField = typeBuilder.DefineField("_duplex", _types.Object, FieldAttributes.Private);

        // ctor(object first, object duplex)
        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object, _types.Object]);
        runtime.RequireNodeStreams().ComposeBridgeCtor = ctor;
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_1); cil.Emit(OpCodes.Stfld, firstField);
        cil.Emit(OpCodes.Ldarg_0); cil.Emit(OpCodes.Ldarg_2); cil.Emit(OpCodes.Stfld, duplexField);
        cil.Emit(OpCodes.Ret);

        // object ForwardWrite(object[] args): chunk = args[0]; first.Write(chunk, null, null);
        runtime.RequireNodeStreams().ComposeBridgeForwardWrite = typeBuilder.DefineMethod("ForwardWrite", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        {
            var il = runtime.RequireNodeStreams().ComposeBridgeForwardWrite.GetILGenerator();
            EmitWriteToStream(il, runtime.RequireNodeStreams(),
                loadStream: g => { g.Emit(OpCodes.Ldarg_0); g.Emit(OpCodes.Ldfld, firstField); },
                loadChunk: g => { g.Emit(OpCodes.Ldarg_1); g.Emit(OpCodes.Ldc_I4_0); g.Emit(OpCodes.Ldelem_Ref); });
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // object PushData(object[] args): duplex.Push(args[0]);
        runtime.RequireNodeStreams().ComposeBridgePushData = typeBuilder.DefineMethod("PushData", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        {
            var il = runtime.RequireNodeStreams().ComposeBridgePushData.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, duplexField);
            il.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().ReadableType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePush);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // object PushEnd(object[] args): duplex.Push(null);
        runtime.RequireNodeStreams().ComposeBridgePushEnd = typeBuilder.DefineMethod("PushEnd", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        {
            var il = runtime.RequireNodeStreams().ComposeBridgePushEnd.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, duplexField);
            il.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().ReadableType);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePush);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // object EndFirst(object[] args): first.End(null, null, null);
        runtime.RequireNodeStreams().ComposeBridgeEndFirst = typeBuilder.DefineMethod("EndFirst", MethodAttributes.Public, _types.Object, [_types.ObjectArray]);
        {
            var il = runtime.RequireNodeStreams().ComposeBridgeEndFirst.GetILGenerator();
            EmitEndStream(il, runtime.RequireNodeStreams(), g => { g.Emit(OpCodes.Ldarg_0); g.Emit(OpCodes.Ldfld, firstField); });
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
    }

    /// <summary>Emits: if (s is $Duplex) ((Duplex)s).Write(chunk,null,null); else if (s is $Writable) ((Writable)s).Write(chunk,null,null);</summary>
    private void EmitWriteToStream(ILGenerator il, EmittedNodeStreamRuntime nodeStreams, Action<ILGenerator> loadStream, Action<ILGenerator> loadChunk)
    {
        var sLocal = il.DeclareLocal(_types.Object);
        loadStream(il);
        il.Emit(OpCodes.Stloc, sLocal);

        var notDuplex = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, nodeStreams.DuplexType);
        il.Emit(OpCodes.Brfalse, notDuplex);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, nodeStreams.DuplexType);
        loadChunk(il);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, nodeStreams.DuplexWrite);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notDuplex);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, nodeStreams.WritableType);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, nodeStreams.WritableType);
        loadChunk(il);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, nodeStreams.WritableWrite);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(done);
    }

    /// <summary>Emits: if (s is $Duplex) ((Duplex)s).End(null,null,null); else if (s is $Writable) ((Writable)s).End(null,null,null);</summary>
    private void EmitEndStream(ILGenerator il, EmittedNodeStreamRuntime nodeStreams, Action<ILGenerator> loadStream)
    {
        var sLocal = il.DeclareLocal(_types.Object);
        loadStream(il);
        il.Emit(OpCodes.Stloc, sLocal);

        var notDuplex = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, nodeStreams.DuplexType);
        il.Emit(OpCodes.Brfalse, notDuplex);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, nodeStreams.DuplexType);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, nodeStreams.DuplexEnd);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notDuplex);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, nodeStreams.WritableType);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, nodeStreams.WritableType);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, nodeStreams.WritableEnd);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(done);
    }

    /// <summary>
    /// public static object Compose(object[] streams) — pipes the streams together and returns a
    /// $Duplex whose writable side feeds the first and readable side is fed by the last.
    /// </summary>
    private void EmitStreamComposeMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Compose",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]);
        runtime.RequireNodeStreams().Compose = method;

        var il = method.GetILGenerator();
        var nLocal = il.DeclareLocal(_types.Int32);
        var iLocal = il.DeclareLocal(_types.Int32);
        var dLocal = il.DeclareLocal(runtime.RequireNodeStreams().DuplexType);
        var bridgeLocal = il.DeclareLocal(runtime.RequireNodeStreams().ComposeBridgeType);
        var lastLocal = il.DeclareLocal(_types.Object);

        var retNull = il.DefineLabel();

        // n = streams.Length; if (n == 0) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, nLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Brfalse, retNull);

        // pipe consecutive: for (i=0; i<n-1; i++) if (streams[i] is $Readable) ((Readable)streams[i]).Pipe(streams[i+1], null);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var pipeLoop = il.DefineLabel();
        var pipeEnd = il.DefineLabel();
        il.MarkLabel(pipeLoop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, pipeEnd);

        var notReadable = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, runtime.RequireNodeStreams().ReadableType);
        il.Emit(OpCodes.Brfalse, notReadable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().ReadableType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePipe);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(notReadable);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, pipeLoop);
        il.MarkLabel(pipeEnd);

        // last = streams[n-1];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, lastLocal);

        // d = new $Duplex(); d.SetObjectMode(true);
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().DuplexCtor);
        il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.RequireNodeStreams().ReadableSetObjectMode);

        // bridge = new $StreamComposeBridge(streams[0], d);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().ComposeBridgeCtor);
        il.Emit(OpCodes.Stloc, bridgeLocal);

        // d.SetWriteCallback(new $TSFunction(bridge, ForwardWrite));
        il.Emit(OpCodes.Ldloc, dLocal);
        EmitBridgeTSFunction(il, runtime, bridgeLocal, runtime.RequireNodeStreams().ComposeBridgeForwardWrite);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().DuplexSetWriteCallback);

        // ((EventEmitter)last).On("data", new $TSFunction(bridge, PushData));
        EmitOnListener(il, runtime, () => { il.Emit(OpCodes.Ldloc, lastLocal); }, "data", bridgeLocal, runtime.RequireNodeStreams().ComposeBridgePushData);
        // ((EventEmitter)last).On("end", new $TSFunction(bridge, PushEnd));
        EmitOnListener(il, runtime, () => { il.Emit(OpCodes.Ldloc, lastLocal); }, "end", bridgeLocal, runtime.RequireNodeStreams().ComposeBridgePushEnd);
        // ((EventEmitter)d).On("finish", new $TSFunction(bridge, EndFirst));
        EmitOnListener(il, runtime, () => { il.Emit(OpCodes.Ldloc, dLocal); }, "finish", bridgeLocal, runtime.RequireNodeStreams().ComposeBridgeEndFirst);

        // return d;
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(retNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBridgeTSFunction(ILGenerator il, EmittedRuntime runtime, LocalBuilder bridgeLocal, MethodBuilder bridgeMethod)
    {
        il.Emit(OpCodes.Ldloc, bridgeLocal);
        EmitInstanceMethodInfoLiteral(il, bridgeMethod, runtime.RequireNodeStreams().ComposeBridgeType);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
    }

    /// <summary>
    /// Emits the module-level default-highWaterMark state + accessors (#1030):
    /// static int fields (init 16384 byte / 16 object via .cctor) and
    /// GetDefaultHwm(objectMode)→double / SetDefaultHwm(objectMode, value)→null.
    /// </summary>
    private void EmitStreamDefaultHwm(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var byteField = typeBuilder.DefineField("_defaultHwmByte", _types.Int32, FieldAttributes.Private | FieldAttributes.Static);
        var objField = typeBuilder.DefineField("_defaultHwmObject", _types.Int32, FieldAttributes.Private | FieldAttributes.Static);

        // .cctor: _defaultHwmByte = 16384; _defaultHwmObject = 16;
        var cctor = typeBuilder.DefineTypeInitializer();
        var cil = cctor.GetILGenerator();
        cil.Emit(OpCodes.Ldc_I4, 16384);
        cil.Emit(OpCodes.Stsfld, byteField);
        cil.Emit(OpCodes.Ldc_I4, 16);
        cil.Emit(OpCodes.Stsfld, objField);
        cil.Emit(OpCodes.Ret);

        // public static object GetDefaultHwm(object objectMode)
        var get = typeBuilder.DefineMethod("GetDefaultHwm", MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        runtime.RequireNodeStreams().GetDefaultHighWaterMark = get;
        {
            var il = get.GetILGenerator();
            var byteLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
            il.Emit(OpCodes.Brfalse, byteLabel);
            il.Emit(OpCodes.Ldsfld, objField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(byteLabel);
            il.Emit(OpCodes.Ldsfld, byteField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }

        // public static object SetDefaultHwm(object objectMode, object value)
        var set = typeBuilder.DefineMethod("SetDefaultHwm", MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object, _types.Object]);
        runtime.RequireNodeStreams().SetDefaultHighWaterMark = set;
        {
            var il = set.GetILGenerator();
            var byteLabel = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
            il.Emit(OpCodes.Brfalse, byteLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stsfld, objField);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(byteLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stsfld, byteField);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitOnListener(ILGenerator il, EmittedRuntime runtime, Action loadEmitter, string eventName, LocalBuilder bridgeLocal, MethodBuilder bridgeMethod)
    {
        loadEmitter();
        il.Emit(OpCodes.Castclass, runtime.EventEmitter.Type);
        il.Emit(OpCodes.Ldstr, eventName);
        EmitBridgeTSFunction(il, runtime, bridgeLocal, bridgeMethod);
        il.Emit(OpCodes.Callvirt, runtime.EventEmitter.On);
        il.Emit(OpCodes.Pop);
    }
}
