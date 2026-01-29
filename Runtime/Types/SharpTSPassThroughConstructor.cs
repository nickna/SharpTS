using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the PassThrough stream constructor exported from the 'stream' module.
/// Supports instantiation via <c>new PassThrough(options?)</c>.
/// </summary>
public sealed class SharpTSPassThroughConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the PassThrough constructor.
    /// </summary>
    public static readonly SharpTSPassThroughConstructor Instance = new();

    private SharpTSPassThroughConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// PassThrough constructor takes 0 required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSPassThrough"/> instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        var stream = new SharpTSPassThrough();

        // Process options if provided
        if (arguments.Count > 0 && arguments[0] is SharpTSObject options)
        {
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
    /// Gets a property from the PassThrough constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            _ => null
        };
    }

    /// <summary>
    /// Sets a property on the PassThrough constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        return false;
    }

    public override string ToString() => "[Function: PassThrough]";
}
