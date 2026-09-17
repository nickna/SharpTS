using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Writable class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSWritable
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $WriteCallbackWrapper helper class.
    /// This wraps the user-provided callback (or null) so that stream write handlers
    /// always receive a callable "done" callback as their third argument.
    /// Matches the interpreter's WriteCallbackWrapper behavior.
    /// </summary>
    private void EmitTSWriteCallbackWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$WriteCallbackWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.RequireNodeStreams().WriteCallbackWrapperType = typeBuilder;

        // Field: _userCallback (object, may be null)
        runtime.RequireNodeStreams().WriteCallbackWrapperUserCallbackField = typeBuilder.DefineField(
            "_userCallback", _types.Object, FieldAttributes.Private);
        // Field: _stream (object, the parent $Writable or $Duplex)
        runtime.RequireNodeStreams().WriteCallbackWrapperStreamField = typeBuilder.DefineField(
            "_stream", _types.Object, FieldAttributes.Private);
        // Field: _chunkSize (int)
        runtime.RequireNodeStreams().WriteCallbackWrapperChunkSizeField = typeBuilder.DefineField(
            "_chunkSize", _types.Int32, FieldAttributes.Private);

        // Constructor: public $WriteCallbackWrapper(object userCallback, object stream, int chunkSize)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object, _types.Int32]
        );
        runtime.RequireNodeStreams().WriteCallbackWrapperCtor = ctorBuilder;
        _ = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WriteCallbackWrapperUserCallbackField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_3);
        ctorIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WriteCallbackWrapperChunkSizeField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke method: public object Invoke(object[] args)
        // Called when user code does callback() or callback(error).
        // Calls the original user callback with no args (matching Node.js behavior).
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.RequireNodeStreams().WriteCallbackWrapperInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();
        var noCallbackLabel = invokeIL.DefineLabel();

        // Subtract _chunkSize from _stream._writableLength (if _stream is $Writable)
        // _stream._writableLength -= _chunkSize
        var notWritableLabel = invokeIL.DefineLabel();
        var afterSubtractLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Brfalse, afterSubtractLabel);

        // Try cast to $Writable first
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Isinst, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Brfalse, notWritableLabel);

        // stream._writableLength -= _chunkSize
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Dup);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperChunkSizeField);
        invokeIL.Emit(OpCodes.Sub);
        invokeIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableLengthField);

        // Check drain: if (_needDrain && _writableLength < _highWaterMark) emit drain
        var noDrainLabel1 = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableNeedDrainField);
        invokeIL.Emit(OpCodes.Brfalse, noDrainLabel1);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableHighWaterMarkField);
        invokeIL.Emit(OpCodes.Bge, noDrainLabel1);
        // _needDrain = false
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableNeedDrainField);
        // emit('drain', [])
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().WritableType);
        invokeIL.Emit(OpCodes.Ldstr, "drain");
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Callvirt, runtime.EventEmitter.Emit);
        invokeIL.Emit(OpCodes.Pop);
        invokeIL.MarkLabel(noDrainLabel1);
        invokeIL.Emit(OpCodes.Br, afterSubtractLabel);

        invokeIL.MarkLabel(notWritableLabel);
        // For Duplex: try cast to $Duplex and subtract from its _writableLength
        // (Duplex inherits from $Readable, not $Writable, so needs separate handling)
        // This is handled by $Duplex's own WriteCallbackWrapper or shared field access
        // For now, skip — Duplex will be handled in task #7

        invokeIL.MarkLabel(afterSubtractLabel);

        // if (_userCallback != null && _userCallback is $TSFunction)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperUserCallbackField);
        invokeIL.Emit(OpCodes.Brfalse, noCallbackLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperUserCallbackField);
        invokeIL.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        invokeIL.Emit(OpCodes.Brfalse, noCallbackLabel);

        // _userCallback.Invoke([])
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WriteCallbackWrapperUserCallbackField);
        invokeIL.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        invokeIL.Emit(OpCodes.Pop);

        invokeIL.MarkLabel(noCallbackLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    private void EmitTSWritableClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Writable : $EventEmitter
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Writable",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.EventEmitter.Type  // Extends $EventEmitter
        );
        runtime.RequireNodeStreams().WritableType = typeBuilder;

        // Define fields
        runtime.RequireNodeStreams().WritableWritableField = typeBuilder.DefineField("_writable", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableEndedField = typeBuilder.DefineField("_ended", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableFinishedField = typeBuilder.DefineField("_finished", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableDestroyedField = typeBuilder.DefineField("_destroyed", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableCorkedField = typeBuilder.DefineField("_corked", _types.Boolean, FieldAttributes.Private);

        var listType = _types.ListOfObject;
        runtime.RequireNodeStreams().WritableCorkBufferField = typeBuilder.DefineField("_corkBuffer", listType, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableWriteCallbackField = typeBuilder.DefineField("_writeCallback", _types.Object, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableFinalCallbackField = typeBuilder.DefineField("_finalCallback", _types.Object, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableHighWaterMarkField = typeBuilder.DefineField("_highWaterMark", _types.Int32, FieldAttributes.Public);
        runtime.RequireNodeStreams().WritableObjectModeField = typeBuilder.DefineField("_objectMode", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableAutoDestroyField = typeBuilder.DefineField("_autoDestroy", _types.Boolean, FieldAttributes.Private);
        runtime.RequireNodeStreams().WritableLengthField = typeBuilder.DefineField("_writableLength", _types.Int32, FieldAttributes.Public);
        runtime.RequireNodeStreams().WritableNeedDrainField = typeBuilder.DefineField("_needDrain", _types.Boolean, FieldAttributes.Public);
        runtime.RequireNodeStreams().WritableErroredField = typeBuilder.DefineField("_errored", _types.Boolean, FieldAttributes.Private); // #1030

        // Emit the helper callback wrapper class (after fields so it can reference them)
        EmitTSWriteCallbackWrapperClass(moduleBuilder, runtime);

        // Constructor
        EmitTSWritableCtor(typeBuilder, runtime);

        // Methods (Cork/Uncork before End, since End calls Uncork)
        EmitTSWritableWrite(typeBuilder, runtime);
        EmitTSWritableCork(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableUncork(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableEnd(typeBuilder, runtime);
        EmitTSWritableDestroy(typeBuilder, runtime);
        EmitTSWritableSetDefaultEncoding(typeBuilder, runtime);

        // Setter methods for callbacks
        EmitTSWritableSetWriteCallback(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableSetFinalCallback(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableSetObjectMode(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableSetAutoDestroy(typeBuilder, runtime.RequireNodeStreams());
        EmitTSWritableSetHighWaterMark(typeBuilder, runtime.RequireNodeStreams());

        // Property getters
        EmitTSWritablePropertyGetters(typeBuilder, runtime.RequireNodeStreams());

        typeBuilder.CreateType();
    }

    private void EmitTSWritableCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.RequireNodeStreams().WritableCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($EventEmitter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Ctor);

        // _writable = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableWritableField);

        // _ended = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableEndedField);

        // _finished = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableFinishedField);

        // _destroyed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableDestroyedField);

        // _corked = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableCorkedField);

        // _corkBuffer = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableCorkBufferField);

        // _highWaterMark = 16384
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableHighWaterMarkField);

        // _objectMode = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableObjectModeField);

        // _autoDestroy = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableAutoDestroyField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public bool Write(object? chunk, object? encoding, object? callback)
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireNodeStreams().WritableWrite = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var callCallbackLabel = il.DefineLabel();
        var notCorkedLabel = il.DefineLabel();

        // if (_destroyed || _ended) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // if (_corked) { _corkBuffer.Add(new object[] { chunk, encoding, callback }); return false; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableCorkedField);
        il.Emit(OpCodes.Brfalse, notCorkedLabel);

        // Buffer the write: _corkBuffer.Add(new object[] { chunk, encoding, callback })
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableCorkBufferField);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // encoding
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_3); // callback
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);
        il.Emit(OpCodes.Ldc_I4_0); // return false (matches interpreter)
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notCorkedLabel);

        // Compute chunk size: chunk is string ? string.Length : 0
        // (objectMode uses 1, but for simplicity we just use string length)
        var chunkSizeLocal = il.DeclareLocal(_types.Int32);
        var notStringLabel = il.DefineLabel();
        var afterChunkSizeLabel = il.DefineLabel();

        // if (_objectMode) chunkSize = 1
        var notObjectModeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableObjectModeField);
        il.Emit(OpCodes.Brfalse, notObjectModeLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, chunkSizeLocal);
        il.Emit(OpCodes.Br, afterChunkSizeLabel);

        il.MarkLabel(notObjectModeLabel);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, chunkSizeLocal);
        il.Emit(OpCodes.Br, afterChunkSizeLabel);
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, chunkSizeLocal);
        il.MarkLabel(afterChunkSizeLabel);

        // _writableLength += chunkSize
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        il.Emit(OpCodes.Ldloc, chunkSizeLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableLengthField);

        // If _writeCallback is set, invoke it
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableWriteCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableWriteCallbackField);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableWriteCallbackField);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);

        // Load 'this' (the stream) for InvokeWithThis
        il.Emit(OpCodes.Ldarg_0);

        // Create args array: [chunk, encoding ?? "utf8", callback_wrapper]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        // encoding ?? "utf8"
        var hasEncodingLabel = il.DefineLabel();
        var afterEncodingLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, hasEncodingLabel);
        il.Emit(OpCodes.Ldstr, "utf8");
        il.Emit(OpCodes.Br, afterEncodingLabel);
        il.MarkLabel(hasEncodingLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.MarkLabel(afterEncodingLabel);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        // Wrap the callback in $WriteCallbackWrapper(userCallback, stream, chunkSize)
        il.Emit(OpCodes.Ldarg_3); // callback (may be null)
        il.Emit(OpCodes.Ldarg_0); // this (stream)
        il.Emit(OpCodes.Ldloc, chunkSizeLocal);
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().WriteCallbackWrapperCtor);
        il.Emit(OpCodes.Stelem_Ref);

        // Call InvokeWithThis(this, args)
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.InvokeWithThis);
        il.Emit(OpCodes.Pop);

        // return _writableLength < _highWaterMark
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableHighWaterMarkField);
        il.Emit(OpCodes.Bge, returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        // Default: sync completion — subtract chunkSize immediately, call user callback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        il.Emit(OpCodes.Ldloc, chunkSizeLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableLengthField);

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(callCallbackLabel);
        // return _writableLength < _highWaterMark
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableLengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableHighWaterMarkField);
        il.Emit(OpCodes.Bge, returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        // Set _needDrain = true before returning false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableNeedDrainField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Writable End(object? chunk, object? encoding, object? callback)
        var method = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.RequireNodeStreams().WritableEnd = method;

        var il = method.GetILGenerator();
        var alreadyEndedLabel = il.DefineLabel();
        var noChunkLabel = il.DefineLabel();
        var noFinalCallbackLabel = il.DefineLabel();

        // if (_ended) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableEndedField);
        il.Emit(OpCodes.Brtrue, alreadyEndedLabel);

        // Write final chunk BEFORE setting _ended (Write() rejects when _ended is true)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noChunkLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().WritableWrite);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noChunkLabel);

        // _ended = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableEndedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableWritableField);

        // Flush cork buffer if corked
        var notCorkedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableCorkedField);
        il.Emit(OpCodes.Brfalse, notCorkedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().WritableUncork);
        il.MarkLabel(notCorkedLabel);

        // Invoke _finalCallback if set
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableFinalCallbackField);
        il.Emit(OpCodes.Brfalse, noFinalCallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableFinalCallbackField);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noFinalCallbackLabel);

        // _finalCallback.InvokeWithThis(this, [new $WriteCallbackWrapper(null)])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableFinalCallbackField);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldarg_0); // this
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // no user callback
        il.Emit(OpCodes.Ldarg_0); // stream
        il.Emit(OpCodes.Ldc_I4_0); // chunkSize = 0
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().WriteCallbackWrapperCtor);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.InvokeWithThis);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noFinalCallbackLabel);

        // _finished = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableFinishedField);

        // emit 'prefinish' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "prefinish");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // emit 'finish' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "finish");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        // If autoDestroy, emit 'close' event after 'finish'
        var skipCloseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableAutoDestroyField);
        il.Emit(OpCodes.Brfalse, skipCloseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipCloseLabel);

        il.MarkLabel(alreadyEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableCork(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void Cork()
        var method = typeBuilder.DefineMethod(
            "Cork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableCorkedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableUncork(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void Uncork()
        var method = typeBuilder.DefineMethod(
            "Uncork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        nodeStreams.WritableUncork = method;

        var il = method.GetILGenerator();
        var notCorkedLabel = il.DefineLabel();

        // if (!_corked) return
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableCorkedField);
        il.Emit(OpCodes.Brfalse, notCorkedLabel);

        // _corked = false (must be set before flushing so Write() calls go through)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableCorkedField);

        // Flush: for (int i = 0; i < _corkBuffer.Count; i++) {
        //   var entry = (object[])_corkBuffer[i];
        //   Write(entry[0], entry[1], entry[2]);
        // }
        var indexLocal = il.DeclareLocal(_types.Int32);
        var entryLocal = il.DeclareLocal(typeof(object[]));
        var loopStartLabel = il.DefineLabel();
        var loopCondLabel = il.DefineLabel();

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopCondLabel);

        // Loop body
        il.MarkLabel(loopStartLabel);

        // entry = (object[])_corkBuffer[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableCorkBufferField);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(object[]));
        il.Emit(OpCodes.Stloc, entryLocal);

        // this.Write(entry[0], entry[1], entry[2])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref); // chunk
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref); // encoding
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref); // callback
        il.Emit(OpCodes.Callvirt, nodeStreams.WritableWrite);
        il.Emit(OpCodes.Pop); // discard bool return

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop condition: i < _corkBuffer.Count
        il.MarkLabel(loopCondLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableCorkBufferField);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Blt, loopStartLabel);

        // _corkBuffer.Clear()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableCorkBufferField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Clear")!);

        il.MarkLabel(notCorkedLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableDestroy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Writable Destroy(object? error)
        var method = typeBuilder.DefineMethod(
            "Destroy",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.RequireNodeStreams().WritableDestroy = method;

        var il = method.GetILGenerator();
        var alreadyDestroyedLabel = il.DefineLabel();
        var noErrorLabel = il.DefineLabel();

        // if (_destroyed) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableDestroyedField);
        il.Emit(OpCodes.Brtrue, alreadyDestroyedLabel);

        // _destroyed = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableDestroyedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableWritableField);

        // _corkBuffer.Clear()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().WritableCorkBufferField);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Clear")!);

        // if (error != null) { _errored = true; emit 'error'; }  (#1030)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noErrorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().WritableErroredField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "error");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noErrorLabel);
        // emit 'close'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "close");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyDestroyedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetDefaultEncoding(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Writable SetDefaultEncoding(string encoding)
        var method = typeBuilder.DefineMethod(
            "SetDefaultEncoding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        _ = method;

        var il = method.GetILGenerator();
        // Just return this (no-op for compatibility)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetWriteCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetWriteCallback(object callback)
        var method = typeBuilder.DefineMethod(
            "SetWriteCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        nodeStreams.WritableSetWriteCallback = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableWriteCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetFinalCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetFinalCallback(object callback)
        var method = typeBuilder.DefineMethod(
            "SetFinalCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        nodeStreams.WritableSetFinalCallback = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableFinalCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetObjectMode(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetObjectMode(bool value)
        var method = typeBuilder.DefineMethod(
            "SetObjectMode",
            MethodAttributes.Public,
            _types.Void,
            [_types.Boolean]
        );
        nodeStreams.WritableSetObjectMode = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableObjectModeField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetAutoDestroy(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetAutoDestroy(bool value)
        var method = typeBuilder.DefineMethod(
            "SetAutoDestroy",
            MethodAttributes.Public,
            _types.Void,
            [_types.Boolean]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableAutoDestroyField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetHighWaterMark(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetHighWaterMark(int value)
        var method = typeBuilder.DefineMethod(
            "SetHighWaterMark",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.WritableHighWaterMarkField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritablePropertyGetters(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // errored property (#1030): backs stream.isErrored
        var erroredProp = typeBuilder.DefineProperty("Errored", PropertyAttributes.None, _types.Boolean, null);
        var getErrored = typeBuilder.DefineMethod(
            "get_Errored",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        nodeStreams.WritableErroredGetter = getErrored;
        var eil = getErrored.GetILGenerator();
        eil.Emit(OpCodes.Ldarg_0);
        eil.Emit(OpCodes.Ldfld, nodeStreams.WritableErroredField);
        eil.Emit(OpCodes.Ret);
        erroredProp.SetGetMethod(getErrored);

        // writable property: _writable && !_ended && !_destroyed
        // Note: Use PascalCase getter names (get_Writable) for GetFieldsProperty lookup
        var writableProp = typeBuilder.DefineProperty("Writable", PropertyAttributes.None, _types.Boolean, null);
        var getWritable = typeBuilder.DefineMethod(
            "get_Writable",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        var il = getWritable.GetILGenerator();
        var falseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableWritableField);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableEndedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableDestroyedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        writableProp.SetGetMethod(getWritable);

        // writableEnded property
        var writableEndedProp = typeBuilder.DefineProperty("WritableEnded", PropertyAttributes.None, _types.Boolean, null);
        var getWritableEnded = typeBuilder.DefineMethod(
            "get_WritableEnded",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getWritableEnded.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableEndedField);
        il.Emit(OpCodes.Ret);
        writableEndedProp.SetGetMethod(getWritableEnded);

        // writableFinished property
        var writableFinishedProp = typeBuilder.DefineProperty("WritableFinished", PropertyAttributes.None, _types.Boolean, null);
        var getWritableFinished = typeBuilder.DefineMethod(
            "get_WritableFinished",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getWritableFinished.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableFinishedField);
        il.Emit(OpCodes.Ret);
        writableFinishedProp.SetGetMethod(getWritableFinished);

        // writableLength property
        var writableLengthProp = typeBuilder.DefineProperty("WritableLength", PropertyAttributes.None, _types.Double, null);
        var getWritableLength = typeBuilder.DefineMethod(
            "get_WritableLength",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        il = getWritableLength.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableLengthField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
        writableLengthProp.SetGetMethod(getWritableLength);

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
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableDestroyedField);
        il.Emit(OpCodes.Ret);
        destroyedProp.SetGetMethod(getDestroyed);

        // writableObjectMode property
        var writableObjectModeProp = typeBuilder.DefineProperty("WritableObjectMode", PropertyAttributes.None, _types.Boolean, null);
        var getWritableObjectMode = typeBuilder.DefineMethod(
            "get_WritableObjectMode",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Boolean,
            Type.EmptyTypes
        );
        il = getWritableObjectMode.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableObjectModeField);
        il.Emit(OpCodes.Ret);
        writableObjectModeProp.SetGetMethod(getWritableObjectMode);

        // writableHighWaterMark property
        var writableHwmProp = typeBuilder.DefineProperty("WritableHighWaterMark", PropertyAttributes.None, _types.Double, null);
        var getWritableHwm = typeBuilder.DefineMethod(
            "get_WritableHighWaterMark",
            MethodAttributes.Public | MethodAttributes.SpecialName,
            _types.Double,
            Type.EmptyTypes
        );
        il = getWritableHwm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.WritableHighWaterMarkField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
        writableHwmProp.SetGetMethod(getWritableHwm);
    }
}
