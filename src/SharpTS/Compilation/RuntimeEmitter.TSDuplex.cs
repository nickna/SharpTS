using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Duplex class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSDuplex
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Phase 1: Define the $Duplex type, fields, and methods.
    /// Does NOT call CreateType() - that's done in Phase 2.
    /// </summary>
    private void EmitTSDuplexTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public class $Duplex : $Readable
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Duplex",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.RequireNodeStreams().ReadableType  // Extends $Readable
        );
        runtime.RequireNodeStreams().DuplexType = typeBuilder;

        // Define writable-side fields
        runtime.RequireNodeStreams().DuplexWritableField = typeBuilder.DefineField("_writable", _types.Boolean, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWriteEndedField = typeBuilder.DefineField("_writeEnded", _types.Boolean, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWriteFinishedField = typeBuilder.DefineField("_writeFinished", _types.Boolean, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWriteCorkedField = typeBuilder.DefineField("_writeCorked", _types.Boolean, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWriteCorkBufferField = typeBuilder.DefineField("_writeCorkBuffer", _types.ListOfObject, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWriteCallbackField = typeBuilder.DefineField("_writeCallback", _types.Object, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexFinalCallbackField = typeBuilder.DefineField("_finalCallback", _types.Object, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWritableObjectModeField = typeBuilder.DefineField("_writableObjectMode", _types.Boolean, FieldAttributes.Family);
        typeBuilder.DefineField("_duplexWritableLength", _types.Int32, FieldAttributes.Family);
        runtime.RequireNodeStreams().DuplexWritableHighWaterMarkField = typeBuilder.DefineField("_duplexWritableHwm", _types.Int32, FieldAttributes.Family);
        typeBuilder.DefineField("_duplexNeedDrain", _types.Boolean, FieldAttributes.Family);

        // Constructor
        EmitTSDuplexCtor(typeBuilder, runtime.RequireNodeStreams());

        // Writable-side methods
        runtime.RequireNodeStreams().DuplexWrite = EmitTSDuplexWrite(typeBuilder, runtime);
        runtime.RequireNodeStreams().DuplexEnd = EmitTSDuplexEnd(typeBuilder, runtime);
        EmitTSDuplexCork(typeBuilder, runtime.RequireNodeStreams());
        EmitTSDuplexUncork(typeBuilder, runtime.RequireNodeStreams());

        // Property getters for writable side
        EmitTSDuplexWritablePropertyGetters(typeBuilder, runtime.RequireNodeStreams());

        // Setter methods for callbacks
        EmitTSDuplexSetWriteCallback(typeBuilder, runtime.RequireNodeStreams());
        EmitTSDuplexSetFinalCallback(typeBuilder, runtime.RequireNodeStreams());
        EmitTSDuplexSetObjectMode(typeBuilder, runtime.RequireNodeStreams());
    }

    /// <summary>
    /// Phase 2: Finalize the $Duplex type by calling CreateType().
    /// Must be called after $Readable is finalized.
    /// </summary>
    private void EmitTSDuplexFinalize(EmittedNodeStreamRuntime nodeStreams)
    {
        var typeBuilder = (TypeBuilder)nodeStreams.DuplexType;
        typeBuilder.CreateType();
    }

    private void EmitTSDuplexCtor(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        nodeStreams.DuplexCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($Readable)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, nodeStreams.ReadableCtor);

        // _writable = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWritableField);

        // _writeEnded = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteEndedField);

        // _writeFinished = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteFinishedField);

        // _writeCorked = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteCorkedField);

        // _writeCorkBuffer = new List<object?>()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteCorkBufferField);

        // _writableObjectMode = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWritableObjectModeField);

        // _duplexWritableHwm = 16384
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, 16384);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWritableHighWaterMarkField);

        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitTSDuplexWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public virtual bool Write(object? chunk, object? encoding, object? callback)
        // Made virtual so Transform and PassThrough can override
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var callCallbackLabel = il.DefineLabel();
        var noCallbackLabel = il.DefineLabel();

        // if (_destroyed || _writeEnded) return false
        // Check _destroyed from inherited $Readable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().ReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // If _writeCallback is set, invoke it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteCallbackField);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteCallbackField);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);

        // Load 'this' for InvokeWithThis
        il.Emit(OpCodes.Ldarg_0);

        // Create args array: [chunk, encoding ?? "utf8", callback_wrapper]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
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
        // Wrap the callback in $WriteCallbackWrapper(userCallback, stream, chunkSize=0)
        // Duplex uses chunkSize=0 for simplicity (backpressure tracked differently)
        il.Emit(OpCodes.Ldarg_3); // callback (may be null)
        il.Emit(OpCodes.Ldnull); // stream (null — Duplex doesn't use Writable's _writableLength)
        il.Emit(OpCodes.Ldc_I4_0); // chunkSize
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().WriteCallbackWrapperCtor);
        il.Emit(OpCodes.Stelem_Ref);

        // Call InvokeWithThis(this, args)
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.InvokeWithThis);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        // Call user callback if provided
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
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitTSDuplexEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public virtual $Duplex End(object? chunk, object? encoding, object? callback)
        // Made virtual so Transform can override
        var method = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeBuilder,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var alreadyEndedLabel = il.DefineLabel();

        // if (_writeEnded) return this
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, alreadyEndedLabel);

        // _writeEnded = true; _writable = false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().DuplexWriteEndedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().DuplexWritableField);

        // _writeFinished = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().DuplexWriteFinishedField);

        // emit 'finish' event
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "finish");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.EventEmitter.Emit);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(alreadyEndedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitTSDuplexCork(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "Cork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteCorkedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDuplexUncork(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "Uncork",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteCorkedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDuplexWritablePropertyGetters(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // writable property - Note: Use PascalCase getter names
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
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWritableField);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeStreams.ReadableDestroyedField);
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
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWriteEndedField);
        il.Emit(OpCodes.Ret);
        writableEndedProp.SetGetMethod(getWritableEnded);

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
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWritableObjectModeField);
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
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWritableHighWaterMarkField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
        writableHwmProp.SetGetMethod(getWritableHwm);

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
        il.Emit(OpCodes.Ldfld, nodeStreams.DuplexWriteFinishedField);
        il.Emit(OpCodes.Ret);
        writableFinishedProp.SetGetMethod(getWritableFinished);
    }

    private void EmitTSDuplexSetWriteCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "SetWriteCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        nodeStreams.DuplexSetWriteCallback = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWriteCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDuplexSetFinalCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "SetFinalCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexFinalCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSDuplexSetObjectMode(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        // public void SetObjectMode(bool value)
        // Sets both the readable side (_objectMode from $Readable) and writable side
        var method = typeBuilder.DefineMethod(
            "SetObjectMode",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Void,
            [_types.Boolean]
        );
        nodeStreams.DuplexSetObjectMode = method;

        var il = method.GetILGenerator();
        // Set inherited _objectMode from $Readable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.ReadableObjectModeField);
        // Set _writableObjectMode
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.DuplexWritableObjectModeField);
        il.Emit(OpCodes.Ret);
    }
}
