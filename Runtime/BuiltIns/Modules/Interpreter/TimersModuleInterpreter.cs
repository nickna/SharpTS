using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'timers' module.
/// </summary>
/// <remarks>
/// Provides timer functions that are also available as globals.
/// Includes setTimeout, setInterval, setImmediate and their clear counterparts.
/// </remarks>
public static class TimersModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the timers module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["setTimeout"] = new BuiltInMethod("setTimeout", 1, int.MaxValue, SetTimeout),
            ["clearTimeout"] = new BuiltInMethod("clearTimeout", 0, 1, ClearTimeout),
            ["setInterval"] = new BuiltInMethod("setInterval", 1, int.MaxValue, SetInterval),
            ["clearInterval"] = new BuiltInMethod("clearInterval", 0, 1, ClearInterval),
            ["setImmediate"] = new BuiltInMethod("setImmediate", 1, int.MaxValue, SetImmediate),
            ["clearImmediate"] = new BuiltInMethod("clearImmediate", 0, 1, ClearImmediate)
        };
    }

    /// <summary>
    /// setTimeout(callback, delay?, ...args) - schedules callback after delay milliseconds.
    /// </summary>
    private static object? SetTimeout(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setTimeout requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setTimeout callback must be a function");

        double delayMs = 0;
        if (args.Count > 1 && args[1] is double d)
            delayMs = d;

        var callbackArgs = args.Count > 2
            ? args.Skip(2).ToList()
            : new List<object?>();

        return TimerBuiltIns.SetTimeout(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// clearTimeout(handle?) - cancels a pending timeout.
    /// </summary>
    private static object? ClearTimeout(Interp interpreter, object? receiver, List<object?> args)
    {
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearTimeout(handle);
        return null;
    }

    /// <summary>
    /// setInterval(callback, delay?, ...args) - schedules callback to repeat every delay milliseconds.
    /// </summary>
    private static object? SetInterval(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setInterval requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setInterval callback must be a function");

        double delayMs = 0;
        if (args.Count > 1 && args[1] is double d)
            delayMs = d;

        var callbackArgs = args.Count > 2
            ? args.Skip(2).ToList()
            : new List<object?>();

        return TimerBuiltIns.SetInterval(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// clearInterval(handle?) - cancels a pending interval.
    /// </summary>
    private static object? ClearInterval(Interp interpreter, object? receiver, List<object?> args)
    {
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearInterval(handle);
        return null;
    }

    /// <summary>
    /// setImmediate(callback, ...args) - schedules callback to run in the next iteration of the event loop.
    /// </summary>
    /// <remarks>
    /// Implemented as setTimeout(callback, 0, ...args) since the interpreter doesn't have
    /// a separate immediate queue.
    /// </remarks>
    private static object? SetImmediate(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setImmediate requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setImmediate callback must be a function");

        var callbackArgs = args.Count > 1
            ? args.Skip(1).ToList()
            : new List<object?>();

        // setImmediate is essentially setTimeout with 0 delay
        return TimerBuiltIns.SetTimeout(interpreter, callback, 0, callbackArgs);
    }

    /// <summary>
    /// clearImmediate(handle?) - cancels a pending immediate.
    /// </summary>
    private static object? ClearImmediate(Interp interpreter, object? receiver, List<object?> args)
    {
        // clearImmediate uses the same mechanism as clearTimeout
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearTimeout(handle);
        return null;
    }
}
