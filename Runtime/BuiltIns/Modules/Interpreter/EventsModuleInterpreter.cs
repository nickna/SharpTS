using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'events' module.
/// </summary>
/// <remarks>
/// Provides the EventEmitter class for event-driven programming.
/// The events module exports the EventEmitter constructor.
/// </remarks>
public static class EventsModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the events module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["EventEmitter"] = SharpTSEventEmitterConstructor.Instance
        };
    }
}
