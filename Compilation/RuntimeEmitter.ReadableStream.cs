using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$ReadableStream</c>, <c>$ReadableStreamDefaultController</c>,
/// and <c>$ReadableStreamDefaultReader</c> classes.
/// </summary>
/// <remarks>
/// Pure-IL companion to <see cref="SharpTS.Runtime.Types.SharpTSReadableStream"/>.
/// V1 scope: synchronous pull, no pending-reads parking. Async user callbacks
/// (pull returning a promise) are not yet supported by the emitted version —
/// the existing tests use sync pull, so the constraint is invisible to them.
///
/// pipeTo/pipeThrough/tee are exposed as instance methods that delegate
/// through the runtime <c>WebStreamsHelpers.PipeToAny</c>/<c>TeeAny</c>
/// helpers via reflection (the same late-binding pattern used by the
/// allowlisted Compilation files). Bringing those into pure IL is a future
/// follow-up.
/// </remarks>
public partial class RuntimeEmitter
{
    private FieldBuilder _readableStreamQueueField = null!;
    private FieldBuilder _readableStreamStateField = null!;       // 0=readable, 1=closed, 2=errored
    private FieldBuilder _readableStreamStoredErrorField = null!;
    private FieldBuilder _readableStreamLockedField = null!;
    private FieldBuilder _readableStreamPullCbField = null!;
    private FieldBuilder _readableStreamCancelCbField = null!;
    private FieldBuilder _readableStreamHwmField = null!;
    private FieldBuilder _readableStreamCloseRequestedField = null!;
    private FieldBuilder _readableStreamControllerField = null!;
    private FieldBuilder _readableStreamReaderField = null!;
    // Pending reads queue — TaskCompletionSource<object> instances whose
    // Task the awaiting reader.read() is suspended on. Filled when Read()
    // finds queue empty + no sync pull chunks; drained by Enqueue() before
    // it pushes chunks to the main queue, so a pending reader resumes
    // immediately when a chunk arrives later (push-style streams).
    private FieldBuilder _readableStreamPendingReadsField = null!;

    private FieldBuilder _readableControllerStreamField = null!;
    private FieldBuilder _readableReaderStreamField = null!;

    private Type _listOfObject = null!;
    private Type _pendingReadsQueueType = null!;       // Queue<TaskCompletionSource<object>>
    private Type _pendingReadsTcsType = null!;         // TaskCompletionSource<object>

    private void EmitReadableStreamClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _listOfObject = typeof(List<object?>);
        _pendingReadsTcsType = typeof(TaskCompletionSource<object>);
        _pendingReadsQueueType = typeof(Queue<TaskCompletionSource<object>>);

        var streamBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ReadableStream",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var controllerBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ReadableStreamDefaultController",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var readerBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$ReadableStreamDefaultReader",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        runtime.ReadableStreamType = streamBuilder;
        _ = controllerBuilder;
        _ = readerBuilder;

        EmitReadableStreamFields(streamBuilder);

        // Define controller fields + ctor first so the stream constructor can
        // reference the controller ctor.
        _readableControllerStreamField = controllerBuilder.DefineField(
            "_stream", streamBuilder, FieldAttributes.Private);
        var controllerCtor = EmitReadableStreamControllerCtor(controllerBuilder, streamBuilder);

        // Define reader fields + ctor first so getReader() can Newobj it.
        _readableReaderStreamField = readerBuilder.DefineField(
            "_stream", streamBuilder, FieldAttributes.Private);
        var readerCtor = EmitReadableStreamReaderCtor(readerBuilder, streamBuilder);
        _ = readerCtor;

        // Now the stream constructor (uses controllerCtor).
        var streamCtor = EmitReadableStreamConstructor(streamBuilder, controllerBuilder, controllerCtor, runtime);
        runtime.ReadableStreamCtor = streamCtor;

        // Stream methods.
        EmitReadableStreamLockedGetter(streamBuilder);
        var enqueueMethod = EmitReadableStreamEnqueue(streamBuilder, runtime);
        var closeMethod = EmitReadableStreamCloseStream(streamBuilder, runtime);
        // "Terminate" is a thin alias for CloseStream. It exists so that the
        // TransformStream compiled path, which passes the underlying readable
        // directly as the "controller" to user transform(chunk, controller)
        // callbacks, can expose a terminate() method — PascalCase reflection
        // lookup finds "Terminate" and invokes it via $TSFunction wrapping.
        EmitReadableStreamTerminateAlias(streamBuilder, closeMethod);
        var errorMethod = EmitReadableStreamErrorMethod(streamBuilder);
        var desiredSizeMethod = EmitReadableStreamDesiredSizeProperty(streamBuilder);
        var readMethod = EmitReadableStreamRead(streamBuilder, runtime);
        EmitReadableStreamGetReader(streamBuilder, readerCtor);
        var cancelMethod = EmitReadableStreamCancel(streamBuilder, runtime);
        var pipeToMethod = EmitReadableStreamPipeTo(streamBuilder, readMethod, cancelMethod, runtime);
        EmitReadableStreamPipeThrough(streamBuilder, pipeToMethod);
        EmitReadableStreamTee(streamBuilder, streamCtor, readMethod, enqueueMethod, closeMethod, runtime);

        runtime.ReadableStreamEnqueue = enqueueMethod;
        runtime.ReadableStreamCloseStream = closeMethod;
        runtime.ReadableStreamErrorStream = errorMethod;
        _ = readMethod;

        // Static ReadableStream.from(iterable) (#269) — must be defined before
        // streamBuilder.CreateType() below. Uses the ctor + Enqueue + CloseStream
        // just defined and the $Runtime.IterateToList primitive.
        runtime.ReadableStreamFrom = EmitReadableStreamFromStatic(
            streamBuilder, streamCtor, enqueueMethod, closeMethod, runtime);

        // Controller methods (forward to stream).
        EmitReadableControllerEnqueue(controllerBuilder, streamBuilder, enqueueMethod);
        EmitReadableControllerClose(controllerBuilder, streamBuilder, closeMethod);
        EmitReadableControllerError(controllerBuilder, streamBuilder, errorMethod);
        EmitReadableControllerDesiredSizeProperty(controllerBuilder, streamBuilder, desiredSizeMethod);

        // Reader methods (forward to stream).
        EmitReadableReaderRead(readerBuilder, streamBuilder, readMethod);
        EmitReadableReaderReleaseLock(readerBuilder, streamBuilder);
        EmitReadableReaderCancel(readerBuilder, streamBuilder, cancelMethod);
        EmitReadableReaderClosedGetter(readerBuilder, streamBuilder);

