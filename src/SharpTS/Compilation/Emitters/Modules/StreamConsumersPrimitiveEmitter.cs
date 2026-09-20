using System.Collections.Immutable;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>Compiled host-value adapters for stream/consumers.</summary>
public sealed class StreamConsumersPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:stream/consumers";

    private static readonly IReadOnlyList<string> _exportedMembers = (ImmutableArray<string>)["drainQueuedWebStream", "bufferToArrayBuffer"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName is not "drainQueuedWebStream" and not "bufferToArrayBuffer") return false;
        if (arguments.Count == 0)
        {
            emitter.Context.IL.Emit(OpCodes.Ldnull);
        }
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        var context = emitter.Context;
        if (methodName == "drainQueuedWebStream")
        {
            context.IL.Emit(OpCodes.Castclass, context.Runtime!.RequireWebStreams().ReadableType);
            context.IL.Emit(OpCodes.Callvirt, context.Runtime.RequireWebStreams().ReadableDrainQueuedChunks);
            return true;
        }

        var il = context.IL;
        var runtime = context.Runtime!;
        var bytes = il.DeclareLocal(typeof(byte[]));
        var result = il.DeclareLocal(runtime.RequireArrayBuffer().Type);

        il.Emit(OpCodes.Castclass, runtime.RequireBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireBuffer().GetData);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.RequireArrayBuffer().Ctor);
        il.Emit(OpCodes.Stloc, result);

        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, runtime.RequireArrayBuffer().GetBuffer);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod(
            "Copy", [typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ldloc, result);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;
}
