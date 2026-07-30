using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Readable class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSReadable
/// </summary>
public partial class RuntimeEmitter
{
    // $Readable fields and methods
    private MethodBuilder _tsReadableFlushChunkToPipes = null!;
    private FieldBuilder _tsReadableBufferField = null!;
    private FieldBuilder _tsReadablePipeDestinationsField = null!;
    private FieldBuilder _tsReadableEndedField = null!;
    private FieldBuilder _tsReadableDestroyedField = null!;
    private FieldBuilder _tsReadableEncodingField = null!;
    private FieldBuilder _tsReadableReadableField = null!;
    private FieldBuilder _tsReadableFlowingField = null!; // int: -1=initial, 0=paused, 1=flowing
    private FieldBuilder _tsReadableObjectModeField = null!;
    private FieldBuilder _tsReadableHighWaterMarkField = null!;
    // Async iteration support (#1024)
    private FieldBuilder _tsReadableErroredField = null!;
    private FieldBuilder _tsReadableErrorField = null!;
    private FieldBuilder _tsReadableIterWaiterField = null!;

    /// <summary>
    /// Phase 1: Define the $Readable type, fields, and constructor.
    /// Must be called before Duplex is defined (since Duplex extends Readable).
    /// </summary>
    private void EmitTSReadableTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Readable : $EventEmitter
        var typeBuilder = moduleBuilder.DefineType(
            "$Readable",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType  // Extends $EventEmitter
        );
        runtime.TSReadableType = typeBuilder;

        // Define fields - use List<object> and Queue<object> for simplicity
        // Use Family (protected) for fields that derived classes need to access
        var queueOfObject = typeof(Queue<object?>);
        _tsReadableBufferField = typeBuilder.DefineField("_readBuffer", queueOfObject, FieldAttributes.Family);

        _tsReadablePipeDestinationsField = typeBuilder.DefineField("_pipeDestinations", _types.ListOfObject, FieldAttributes.Family);

        _tsReadableEndedField = typeBuilder.DefineField("_ended", _types.Boolean, FieldAttributes.Family);
        _tsReadableDestroyedField = typeBuilder.DefineField("_destroyed", _types.Boolean, FieldAttributes.Family);
        _tsReadableEncodingField = typeBuilder.DefineField("_encoding", _types.String, FieldAttributes.Family);
        _tsReadableReadableField = typeBuilder.DefineField("_readable", _types.Boolean, FieldAttributes.Family);
        _tsReadableFlowingField = typeBuilder.DefineField("_flowing", _types.Int32, FieldAttributes.Family);
        _tsReadableObjectModeField = typeBuilder.DefineField("_objectMode", _types.Boolean, FieldAttributes.Family);
        _tsReadableHighWaterMarkField = typeBuilder.DefineField("_highWaterMark", _types.Int32, FieldAttributes.Family);
        typeBuilder.DefineField("_bufferSize", _types.Int32, FieldAttributes.Family);
        // Async iteration support (#1024): error state + parked async-iterator pull.
        _tsReadableErroredField = typeBuilder.DefineField("_errored", _types.Boolean, FieldAttributes.Family);
        _tsReadableErrorField = typeBuilder.DefineField("_error", _types.Object, FieldAttributes.Family);
        _tsReadableIterWaiterField = typeBuilder.DefineField("_iterWaiter", _types.TaskCompletionSourceOfObject, FieldAttributes.Family);

        // Constructor
        EmitTSReadableCtor(typeBuilder, runtime, queueOfObject);

        // Methods that don't depend on Duplex
        EmitTSReadableRead(typeBuilder, runtime, queueOfObject);
        // NOTE: Push and Pipe are emitted in Phase 2 since they need Duplex type
        EmitTSReadableUnpipe(typeBuilder, runtime);
        EmitTSReadableSetEncoding(typeBuilder, runtime);
        EmitTSReadableDestroy(typeBuilder, runtime, queueOfObject);
        EmitTSReadableUnshift(typeBuilder, runtime, queueOfObject);
        EmitTSReadablePause(typeBuilder, runtime);
        EmitTSReadableResume(typeBuilder, runtime);
        EmitTSReadableIsPaused(typeBuilder, runtime);
        EmitTSReadableToArray(typeBuilder, runtime, queueOfObject);
        EmitTSReadableForEach(typeBuilder, runtime, queueOfObject);
        EmitTSReadableSetObjectMode(typeBuilder, runtime);
        EmitTSReadableSetHighWaterMark(typeBuilder, runtime);

