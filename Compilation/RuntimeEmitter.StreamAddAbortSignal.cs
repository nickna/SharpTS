using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the real <c>stream.addAbortSignal(signal, stream)</c> for standalone output (#1027):
/// destroys the stream with an <c>AbortError</c> when the signal fires (or immediately if the
/// signal is already aborted). Uses the #985 compiled-AbortSignal path (the signal's
/// <c>_listeners</c> slot via <c>AbortSignalAddEventListener</c>), so output stays standalone.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the <c>$StreamAbortCallback</c> closure class (field: the stream; method
    /// <c>OnAbort()</c> destroys it with an AbortError). Wrapped in a $TSFunction and added as
    /// the signal's abort listener. Emitted in the stream block (needs $Readable/$Writable
    /// Destroy + $Error, all available by then).
    /// </summary>
    private void EmitStreamAbortCallbackClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$StreamAbortCallback",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit,
            _types.Object);
        runtime.StreamAbortCallbackType = typeBuilder;

        var streamField = typeBuilder.DefineField("_stream", _types.Object, FieldAttributes.Private);

        // ctor(object stream)
        var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [_types.Object]);
        runtime.StreamAbortCallbackCtor = ctor;
        var cil = ctor.GetILGenerator();
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        cil.Emit(OpCodes.Ldarg_0);
        cil.Emit(OpCodes.Ldarg_1);
        cil.Emit(OpCodes.Stfld, streamField);
        cil.Emit(OpCodes.Ret);

        // object OnAbort() — destroys _stream with an AbortError, returns null.
        var onAbort = typeBuilder.DefineMethod("OnAbort", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        runtime.StreamAbortCallbackOnAbort = onAbort;
        var il = onAbort.GetILGenerator();
        EmitDestroyStreamWithAbortError(il, runtime, gen =>
        {
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldfld, streamField);
        });
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits <c>$Runtime.StreamAddAbortSignal(object signal, object stream)</c>. Must run after
    /// EmitAbortControllerMethods (AbortSignalGetAborted / AbortSignalAddEventListener) and after
    /// the $StreamAbortCallback class. Gated on UsesNodeStreams &amp;&amp; UsesAbortController.
    /// </summary>
    private void EmitStreamAddAbortSignalMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StreamAddAbortSignal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.StreamAddAbortSignal = method;

        var il = method.GetILGenerator();
        var retStream = il.DefineLabel();

        // if (signal == null || stream == null) return stream;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, retStream);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, retStream);

        // Only handle a real signal (Dictionary-backed). Otherwise no-op.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, retStream);

        // if (AbortSignalGetAborted(signal)) { destroy now; } else { register listener; }
        var registerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
        il.Emit(OpCodes.Brfalse, registerLabel);

        // already aborted → destroy the stream now
        EmitDestroyStreamWithAbortError(il, runtime, gen => gen.Emit(OpCodes.Ldarg_1));
        il.Emit(OpCodes.Br, retStream);

        // register: AbortSignalAddEventListener(signal, "abort", new $TSFunction(new $StreamAbortCallback(stream), OnAbort));
        il.MarkLabel(registerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "abort");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.StreamAbortCallbackCtor);
        EmitInstanceMethodInfoLiteral(il, runtime.StreamAbortCallbackOnAbort, runtime.StreamAbortCallbackType);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Call, runtime.AbortSignalAddEventListener);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(retStream);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: var err = new $Error("The operation was aborted"); err.Name = "AbortError";
    /// var s = &lt;loadStream&gt;; if (s is $Readable) ((Readable)s).Destroy(err); else if (s is $Writable) ((Writable)s).Destroy(err);
    /// Leaves the stack unchanged.
    /// </summary>
    private void EmitDestroyStreamWithAbortError(ILGenerator il, EmittedRuntime runtime, Action<ILGenerator> loadStream)
    {
        var errLocal = il.DeclareLocal(_types.Object);
        var sLocal = il.DeclareLocal(_types.Object);

        // err = new $Error("The operation was aborted"); err.Name = "AbortError";
        il.Emit(OpCodes.Ldstr, "The operation was aborted");
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "AbortError");
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameSetter);
        il.Emit(OpCodes.Stloc, errLocal);

        loadStream(il);
        il.Emit(OpCodes.Stloc, sLocal);

        var notReadable = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, runtime.TSReadableType);
        il.Emit(OpCodes.Brfalse, notReadable);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, runtime.TSReadableType);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSReadableDestroy);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notReadable);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Isinst, runtime.TSWritableType);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Castclass, runtime.TSWritableType);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSWritableDestroy);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(done);
    }
}
