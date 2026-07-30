using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$WritableStream</c>, <c>$WritableStreamDefaultController</c>,
/// and <c>$WritableStreamDefaultWriter</c> classes for the WHATWG Web Streams API.
/// </summary>
/// <remarks>
/// Pure-IL companion to <see cref="SharpTS.Runtime.Types.SharpTSWritableStream"/>.
/// Compiled DLLs constructed with <c>new WritableStream(...)</c> instantiate
/// these emitted classes directly via <c>Newobj</c> rather than going through
/// the late-binding pattern in <c>RuntimeEmitter.StreamsWeb.cs</c>.
///
/// Public method names use PascalCase so the JS-side reflection-based dispatch
/// in <c>GetFieldsProperty</c> finds them via case-insensitive
/// <see cref="Type.GetMethod(string, BindingFlags)"/> lookup.
///
/// User callbacks (<c>start</c>, <c>write</c>, <c>close</c>, <c>abort</c>) are
/// dispatched through <c>$Runtime.InvokeMethodValue</c> which already handles
/// every callable shape SharpTS produces (TSFunction, BoundTSFunction, the
/// various wrapper types). Async user callbacks that return a <c>$Promise</c>
/// or <c>Task&lt;object&gt;</c> are unwrapped via the helper
/// <c>EmitCallUserCallbackAsTask</c> which produces a <c>Task&lt;object&gt;</c>
/// suitable for the compiled <c>await</c> path.
/// </remarks>
public partial class RuntimeEmitter
{
    // --- $WritableStream fields ---
    private FieldBuilder _writableStreamWriteCallbackField = null!;
    private FieldBuilder _writableStreamCloseCallbackField = null!;
    private FieldBuilder _writableStreamAbortCallbackField = null!;
    private FieldBuilder _writableStreamHwmField = null!;
    private FieldBuilder _writableStreamStateField = null!;
    private FieldBuilder _writableStreamStoredErrorField = null!;
    private FieldBuilder _writableStreamWriterField = null!;
    private FieldBuilder _writableStreamControllerField = null!;

    // --- $WritableStreamDefaultController fields ---
    private FieldBuilder _writableControllerStreamField = null!;

    // --- $WritableStreamDefaultWriter fields ---
    private FieldBuilder _writableWriterStreamField = null!;

