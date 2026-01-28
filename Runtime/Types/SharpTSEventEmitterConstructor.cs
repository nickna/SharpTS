using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents the EventEmitter constructor/class object exported from the 'events' module.
/// Supports instantiation via <c>new EventEmitter()</c> and static property access.
/// </summary>
/// <remarks>
/// Implements <see cref="ISharpTSCallable"/> to allow construction of new <see cref="SharpTSEventEmitter"/>
/// instances when called with the <c>new</c> keyword.
/// </remarks>
public sealed class SharpTSEventEmitterConstructor : ISharpTSCallable
{
    /// <summary>
    /// The singleton instance of the EventEmitter constructor.
    /// </summary>
    public static readonly SharpTSEventEmitterConstructor Instance = new();

    private SharpTSEventEmitterConstructor() { }

    /// <summary>
    /// Gets the arity (number of required parameters) for the constructor.
    /// EventEmitter constructor takes no required arguments.
    /// </summary>
    public int Arity() => 0;

    /// <summary>
    /// Creates a new <see cref="SharpTSEventEmitter"/> instance.
    /// </summary>
    /// <param name="interpreter">The interpreter instance.</param>
    /// <param name="arguments">Constructor arguments (ignored for EventEmitter).</param>
    /// <returns>A new EventEmitter instance.</returns>
    public object? Call(Interp interpreter, List<object?> arguments)
    {
        return new SharpTSEventEmitter();
    }

    /// <summary>
    /// Gets a property from the EventEmitter constructor (static properties/methods).
    /// </summary>
    public object? GetProperty(string name)
    {
        return name switch
        {
            // Static property: EventEmitter.defaultMaxListeners
            "defaultMaxListeners" => (double)SharpTSEventEmitter.DefaultMaxListeners,
            _ => null
        };
    }

    /// <summary>
    /// Sets a property on the EventEmitter constructor (static properties).
    /// </summary>
    public bool SetProperty(string name, object? value)
    {
        switch (name)
        {
            case "defaultMaxListeners":
                if (value is double d)
                {
                    SharpTSEventEmitter.DefaultMaxListeners = (int)d;
                    return true;
                }
                throw new Exception("defaultMaxListeners must be a number");
            default:
                return false;
        }
    }

    public override string ToString() => "[Function: EventEmitter]";
}
