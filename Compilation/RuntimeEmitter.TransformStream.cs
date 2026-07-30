using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the standalone <c>$TransformStream</c> + <c>$TransformStreamDefaultController</c>
/// classes for the WHATWG Web Streams API.
/// </summary>
/// <remarks>
/// A TransformStream owns a paired <c>$ReadableStream</c> + <c>$WritableStream</c>:
/// chunks written to the writable side run through the user's <c>transform</c>
/// callback (which calls <c>controller.enqueue</c>) and emerge on the readable
/// side. <c>flush</c> runs on close, then closes the readable.
///
/// V1 implementation: synchronous transform pipeline. The writable's underlying
/// sink is built as a small <c>Dictionary&lt;string, object?&gt;</c> with
/// <c>BuiltInMethod</c>-shaped <c>write</c>/<c>close</c>/<c>abort</c> entries
/// constructed in IL using the same <c>$Runtime.InvokeMethodValue</c> dispatch
/// path that the rest of the pure-IL streams use. The transform's controller
/// is the readable's controller (re-used directly), so user
/// <c>transform(chunk, controller)</c> calls <c>controller.enqueue</c> push
/// chunks straight to the readable side.
/// </remarks>
public partial class RuntimeEmitter
{
    private FieldBuilder _transformStreamReadableField = null!;
    private FieldBuilder _transformStreamWritableField = null!;

    // $TransformSinkHolder fields — instance fields holding the user transformer
    // and the readable side. Its Write/Close/Abort methods translate
    // writable-sink operations into transformer.transform/flush calls and
    // readable.Enqueue/CloseStream/ErrorStream side effects.
    private FieldBuilder _transformHolderTransformerField = null!;
    private FieldBuilder _transformHolderReadableField = null!;
    private ConstructorBuilder _transformHolderCtor = null!;
    private MethodBuilder _transformHolderWriteMethod = null!;
    private MethodBuilder _transformHolderCloseMethod = null!;
    private MethodBuilder _transformHolderAbortMethod = null!;

    private void EmitTransformStreamClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // First emit the sink holder class — used by $TransformStream's
        // constructor to wrap (transformer, readable) into something that
        // looks like a writable's underlying sink.
        EmitTransformSinkHolderClass(moduleBuilder, runtime);

        var streamBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TransformStream",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        runtime.TransformStreamType = streamBuilder;

        _transformStreamReadableField = streamBuilder.DefineField(
            "_readable", runtime.ReadableStreamType, FieldAttributes.Private);
        _transformStreamWritableField = streamBuilder.DefineField(
            "_writable", runtime.WritableStreamType, FieldAttributes.Private);

        var ctor = EmitTransformStreamConstructor(streamBuilder, runtime);
        runtime.TransformStreamCtor = ctor;

        EmitTransformStreamReadableProperty(streamBuilder);
        EmitTransformStreamWritableProperty(streamBuilder);

