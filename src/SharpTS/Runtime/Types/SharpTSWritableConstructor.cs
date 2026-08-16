using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the Writable stream constructor exported from the 'stream' module.
/// Supports instantiation via <c>new Writable(options?)</c>.
/// </summary>
public sealed class SharpTSWritableConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the Writable constructor.
    /// </summary>
    public static readonly SharpTSWritableConstructor Instance = new();

    private SharpTSWritableConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// Writable constructor takes 0 required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSWritable"/> instance.
    /// </summary>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        var stream = new SharpTSWritable();

        // Process options if provided
        if (arguments.Count > 0 && arguments[0] is SharpTSObject options)
        {
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

            // destroy callback: called when destroy() is called
            if (options.GetProperty("destroy") is ISharpTSCallable destroyCallback)
            {
                stream.SetDestroyCallback(destroyCallback);
            }

            // objectMode option
            if (options.GetProperty("objectMode") is true)
            {
                stream.ObjectMode = true;
            }

            // autoDestroy option (default: true in Node.js, but false here for backward compatibility)
            if (options.GetProperty("autoDestroy") is true)
            {
                stream.AutoDestroy = true;
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
    /// Gets a property from the Writable constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            "isWritable" => BuiltInMethod.CreateV2("isWritable", 1, IsWritable),
            _ => null
        };
    }

    private static RuntimeValue IsWritable(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args.Length > 0 ? args[0].ToObject() : null;
        return RuntimeValue.FromBoolean(obj is SharpTSWritable or SharpTSDuplex);
    }

    /// <summary>
    /// Sets a property on the Writable constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        return false;
    }

    public override string ToString() => "[Function: Writable]";
}
