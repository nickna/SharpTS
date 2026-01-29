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
            _ => null
        };
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
