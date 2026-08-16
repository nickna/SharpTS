using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the Duplex stream constructor exported from the 'stream' module.
/// Supports instantiation via <c>new Duplex(options?)</c>.
/// </summary>
public sealed class SharpTSDuplexConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the Duplex constructor.
    /// </summary>
    public static readonly SharpTSDuplexConstructor Instance = new();

    private SharpTSDuplexConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// Duplex constructor takes 0 required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSDuplex"/> instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        var stream = new SharpTSDuplex();

        // Process options if provided
        if (arguments.Count > 0 && arguments[0] is SharpTSObject options)
        {
            // read callback: called when data is requested
            if (options.GetProperty("read") is ISharpTSCallable readCallback)
            {
                stream.SetReadCallback(readCallback);
            }

            // write callback: called when data is written
            if (options.GetProperty("write") is ISharpTSCallable writeCallback)
            {
                stream.SetWriteCallback(writeCallback);
            }

            // final callback: called when end() is called
            if (options.GetProperty("final") is ISharpTSCallable finalCallback)
            {
                stream.SetFinalCallback(finalCallback);
            }

            // encoding option
            if (options.GetProperty("encoding") is string encoding)
            {
                var setEncoding = stream.GetMember("setEncoding") as Runtime.BuiltIns.BuiltInMethod;
                setEncoding?.Bind(stream).Call(interpreter, [encoding]);
            }

            // objectMode option - sets both readable and writable sides
            if (options.GetProperty("objectMode") is true)
            {
                stream.ObjectMode = true;
                stream.WritableObjectMode = true;
            }

            // highWaterMark option - sets both readable and writable sides
            if (options.GetProperty("highWaterMark") is double hwm)
            {
                stream.HighWaterMark = (int)hwm;
                stream.WritableHighWaterMark = (int)hwm;
            }
        }

        return stream;
    }

    /// <summary>
    /// Gets a property from the Duplex constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "from" => BuiltInMethod.CreateV2("from", 1, DuplexFrom),
            _ => null
        };
    }

    /// <summary>
    /// Duplex.from(source) — builds a Duplex whose readable side yields the items of an
    /// iterable/array source (mirrors Readable.from); other source kinds push nothing (#1028).
    /// </summary>
    private static RuntimeValue DuplexFrom(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var source = args.Length > 0 ? args[0].ToObject() : null;
        var stream = new SharpTSDuplex { ObjectMode = true, WritableObjectMode = true };

        if (source is SharpTSArray arr)
        {
            foreach (var item in arr)
                stream.PushFromHost(interpreter, item);
        }
        else if (source is List<object?> list)
        {
            foreach (var item in list)
                stream.PushFromHost(interpreter, item);
        }

        stream.PushFromHost(interpreter, null); // EOF
        return RuntimeValue.FromObject(stream);
    }

    /// <summary>
    /// Sets a property on the Duplex constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        return false;
    }

    public override string ToString() => "[Function: Duplex]";
}