        streamBuilder.CreateType();
    }

    private void EmitTransformSinkHolderClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var holder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TransformSinkHolder",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object);

        _transformHolderTransformerField = holder.DefineField(
            "_transformer", _types.Object, FieldAttributes.Private);
        _transformHolderReadableField = holder.DefineField(
            "_readable", runtime.ReadableStreamType, FieldAttributes.Private);

        // Constructor: $TransformSinkHolder(object transformer, $ReadableStream readable)
        var ctor = holder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, runtime.ReadableStreamType]);
        _transformHolderCtor = ctor;
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, _transformHolderReadableField);
            il.Emit(OpCodes.Ret);
        }

        // public Write(object chunk, object controllerArg) — sink callback
        // signature is (chunk, writableController) per the WHATWG spec, but
        // we ignore the writableController and pass our readable as the
        // transform's controller (matching the JS-side spec where the
        // transform's controller is the one tied to the readable side).
        _transformHolderWriteMethod = holder.DefineMethod(
            "Write",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.Object]);
        {
            var il = _transformHolderWriteMethod.GetILGenerator();
            // If _transformer is null, just enqueue the chunk pass-through.
            var hasTransformerLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Brtrue, hasTransformerLabel);
            // Pass-through: _readable.Enqueue(chunk); return null
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.ReadableStreamEnqueue);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(hasTransformerLabel);
            // Look up _transformer.transform via GetFieldsProperty
            var transformFnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Ldstr, "transform");
            il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
            il.Emit(OpCodes.Stloc, transformFnLocal);

            // If transform is null/$Undefined, fall back to pass-through.
            var hasTransformLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, transformFnLocal);
            il.Emit(OpCodes.Brfalse, hasTransformLabel);
            il.Emit(OpCodes.Ldloc, transformFnLocal);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Beq, hasTransformLabel);

            // Found transform — call _transformer.transform(chunk, _readable)
            // via $Runtime.InvokeMethodValue. Use _readable as the controller arg
            // since _readable already has Enqueue/CloseStream/ErrorStream methods
            // that the user transform body can call as controller.enqueue/etc.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Ldloc, transformFnLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);            // chunk
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);  // controller = _readable
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Ret);

            // Pass-through fallback
            il.MarkLabel(hasTransformLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.ReadableStreamEnqueue);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // public Close() — sink close callback. Runs flush(controller) if
        // present, then closes the readable.
        _transformHolderCloseMethod = holder.DefineMethod(
            "Close",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        {
            var il = _transformHolderCloseMethod.GetILGenerator();
            // If transformer present, call transformer.flush(_readable) if defined
            var noTransformerLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Brfalse, noTransformerLabel);

            var flushLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Ldstr, "flush");
            il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
            il.Emit(OpCodes.Stloc, flushLocal);

            var noFlushLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, flushLocal);
            il.Emit(OpCodes.Brfalse, noFlushLabel);
            il.Emit(OpCodes.Ldloc, flushLocal);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Beq, noFlushLabel);

            // _transformer.flush(_readable) via InvokeMethodValue
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderTransformerField);
            il.Emit(OpCodes.Ldloc, flushLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Pop);  // discard

            il.MarkLabel(noFlushLabel);
            il.MarkLabel(noTransformerLabel);

            // _readable.CloseStream()
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);
            il.Emit(OpCodes.Callvirt, runtime.ReadableStreamCloseStream);

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // public Abort(object reason)
        _transformHolderAbortMethod = holder.DefineMethod(
            "Abort",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object]);
        {
            var il = _transformHolderAbortMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _transformHolderReadableField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.ReadableStreamErrorStream);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // Static factory method on the holder. Returns a $TransformSinkHolder
        // instance directly — the $WritableStream constructor's
        // GetFieldsProperty-based extraction finds Write/Close/Abort on it
        // via reflection.
        var buildSink = holder.DefineMethod(
            "Build",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, runtime.ReadableStreamType]);
        {
            var il = buildSink.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, _transformHolderCtor);
            il.Emit(OpCodes.Ret);
        }
        runtime.BuildTransformSink = buildSink;

        holder.CreateType();
    }

    private ConstructorBuilder EmitTransformStreamConstructor(TypeBuilder t, EmittedRuntime runtime)
    {
        // public $TransformStream(object? transformer, object? writableStrategy, object? readableStrategy)
        var ctor = t.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object, _types.Object]);

        var il = ctor.GetILGenerator();

        // base()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));

        // _readable = new $ReadableStream(null, readableStrategy)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Newobj, runtime.ReadableStreamCtor);
        il.Emit(OpCodes.Stfld, _transformStreamReadableField);

        // Build the writable's underlying sink: a Dictionary<string, object?>
        // with closures over (this, transformer) for write/close/abort.
        // Since we need closures and can't easily emit them in IL, we cheat:
        // construct a SinkBuilder helper at runtime that holds the references.
        // V1 simpler approach: make the writable directly use the same
        // underlying source dict as the transformer (which may not have
        // write/close/abort but does have transform/flush). Bridge those.
        //
        // Cleanest pure-IL path: build a Dictionary<string, object?> with
        // Func<object?[], object?> entries that are static methods on
        // $TransformStream taking (this, transformer, args[]) explicitly.
        // The Func captures `this` via a closure object.
        //
        // For minimum-viable v1: build the sink as a dict containing direct
        // references to static helper methods on $RuntimeTypes that accept
        // (transformStream, args) explicitly. Defer per-instance closures
        // by passing the transformStream as an extra arg embedded in the dict.

        // For now, the simplest viable approach: build a sink that's a
        // Dictionary<string, object?> with a "transformer" field plus a
        // "transformStream" back-reference, then have $Runtime have a
        // "TransformWriteAdapter" delegate that pulls these out and runs the
        // transform.
        //
        // Even simpler: emit the sink as a Dictionary holding (transformer)
        // and build the writable using an ad-hoc closure delegate. The
        // delegate holds a strong reference to `this`.

        // Pragmatic approach: build sinkDict = new Dictionary<string, object?>
        // { ["__transformer"] = transformer, ["__transformStream"] = this },
        // then call $Runtime.WrapTransformSink(sinkDict) which converts the
        // sink fields into proper write/close/abort callables. For v1, we
        // skip this whole song-and-dance and use the simplest path:

        // Path: build sinkDict = transformer (the user's transformer object).
        // The user's transformer has transform/flush methods, but the writable
        // expects write/close/abort. We need a TRANSLATION layer.
        //
        // I'll emit a runtime helper $Runtime.BuildTransformSink(transformer,
        // readable) that wraps the user transformer's methods into a
        // write/close/abort dictionary. This helper is emitted in pure IL too,
        // but it's static and can be called once per TransformStream
        // construction.
        //
        // For now, simplest: just pass a stub null sink to the writable, and
        // the writes will be no-ops. THE TESTS NEED THIS TO ACTUALLY WORK so
        // let me just emit the runtime helper.

        // Build a TransformSink dict using $Runtime.BuildTransformSink helper.
        var sinkLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);                         // transformer
        il.Emit(OpCodes.Ldarg_0);                         // this (for accessing _readable)
        il.Emit(OpCodes.Ldfld, _transformStreamReadableField);
        il.Emit(OpCodes.Call, runtime.BuildTransformSink);
        il.Emit(OpCodes.Stloc, sinkLocal);

        // _writable = new $WritableStream(sink, writableStrategy)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, sinkLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, runtime.WritableStreamCtor);
        il.Emit(OpCodes.Stfld, _transformStreamWritableField);

        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private void EmitTransformStreamReadableProperty(TypeBuilder t)
    {
        var prop = t.DefineProperty("Readable", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = t.DefineMethod(
            "get_Readable",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _transformStreamReadableField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }

    private void EmitTransformStreamWritableProperty(TypeBuilder t)
    {
        var prop = t.DefineProperty("Writable", PropertyAttributes.None, _types.Object, Type.EmptyTypes);
        var getter = t.DefineMethod(
            "get_Writable",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Object,
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _transformStreamWritableField);
        il.Emit(OpCodes.Ret);
        prop.SetGetMethod(getter);
    }
}
