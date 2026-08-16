using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the Readable stream constructor exported from the 'stream' module.
/// Supports instantiation via <c>new Readable(options?)</c>.
/// </summary>
public sealed class SharpTSReadableConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the Readable constructor.
    /// </summary>
    public static readonly SharpTSReadableConstructor Instance = new();

    private SharpTSReadableConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// Readable constructor takes 0 required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSReadable"/> instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        var stream = new SharpTSReadable();

        // Process options if provided
        if (arguments.Count > 0 && arguments[0] is SharpTSObject options)
        {
            // read callback: called when data is requested
            if (options.GetProperty("read") is ISharpTSCallable readCallback)
            {
                // Store for subclass implementations
                // Note: In the simple sync model, we don't use this callback
            }

            // encoding option
            if (options.GetProperty("encoding") is string encoding)
            {
                var setEncoding = stream.GetMember("setEncoding") as Runtime.BuiltIns.BuiltInMethod;
                setEncoding?.Bind(stream).Call(interpreter, [encoding]);
            }

            // objectMode option
            if (options.GetProperty("objectMode") is true)
            {
                stream.ObjectMode = true;
            }

            // highWaterMark option
            if (options.GetProperty("highWaterMark") is double hwm)
            {
                stream.HighWaterMark = (int)hwm;
            }
        }

        return stream;
    }

    /// <summary>
    /// Gets a property from the Readable constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "from" => BuiltInMethod.CreateV2("from", 1, 2, ReadableFrom),
            "isReadable" => BuiltInMethod.CreateV2("isReadable", 1, IsReadable),
            "toWeb" => BuiltInMethod.CreateV2("toWeb", 1, ToWeb),
            "fromWeb" => BuiltInMethod.CreateV2("fromWeb", 1, FromWeb),
            _ => null
        };
    }

    /// <summary>
    /// Readable.toWeb(readable) — converts a Node Readable to a WHATWG ReadableStream by draining
    /// its currently-buffered chunks (documented subset, #1029). Mirrors
    /// <c>ReadableStream.from(readable.toArray())</c>.
    /// </summary>
    private static RuntimeValue ToWeb(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var ws = new SharpTSReadableStream(interpreter, underlyingSource: null, strategy: null);
        if (args.Length > 0 && args[0].ToObject() is SharpTSReadable r)
        {
            foreach (var chunk in r.DrainBufferToList())
                ws.EnqueueInternal(chunk);
        }
        ws.CloseInternal();
        return RuntimeValue.FromObject(ws);
    }

    /// <summary>
    /// Readable.fromWeb(readableStream) — converts a WHATWG ReadableStream to a Node Readable by
    /// draining its currently-queued chunks (documented subset, #1029).
    /// </summary>
    private static RuntimeValue FromWeb(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var stream = new SharpTSReadable { ObjectMode = true };
        if (args.Length > 0 && args[0].ToObject() is SharpTSReadableStream ws)
        {
            while (ws.Queue.Count > 0)
                stream.PushFromHost(interpreter, ws.Queue.Dequeue().Value);
        }
        stream.PushFromHost(interpreter, null); // EOF
        return RuntimeValue.FromObject(stream);
    }

    /// <summary>
    /// Readable.from(iterable, options?) — creates a Readable from an iterable in object mode.
    /// </summary>
    private static RuntimeValue ReadableFrom(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var iterable = args.Length > 0 ? args[0].ToObject() : null;
        var stream = new SharpTSReadable();
        stream.ObjectMode = true;

        // Extract options
        if (args.Length > 1 && args[1].ToObject() is SharpTSObject options)
        {
            if (options.GetProperty("objectMode") is false)
                stream.ObjectMode = false;
        }

        // Push items from iterable
        if (iterable is SharpTSArray arr)
        {
            foreach (var item in arr)
            {
                var pushMethod = stream.GetMember("push") as BuiltInMethod;
                pushMethod?.Bind(stream).CallV2(interpreter, [RuntimeValue.FromBoxed(item)]);
            }
        }
        else if (iterable is List<object?> list)
        {
            foreach (var item in list)
            {
                var pushMethod = stream.GetMember("push") as BuiltInMethod;
                pushMethod?.Bind(stream).CallV2(interpreter, [RuntimeValue.FromBoxed(item)]);
            }
        }

        // Push null to signal EOF
        var pushEnd = stream.GetMember("push") as BuiltInMethod;
        pushEnd?.Bind(stream).CallV2(interpreter, [RuntimeValue.Null]);

        return RuntimeValue.FromObject(stream);
    }

    /// <summary>
    /// Readable.isReadable(stream) — checks if stream is a readable stream.
    /// </summary>
    private static RuntimeValue IsReadable(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args.Length > 0 ? args[0].ToObject() : null;
        return RuntimeValue.FromBoolean(obj is SharpTSReadable);
    }

    /// <summary>
    /// Sets a property on the Readable constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        return false;
    }

    public override string ToString() => "[Function: Readable]";
}
