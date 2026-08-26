using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>Interpreter host-value adapters used by the TypeScript consumers facade.</summary>
public static class StreamConsumersPrimitiveInterpreter
{
    public static Dictionary<string, object?> GetExports() => new()
    {
        ["drainQueuedWebStream"] = BuiltInMethod.CreateV2(
            "drainQueuedWebStream", 1, DrainQueuedWebStream),
        ["bufferToArrayBuffer"] = BuiltInMethod.CreateV2(
            "bufferToArrayBuffer", 1, BufferToArrayBuffer)
    };

    private static RuntimeValue DrainQueuedWebStream(
        Execution.Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not SharpTSReadableStream stream)
            throw new Exception("The value must be a ReadableStream");

        var chunks = stream.Queue.Select(chunk => chunk.Value).ToList();
        stream.Queue.Clear();
        stream.QueueTotalSize = 0;
        stream.Disturbed = true;
        return RuntimeValue.FromObject(new SharpTSArray(chunks));
    }

    private static RuntimeValue BufferToArrayBuffer(
        Execution.Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || args[0].ToObject() is not SharpTSBuffer buffer)
            throw new Exception("The value must be a Buffer");

        var result = new SharpTSArrayBuffer(buffer.Length);
        buffer.Data.CopyTo(result.AsSpan());
        return RuntimeValue.FromObject(result);
    }
}
