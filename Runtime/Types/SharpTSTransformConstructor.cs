using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the Transform stream constructor exported from the 'stream' module.
/// Supports instantiation via <c>new Transform(options?)</c>.
/// </summary>
public sealed class SharpTSTransformConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the Transform constructor.
    /// </summary>
    public static readonly SharpTSTransformConstructor Instance = new();

    private SharpTSTransformConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// Transform constructor takes 0 required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSTransform"/> instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        var stream = new SharpTSTransform();

        // Process options if provided
        if (arguments.Count > 0 && arguments[0] is SharpTSObject options)
        {
            // transform callback: called for each chunk
            if (options.GetProperty("transform") is ISharpTSCallable transformCallback)
            {
                stream.SetTransformCallback(transformCallback);
            }

            // flush callback: called when the stream ends
            if (options.GetProperty("flush") is ISharpTSCallable flushCallback)
            {
                stream.SetFlushCallback(flushCallback);
            }

            // read callback
            if (options.GetProperty("read") is ISharpTSCallable readCallback)
            {
                stream.SetReadCallback(readCallback);
            }

            // write callback (for Duplex compatibility)
            if (options.GetProperty("write") is ISharpTSCallable writeCallback)
            {
                stream.SetWriteCallback(writeCallback);
            }

            // final callback
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
        }

        return stream;
    }

    /// <summary>
    /// Gets a property from the Transform constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            _ => null
        };
    }

    /// <summary>
    /// Sets a property on the Transform constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        return false;
    }

    public override string ToString() => "[Function: Transform]";
}
