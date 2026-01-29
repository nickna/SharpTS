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

            // highWaterMark is typically used for async backpressure
            // In sync mode we don't need it, but accept it for compatibility
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
            _ => null
        };
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
