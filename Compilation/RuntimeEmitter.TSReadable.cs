using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Readable class for standalone stream support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSReadable
/// </summary>
public partial class RuntimeEmitter
{
    // $Readable fields
    private FieldBuilder _tsReadableBufferField = null!;
    private FieldBuilder _tsReadablePipeDestinationsField = null!;
    private FieldBuilder _tsReadableEndedField = null!;
    private FieldBuilder _tsReadableDestroyedField = null!;
    private FieldBuilder _tsReadableEncodingField = null!;
    private FieldBuilder _tsReadableReadableField = null!;

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

        // Constructor
        EmitTSReadableCtor(typeBuilder, runtime, queueOfObject);

        // Methods that don't depend on Duplex
        EmitTSReadableRead(typeBuilder, runtime, queueOfObject);
        EmitTSReadablePush(typeBuilder, runtime, queueOfObject);
        // NOTE: Pipe is emitted in Phase 2 since it needs Duplex type
        EmitTSReadableUnpipe(typeBuilder, runtime);
        EmitTSReadableSetEncoding(typeBuilder, runtime);
        EmitTSReadableDestroy(typeBuilder, runtime, queueOfObject);
        EmitTSReadableUnshift(typeBuilder, runtime, queueOfObject);
        EmitTSReadablePause(typeBuilder, runtime);
        EmitTSReadableResume(typeBuilder, runtime);
        EmitTSReadableIsPaused(typeBuilder, runtime);

        // Property getters
        EmitTSReadablePropertyGetters(typeBuilder, runtime, queueOfObject);
    }

    /// <summary>
    /// Phase 2: Emit methods that depend on Duplex type.
    /// Must be called after Duplex type is defined.
    /// </summary>
    private void EmitTSReadableMethods(EmittedRuntime runtime)
    {
        var typeBuilder = (TypeBuilder)runtime.TSReadableType;
        var queueOfObject = typeof(Queue<object?>);

        // Emit Pipe method (depends on TSDuplexType)
        EmitTSReadablePipe(typeBuilder, runtime, queueOfObject);

        // Finalize the type
        typeBuilder.CreateType();
    }

    // Keep the old method for backward compatibility (calls both phases when Duplex is available)
    private void EmitTSReadableClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitTSReadableTypeDefinition(moduleBuilder, runtime);
        // Note: EmitTSReadableMethods will be called separately after Duplex is defined
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
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
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
        runtime.TSReadableRead = method;

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

        // Simplified: read all and concatenate
        var resultLocal = il.DeclareLocal(_types.StringBuilder);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // var result = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.StringBuilder.GetConstructor(Type.EmptyTypes)!);
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
        var dequeueMethod = queueType.GetMethod("Dequeue")!;
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

        il.Emit(OpCodes.Callvirt, _types.StringBuilder.GetMethod("Append", [_types.String])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return result.ToString()
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
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

        // Emit 'end' event: this.Emit("end", [])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "end");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.TSEventEmitterEmit);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, returnFalseLabel);

        il.MarkLabel(notNullLabel);
        // _readBuffer.Enqueue(chunk)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Ldarg_1);
        var enqueueMethod = queueType.GetMethod("Enqueue")!;
        il.Emit(OpCodes.Callvirt, enqueueMethod);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

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
        il.Emit(OpCodes.Pop);
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
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(notWritableLabel);
        // Just dequeue and discard if not writable
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
        runtime.TSReadableUnpipe = method;

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
        runtime.TSReadableSetEncoding = method;

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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);

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

    private void EmitTSReadableUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        // public $Readable Unshift(object chunk)
        var method = typeBuilder.DefineMethod(
            "Unshift",
            MethodAttributes.Public,
            typeBuilder,
            [_types.Object]
        );
        runtime.TSReadableUnshift = method;

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
        runtime.TSReadablePause = method;

        var il = method.GetILGenerator();
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
        runtime.TSReadableResume = method;

        var il = method.GetILGenerator();
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
        runtime.TSReadableIsPaused = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0); // Always return false in sync mode
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
    }
}
