using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'stream/promises' module.
/// Promise-based versions of pipeline and finished.
/// </summary>
public sealed class StreamPromisesModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "stream/promises";

    private static readonly string[] _exportedMembers =
    [
        "pipeline",
        "finished"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        switch (methodName)
        {
            case "pipeline":
                EmitPromisePipelineCall(emitter, arguments);
                return true;
            case "finished":
                EmitPromiseFinishedCall(emitter, arguments);
                return true;
            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // Return false so that EmitBuiltInModuleMethodWrapper creates proper TSFunction wrappers.
        return false;
    }

    private static void EmitPromisePipelineCall(IEmitterContext emitter, List<Expr> arguments)
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

        il.Emit(OpCodes.Call, ctx.Runtime!.StreamPromisePipeline);
    }

    private static void EmitPromiseFinishedCall(IEmitterContext emitter, List<Expr> arguments)
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

        il.Emit(OpCodes.Call, ctx.Runtime!.StreamPromiseFinished);
    }
}
