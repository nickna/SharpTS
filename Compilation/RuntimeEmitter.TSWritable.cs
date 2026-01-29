using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Writable class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSWritable
/// </summary>
public partial class RuntimeEmitter
{
    // $Writable fields
    private FieldBuilder _tsWritableWritableField = null!;
    private FieldBuilder _tsWritableEndedField = null!;
    private FieldBuilder _tsWritableFinishedField = null!;
    private FieldBuilder _tsWritableDestroyedField = null!;
    private FieldBuilder _tsWritableCorkedField = null!;
    private FieldBuilder _tsWritableCorkBufferField = null!;
    private FieldBuilder _tsWritableWriteCallbackField = null!;
    private FieldBuilder _tsWritableFinalCallbackField = null!;

    private void EmitTSWritableClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Writable : $EventEmitter
        var typeBuilder = moduleBuilder.DefineType(
            "$Writable",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.TSEventEmitterType  // Extends $EventEmitter
        );
        runtime.TSWritableType = typeBuilder;

        // Define fields
        _tsWritableWritableField = typeBuilder.DefineField("_writable", _types.Boolean, FieldAttributes.Private);
        _tsWritableEndedField = typeBuilder.DefineField("_ended", _types.Boolean, FieldAttributes.Private);
        _tsWritableFinishedField = typeBuilder.DefineField("_finished", _types.Boolean, FieldAttributes.Private);
        _tsWritableDestroyedField = typeBuilder.DefineField("_destroyed", _types.Boolean, FieldAttributes.Private);
        _tsWritableCorkedField = typeBuilder.DefineField("_corked", _types.Boolean, FieldAttributes.Private);

        var listType = _types.ListOfObject;
        _tsWritableCorkBufferField = typeBuilder.DefineField("_corkBuffer", listType, FieldAttributes.Private);
        _tsWritableWriteCallbackField = typeBuilder.DefineField("_writeCallback", _types.Object, FieldAttributes.Private);
        _tsWritableFinalCallbackField = typeBuilder.DefineField("_finalCallback", _types.Object, FieldAttributes.Private);

        // Constructor
        EmitTSWritableCtor(typeBuilder, runtime);

        // Methods
        EmitTSWritableWrite(typeBuilder, runtime);
        EmitTSWritableEnd(typeBuilder, runtime);
        EmitTSWritableCork(typeBuilder, runtime);
        EmitTSWritableUncork(typeBuilder, runtime);
        EmitTSWritableDestroy(typeBuilder, runtime);
        EmitTSWritableSetDefaultEncoding(typeBuilder, runtime);

        // Setter methods for callbacks
        EmitTSWritableSetWriteCallback(typeBuilder, runtime);
        EmitTSWritableSetFinalCallback(typeBuilder, runtime);

        // Property getters
        EmitTSWritablePropertyGetters(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSWritableCtor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        runtime.TSWritableCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($EventEmitter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterCtor);

        // _writable = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsWritableWritableField);

        // _ended = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableEndedField);

        // _finished = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableFinishedField);

        // _destroyed = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableDestroyedField);

        // _corked = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableCorkedField);

        // _corkBuffer = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsWritableCorkBufferField);

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
        runtime.TSWritableWrite = method;

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var callCallbackLabel = il.DefineLabel();

        // if (_destroyed || _ended) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // If _writeCallback is set, invoke it
        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableWriteCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Call _writeCallback with (chunk, encoding, done_callback)
        // For simplicity, we invoke via $TSFunction.Invoke if it's a function
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableWriteCallbackField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableWriteCallbackField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);

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
        il.Emit(OpCodes.Ldarg_3); // callback (may be null)
        il.Emit(OpCodes.Stelem_Ref);

        // Call InvokeWithThis(this, args) instead of Invoke(args)
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Pop);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        // Default: just accept the data, call user callback if provided
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, callCallbackLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(callCallbackLabel);
        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
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
        runtime.TSWritableEnd = method;

        var il = method.GetILGenerator();
        var alreadyEndedLabel = il.DefineLabel();

        // if (_ended) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableEndedField);
        il.Emit(OpCodes.Brtrue, alreadyEndedLabel);

        // _ended = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsWritableEndedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableWritableField);

        // Write final chunk if provided
        var noChunkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noChunkLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSWritableWrite);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noChunkLabel);

        // _finished = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsWritableFinishedField);

        // emit 'finish' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "finish");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableCork(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void Cork()
        var method = typeBuilder.DefineMethod(
            "Cork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSWritableCork = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsWritableCorkedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableUncork(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void Uncork()
        var method = typeBuilder.DefineMethod(
            "Uncork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );
        runtime.TSWritableUncork = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableCorkedField);
        // TODO: flush cork buffer
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
        runtime.TSWritableDestroy = method;

        var il = method.GetILGenerator();
        var alreadyDestroyedLabel = il.DefineLabel();
        var noErrorLabel = il.DefineLabel();

        // if (_destroyed) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableDestroyedField);
        il.Emit(OpCodes.Brtrue, alreadyDestroyedLabel);

        // _destroyed = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsWritableDestroyedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsWritableWritableField);

        // _corkBuffer.Clear()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableCorkBufferField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Clear")!);

        // if (error != null) emit 'error'
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noErrorLabel);
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

    private void EmitTSWritableSetDefaultEncoding(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $Writable SetDefaultEncoding(string encoding)
        var method = typeBuilder.DefineMethod(
            "SetDefaultEncoding",
            MethodAttributes.Public,
            typeBuilder,
            [_types.String]
        );
        runtime.TSWritableSetDefaultEncoding = method;

        var il = method.GetILGenerator();
        // Just return this (no-op for compatibility)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetWriteCallback(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void SetWriteCallback(object callback)
        var method = typeBuilder.DefineMethod(
            "SetWriteCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsWritableWriteCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritableSetFinalCallback(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public void SetFinalCallback(object callback)
        var method = typeBuilder.DefineMethod(
            "SetFinalCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsWritableFinalCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSWritablePropertyGetters(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
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
        il.Emit(OpCodes.Ldfld, _tsWritableWritableField);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableEndedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsWritableDestroyedField);
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
        il.Emit(OpCodes.Ldfld, _tsWritableEndedField);
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
        il.Emit(OpCodes.Ldfld, _tsWritableFinishedField);
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
        il.Emit(OpCodes.Ldfld, _tsWritableCorkBufferField);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetProperty("Count")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Ldfld, _tsWritableDestroyedField);
        il.Emit(OpCodes.Ret);
        destroyedProp.SetGetMethod(getDestroyed);
    }
}
