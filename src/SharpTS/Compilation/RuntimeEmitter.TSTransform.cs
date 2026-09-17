using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Transform class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSTransform
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $TransformDoneCallback helper class that wraps the done callback
    /// passed to transform functions. When called with (error, data), it pushes
    /// data to the readable side of the transform stream.
    /// </summary>
    private void EmitTSTransformDoneCallbackClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TransformDoneCallback
        // Standalone class with Invoke method matching the calling convention
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TransformDoneCallback",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object  // Standalone class, not extending $TSFunction
        );
        runtime.RequireNodeStreams().TransformDoneCallbackType = typeBuilder;

        // Fields
        runtime.RequireNodeStreams().TransformDoneCallbackStreamField = typeBuilder.DefineField(
            "_stream", _types.Object, FieldAttributes.Private);
        runtime.RequireNodeStreams().TransformDoneCallbackUserCallbackField = typeBuilder.DefineField(
            "_userCallback", _types.Object, FieldAttributes.Private);

        // Constructor: public $TransformDoneCallback(object stream, object userCallback)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]
        );
        runtime.RequireNodeStreams().TransformDoneCallbackCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor (Object)
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._stream = stream
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().TransformDoneCallbackStreamField);
        // this._userCallback = userCallback
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, runtime.RequireNodeStreams().TransformDoneCallbackUserCallbackField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke method: public object Invoke(object[] args)
        // This is called when the transform callback calls callback(error, data)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.RequireNodeStreams().TransformDoneCallbackInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();
        var noDataLabel = invokeIL.DefineLabel();
        var noUserCallbackLabel = invokeIL.DefineLabel();
        var returnLabel = invokeIL.DefineLabel();

        // Extract data from args[1] if present
        // if (args != null && args.Length > 1 && args[1] != null)
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Brfalse, noDataLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Ldc_I4_2);
        invokeIL.Emit(OpCodes.Blt, noDataLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Brfalse, noDataLabel);

        // data = args[1]; _stream.Push(data)
        // Cast _stream to $Readable (which has Push method)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformDoneCallbackStreamField);
        invokeIL.Emit(OpCodes.Castclass, runtime.RequireNodeStreams().ReadableType);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_1);
        invokeIL.Emit(OpCodes.Ldelem_Ref);
        invokeIL.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePush);
        invokeIL.Emit(OpCodes.Pop);

        invokeIL.MarkLabel(noDataLabel);

        // Call user callback if provided
        // if (_userCallback != null && _userCallback is $TSFunction)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformDoneCallbackUserCallbackField);
        invokeIL.Emit(OpCodes.Brfalse, noUserCallbackLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformDoneCallbackUserCallbackField);
        invokeIL.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        invokeIL.Emit(OpCodes.Brfalse, noUserCallbackLabel);

        // _userCallback.Invoke([])
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformDoneCallbackUserCallbackField);
        invokeIL.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        invokeIL.Emit(OpCodes.Pop);

        invokeIL.MarkLabel(noUserCallbackLabel);
        invokeIL.Emit(OpCodes.Ldnull);
        invokeIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    private void EmitTSTransformClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // First emit the helper callback class
        EmitTSTransformDoneCallbackClass(moduleBuilder, runtime);
        // Define class: public class $Transform : $Duplex
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Transform",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            runtime.RequireNodeStreams().DuplexType  // Extends $Duplex
        );
        runtime.RequireNodeStreams().TransformType = typeBuilder;

        // Define transform-specific fields
        runtime.RequireNodeStreams().TransformCallbackField = typeBuilder.DefineField("_transformCallback", _types.Object, FieldAttributes.Family);
        runtime.RequireNodeStreams().TransformFlushCallbackField = typeBuilder.DefineField("_flushCallback", _types.Object, FieldAttributes.Family);

        // Constructor
        EmitTSTransformCtor(typeBuilder, runtime.RequireNodeStreams());

        // Override Write method to call transform callback
        runtime.RequireNodeStreams().TransformWriteMethod = EmitTSTransformWrite(typeBuilder, runtime);

        // Override End method to call flush callback
        EmitTSTransformEnd(typeBuilder, runtime);

        // Setter methods for callbacks
        EmitTSTransformSetTransformCallback(typeBuilder, runtime.RequireNodeStreams());
        EmitTSTransformSetFlushCallback(typeBuilder, runtime.RequireNodeStreams());

        typeBuilder.CreateType();
    }

    private void EmitTSTransformCtor(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        nodeStreams.TransformCtor = ctor;

        var il = ctor.GetILGenerator();

        // Call base constructor ($Duplex)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, nodeStreams.DuplexCtor);

        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitTSTransformWrite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public override bool Write(object? chunk, object? encoding, object? callback)
        // Remove NewSlot to properly override $Duplex.Write
        var method = typeBuilder.DefineMethod(
            "Write",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            _types.Boolean,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var returnFalseLabel = il.DefineLabel();
        var noTransformLabel = il.DefineLabel();

        // if (_destroyed || _writeEnded) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().ReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().DuplexWriteEndedField);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // If _transformCallback is set, invoke it
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformCallbackField);
        il.Emit(OpCodes.Brfalse, noTransformLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformCallbackField);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noTransformLabel);

        // Call transform callback: transform(chunk, encoding, done)
        // The done callback should push transformed data to readable side

        // Declare locals for callback and args
        var callbackLocal = il.DeclareLocal(runtime.FunctionValues.Type);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Store callback
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformCallbackField);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Create args: [chunk, encoding, done_callback]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // chunk
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
        // Create a $TransformDoneCallback that wraps the user callback
        // This callback will push data to the readable side when called with (error, data)
        il.Emit(OpCodes.Ldarg_0); // this (the Transform stream)
        il.Emit(OpCodes.Ldarg_3); // user callback or null
        il.Emit(OpCodes.Newobj, runtime.RequireNodeStreams().TransformDoneCallbackCtor);
        il.Emit(OpCodes.Stelem_Ref);
        // Store args array
        il.Emit(OpCodes.Stloc, argsLocal);

        // Use InvokeWithThis to properly handle method shorthand syntax
        // which expects 'this' to be bound to the transform instance
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldarg_0);  // this (for InvokeWithThis's thisArg parameter)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.InvokeWithThis);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noTransformLabel);
        // Default: pass through (push to readable)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePush);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitTSTransformEnd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public override $Duplex End(object? chunk, object? encoding, object? callback)
        // Must return $Duplex (same as base) so virtual dispatch works correctly
        var method = typeBuilder.DefineMethod(
            "End",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            runtime.RequireNodeStreams().DuplexType,
            [_types.Object, _types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        var alreadyEndedLabel = il.DefineLabel();
        var noFlushLabel = il.DefineLabel();

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

        // Write final chunk if provided
        var noChunkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noChunkLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        // Call Write method
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().TransformWriteMethod);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noChunkLabel);

        // Call flush callback if set
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformFlushCallbackField);
        il.Emit(OpCodes.Brfalse, noFlushLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformFlushCallbackField);
        il.Emit(OpCodes.Isinst, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Brfalse, noFlushLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtime.RequireNodeStreams().TransformFlushCallbackField);
        il.Emit(OpCodes.Castclass, runtime.FunctionValues.Type);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_3); // callback
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.FunctionValues.Invoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noFlushLabel);

        // Push null to signal end of readable side
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.RequireNodeStreams().ReadablePush);
        il.Emit(OpCodes.Pop);

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
    }

    private void EmitTSTransformSetTransformCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "SetTransformCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        nodeStreams.TransformSetTransformCallback = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.TransformCallbackField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSTransformSetFlushCallback(TypeBuilder typeBuilder, EmittedNodeStreamRuntime nodeStreams)
    {
        var method = typeBuilder.DefineMethod(
            "SetFlushCallback",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]
        );
        nodeStreams.TransformSetFlushCallback = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, nodeStreams.TransformFlushCallbackField);
        il.Emit(OpCodes.Ret);
    }
}