    private void EmitWritableStreamClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Emit controller and writer first as forward type declarations so the
        // stream constructor can reference their ctors. Phase 2 fills in their
        // method bodies after $WritableStream is fully defined.
        var controllerBuilder = moduleBuilder.DefineType(
            "$WritableStreamDefaultController",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var writerBuilder = moduleBuilder.DefineType(
            "$WritableStreamDefaultWriter",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        var streamBuilder = moduleBuilder.DefineType(
            "$WritableStream",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        runtime.WritableStreamType = streamBuilder;
        _ = controllerBuilder;
        _ = writerBuilder;

        EmitWritableStreamFields(streamBuilder);

        // Define writer fields + ctor BEFORE the stream's GetWriter so the
        // forward reference resolves cleanly. The writer ctor is captured for
        // use by GetWriter via Newobj.
        _writableWriterStreamField = writerBuilder.DefineField(
            "_stream", streamBuilder, FieldAttributes.Private);
        var writerCtor = EmitWritableStreamWriterCtor(writerBuilder, streamBuilder);
        _ = writerCtor;

        // Define controller fields + ctor BEFORE the stream uses it.
        _writableControllerStreamField = controllerBuilder.DefineField(
            "_stream", streamBuilder, FieldAttributes.Private);
        var controllerCtor = EmitWritableStreamControllerCtor(controllerBuilder, streamBuilder);
        EmitWritableStreamControllerErrorMethod(controllerBuilder, streamBuilder, runtime);

        var streamCtor = EmitWritableStreamConstructor(streamBuilder, controllerBuilder, controllerCtor, runtime);
        runtime.WritableStreamCtor = streamCtor;

        var writeMethod = EmitWritableStreamWrite(streamBuilder, runtime);
        var closeMethod = EmitWritableStreamClose(streamBuilder, runtime);
        var abortMethod = EmitWritableStreamAbort(streamBuilder, runtime);
        var lockedGetter = EmitWritableStreamLockedGetter(streamBuilder);
        EmitWritableStreamGetWriter(streamBuilder, writerCtor);

        _ = writeMethod;
        _ = closeMethod;
        _ = abortMethod;

        EmitWritableStreamWriterDelegatingMethod(writerBuilder, streamBuilder, "Write", writeMethod, [_types.Object]);
        EmitWritableStreamWriterDelegatingMethod(writerBuilder, streamBuilder, "Close", closeMethod, Type.EmptyTypes);
        EmitWritableStreamWriterDelegatingMethod(writerBuilder, streamBuilder, "Abort", abortMethod, [_types.Object]);
        EmitWritableStreamWriterReleaseLock(writerBuilder, streamBuilder);
        EmitWritableStreamWriterDesiredSizeGetter(writerBuilder, streamBuilder);
        EmitWritableStreamWriterClosedReadyGetter(writerBuilder, streamBuilder, "Closed");
        EmitWritableStreamWriterClosedReadyGetter(writerBuilder, streamBuilder, "Ready");

        // Finalize types in dependency order: controller first (no other
        // emitted-type dependencies), then writer (depends on controller? no,
        // just on stream), then stream (references controller and writer ctors).
        controllerBuilder.CreateType();
        writerBuilder.CreateType();
        streamBuilder.CreateType();
    }

    private void EmitWritableStreamFields(TypeBuilder t)
    {
        _writableStreamWriteCallbackField = t.DefineField("_writeCallback", _types.Object, FieldAttributes.Private);
        _writableStreamCloseCallbackField = t.DefineField("_closeCallback", _types.Object, FieldAttributes.Private);
        _writableStreamAbortCallbackField = t.DefineField("_abortCallback", _types.Object, FieldAttributes.Private);
        _writableStreamHwmField = t.DefineField("_highWaterMark", _types.Double, FieldAttributes.Private);
        // _state: 0 = writable, 1 = closed, 2 = errored
        _writableStreamStateField = t.DefineField("_state", _types.Int32, FieldAttributes.Private);
        _writableStreamStoredErrorField = t.DefineField("_storedError", _types.Object, FieldAttributes.Private);
        _writableStreamWriterField = t.DefineField("_writer", _types.Object, FieldAttributes.Private);
        _writableStreamControllerField = t.DefineField("_controller", _types.Object, FieldAttributes.Private);
    }

    private ConstructorBuilder EmitWritableStreamConstructor(TypeBuilder streamBuilder, TypeBuilder controllerBuilder, ConstructorBuilder controllerCtor, EmittedRuntime runtime)
    {
        // public $WritableStream(object? underlyingSink, object? strategy)
        var ctor = streamBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]);

        var il = ctor.GetILGenerator();

        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _state = 0 (writable)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _writableStreamStateField);

        // _highWaterMark = ExtractHighWaterMark(strategy)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        EmitExtractHighWaterMarkInline(il);
        il.Emit(OpCodes.Stfld, _writableStreamHwmField);

        // Extract user callbacks from underlyingSink (write/close/abort).
        // Uses GetFieldsProperty so the sink can be a Dictionary, $Object,
        // or any object exposing the methods (e.g., $TransformSinkHolder).
        // Each is left as object? — InvokeMethodValue handles dispatch later.
        EmitExtractCallbackFromDict(il, _writableStreamWriteCallbackField, "write", runtime);
        EmitExtractCallbackFromDict(il, _writableStreamCloseCallbackField, "close", runtime);
        EmitExtractCallbackFromDict(il, _writableStreamAbortCallbackField, "abort", runtime);

        // _controller = new $WritableStreamDefaultController(this). Previously
        // left null; now wired so `write(chunk, controller)` callbacks receive
        // a real controller exposing Error(reason).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, controllerCtor);
        il.Emit(OpCodes.Stfld, _writableStreamControllerField);