        controllerBuilder.CreateType();
        readerBuilder.CreateType();
        streamBuilder.CreateType();
    }

    private void EmitReadableStreamFields(TypeBuilder t)
    {
        _readableStreamQueueField = t.DefineField("_queue", _listOfObject, FieldAttributes.Private);
        _readableStreamStateField = t.DefineField("_state", _types.Int32, FieldAttributes.Private);
        _readableStreamStoredErrorField = t.DefineField("_storedError", _types.Object, FieldAttributes.Private);
        _readableStreamLockedField = t.DefineField("_locked", _types.Boolean, FieldAttributes.Private);
        _readableStreamPullCbField = t.DefineField("_pullCb", _types.Object, FieldAttributes.Private);
        _readableStreamCancelCbField = t.DefineField("_cancelCb", _types.Object, FieldAttributes.Private);
        _readableStreamHwmField = t.DefineField("_highWaterMark", _types.Double, FieldAttributes.Private);
        _readableStreamCloseRequestedField = t.DefineField("_closeRequested", _types.Boolean, FieldAttributes.Private);
        _readableStreamControllerField = t.DefineField("_controller", _types.Object, FieldAttributes.Private);
        _readableStreamReaderField = t.DefineField("_reader", _types.Object, FieldAttributes.Private);
        _readableStreamPendingReadsField = t.DefineField("_pendingReads", _pendingReadsQueueType, FieldAttributes.Private);
    }

    private ConstructorBuilder EmitReadableStreamConstructor(
        TypeBuilder streamBuilder,
        TypeBuilder controllerBuilder,
        ConstructorBuilder controllerCtor,
        EmittedRuntime runtime)
    {
        // public $ReadableStream(object? underlyingSource, object? strategy)
        var ctor = streamBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]);

        var il = ctor.GetILGenerator();

        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _state = 0 (readable)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _readableStreamStateField);

        // _queue = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _listOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _readableStreamQueueField);

        // _pendingReads = new Queue<TaskCompletionSource<object>>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _pendingReadsQueueType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _readableStreamPendingReadsField);

        // _highWaterMark = ExtractHighWaterMark(strategy) — defaults to 1 if no strategy
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        EmitExtractHighWaterMarkInline(il);
        il.Emit(OpCodes.Stfld, _readableStreamHwmField);

        // Default HWM to 1 if zero (matches WHATWG default)
        var hwmDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamHwmField);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, hwmDoneLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Stfld, _readableStreamHwmField);
        il.MarkLabel(hwmDoneLabel);

        // Extract pull/cancel callbacks (and remember start, which fires immediately).
        EmitExtractCallbackFromDictForReadable(il, _readableStreamPullCbField, "pull", runtime);
        EmitExtractCallbackFromDictForReadable(il, _readableStreamCancelCbField, "cancel", runtime);

        // _controller = new $ReadableStreamDefaultController(this)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, controllerCtor);
        il.Emit(OpCodes.Stfld, _readableStreamControllerField);

        // Call start(controller) if present.
        // Stack setup for InvokeMethodValue:
        //   [receiver=null, startCallback, args=[controller]]
        // Skip if start is null.
        var startLocal = il.DeclareLocal(_types.Object);
        var startNullLabel = il.DefineLabel();

        // Try to get "start" from underlyingSource dict
        EmitTryGetFromDictToLocal(il, "start", startLocal);

        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Brfalse, startNullLabel);

        il.Emit(OpCodes.Ldnull); // receiver
        il.Emit(OpCodes.Ldloc, startLocal); // callback
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamControllerField); // controller arg
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop); // discard return value (start can be sync or return undefined)

        il.MarkLabel(startNullLabel);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    /// <summary>
    /// Same as <see cref="EmitExtractCallbackFromDict"/> but stores into a
    /// readable-stream field. Sharing this with the writable version would
    /// require parameterising on FieldBuilder, which we already do — so this
    /// is just an alias call.
    /// </summary>
    private void EmitExtractCallbackFromDictForReadable(ILGenerator il, FieldBuilder targetField, string callbackName, EmittedRuntime runtime)
    {
        EmitExtractCallbackFromDict(il, targetField, callbackName, runtime);
    }

    /// <summary>
    /// Emits IL that pulls a value out of the dict at <c>arg1</c> for the
    /// given key and stores it in the supplied local. Leaves the local set to
    /// null if the dict-or-key is missing. Stack on entry/exit: empty.
    /// </summary>
    private void EmitTryGetFromDictToLocal(ILGenerator il, string keyName, LocalBuilder targetLocal)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var notDictLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, keyName);
        il.Emit(OpCodes.Ldloca, targetLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, notDictLabel);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, targetLocal);

        il.MarkLabel(doneLabel);
    }

    private void EmitReadableStreamLockedGetter(TypeBuilder t)
    {
        var prop = t.DefineProperty("Locked", PropertyAttributes.None, _types.Boolean, Type.EmptyTypes);
        var getter = t.DefineMethod(
            "get_Locked",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamLockedField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private MethodBuilder EmitReadableStreamEnqueue(TypeBuilder t, EmittedRuntime runtime)
    {
        // public void Enqueue(object? chunk) — adds chunk to the queue, or
        // resolves a pending read if one is parked on the pending-reads
        // TaskCompletionSource queue. The latter path enables push-style
        // streams where reader.read() is called before any enqueue happens,
        // then later code (e.g., a timer or background task) enqueues the
        // chunk and resolves the parked read.
        var method = t.DefineMethod(
            "Enqueue",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]);

        var il = method.GetILGenerator();

        var appendToQueueLabel = il.DefineLabel();

        // if (_pendingReads.Count > 0) { resolve one pending read; return; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, appendToQueueLabel);

        // var tcs = _pendingReads.Dequeue();
        var tcsLocal = il.DeclareLocal(_pendingReadsTcsType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetMethod("Dequeue")!);
        il.Emit(OpCodes.Stloc, tcsLocal);

        // tcs.TrySetResult({ value: chunk, done: false })
        il.Emit(OpCodes.Ldloc, tcsLocal);
        // Build the result dict inline (shared with Read())
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _pendingReadsTcsType.GetMethod("TrySetResult", [typeof(object)])!);
        il.Emit(OpCodes.Pop); // discard bool result
        il.Emit(OpCodes.Ret);

        // No pending reader: append to the backing queue.
        il.MarkLabel(appendToQueueLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits <c>public static object From(object iterable)</c> on $ReadableStream
    /// (#269). Mirrors the interpreter's ReadableStream.from: eagerly drains the
    /// iterable (via $Runtime.IterateToList, which honours the Symbol.iterator
    /// protocol plus arrays/strings/sets) into a fresh stream's queue, then closes
    /// it so a reader observes the elements followed by done.
    /// </summary>
    private MethodBuilder EmitReadableStreamFromStatic(
        TypeBuilder streamBuilder,
        ConstructorBuilder streamCtor,
        MethodBuilder enqueueMethod,
        MethodBuilder closeMethod,
        EmittedRuntime runtime)
    {
        // Named lowercase "from" (not "From") so the dynamic property path —
        // `(ReadableStream as any).from(...)` → GetProperty($ReadableStream, "from")
        // → SafeGetMethod (case-sensitive) — finds it and wraps it in a $TSFunction
        // callable. The static-emitter path calls it by MethodBuilder, so the name
        // is irrelevant there.
        var method = streamBuilder.DefineMethod(
            "from",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);

        var il = method.GetILGenerator();

        // var stream = new $ReadableStream(null, null);
        var streamLocal = il.DeclareLocal(streamBuilder);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, streamCtor);
        il.Emit(OpCodes.Stloc, streamLocal);

        // var list = $Runtime.IterateToList(iterable, Symbol.iterator, typeof($Runtime));
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, listLocal);

        // for (int i = 0; i < list.Count; i++) stream.Enqueue(list[i]);
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopCond = il.DefineLabel();
        var loopBody = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCond);
        il.MarkLabel(loopBody);
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, enqueueMethod);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopCond);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, loopBody);

        // stream.CloseStream(); return stream;
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Callvirt, closeMethod);
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitReadableStreamCloseStream(TypeBuilder t, EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "CloseStream",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        // _closeRequested = true; if queue is empty, _state = 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _readableStreamCloseRequestedField);

        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_listOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmptyLabel);

        // queue empty → mark closed now
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _readableStreamStateField);

        il.MarkLabel(notEmptyLabel);

        // Drain any parked readers with { value: undefined, done: true }. A
        // parked reader waiting on this stream would hang forever otherwise.
        EmitDrainPendingReadsWithDone(il, runtime);

        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitReadableStreamTerminateAlias(TypeBuilder t, MethodInfo closeMethod)
    {
        var method = t.DefineMethod(
            "Terminate",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, closeMethod);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitReadableStreamErrorMethod(TypeBuilder t)
    {
        var method = t.DefineMethod(
            "ErrorStream",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stfld, _readableStreamStateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _readableStreamStoredErrorField);
        // Clear queue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetMethod("Clear")!);

        // Reject any parked readers with the stored error.
        EmitDrainPendingReadsWithError(il);

        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits: while (_pendingReads.Count &gt; 0) { _pendingReads.Dequeue().TrySetResult({value:undefined, done:true}); }
    /// </summary>
    private void EmitDrainPendingReadsWithDone(ILGenerator il, EmittedRuntime runtime)
    {
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetMethod("Dequeue")!);

        // Build done result dict inline
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _pendingReadsTcsType.GetMethod("TrySetResult", [typeof(object)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits: while (_pendingReads.Count &gt; 0) { _pendingReads.Dequeue().TrySetException(new Exception(_storedError?.ToString() ?? "stream errored")); }
    /// </summary>
    private void EmitDrainPendingReadsWithError(ILGenerator il)
    {
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetMethod("Dequeue")!);

        // new Exception(_storedError?.ToString() ?? "stream errored")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamStoredErrorField);
        var notNullLabel = il.DefineLabel();
        var msgDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "stream errored");
        il.Emit(OpCodes.Br, msgDoneLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(msgDoneLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));

        il.Emit(OpCodes.Callvirt, _pendingReadsTcsType.GetMethod("TrySetException", [typeof(Exception)])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
    }

    private MethodBuilder EmitReadableStreamDesiredSizeProperty(TypeBuilder t)
    {
        // Property "DesiredSize" returning double.
        // Computed: _highWaterMark - _queue.Count
        var prop = t.DefineProperty("DesiredSize", PropertyAttributes.None, _types.Double, Type.EmptyTypes);
        var getter = t.DefineMethod(
            "get_DesiredSize",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Double,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamHwmField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_listOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
        return getter;
    }

    private MethodBuilder EmitReadableStreamRead(TypeBuilder t, EmittedRuntime runtime)
    {
        // public Task<object> Read()
        // Flow:
        //   1. If queue has chunks: pop, return Task.FromResult({value, done:false})
        //   2. If state is closed: return Task.FromResult({value:undefined, done:true})
        //   3. If state is errored: return Task.FromException(...)
        //   4. Else: try calling pull() synchronously once, then retry
        //   5. If queue is still empty and readable with no close requested:
        //      park a new TaskCompletionSource in _pendingReads and return its
        //      Task. A later Enqueue/CloseStream/ErrorStream call will resolve it.
        var method = t.DefineMethod(
            "Read",
            MethodAttributes.Public,
            _types.TaskOfObject,
            Type.EmptyTypes);

        var il = method.GetILGenerator();

        // Local: bool pulledOnce — ensures we call pull() at most once per Read()
        // invocation so we can't infinite-loop if pull never enqueues anything.
        var pulledOnceLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, pulledOnceLocal);

        var checkStateLabel = il.DefineLabel();
        var closedLabel = il.DefineLabel();
        var erroredLabel = il.DefineLabel();
        var dequeueLabel = il.DefineLabel();
        var parkLabel = il.DefineLabel();

        // Try dequeue first.
        il.MarkLabel(checkStateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_listOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, dequeueLabel);

        // Queue empty: check state.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamStateField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, closedLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamStateField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, erroredLabel);

        // Check closeRequested with empty queue → close now and return done
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamCloseRequestedField);
        var notCloseRequestedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notCloseRequestedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _readableStreamStateField);
        il.Emit(OpCodes.Br, closedLabel);

        il.MarkLabel(notCloseRequestedLabel);
        // State is readable. If pull callback exists AND we haven't called it yet,
        // call it once synchronously. Otherwise, park on a TCS.
        il.Emit(OpCodes.Ldloc, pulledOnceLocal);
        il.Emit(OpCodes.Brtrue, parkLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPullCbField);
        il.Emit(OpCodes.Brfalse, parkLabel);

        // Mark pull as called, then invoke pull(controller).
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, pulledOnceLocal);

        il.Emit(OpCodes.Ldnull); // receiver
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPullCbField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamControllerField);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        // Stack: [pullResult]. If the pull callback was async (returned a
        // Task<object> or $Promise), synchronously await it before retrying
        // the flow so the enqueued chunks are visible. For sync pulls the
        // result is ignored. This blocks the calling thread but matches
        // PipeTo's sync-pump strategy (spec-wise pull resolution should
        // re-trigger read() resolution, which we approximate via blocking).
        var pullResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, pullResultLocal);

        var pullNotTaskLabel = il.DefineLabel();
        var pullNotPromiseLabel = il.DefineLabel();
        var pullAwaitDoneLabel = il.DefineLabel();

        // if (pullResult is Task<object> t) t.GetAwaiter().GetResult()
        il.Emit(OpCodes.Ldloc, pullResultLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, pullNotTaskLabel);
        il.Emit(OpCodes.Ldloc, pullResultLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        var pullAwaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
        EmitSyncAwaitTaskOfObject(il, pullAwaiterLocal);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, pullAwaitDoneLabel);

        il.MarkLabel(pullNotTaskLabel);
        // if (pullResult is $Promise p) p.GetValueAsync().GetAwaiter().GetResult()
        il.Emit(OpCodes.Ldloc, pullResultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, pullNotPromiseLabel);
        il.Emit(OpCodes.Ldloc, pullResultLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        EmitSyncAwaitTaskOfObject(il, pullAwaiterLocal);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, pullAwaitDoneLabel);

        il.MarkLabel(pullNotPromiseLabel);
        // Not a task/promise — sync pull; nothing to await.

        il.MarkLabel(pullAwaitDoneLabel);
        // Retry the whole flow (queue may be populated, or close requested).
        il.Emit(OpCodes.Br, checkStateLabel);

        // Park path: create a TCS, enqueue into _pendingReads, return its Task.
        il.MarkLabel(parkLabel);
        var parkedTcsLocal = il.DeclareLocal(_pendingReadsTcsType);
        il.Emit(OpCodes.Newobj, _pendingReadsTcsType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, parkedTcsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamPendingReadsField);
        il.Emit(OpCodes.Ldloc, parkedTcsLocal);
        il.Emit(OpCodes.Callvirt, _pendingReadsQueueType.GetMethod("Enqueue", [_pendingReadsTcsType])!);
        il.Emit(OpCodes.Ldloc, parkedTcsLocal);
        il.Emit(OpCodes.Callvirt, _pendingReadsTcsType.GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // Dequeue path
        il.MarkLabel(dequeueLabel);
        // var chunk = _queue[0]; _queue.RemoveAt(0);
        var chunkLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_listOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, chunkLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetMethod("RemoveAt")!);

        // Build result dict { value: chunk, done: false } and wrap in Task.FromResult
        EmitMakeReadResultAsTask(il, chunkLocal, doneFlag: false, runtime);
        il.Emit(OpCodes.Ret);

        // Closed path: Task.FromResult({value:undefined, done:true})
        il.MarkLabel(closedLabel);
        EmitMakeReadResultAsTask(il, chunkLocal: null, doneFlag: true, runtime);
        il.Emit(OpCodes.Ret);

        // Errored path: Task.FromException
        il.MarkLabel(erroredLabel);
        // Stack: build Task.FromException<object>(new Exception(error))
        // Simpler: return Task.FromResult({value:undefined, done:true}) — error
        // is observed via the closed promise. Tests for ErrorPropagatesToReader
        // catch via the read() rejection so we DO need to surface a rejected task.
        // Build: TaskCompletionSource<object>; SetException; return tcs.Task.
        EmitErroredTaskFromStoredError(il);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a read result iterator object as a <c>Dictionary&lt;string, object?&gt;</c>
    /// wrapped in a <c>Task.FromResult&lt;object&gt;</c>. Pass <paramref name="chunkLocal"/>
    /// = null with <paramref name="doneFlag"/> = true for the EOF case (uses
    /// <c>$Undefined.Instance</c> for the value field to match JS semantics).
    /// </summary>
    private void EmitMakeReadResultAsTask(ILGenerator il, LocalBuilder? chunkLocal, bool doneFlag, EmittedRuntime runtime)
    {
        // new Dictionary<string, object?>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject, Type.EmptyTypes)!);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        // dict["value"] = chunk OR $Undefined.Instance (matches JS semantics
        // for the iterator-protocol EOF result, where reading after close
        // returns { value: undefined, done: true } rather than { value: null }).
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        if (chunkLocal != null)
        {
            il.Emit(OpCodes.Ldloc, chunkLocal);
        }
        else
        {
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        }
        il.Emit(OpCodes.Callvirt, setItem);

        // dict["done"] = box(doneFlag)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(doneFlag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, setItem);

        // Task.FromResult<object>(dict)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
    }

    private void EmitErroredTaskFromStoredError(ILGenerator il)
    {
        // Build a Task<object> that's faulted with an Exception wrapping
        // _storedError.ToString().
        var tcsType = typeof(TaskCompletionSource<object>);
        var tcsLocal = il.DeclareLocal(tcsType);

        il.Emit(OpCodes.Newobj, tcsType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tcsLocal);

        // tcs.SetException(new Exception(_storedError.ToString() ?? "stream errored"))
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamStoredErrorField);
        // null check / fallback
        var notNullLabel = il.DefineLabel();
        var msgDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "stream errored");
        il.Emit(OpCodes.Br, msgDoneLabel);
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(msgDoneLabel);

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Callvirt, tcsType.GetMethod("SetException", [typeof(Exception)])!);

        // Return tcs.Task
        il.Emit(OpCodes.Ldloc, tcsLocal);
        il.Emit(OpCodes.Callvirt, tcsType.GetProperty("Task")!.GetGetMethod()!);
    }

    private void EmitReadableStreamGetReader(TypeBuilder streamBuilder, ConstructorBuilder readerCtor)
    {
        var method = streamBuilder.DefineMethod(
            "GetReader",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);

        var il = method.GetILGenerator();

        // if (_locked) throw
        var notLockedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamLockedField);
        il.Emit(OpCodes.Brfalse, notLockedLabel);
        il.Emit(OpCodes.Ldstr, "TypeError: ReadableStream is already locked to a reader");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notLockedLabel);
        // _locked = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _readableStreamLockedField);

        // _reader = new $ReadableStreamDefaultReader(this); return _reader
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, readerCtor);
        il.Emit(OpCodes.Stfld, _readableStreamReaderField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamReaderField);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitReadableStreamCancel(TypeBuilder t, EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "Cancel",
            MethodAttributes.Public,
            _types.TaskOfObject,
            [_types.Object]);

        var il = method.GetILGenerator();

        // _state = 1; _queue.Clear()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _readableStreamStateField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamQueueField);
        il.Emit(OpCodes.Callvirt, _listOfObject.GetMethod("Clear")!);

        // If cancel callback present, call it (sync) and return Task.FromResult
        var noCbLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamCancelCbField);
        il.Emit(OpCodes.Brfalse, noCbLabel);

        il.Emit(OpCodes.Ldnull); // receiver
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableStreamCancelCbField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noCbLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits a synchronous <c>PipeTo(object dest, object opts)</c> method.
    /// </summary>
    /// <remarks>
    /// The pump loop is implemented synchronously: each <c>this.Read()</c> and
    /// <c>dest.Write(chunk)</c> task is unwrapped via <c>GetAwaiter().GetResult()</c>
    /// before the next iteration. This blocks the calling thread but avoids
    /// having to manually emit an async state machine in IL.
    ///
    /// For sync user callbacks (the case all current tests exercise), the
    /// individual tasks are pre-completed via <c>Task.FromResult</c>, so
    /// <c>GetResult</c> returns immediately and no actual blocking occurs.
    /// Async user callbacks would block the caller's thread; that's a known
    /// trade-off for v1 documented in the plan.
    ///
    /// Dest dispatch is reflection-based on <c>dest.GetType().GetMethod("Write")</c>
    /// and <c>"Close"</c>, so any object exposing those public methods works
    /// (covers both the emitted <c>$WritableStream</c> and the runtime
    /// <c>SharpTSWritableStream</c>).
    /// </remarks>
    private MethodBuilder EmitReadableStreamPipeTo(TypeBuilder t, MethodInfo readMethod, MethodInfo cancelMethod, EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "PipeTo",
            MethodAttributes.Public,
            _types.TaskOfObject,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();

        // Locals
        var writeCallableLocal = il.DeclareLocal(_types.Object);
        var closeCallableLocal = il.DeclareLocal(_types.Object);
        var abortCallableLocal = il.DeclareLocal(_types.Object);
        var signalLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var doneObjLocal = il.DeclareLocal(_types.Object);
        var chunkLocal = il.DeclareLocal(_types.Object);
        var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
        var writeArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var resultObjLocal = il.DeclareLocal(_types.Object);
        var reasonLocal = il.DeclareLocal(_types.Object);
        // Scratch slot for the Task<object> the pump cooperatively waits on (read
        // then write reuse it — read is fully consumed before the write). See
        // EmitCooperativeAwaitTaskOrSignal (#448).
        var coopTaskLocal = il.DeclareLocal(_types.TaskOfObject);

        // Extract opts.signal. Null opts, missing field, or $Undefined all
        // normalise to null so the per-iteration check can Brfalse cleanly.
        var noSignalLabel = il.DefineLabel();
        var haveSignalLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, signalLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, noSignalLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldstr, "signal");
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, haveSignalLabel);
        il.Emit(OpCodes.Stloc, signalLocal);
        il.Emit(OpCodes.Br, noSignalLabel);
        il.MarkLabel(haveSignalLabel);
        il.Emit(OpCodes.Pop); // discard duplicated undefined
        il.MarkLabel(noSignalLabel);

        // Acquire a writer via dest.getWriter() — matches WHATWG spec which
        // says pipeTo locks the destination through a writer. Works
        // uniformly for both runtime SharpTSWritableStream (whose write/close
        // are exposed on the WRITER, not the stream) and emitted $WritableStream.
        var writerLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);                                        // dest
        il.Emit(OpCodes.Ldstr, "getWriter");
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);                // → callable
        // Stack: [getWriterCallable]
        var getWriterCallableLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, getWriterCallableLocal);

        il.Emit(OpCodes.Ldarg_1);                                        // receiver = dest
        il.Emit(OpCodes.Ldloc, getWriterCallableLocal);                  // callable
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);                          // empty args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, writerLocal);

        // writeCallable = $Runtime.GetFieldsProperty(writer, "write")
        il.Emit(OpCodes.Ldloc, writerLocal);
        il.Emit(OpCodes.Ldstr, "write");
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Stloc, writeCallableLocal);

        // closeCallable = $Runtime.GetFieldsProperty(writer, "close")
        il.Emit(OpCodes.Ldloc, writerLocal);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Stloc, closeCallableLocal);

        // abortCallable = $Runtime.GetFieldsProperty(writer, "abort")
        il.Emit(OpCodes.Ldloc, writerLocal);
        il.Emit(OpCodes.Ldstr, "abort");
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Stloc, abortCallableLocal);

        // Loop label
        var loopTop = il.DefineLabel();
        var donePathLabel = il.DefineLabel();

        il.MarkLabel(loopTop);

        // #355: When a signal is present, drive the event loop one tick at the
        // top of each iteration — BEFORE the abort check below — so an abort
        // delivered through the event loop actually fires. The pump is a
        // synchronous blocking loop (each Read()/Write() is GetAwaiter().GetResult()'d
        // on this thread), so without this the main thread never yields and a
        // mid-pipe `setTimeout(() => ac.abort(), 0)` callback can't run: only a
        // signal already aborted when a read is reached would ever be observed.
        // $Runtime.ProcessPendingTimers — the same routine $EventLoop.Run()/
        // WaitForTask invoke — drains microtasks and fires due timers, so this
        // also covers a microtask-driven abort (`Promise.resolve().then(() => ac.abort())`).
        // Scoped to the signal path: non-aborting pipes keep their existing
        // ordering and pay nothing. All references are same-DLL emitted types,
        // so the pure-IL stream stays standalone (no SharpTS.dll dependency).
        EmitPumpEventLoopForSignal(il, signalLocal, runtime);

        // Per-iteration signal check. If signal != null AND signal.aborted,
        // call writer.abort(reason) + source.cancel(reason), then throw so
        // the PipeTo task is rejected.
        //
        // In compiled mode, AbortSignal is stored as a Dictionary<string,
        // object?> with `_token` / `_reason` / `_reasonSet` keys (see
        // RuntimeEmitter.AbortController.cs). Dispatch through the existing
        // $Runtime.AbortSignalGetAborted / AbortSignalGetReason helpers
        // which understand that layout. These helpers cast the signal to
        // Dictionary, so a non-dict signal would InvalidCastException —
        // guard with an isinst check first.
        var signalOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Brfalse, signalOkLabel);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, signalOkLabel);

        // if (!AbortSignalGetAborted(signal)) goto signalOkLabel
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
        il.Emit(OpCodes.Brfalse, signalOkLabel);

        // Aborted. Extract reason via the helper.
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetReason);
        il.Emit(OpCodes.Stloc, reasonLocal);

        // writer.abort(reason) — fire-and-forget (wrap in try/catch so a
        // failure to abort doesn't mask the original abort reason).
        var noAbortCbLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, abortCallableLocal);
        il.Emit(OpCodes.Brfalse, noAbortCbLabel);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, writerLocal);
        il.Emit(OpCodes.Ldloc, abortCallableLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, reasonLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        EmitUnwrapResultToTask(il, runtime, awaiterLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();
        il.MarkLabel(noAbortCbLabel);

        // source.cancel(reason) — calls this.Cancel(reason) on the emitted
        // stream. Wrap in try/catch for the same reason.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, reasonLocal);
        il.Emit(OpCodes.Callvirt, cancelMethod);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal);
        il.Emit(OpCodes.Pop);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.EndExceptionBlock();

        // Return a rejected Task<object> rather than throwing synchronously.
        // PipeTo's caller does `await source.pipeTo(...)`; for the user's
        // try/catch to observe the rejection, the awaited task must reach
        // the faulted state. A synchronous throw would escape via
        // TargetInvocationException before the caller ever sees a Task.
        //
        // Build: var tcs = new TCS<object>(); tcs.SetException(new Exception(reason));
        //        return tcs.Task;
        var abortTcsLocal = il.DeclareLocal(typeof(TaskCompletionSource<object>));
        il.Emit(OpCodes.Newobj, typeof(TaskCompletionSource<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, abortTcsLocal);

        il.Emit(OpCodes.Ldloc, abortTcsLocal);
        il.Emit(OpCodes.Ldloc, reasonLocal);
        var haveReasonStrLabel = il.DefineLabel();
        var reasonStrDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, haveReasonStrLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        il.Emit(OpCodes.Br, reasonStrDoneLabel);
        il.MarkLabel(haveReasonStrLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(reasonStrDoneLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object>).GetMethod("SetException", [typeof(Exception)])!);

        il.Emit(OpCodes.Ldloc, abortTcsLocal);
        il.Emit(OpCodes.Callvirt, typeof(TaskCompletionSource<object>).GetProperty("Task")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(signalOkLabel);

        // Call this.Read() → Task<object>. For a sync-pull source the task is
        // already completed and the cooperative wait returns immediately. For a
        // push-style source the task parks on a pending TCS that only an
        // event-loop callback (e.g. setTimeout → controller.enqueue) can settle,
        // so we must drive the loop instead of blocking the main thread in
        // GetResult() — otherwise the pump deadlocks (#448). On a mid-pipe abort
        // the wait branches back to loopTop (the signal check runs teardown); on a
        // never-settling read it gives up to donePathLabel (graceful close).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, readMethod);
        il.Emit(OpCodes.Stloc, coopTaskLocal);
        EmitCooperativeAwaitTaskOrSignal(il, runtime, coopTaskLocal, signalLocal, loopTop, donePathLabel);
        il.Emit(OpCodes.Ldloc, coopTaskLocal);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal); // completed → returns immediately
        il.Emit(OpCodes.Stloc, resultObjLocal);

        // Cast to Dictionary<string, object?>
        il.Emit(OpCodes.Ldloc, resultObjLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // doneObj = dict["done"]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, doneObjLocal);

        // if ((bool)doneObj) goto donePathLabel
        il.Emit(OpCodes.Ldloc, doneObjLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, donePathLabel);

        // chunk = dict["value"]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, chunkLocal);

        // writeArgs = new object[] { chunk }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, writeArgsLocal);

        // $Runtime.InvokeMethodValue(writer, writeCallable, writeArgs) → object
        il.Emit(OpCodes.Ldloc, writerLocal);             // receiver = writer
        il.Emit(OpCodes.Ldloc, writeCallableLocal);      // callable
        il.Emit(OpCodes.Ldloc, writeArgsLocal);          // args
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        // Cooperative wait for the write too: a push-style WritableStream whose
        // write() resolves later through the event loop would otherwise deadlock
        // the sync pump the same way a push source read does (#448).
        EmitCooperativeUnwrapResultToTask(il, runtime, awaiterLocal, signalLocal, coopTaskLocal, loopTop, donePathLabel);

        il.Emit(OpCodes.Br, loopTop);

        // Done path: call writer.close() and return
        il.MarkLabel(donePathLabel);
        var noCloseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, closeCallableLocal);
        il.Emit(OpCodes.Brfalse, noCloseLabel);
        il.Emit(OpCodes.Ldloc, writerLocal);
        il.Emit(OpCodes.Ldloc, closeCallableLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        EmitUnwrapResultToTask(il, runtime, awaiterLocal);

        il.MarkLabel(noCloseLabel);

        // return Task.FromResult($Undefined.Instance)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Consumes an <c>object</c> result off the stack (the return of
    /// <c>InvokeMethodValue</c>, which may be a <c>Task&lt;object&gt;</c>, a
    /// <c>$Promise</c>, or any other value) and synchronously waits for the
    /// underlying task to complete. Pops the value cleanly.
    /// </summary>
    private void EmitUnwrapResultToTask(ILGenerator il, EmittedRuntime runtime, LocalBuilder awaiterLocal)
    {
        // Stack: [object result]
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        var notTaskLabel = il.DefineLabel();
        var notPromiseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (result is Task<object> t) sync-await(t)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, doneLabel);

        // else if (result is $Promise p) sync-await(p.GetValueAsync())
        il.MarkLabel(notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseGetValueAsync);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, doneLabel);

        // else: nothing to await
        il.MarkLabel(notPromiseLabel);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Cooperative variant of <see cref="EmitUnwrapResultToTask"/> for the pump's
    /// main-loop <c>writer.write()</c> await. Normalises the <c>object</c> result on
    /// the stack to a <c>Task&lt;object&gt;</c> (direct task, <c>$Promise</c>, or
    /// nothing), then drives the event loop via
    /// <see cref="EmitCooperativeAwaitTaskOrSignal"/> instead of blocking — so a
    /// push-style <c>WritableStream</c> whose <c>write()</c> settles through the loop
    /// doesn't deadlock the pump (#448). On abort it branches to
    /// <paramref name="abortLabel"/>; on a never-settling write to
    /// <paramref name="quiesceLabel"/>.
    /// </summary>
    private void EmitCooperativeUnwrapResultToTask(
        ILGenerator il, EmittedRuntime runtime, LocalBuilder awaiterLocal,
        LocalBuilder signalLocal, LocalBuilder coopTaskLocal,
        Label abortLabel, Label quiesceLabel)
    {
        // Stack: [object result]
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        var notTaskLabel = il.DefineLabel();
        var notPromiseLabel = il.DefineLabel();
        var haveTaskLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (result is Task<object> t) { coopTask = t; goto haveTask }
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, coopTaskLocal);
        il.Emit(OpCodes.Br, haveTaskLabel);

        // else if (result is $Promise p) { coopTask = p.GetValueAsync(); goto haveTask }
        il.MarkLabel(notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseGetValueAsync);
        il.Emit(OpCodes.Stloc, coopTaskLocal);
        il.Emit(OpCodes.Br, haveTaskLabel);

        // else: nothing to await
        il.MarkLabel(notPromiseLabel);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(haveTaskLabel);
        EmitCooperativeAwaitTaskOrSignal(il, runtime, coopTaskLocal, signalLocal, abortLabel, quiesceLabel);
        il.Emit(OpCodes.Ldloc, coopTaskLocal);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal); // completed → returns immediately
        il.Emit(OpCodes.Pop);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits a cooperative wait for the <c>Task&lt;object&gt;</c> already stored in
    /// <paramref name="coopTaskLocal"/>: loops driving <c>$EventLoop.PumpOnce()</c>
    /// (drain queue + fire timers + 1ms tick) until the task completes, the abort
    /// signal fires, or the loop stays quiescent past the give-up window. This lets
    /// a push-style read/write that only an event-loop callback can settle make
    /// progress without the synchronous pump blocking the main thread (#448).
    /// </summary>
    /// <remarks>
    /// On normal completion control falls through with the (now completed) task left
    /// in <paramref name="coopTaskLocal"/> for the caller to <c>GetResult()</c>
    /// immediately. On a mid-pipe abort it branches to <paramref name="abortLabel"/>
    /// (the pump's loop top, whose signal check runs writer.abort + source.cancel
    /// teardown). On a never-settling task it branches to
    /// <paramref name="quiesceLabel"/> (the pump's done path → graceful close) after
    /// a 250ms quiescence window, mirroring <c>$EventLoop.WaitForTask</c>'s backstop
    /// so a producerless source can't hang the process. Stack on entry/exit (the
    /// fall-through and both branch targets): empty. References only emitted
    /// <c>$EventLoop</c>/<c>$Runtime</c> members, so the stream stays standalone.
    /// </remarks>
    private void EmitCooperativeAwaitTaskOrSignal(
        ILGenerator il, EmittedRuntime runtime,
        LocalBuilder coopTaskLocal, LocalBuilder signalLocal,
        Label abortLabel, Label quiesceLabel)
    {
        // Continuous quiescent wall-clock time before concluding the task can never
        // settle — same time-based backstop as $EventLoop.WaitForTask.
        const long QuiescentMsBeforeGiveUp = 250;

        var quiescentStartLocal = il.DeclareLocal(typeof(long));
        var waitLoopTop = il.DefineLabel();
        var notAbortedLabel = il.DefineLabel();
        var notCompletedAfterPump = il.DefineLabel();
        var busyResetLabel = il.DefineLabel();
        var startStreakLabel = il.DefineLabel();
        var completedLabel = il.DefineLabel();

        var isCompletedGetter = typeof(Task).GetProperty("IsCompleted")!.GetGetMethod()!;
        var tickCount64Getter = typeof(Environment).GetProperty("TickCount64")!.GetGetMethod()!;

        // quiescentStart = -1
        il.Emit(OpCodes.Ldc_I8, -1L);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);

        il.MarkLabel(waitLoopTop);

        // if (task.IsCompleted) goto completed
        il.Emit(OpCodes.Ldloc, coopTaskLocal);
        il.Emit(OpCodes.Callvirt, isCompletedGetter);
        il.Emit(OpCodes.Brtrue, completedLabel);

        // Abort check (isinst-guarded, same as the pump's loop-top check):
        // if (signal != null && signal is Dictionary && AbortSignalGetAborted(signal)) goto abort
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
        il.Emit(OpCodes.Brtrue, abortLabel);
        il.MarkLabel(notAbortedLabel);

        // busy = $EventLoop.GetInstance().PumpOnce()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Callvirt, runtime.EventLoopPumpOnce);
        // Stack: [busy]

        // if (task.IsCompleted) { pop busy; goto completed }
        il.Emit(OpCodes.Ldloc, coopTaskLocal);
        il.Emit(OpCodes.Callvirt, isCompletedGetter);
        il.Emit(OpCodes.Brfalse, notCompletedAfterPump);
        il.Emit(OpCodes.Pop); // discard busy
        il.Emit(OpCodes.Br, completedLabel);
        il.MarkLabel(notCompletedAfterPump);
        // Stack: [busy]

        // if (busy != 0) { quiescentStart = -1; loop } — Brtrue consumes busy
        il.Emit(OpCodes.Brtrue, busyResetLabel);

        // idle: track the quiescent streak.
        //   if (quiescentStart < 0) quiescentStart = TickCount64
        //   else if (TickCount64 - quiescentStart >= grace) goto quiesce
        il.Emit(OpCodes.Ldloc, quiescentStartLocal);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Blt, startStreakLabel);
        il.Emit(OpCodes.Call, tickCount64Getter);
        il.Emit(OpCodes.Ldloc, quiescentStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I8, QuiescentMsBeforeGiveUp);
        il.Emit(OpCodes.Bge, quiesceLabel);
        il.Emit(OpCodes.Br, waitLoopTop);

        il.MarkLabel(startStreakLabel);
        il.Emit(OpCodes.Call, tickCount64Getter);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);
        il.Emit(OpCodes.Br, waitLoopTop);

        // busy: reset the idle streak and loop
        il.MarkLabel(busyResetLabel);
        il.Emit(OpCodes.Ldc_I8, -1L);
        il.Emit(OpCodes.Stloc, quiescentStartLocal);
        il.Emit(OpCodes.Br, waitLoopTop);

        // Completed: fall through with the task ready for an immediate sync-await.
        il.MarkLabel(completedLabel);
    }

    /// <summary>
    /// Emits IL that consumes a <c>Task&lt;object&gt;</c> off the stack and
    /// pushes its synchronously-awaited <c>object</c> result. Uses
    /// <c>GetAwaiter().GetResult()</c> via a stored awaiter local. The local
    /// is reused across call sites for fewer slots.
    /// </summary>
    private void EmitSyncAwaitTaskOfObject(ILGenerator il, LocalBuilder awaiterLocal)
    {
        // Stack: [Task<object>]
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        // Stack: [TaskAwaiter<object>]
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult"));
        // Stack: [object]
    }

    /// <summary>
    /// Emits a one-tick drive of the emitted event loop for the <c>PipeTo</c>
    /// pump so an event-loop-driven mid-pipe <c>AbortSignal</c> can fire while
    /// the synchronous pump holds the main thread (#355).
    /// </summary>
    /// <remarks>
    /// Emits the equivalent of:
    /// <code>
    /// if (signal != null) $Runtime.ProcessPendingTimers();  // drains microtasks + fires due timers
    /// </code>
    /// <c>ProcessPendingTimers</c> self-initializes (so it's safe even when no
    /// timer was ever scheduled — the no-timer case still drains the microtask
    /// queue, covering a <c>Promise.then()</c>-driven abort) and is the same
    /// routine <c>$EventLoop.Run()</c>/<c>WaitForTask</c> invoke. Stack on
    /// entry/exit: empty. References only same-DLL emitted <c>$Runtime</c>, so
    /// the pure-IL stream stays standalone (no SharpTS.dll dependency).
    /// </remarks>
    private void EmitPumpEventLoopForSignal(ILGenerator il, LocalBuilder signalLocal, EmittedRuntime runtime)
    {
        var skipLabel = il.DefineLabel();

        // if (signal == null) skip — keep the pump cost on the signal path only.
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // $Runtime.ProcessPendingTimers() → int ms-until-next-timer (discarded).
        il.Emit(OpCodes.Call, runtime.ProcessPendingTimers);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits <c>PipeThrough(object transform, object opts)</c>: synchronously
    /// pipes <c>this</c> into <c>transform.writable</c>, then returns
    /// <c>transform.readable</c>. The transform's <c>Writable</c>/<c>Readable</c>
    /// properties are accessed via reflection (PascalCase, matching the JS
    /// convention <c>{ writable, readable }</c>).
    /// </summary>
    private void EmitReadableStreamPipeThrough(TypeBuilder t, MethodInfo pipeToMethod)
    {
        var method = t.DefineMethod(
            "PipeThrough",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();

        var transformTypeLocal = il.DeclareLocal(typeof(Type));
        var writableLocal = il.DeclareLocal(_types.Object);
        var readableLocal = il.DeclareLocal(_types.Object);
        var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);

        // transformType = transform.GetType()
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, transformTypeLocal);

        // writable = transformType.GetProperty("Writable").GetValue(transform)
        il.Emit(OpCodes.Ldloc, transformTypeLocal);
        il.Emit(OpCodes.Ldstr, "Writable");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, writableLocal);

        // readable = transformType.GetProperty("Readable").GetValue(transform)
        il.Emit(OpCodes.Ldloc, transformTypeLocal);
        il.Emit(OpCodes.Ldstr, "Readable");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, readableLocal);

        // this.PipeTo(writable, opts) — synchronous; sync-await the result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, writableLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, pipeToMethod);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal);
        il.Emit(OpCodes.Pop); // discard pipeTo result

        il.Emit(OpCodes.Ldloc, readableLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>Tee()</c>: creates two new $ReadableStream branches, eagerly
    /// drains <c>this</c> into both, and returns them as a 2-element
    /// <c>List&lt;object?&gt;</c> (the compiled-mode array shape that JS
    /// destructuring <c>const [a, b] = src.tee()</c> expects).
    /// </summary>
    /// <remarks>
    /// V1 simplification: eager drain rather than spec-correct lazy
    /// pull-and-branch. For sync sources (which all current tests use), this
    /// is observationally identical.
    /// </remarks>
    private void EmitReadableStreamTee(
        TypeBuilder t,
        ConstructorBuilder streamCtor,
        MethodInfo readMethod,
        MethodInfo enqueueMethod,
        MethodInfo closeMethod,
        EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "Tee",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);

        var il = method.GetILGenerator();

        var branch1Local = il.DeclareLocal(t);
        var branch2Local = il.DeclareLocal(t);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var doneObjLocal = il.DeclareLocal(_types.Object);
        var chunkLocal = il.DeclareLocal(_types.Object);
        var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);

        // branch1 = new $ReadableStream(null, null)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, streamCtor);
        il.Emit(OpCodes.Stloc, branch1Local);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, streamCtor);
        il.Emit(OpCodes.Stloc, branch2Local);

        var loopTop = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.MarkLabel(loopTop);

        // dict = await this.Read()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, readMethod);
        EmitSyncAwaitTaskOfObject(il, awaiterLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // doneObj = dict["done"]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, doneObjLocal);

        il.Emit(OpCodes.Ldloc, doneObjLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // chunk = dict["value"]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
        il.Emit(OpCodes.Stloc, chunkLocal);

        // branch1.Enqueue(chunk); branch2.Enqueue(chunk)
        il.Emit(OpCodes.Ldloc, branch1Local);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Callvirt, enqueueMethod);
        il.Emit(OpCodes.Ldloc, branch2Local);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Callvirt, enqueueMethod);

        il.Emit(OpCodes.Br, loopTop);

        il.MarkLabel(doneLabel);
        // branch1.CloseStream(); branch2.CloseStream()
        il.Emit(OpCodes.Ldloc, branch1Local);
        il.Emit(OpCodes.Callvirt, closeMethod);
        il.Emit(OpCodes.Ldloc, branch2Local);
        il.Emit(OpCodes.Callvirt, closeMethod);

        // return new List<object?> { branch1, branch2 }
        var listType = typeof(List<object?>);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, branch1Local);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add", [typeof(object)])!);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, branch2Local);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add", [typeof(object)])!);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    // --- Controller class ---

    private ConstructorBuilder EmitReadableStreamControllerCtor(TypeBuilder controllerBuilder, TypeBuilder streamBuilder)
    {
        var ctor = controllerBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [streamBuilder]);

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _readableControllerStreamField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private void EmitReadableControllerEnqueue(TypeBuilder controllerBuilder, TypeBuilder streamBuilder, MethodInfo streamEnqueue)
    {
        var method = controllerBuilder.DefineMethod(
            "Enqueue",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableControllerStreamField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, streamEnqueue);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableControllerClose(TypeBuilder controllerBuilder, TypeBuilder streamBuilder, MethodInfo streamClose)
    {
        // JS-side calls c.close() — but our stream method is named "CloseStream"
        // to avoid clashing with Stream.Close. Expose it on the controller as
        // "Close" (PascalCase mapping for JS "close").
        var method = controllerBuilder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableControllerStreamField);
        il.Emit(OpCodes.Callvirt, streamClose);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableControllerError(TypeBuilder controllerBuilder, TypeBuilder streamBuilder, MethodInfo streamError)
    {
        var method = controllerBuilder.DefineMethod(
            "Error",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableControllerStreamField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, streamError);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableControllerDesiredSizeProperty(TypeBuilder controllerBuilder, TypeBuilder streamBuilder, MethodInfo streamGetter)
    {
        var prop = controllerBuilder.DefineProperty("DesiredSize", PropertyAttributes.None, _types.Double, Type.EmptyTypes);
        var getter = controllerBuilder.DefineMethod(
            "get_DesiredSize",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Double,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableControllerStreamField);
        il.Emit(OpCodes.Callvirt, streamGetter);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    // --- Reader class ---

    private ConstructorBuilder EmitReadableStreamReaderCtor(TypeBuilder readerBuilder, TypeBuilder streamBuilder)
    {
        var ctor = readerBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [streamBuilder]);

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _readableReaderStreamField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private void EmitReadableReaderRead(TypeBuilder readerBuilder, TypeBuilder streamBuilder, MethodInfo streamRead)
    {
        var method = readerBuilder.DefineMethod(
            "Read",
            MethodAttributes.Public,
            _types.TaskOfObject,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableReaderStreamField);
        il.Emit(OpCodes.Callvirt, streamRead);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableReaderReleaseLock(TypeBuilder readerBuilder, TypeBuilder streamBuilder)
    {
        var method = readerBuilder.DefineMethod(
            "ReleaseLock",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        // _stream._locked = false; _stream._reader = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableReaderStreamField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _readableStreamLockedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableReaderStreamField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _readableStreamReaderField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableReaderCancel(TypeBuilder readerBuilder, TypeBuilder streamBuilder, MethodInfo streamCancel)
    {
        var method = readerBuilder.DefineMethod(
            "Cancel",
            MethodAttributes.Public,
            _types.TaskOfObject,
            [_types.Object]);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _readableReaderStreamField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, streamCancel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReadableReaderClosedGetter(TypeBuilder readerBuilder, TypeBuilder streamBuilder)
    {
        // Pre-resolved Task.FromResult(null) for v1.
        var prop = readerBuilder.DefineProperty("Closed", PropertyAttributes.None, _types.TaskOfObject, Type.EmptyTypes);
        var getter = readerBuilder.DefineMethod(
            "get_Closed",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.TaskOfObject,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }
}
