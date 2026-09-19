using System.Collections.Immutable;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'stream' module.
/// Exports Readable, Writable, Duplex, Transform, PassThrough stream constructors,
/// plus utility functions finished(), pipeline(), and addAbortSignal().
/// </summary>
public sealed class StreamModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "stream";

    private static readonly IReadOnlyList<string> _exportedMembers = (ImmutableArray<string>)
    [
        "Readable",
        "Writable",
        "Duplex",
        "Transform",
        "PassThrough",
        "finished",
        "pipeline",
        "addAbortSignal",
        "compose",
        "isErrored",
        "getDefaultHighWaterMark",
        "setDefaultHighWaterMark",
        "promises"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "finished":
                EmitFinishedCall(emitter, arguments);
                return true;
            case "pipeline":
                EmitPipelineCall(emitter, arguments);
                return true;
            case "addAbortSignal":
                EmitAddAbortSignalCall(emitter, arguments);
                return true;
            case "compose":
                EmitComposeCall(emitter, arguments);
                return true;
            case "isErrored":
                EmitIsErroredCall(emitter, arguments);
                return true;
            case "getDefaultHighWaterMark":
                EmitDefaultHwmCall(emitter, arguments, emitter.Context.Runtime!.RequireNodeStreams().GetDefaultHighWaterMark, getter: true);
                return true;
            case "setDefaultHighWaterMark":
                EmitDefaultHwmCall(emitter, arguments, emitter.Context.Runtime!.RequireNodeStreams().SetDefaultHighWaterMark, getter: false);
                return true;
            case "Readable.from":
                EmitReadableFromCall(emitter, arguments);
                return true;
            case "Duplex.from":
                EmitDuplexFromCall(emitter, arguments);
                return true;
            case "Readable.toWeb":
            case "Readable.fromWeb":
                // #1029: Node↔Web stream conversions are an interpreter-only documented subset —
                // the compiled path is deferred pending stream/web async-iterator + BYOB support
                // (DEFERRED, see STATUS.md). Throw a clear error rather than fail silently.
                EmitNotSupportedInCompiled(emitter, methodName);
                return true;
            case "Readable.isReadable":
                EmitIsReadableCall(emitter, arguments);
                return true;
            case "Writable.isWritable":
                EmitIsWritableCall(emitter, arguments);
                return true;
            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName == "promises")
        {
            // Build a sub-object with pipeline and finished TSFunction wrappers
            EmitPromisesSubObject(emitter);
            return true;
        }

        // Constructor names and function names return false so that
        // EmitBuiltInModuleMethodWrapper creates proper TSFunction wrappers.
        // This makes typeof Readable === "function" work correctly.
        return false;
    }

    private static void EmitPromisesSubObject(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Create Dictionary<string, object?> with pipeline and finished
        var dictType = ctx.Types.DictionaryStringObject;
        var dictCtor = ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = ctx.Types.GetMethod(dictType, "Add", ctx.Types.String, ctx.Types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);

        // Add "pipeline" -> TSFunction wrapping PromisePipeline
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "pipeline");
        il.Emit(OpCodes.Ldnull); // target
        ctx.Types.EmitLoadMethodInfoViaHandle(il, ctx.Runtime!.RequireNodeStreams().PromisePipeline);
        il.Emit(OpCodes.Newobj, ctx.Runtime!.FunctionConstruction.Constructor);
        il.Emit(OpCodes.Call, addMethod);

        // Add "finished" -> TSFunction wrapping PromiseFinished
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "finished");
        il.Emit(OpCodes.Ldnull);
        ctx.Types.EmitLoadMethodInfoViaHandle(il, ctx.Runtime!.RequireNodeStreams().PromiseFinished);
        il.Emit(OpCodes.Newobj, ctx.Runtime!.FunctionConstruction.Constructor);
        il.Emit(OpCodes.Call, addMethod);

        // Wrap in SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.ObjectConstruction.Create);
    }

    private static void EmitFinishedCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Pack args into object[]
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireNodeStreams().Finished);
    }

    private static void EmitPipelineCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireNodeStreams().Pipeline);
    }

    private static void EmitAddAbortSignalCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Real wiring (#1027): $Runtime.StreamAddAbortSignal(signal, stream) destroys the stream
        // with an AbortError when the signal fires, and returns the stream. The helper is only
        // emitted when AbortController is in use; otherwise fall back to returning the stream.
        if (arguments.Count >= 2 && ctx.Runtime!.RequireNodeStreams().HasAbortSignal)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
            il.Emit(OpCodes.Call, ctx.Runtime!.RequireNodeStreams().AddAbortSignal);
        }
        else if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Emits: Readable.isReadable(stream) → bool
    /// Returns true if stream is an instance of $Readable (or any subclass like $Duplex, $Transform).
    /// </summary>
    private static void EmitIsReadableCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
            emitter.EmitExpression(arguments[0]);
        else
            il.Emit(OpCodes.Ldnull);

        il.Emit(OpCodes.Isinst, ctx.Runtime!.RequireNodeStreams().ReadableType);
        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits: Writable.isWritable(stream) → bool
    /// Returns true if stream is a $Writable or $Duplex (which extends $Readable but has write capability).
    /// </summary>
    private static void EmitIsWritableCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
            emitter.EmitExpression(arguments[0]);
        else
            il.Emit(OpCodes.Ldnull);

        // Check: obj is $Writable || obj is $Duplex
        var objLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, objLocal);

        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.RequireNodeStreams().WritableType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.RequireNodeStreams().DuplexType);
        il.Emit(OpCodes.Brtrue, trueLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));

        il.MarkLabel(endLabel);
    }

    private static void EmitReadableFromCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Pack args into object[]
        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.RequireNodeStreams().ReadableFrom);
    }

    /// <summary>
    /// Emits a runtime throw for a stream API that is interpreter-only in compiled output (#1029).
    /// </summary>
    private static void EmitNotSupportedInCompiled(IEmitterContext emitter, string methodName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldstr, $"stream.{methodName} is not yet supported in compiled output (#1029); run in the interpreter.");
        il.Emit(OpCodes.Newobj, ctx.Types.InvalidOperationExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: isErrored(stream) → bool. Reads $Readable.Errored / $Writable.Errored, else false.
    /// </summary>
    private static void EmitIsErroredCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        var objLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, objLocal);

        var notReadable = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.RequireNodeStreams().ReadableType);
        il.Emit(OpCodes.Brfalse, notReadable);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime!.RequireNodeStreams().ReadableType);
        il.Emit(OpCodes.Callvirt, ctx.Runtime!.RequireNodeStreams().ReadableErroredGetter);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notReadable);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.RequireNodeStreams().WritableType);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime!.RequireNodeStreams().WritableType);
        il.Emit(OpCodes.Callvirt, ctx.Runtime!.RequireNodeStreams().WritableErroredGetter);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));

        il.MarkLabel(endLabel);
    }

    /// <summary>Emits get/setDefaultHighWaterMark by forwarding (boxed) args to the runtime helper.</summary>
    private static void EmitDefaultHwmCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.MethodInfo target, bool getter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // arg0 = objectMode (default false/null)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (!getter)
        {
            // arg1 = value
            if (arguments.Count > 1)
            {
                emitter.EmitExpression(arguments[1]);
                emitter.EmitBoxIfNeeded(arguments[1]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        il.Emit(OpCodes.Call, target);
    }

    private static void EmitDuplexFromCall(IEmitterContext emitter, List<Expr> arguments)
    {
        EmitPackedArgsCall(emitter, arguments, emitter.Context.Runtime!.RequireNodeStreams().DuplexFrom);
    }

    private static void EmitComposeCall(IEmitterContext emitter, List<Expr> arguments)
    {
        EmitPackedArgsCall(emitter, arguments, emitter.Context.Runtime!.RequireNodeStreams().Compose);
    }

    /// <summary>Packs the arguments into an object[] and calls the given runtime helper.</summary>
    private static void EmitPackedArgsCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.MethodInfo target)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, target);
    }
}