        // [Symbol.asyncIterator] surface (#1024). Emitted in Phase 1 so MakeIterResult is
        // available to Push (Phase 2a), which settles a parked pull. None of these depend
        // on Duplex/Transform.
        EmitTSReadableAsyncIteratorMethods(typeBuilder, runtime, queueOfObject);

        // Property getters
        EmitTSReadablePropertyGetters(typeBuilder, runtime, queueOfObject);

        // Override OnListenerAdded to enter flowing mode on 'data' event
        EmitTSReadableOnListenerAdded(typeBuilder, runtime);
    }

    /// <summary>
    /// Phase 2a: Emit methods that depend on Duplex type.
    /// Must be called after Duplex type is defined.
    /// </summary>
    private void EmitTSReadablePhaseTwoMethods(EmittedRuntime runtime)
    {
        var typeBuilder = (TypeBuilder)runtime.TSReadableType;
        var queueOfObject = typeof(Queue<object?>);

        // These depend on TSDuplexType
        EmitTSReadableFlushChunkToPipes(typeBuilder, runtime);
        EmitTSReadablePush(typeBuilder, runtime, queueOfObject);
        EmitTSReadablePipe(typeBuilder, runtime, queueOfObject);
    }

    /// <summary>
    /// Phase 2b: Emit Map/Filter methods that depend on Transform type,
    /// then finalize the $Readable type.
    /// Must be called after Transform type and helper callback classes are defined.
    /// </summary>
    private void EmitTSReadableMapFilterMethods(EmittedRuntime runtime)
    {
        var typeBuilder = (TypeBuilder)runtime.TSReadableType;

        EmitTSReadableMap(typeBuilder, runtime);
        EmitTSReadableFilter(typeBuilder, runtime);

        // Async iterator helpers (#1025) — need Push/SetObjectMode (Phase 2a) + $Array/$Promise.
        EmitTSReadableIterHelpers(typeBuilder, runtime, typeof(Queue<object?>));

        // Finalize the type
        typeBuilder.CreateType();
    }

    private void EmitTSReadableCtor(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSReadableCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($EventEmitter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _readBuffer = new Queue<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, queueType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsReadableBufferField);

        // _pipeDestinations = new List<object>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsReadablePipeDestinationsField);

        // _ended = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableEndedField);

        // _destroyed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableDestroyedField);

        // _encoding = "utf8"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Stfld, _tsReadableEncodingField);

        // _readable = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableReadableField);

        // _flowing = -1 (initial/null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);

        // _objectMode = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableObjectModeField);

        // Default highWaterMark: 16384 bytes (16 for object mode, but that's set later)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Stfld, _tsReadableHighWaterMarkField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableRead(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public object? Read(object? size)
        var method = typeBuilder.DefineMethod(
            "Read",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();
        var returnNullLabel = il.DefineLabel();
        var hasDataLabel = il.DefineLabel();

        // if (_destroyed || _readBuffer.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnNullLabel);

        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasDataLabel);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasDataLabel);

        // In object mode, return one object at a time (don't concatenate)
        var notObjectModeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableObjectModeField);
        il.Emit(OpCodes.Brfalse, notObjectModeLabel);

        // Object mode: return _readBuffer.Dequeue()
        var dequeueMethod = queueType.GetMethod("Dequeue")!;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notObjectModeLabel);

        // Non-object mode: read all and concatenate
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var result = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(loopStart);
        // while (_readBuffer.Count > 0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        // result.Append(_readBuffer.Dequeue()?.ToString() ?? "")
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);

        // Convert to string safely
        var chunkLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, chunkLocal);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        var toStringNullLabel = il.DefineLabel();
        var afterToStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, toStringNullLabel);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, afterToStringLabel);
        il.MarkLabel(toStringNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(afterToStringLabel);

        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableSetObjectMode(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void SetObjectMode(bool value)
        var method = typeBuilder.DefineMethod(
            "SetObjectMode",
            MethodAttributes.Public,
            _types.Void,
            [_types.Boolean]
        );
        runtime.TSReadableSetObjectMode = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsReadableObjectModeField);

        // If objectMode=true and highWaterMark is still default (16384), set to 16
        var skipHwm = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableHighWaterMarkField);
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Bne_Un, skipHwm);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Stfld, _tsReadableHighWaterMarkField);
        il.MarkLabel(skipHwm);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableSetHighWaterMark(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void SetHighWaterMark(int value)
        var method = typeBuilder.DefineMethod(
            "SetHighWaterMark",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32]
        );
        runtime.TSReadableSetHighWaterMark = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsReadableHighWaterMarkField);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits FlushChunkToPipes(object chunk): writes chunk to all pipe destinations.
    /// Shared by both flowing and non-flowing paths in Push().
    /// </summary>
    private void EmitTSReadableFlushChunkToPipes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        _tsReadableFlushChunkToPipes = typeBuilder.DefineMethod(
            "FlushChunkToPipes",
            MethodAttributes.Private,
            _types.Void,
            [_types.Object]  // chunk
        );

        var il = _tsReadableFlushChunkToPipes.GetILGenerator();

        var idxLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        var destLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
        il.Emit(OpCodes.Stloc, destLocal);

        var tryWritable = il.DefineLabel();
        var afterWrite = il.DefineLabel();

        var writeResultLocal = il.DeclareLocal(_types.Boolean);

        il.Emit(OpCodes.Ldloc, destLocal);
        il.Emit(OpCodes.Isinst, runtime.TSDuplexType);
        il.Emit(OpCodes.Brfalse, tryWritable);

        il.Emit(OpCodes.Ldloc, destLocal);
        il.Emit(OpCodes.Castclass, runtime.TSDuplexType);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSDuplexWrite);
        il.Emit(OpCodes.Stloc, writeResultLocal);
        il.Emit(OpCodes.Br, afterWrite);

        il.MarkLabel(tryWritable);
        il.Emit(OpCodes.Ldloc, destLocal);
        il.Emit(OpCodes.Isinst, runtime.TSWritableType);
        il.Emit(OpCodes.Brfalse, afterWrite);

        il.Emit(OpCodes.Ldloc, destLocal);
        il.Emit(OpCodes.Castclass, runtime.TSWritableType);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSWritableWrite);
        il.Emit(OpCodes.Stloc, writeResultLocal);

        // If write returned false (backpressure), set _flowing = 0 (paused)
        var noBackpressureLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, writeResultLocal);
        il.Emit(OpCodes.Brtrue, noBackpressureLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);
        il.MarkLabel(noBackpressureLabel);

        il.MarkLabel(afterWrite);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadablePush(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public bool Push(object? chunk)
        var method = typeBuilder.DefineMethod(
            "Push",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Object]
        );
        runtime.TSReadablePush = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var notNullLabel = il.DefineLabel();

        // if (_destroyed) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // if (chunk == null) - EOF signal
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);

        // _ended = true; _readable = false; emit 'end'; return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableEndedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableReadableField);

        // Settle a parked `for await` pull as done (#1024).
        EmitSettleIterWaiterDone(il, runtime);

        // Emit 'end' event: this.Emit("end", [])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "end");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // End all pipe destinations
        {
            var eofIdxLocal = il.DeclareLocal(_types.Int32);
            var eofCountLocal = il.DeclareLocal(_types.Int32);
            var eofLoopStart = il.DefineLabel();
            var eofLoopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, eofCountLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, eofIdxLocal);

            il.MarkLabel(eofLoopStart);
            il.Emit(OpCodes.Ldloc, eofIdxLocal);
            il.Emit(OpCodes.Ldloc, eofCountLocal);
            il.Emit(OpCodes.Bge, eofLoopEnd);

            var eofDestLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
            il.Emit(OpCodes.Ldloc, eofIdxLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
            il.Emit(OpCodes.Stloc, eofDestLocal);

            var eofTryWritable = il.DefineLabel();
            var eofAfterEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, eofDestLocal);
            il.Emit(OpCodes.Isinst, runtime.TSDuplexType);
            il.Emit(OpCodes.Brfalse, eofTryWritable);

            il.Emit(OpCodes.Ldloc, eofDestLocal);
            il.Emit(OpCodes.Castclass, runtime.TSDuplexType);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, runtime.TSDuplexEnd);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, eofAfterEnd);

            il.MarkLabel(eofTryWritable);
            il.Emit(OpCodes.Ldloc, eofDestLocal);
            il.Emit(OpCodes.Isinst, runtime.TSWritableType);
            il.Emit(OpCodes.Brfalse, eofAfterEnd);

            il.Emit(OpCodes.Ldloc, eofDestLocal);
            il.Emit(OpCodes.Castclass, runtime.TSWritableType);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, runtime.TSWritableEnd);
            il.Emit(OpCodes.Pop);

            il.MarkLabel(eofAfterEnd);
            il.Emit(OpCodes.Ldloc, eofIdxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, eofIdxLocal);
            il.Emit(OpCodes.Br, eofLoopStart);

            il.MarkLabel(eofLoopEnd);
        }

        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(notNullLabel);

        // Hand the chunk directly to a parked `for await` pull, if any (#1024).
        // On delivery this returns true from Push so the chunk is not also buffered/emitted.
        EmitDeliverChunkToIterWaiterAndReturn(il, runtime);

        // Check if flowing mode: if (_flowing == 1) emit 'data' directly
        var notFlowingLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableFlowingField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, notFlowingLabel);

        // Flowing: emit 'data' event with chunk
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Flush to pipe destinations
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _tsReadableFlushChunkToPipes!);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFlowingLabel);
        // Not flowing: _readBuffer.Enqueue(chunk), then flush to pipes
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Ldarg_1);
        var enqueueMethod = queueType.GetMethod("Enqueue")!;
        il.Emit(OpCodes.Callvirt, enqueueMethod);

        // Also flush to pipe destinations in non-flowing mode (matches interpreter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _tsReadableFlushChunkToPipes!);

        // Compute total buffer size and return whether it's under highWaterMark
        // Sum string lengths (byte mode) or count items (object mode)
        {
            // Simple approach: use ToArray() on the queue and sum lengths
            var totalLocal = il.DeclareLocal(_types.Int32);
            var arrLocal = il.DeclareLocal(typeof(object?[]));
            var idxLocal = il.DeclareLocal(_types.Int32);
            var arrLenLocal = il.DeclareLocal(_types.Int32);
            var loopStartLbl = il.DefineLabel();
            var loopEndLbl = il.DefineLabel();

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, totalLocal);

            // object[] items = _readBuffer.ToArray()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
            il.Emit(OpCodes.Callvirt, queueType.GetMethod("ToArray")!);
            il.Emit(OpCodes.Stloc, arrLocal);

            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Stloc, arrLenLocal);

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, idxLocal);

            il.MarkLabel(loopStartLbl);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, arrLenLocal);
            il.Emit(OpCodes.Bge, loopEndLbl);

            // chunk = items[idx]
            var chunkLocal2 = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, arrLocal);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, chunkLocal2);

            // Object mode: total += 1; Byte mode: total += (string ? length : 1)
            var notStr2 = il.DefineLabel();
            var afterLen2 = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, chunkLocal2);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, notStr2);
            il.Emit(OpCodes.Ldloc, totalLocal);
            il.Emit(OpCodes.Ldloc, chunkLocal2);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, totalLocal);
            il.Emit(OpCodes.Br, afterLen2);
            il.MarkLabel(notStr2);
            il.Emit(OpCodes.Ldloc, totalLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, totalLocal);
            il.MarkLabel(afterLen2);

            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Br, loopStartLbl);
            il.MarkLabel(loopEndLbl);

            // return total < _highWaterMark
            il.Emit(OpCodes.Ldloc, totalLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsReadableHighWaterMarkField);
            il.Emit(OpCodes.Clt);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadablePipe(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public object Pipe(object destination, object? options)
        var method = typeBuilder.DefineMethod(
            "Pipe",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.TSReadablePipe = method;

        var il = method.GetILGenerator();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Drain buffer to destination
        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        var dequeueMethod = queueType.GetMethod("Dequeue")!;

        il.MarkLabel(loopStart);
        // while (_readBuffer.Count > 0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        // dest.Write(_readBuffer.Dequeue())
        // Check if destination is $Duplex (includes Transform, PassThrough)
        var handleDuplexLabel = il.DefineLabel();
        var handleWritableLabel = il.DefineLabel();
        var notWritableLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSDuplexType);
        il.Emit(OpCodes.Brtrue, handleDuplexLabel);

        // Check if destination is $Writable
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSWritableType);
        il.Emit(OpCodes.Brtrue, handleWritableLabel);

        // Neither - discard data
        il.Emit(OpCodes.Br, notWritableLabel);

        // Handle $Duplex destination
        il.MarkLabel(handleDuplexLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSDuplexType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Ldnull); // encoding
        il.Emit(OpCodes.Ldnull); // callback
        il.Emit(OpCodes.Callvirt, runtime.TSDuplexWrite);
        // If write returned false, stop draining (backpressure)
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Br, loopStart);

        // Handle $Writable destination
        il.MarkLabel(handleWritableLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSWritableType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Ldnull); // encoding
        il.Emit(OpCodes.Ldnull); // callback
        il.Emit(OpCodes.Callvirt, runtime.TSWritableWrite);
        // If write returned false, stop draining (backpressure)
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(notWritableLabel);
        // Unknown destination type — just dequeue and discard
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // If ended, end the destination
        var notEndedLabel = il.DefineLabel();
        var endDuplexLabel = il.DefineLabel();
        var endWritableLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Brfalse, notEndedLabel);

        // Check if destination is $Duplex
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSDuplexType);
        il.Emit(OpCodes.Brtrue, endDuplexLabel);

        // Check if destination is $Writable
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSWritableType);
        il.Emit(OpCodes.Brtrue, endWritableLabel);

        il.Emit(OpCodes.Br, notEndedLabel);

        // End $Duplex destination
        il.MarkLabel(endDuplexLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSDuplexType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSDuplexEnd);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, notEndedLabel);

        // End $Writable destination
        il.MarkLabel(endWritableLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSWritableType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSWritableEnd);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notEndedLabel);

        // Add destination to _pipeDestinations
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        // Set _flowing = 1 (flowing mode) after pipe setup
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);

        // return destination
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableUnpipe(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Readable Unpipe(object? destination)
        var method = typeBuilder.DefineMethod(
            "Unpipe",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();
        // return this (simplified)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableSetEncoding(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Readable SetEncoding(string encoding)
        var method = typeBuilder.DefineMethod(
            "SetEncoding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();
        // _encoding = encoding?.ToLowerInvariant() ?? "utf8"
        var notNullLabel = il.DefineLabel();
        var afterSetLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Br, afterSetLabel);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);

        il.MarkLabel(afterSetLabel);
        il.Emit(OpCodes.Stfld, _tsReadableEncodingField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableDestroy(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public $Readable Destroy(object? error)
        var method = typeBuilder.DefineMethod(
            "Destroy",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.TSReadableDestroy = method;

        var il = method.GetILGenerator();
        var alreadyDestroyedLabel = il.DefineLabel();
        var noErrorLabel = il.DefineLabel();

        // if (_destroyed) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, alreadyDestroyedLabel);

        // _destroyed = true; _readable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableReadableField);

        // _readBuffer.Clear()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        var clearMethod = queueType.GetMethod("Clear")!;
        il.Emit(OpCodes.Callvirt, clearMethod);

        // if (error != null) { _errored = true; _error = error; fault any parked pull; emit 'error'; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noErrorLabel);

        // _errored = true; _error = error;  (#1024)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableErroredField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsReadableErrorField);

        // A pending `for await` pull rejects with the destroy error.
        EmitFaultIterWaiter(il, runtime);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noErrorLabel);
        // emit 'close'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyDestroyedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public $Readable Unshift(object chunk)
        var method = typeBuilder.DefineMethod(
            "Unshift",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        _ = method;

        var il = method.GetILGenerator();
        // Simplified: just enqueue (proper implementation would prepend)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Ldarg_1);
        var enqueueMethod = queueType.GetMethod("Enqueue")!;
        il.Emit(OpCodes.Callvirt, enqueueMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadablePause(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Readable Pause()
        var method = typeBuilder.DefineMethod(
            "Pause",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // _flowing = 0 (paused)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);

        // Emit 'pause' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "pause");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableResume(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Readable Resume()
        var method = typeBuilder.DefineMethod(
            "Resume",
            MethodAttributes.Public,
            typeBuilder,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        var queueType = typeof(Queue<object?>);
        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        var dequeueMethod = queueType.GetMethod("Dequeue")!;

        // _flowing = 1 (flowing)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);

        // Emit 'resume' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "resume");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        // Drain buffer: while (_readBuffer.Count > 0) emit('data', _readBuffer.Dequeue())
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        // emit('data', dequeue())
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // If ended, emit 'end'
        var notEndedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Brfalse, notEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "end");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(notEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableIsPaused(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public bool IsPaused()
        var method = typeBuilder.DefineMethod(
            "IsPaused",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        // return _flowing == 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableFlowingField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadableOnListenerAdded(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Override OnListenerAdded:
        // - "data": enter flowing mode, drain buffer
        // - "end": if already ended and buffer empty, emit 'end' immediately
        var method = typeBuilder.DefineMethod(
            "OnListenerAdded",
            MethodAttributes.Public | MethodAttributes.Virtual,
            _types.Void,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var strEquals = _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!;
        var retLabel = il.DefineLabel();
        var checkEndLabel = il.DefineLabel();

        // === "data" case ===
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brfalse, checkEndLabel);

        // if (_flowing == 1) return (already flowing)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableFlowingField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, retLabel);

        // _flowing = 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableFlowingField);

        // Drain buffer: while (_readBuffer.Count > 0) emit('data', _readBuffer.Dequeue())
        var queueType = typeof(Queue<object?>);
        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        var dequeueMethod = queueType.GetMethod("Dequeue")!;
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "data");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeueMethod);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Br, retLabel);

        // === "end" case ===
        il.MarkLabel(checkEndLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "end");
        il.Emit(OpCodes.Call, strEquals);
        il.Emit(OpCodes.Brfalse, retLabel);

        // if (_ended && _readBuffer.Count == 0) emit 'end'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Brfalse, retLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bne_Un, retLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "end");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSReadablePropertyGetters(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // readable property: _readable && !_ended && !_destroyed
        // Note: Use PascalCase getter names (get_Readable) for GetFieldsProperty lookup
        var readableProp = typeBuilder.DefineProperty("Readable", PropertyAttributes.None, _types.Boolean, null);
        var getReadable = typeBuilder.DefineMethod(
            "get_Readable",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        var il = getReadable.GetILGenerator();
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableReadableField);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        readableProp.SetGetMethod(getReadable);

        // readableEnded property
        var readableEndedProp = typeBuilder.DefineProperty("ReadableEnded", PropertyAttributes.None, _types.Boolean, null);
        var getReadableEnded = typeBuilder.DefineMethod(
            "get_ReadableEnded",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getReadableEnded.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Ret);
        readableEndedProp.SetGetMethod(getReadableEnded);

        // readableLength property
        var readableLengthProp = typeBuilder.DefineProperty("ReadableLength", PropertyAttributes.None, _types.Double, null);
        var getReadableLength = typeBuilder.DefineMethod(
            "get_ReadableLength",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        il = getReadableLength.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
        readableLengthProp.SetGetMethod(getReadableLength);

        // errored property (#1030): backs stream.isErrored
        var erroredProp = typeBuilder.DefineProperty("Errored", PropertyAttributes.None, _types.Boolean, null);
        var getErrored = typeBuilder.DefineMethod(
            "get_Errored",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSReadableErroredGetter = getErrored;
        il = getErrored.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableErroredField);
        il.Emit(OpCodes.Ret);
        erroredProp.SetGetMethod(getErrored);

        // destroyed property
        var destroyedProp = typeBuilder.DefineProperty("Destroyed", PropertyAttributes.None, _types.Boolean, null);
        var getDestroyed = typeBuilder.DefineMethod(
            "get_Destroyed",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getDestroyed.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Ret);
        destroyedProp.SetGetMethod(getDestroyed);

        // readableHighWaterMark property
        var readableHwmProp = typeBuilder.DefineProperty("ReadableHighWaterMark", PropertyAttributes.None, _types.Double, null);
        var getReadableHwm = typeBuilder.DefineMethod(
            "get_ReadableHighWaterMark",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        il = getReadableHwm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableHighWaterMarkField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
        readableHwmProp.SetGetMethod(getReadableHwm);

        // readableObjectMode property
        var readableObjectModeProp = typeBuilder.DefineProperty("ReadableObjectMode", PropertyAttributes.None, _types.Boolean, null);
        var getReadableObjectMode = typeBuilder.DefineMethod(
            "get_ReadableObjectMode",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getReadableObjectMode.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableObjectModeField);
        il.Emit(OpCodes.Ret);
        readableObjectModeProp.SetGetMethod(getReadableObjectMode);

        // readableFlowing property: returns false when _flowing == -1 (initial) or 0 (paused), true when 1
        var readableFlowingProp = typeBuilder.DefineProperty("ReadableFlowing", PropertyAttributes.None, _types.Object, null);
        var getReadableFlowing = typeBuilder.DefineMethod(
            "get_ReadableFlowing",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Object,
            Type.EmptyTypes
        );
        il = getReadableFlowing.GetILGenerator();
        var flowingTrueLabel = il.DefineLabel();
        var flowingEndLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableFlowingField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, flowingTrueLabel);

        // Not flowing: return false (boxed)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Br, flowingEndLabel);

        il.MarkLabel(flowingTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);

        il.MarkLabel(flowingEndLabel);
        il.Emit(OpCodes.Ret);
        readableFlowingProp.SetGetMethod(getReadableFlowing);
    }

    /// <summary>
    /// Emits: public object ToArray()
    /// Drains the read buffer into a List&lt;object?&gt; and returns it.
    /// </summary>
    private void EmitTSReadableToArray(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var method = typeBuilder.DefineMethod(
            "ToArray",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // var list = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, listLocal);

        // while (_readBuffer.Count > 0)
        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, queueType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, doneLabel);

        // list.Add(_readBuffer.Dequeue());
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, queueType.GetMethod("Dequeue")!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object ForEach(object callback)
    /// Drains the read buffer, calling callback(chunk) for each item.
    /// </summary>
    private void EmitTSReadableForEach(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var method = typeBuilder.DefineMethod(
            "ForEach",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var loopLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var callbackLocal = il.DeclareLocal(runtime.TSFunctionType);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Cast callback to $TSFunction
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // while (_readBuffer.Count > 0)
        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, queueType.GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, doneLabel);

        // callback.Invoke([_readBuffer.Dequeue()])
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, queueType.GetMethod("Dequeue")!);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Map(object callback)
    /// Creates a $Transform with objectMode matching this stream, sets a map transform
    /// callback, pipes this→transform, and returns the transform.
    /// </summary>
    private void EmitTSReadableMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Map",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var transformLocal = il.DeclareLocal(runtime.TSTransformType);

        // var transform = new $Transform();
        il.Emit(OpCodes.Newobj, runtime.TSTransformCtor);
        il.Emit(OpCodes.Stloc, transformLocal);

        // transform.SetObjectMode(this._objectMode);
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableObjectModeField);
        il.Emit(OpCodes.Callvirt, runtime.TSReadableSetObjectMode);

        // Create a $MapTransformCallback, then wrap it in a $TSFunction
        // so the Transform.Write method can invoke it via InvokeWithThis.
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); // user callback
        il.Emit(OpCodes.Newobj, runtime.MapTransformCallbackCtor);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Wrap in $TSFunction: new $TSFunction(callbackInstance, callbackInstance.GetType().GetMethod("Invoke"))
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(Type), "GetMethod", _types.String)!);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Callvirt, runtime.TSTransformSetTransformCallback!);

        // this.Pipe(transform)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldnull); // options
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePipe);
        il.Emit(OpCodes.Pop);

        // return transform
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public object Filter(object callback)
    /// Creates a $Transform with objectMode matching this stream, sets a filter transform
    /// callback, pipes this→transform, and returns the transform.
    /// </summary>
    private void EmitTSReadableFilter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Filter",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var transformLocal = il.DeclareLocal(runtime.TSTransformType);

        // var transform = new $Transform();
        il.Emit(OpCodes.Newobj, runtime.TSTransformCtor);
        il.Emit(OpCodes.Stloc, transformLocal);

        // transform.SetObjectMode(this._objectMode);
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableObjectModeField);
        il.Emit(OpCodes.Callvirt, runtime.TSReadableSetObjectMode);

        // Create a $FilterTransformCallback, then wrap it in a $TSFunction
        var callbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1); // user callback
        il.Emit(OpCodes.Newobj, runtime.FilterTransformCallbackCtor);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(Type), "GetMethod", _types.String)!);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Callvirt, runtime.TSTransformSetTransformCallback!);

        // this.Pipe(transform)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ldnull); // options
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePipe);
        il.Emit(OpCodes.Pop);

        // return transform
        il.Emit(OpCodes.Ldloc, transformLocal);
        il.Emit(OpCodes.Ret);
    }
}