        il.Emit(OpCodes.Ret);
        return ctor;
    }

    /// <summary>
    /// Emits IL that extracts a named callback (e.g., "write"/"close"/"abort")
    /// from the underlyingSink and stores it in the target field.
    /// </summary>
    /// <remarks>
    /// Uses <c>$Runtime.GetFieldsProperty</c> for the lookup so the sink can
    /// be ANY shape that exposes the callback by name:
    /// <list type="bullet">
    ///   <item>The user-supplied <c>{ write(){...} }</c> as a
    ///     <c>Dictionary&lt;string, object?&gt;</c> (the dict-key fast path).</item>
    ///   <item>An emitted <c>$Object</c> with method-shorthand entries.</item>
    ///   <item>A pure-IL holder class with public PascalCase methods (e.g.,
    ///     <c>$TransformSinkHolder.Write</c>) — the reflection-based fallback
    ///     in GetFieldsProperty wraps these as <c>$TSFunction</c>.</item>
    /// </list>
    /// A returned <c>$Undefined.Instance</c> is normalised to <c>null</c> so
    /// the "no callback" check downstream (Brfalse) still works.
    /// </remarks>
    private void EmitExtractCallbackFromDict(ILGenerator il, FieldBuilder targetField, string callbackName, EmittedRuntime runtime)
    {
        // Stack: []
        // Get the value via $Runtime.GetFieldsProperty(sink, callbackName).
        // GetFieldsProperty returns $Undefined.Instance for missing properties,
        // so we need to detect that and store null instead.
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, callbackName);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        // If value is $Undefined.Instance, normalise to null.
        var notUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Bne_Un, notUndefinedLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(notUndefinedLabel);

        // this.<targetField> = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stfld, targetField);
    }

    /// <summary>
    /// Emits IL that calls a user callback through <c>$Runtime.InvokeMethodValue</c>
    /// and converts the result into a <see cref="Task{TResult}"/> of object,
    /// suitable for return as a Promise-like value. Stack on entry: callback, args[].
    /// Stack on exit: Task&lt;object&gt;.
    /// </summary>
    private void EmitWrapResultAsTask(ILGenerator il, EmittedRuntime runtime)
    {
        // Call $Runtime.InvokeMethodValue(receiver=null, callback, args)
        // The callback and args are already on the stack; prepend null receiver.
        // Actually re-do: the caller should set up the stack as:
        //   [null receiver, callback, args]
        // before calling this helper.
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        // Stack: [object result]
        // Convert to Task<object>:
        //   - if result is Task<object>, return it
        //   - if result is $Promise, return promise.GetValueAsync()
        //   - else return Task.FromResult<object>(result ?? undefined)
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        var notTaskLabel = il.DefineLabel();
        var notPromiseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // Task<object> check
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Br, doneLabel);

        // $Promise check
        il.MarkLabel(notTaskLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseGetValueAsync);
        il.Emit(OpCodes.Br, doneLabel);

        // Default: Task.FromResult(result ?? undefined)
        il.MarkLabel(notPromiseLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));

        il.MarkLabel(doneLabel);
    }

    private MethodBuilder EmitWritableStreamWrite(TypeBuilder t, EmittedRuntime runtime)
    {
        // public Task<object> Write(object chunk)
        var method = t.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.TaskOfObject,
            [_types.Object]);

        var il = method.GetILGenerator();

        var noCallbackLabel = il.DefineLabel();

        // if (_writeCallback == null) return Task.FromResult<object>(undefined)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamWriteCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Stack setup for InvokeMethodValue: [receiver=null, callback, args]
        // args = [chunk, _controller] — user write(chunk, controller) sees
        // the $WritableStreamDefaultController as the second parameter.
        il.Emit(OpCodes.Ldnull);                                                // receiver
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamWriteCallbackField);              // callback
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);                                  // args = new object[2]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);                                                // chunk
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamControllerField);                 // controller
        il.Emit(OpCodes.Stelem_Ref);
        EmitWrapResultAsTask(il, runtime);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitWritableStreamClose(TypeBuilder t, EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.TaskOfObject,
            Type.EmptyTypes);

        var il = method.GetILGenerator();

        // _state = 1 (closed)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _writableStreamStateField);

        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamCloseCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Stack: [receiver=null, callback, empty args]
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamCloseCallbackField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        EmitWrapResultAsTask(il, runtime);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitWritableStreamAbort(TypeBuilder t, EmittedRuntime runtime)
    {
        var method = t.DefineMethod(
            "Abort",
            MethodAttributes.Public,
            _types.TaskOfObject,
            [_types.Object]);

        var il = method.GetILGenerator();

        // _state = 2 (errored)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stfld, _writableStreamStateField);

        // _storedError = reason
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _writableStreamStoredErrorField);

        var noCallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamAbortCallbackField);
        il.Emit(OpCodes.Brfalse, noCallbackLabel);

        // Stack: [receiver=null, callback, args=[reason]]
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamAbortCallbackField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        EmitWrapResultAsTask(il, runtime);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, typeof(object)));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitWritableStreamLockedGetter(TypeBuilder t)
    {
        // bool get_Locked() — emitted as a property so JS-side
        // ws.locked dispatches via reflection to PascalCase Locked.
        var prop = t.DefineProperty("Locked", PropertyAttributes.None, _types.Boolean, Type.EmptyTypes);
        var getter = t.DefineMethod(
            "get_Locked",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        // return _writer != null
        var nullLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamWriterField);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
        return getter;
    }

    private MethodBuilder EmitWritableStreamGetWriter(TypeBuilder streamBuilder, ConstructorBuilder writerCtor)
    {
        var method = streamBuilder.DefineMethod(
            "GetWriter",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);

        var il = method.GetILGenerator();

        // if (_writer != null) throw new Exception("TypeError: WritableStream already locked");
        var notLockedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamWriterField);
        il.Emit(OpCodes.Brfalse, notLockedLabel);
        il.Emit(OpCodes.Ldstr, "TypeError: WritableStream is already locked to a writer");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notLockedLabel);
        // _writer = new $WritableStreamDefaultWriter(this)
        il.Emit(OpCodes.Ldarg_0);  // for stfld receiver
        il.Emit(OpCodes.Ldarg_0);  // for newobj's stream argument
        il.Emit(OpCodes.Newobj, writerCtor);
        il.Emit(OpCodes.Stfld, _writableStreamWriterField);

        // Return _writer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableStreamWriterField);
        il.Emit(OpCodes.Ret);

        return method;
    }

    // --- Controller class ---

    private ConstructorBuilder EmitWritableStreamControllerCtor(TypeBuilder controllerBuilder, TypeBuilder streamBuilder)
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
        il.Emit(OpCodes.Stfld, _writableControllerStreamField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private void EmitWritableStreamControllerErrorMethod(TypeBuilder controllerBuilder, TypeBuilder streamBuilder, EmittedRuntime runtime)
    {
        var method = controllerBuilder.DefineMethod(
            "Error",
            MethodAttributes.Public,
            _types.Void,
            [_types.Object]);

        var il = method.GetILGenerator();
        // _stream._state = 2; _stream._storedError = reason
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableControllerStreamField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stfld, _writableStreamStateField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableControllerStreamField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _writableStreamStoredErrorField);

        il.Emit(OpCodes.Ret);
    }

    // --- Writer class ---

    private ConstructorBuilder EmitWritableStreamWriterCtor(TypeBuilder writerBuilder, TypeBuilder streamBuilder)
    {
        // Constructor was already defined inside EmitWritableStreamGetWriter
        // (forward reference). We can't double-define. Find the existing one.
        // Workaround: define the writer ctor here and have GetWriter look it up
        // via the builder. Actually the cleanest fix is to define the writer
        // ctor BEFORE GetWriter is emitted. Reorder:
        // (See restructuring in EmitWritableStreamClasses.)

        // For now, this method is a no-op because the ctor body has already
        // been pre-defined. Define it here and hope for the best.
        var ctor = writerBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [streamBuilder]);

        var il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _writableWriterStreamField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    /// <summary>
    /// Emits a writer-side method that delegates to the corresponding stream
    /// method by simply loading <c>this._stream</c> and forwarding the args.
    /// </summary>
    private void EmitWritableStreamWriterDelegatingMethod(
        TypeBuilder writerBuilder,
        TypeBuilder streamBuilder,
        string name,
        MethodInfo streamMethod,
        Type[] paramTypes)
    {
        var method = writerBuilder.DefineMethod(
            name,
            MethodAttributes.Public,
            streamMethod.ReturnType,
            paramTypes);

        var il = method.GetILGenerator();
        // Load _stream as receiver
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableWriterStreamField);
        // Forward args
        for (int i = 0; i < paramTypes.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
        }
        il.Emit(OpCodes.Callvirt, streamMethod);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWritableStreamWriterReleaseLock(TypeBuilder writerBuilder, TypeBuilder streamBuilder)
    {
        var method = writerBuilder.DefineMethod(
            "ReleaseLock",
            MethodAttributes.Public,
            _types.Void,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        // _stream._writer = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableWriterStreamField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _writableStreamWriterField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitWritableStreamWriterDesiredSizeGetter(TypeBuilder writerBuilder, TypeBuilder streamBuilder)
    {
        var prop = writerBuilder.DefineProperty(
            "DesiredSize",
            PropertyAttributes.None,
            _types.Double,
            Type.EmptyTypes);

        var getter = writerBuilder.DefineMethod(
            "get_DesiredSize",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Double,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        // For v1: return _stream._highWaterMark (no queue tracking)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _writableWriterStreamField);
        il.Emit(OpCodes.Ldfld, _writableStreamHwmField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitWritableStreamWriterClosedReadyGetter(TypeBuilder writerBuilder, TypeBuilder streamBuilder, string name)
    {
        // Returns Task.FromResult<object>(undefined) — pre-resolved promise.
        // v1 simplification: no real Promise tracking; the typical
        // `await writer.ready` / `await writer.closed` patterns work because
        // pre-resolved tasks await trivially.
        var prop = writerBuilder.DefineProperty(name, PropertyAttributes.None, _types.TaskOfObject, Type.EmptyTypes);

        var getter = writerBuilder.DefineMethod(
            "get_" + name,
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
