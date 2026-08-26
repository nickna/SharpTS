using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>Compiled host-value adapters for stream/consumers.</summary>
public sealed class StreamConsumersPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:stream/consumers";

    public IReadOnlyList<string> GetExportedMembers() => ["drainQueuedWebStream", "bufferToArrayBuffer"];

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
            context.IL.Emit(OpCodes.Castclass, context.Runtime!.ReadableStreamType);
            context.IL.Emit(OpCodes.Callvirt, context.Runtime.ReadableStreamDrainQueuedChunks);
            return true;
        }

        var il = context.IL;
        var runtime = context.Runtime!;
        var bytes = il.DeclareLocal(typeof(byte[]));
        var result = il.DeclareLocal(runtime.ArrayBufferType);

        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Callvirt, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, bytes);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.ArrayBufferCtor);
        il.Emit(OpCodes.Stloc, result);

        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferGetBuffer);
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
