using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'perf_hooks' module.
/// </summary>
/// <remarks>
/// Provides high-resolution timing APIs similar to the browser's Performance API.
/// Currently implements the core performance.now() functionality.
/// </remarks>
public static class PerfHooksModuleInterpreter
{
    // High-resolution timer start point (process start time)
    private static readonly long StartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    private static readonly double TicksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;

    /// <summary>
    /// Gets all exported values for the perf_hooks module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["performance"] = CreatePerformanceObject()
        };
    }

    /// <summary>
    /// Creates the performance object with all its methods and properties.
    /// </summary>
    private static SharpTSObject CreatePerformanceObject()
    {
        var fields = new Dictionary<string, object?>
        {
            ["now"] = new BuiltInMethod("now", 0, 0, PerformanceNow),
            ["timeOrigin"] = GetTimeOrigin()
        };

        return new SharpTSObject(fields);
    }

    /// <summary>
    /// Returns a high resolution time stamp in milliseconds.
    /// The value represents the time elapsed since the process started.
    /// </summary>
    private static object? PerformanceNow(Interp interpreter, object? receiver, List<object?> args)
    {
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - StartTicks;
        return elapsed / TicksPerMs;
    }

    /// <summary>
    /// Gets the Unix timestamp of when the process started (in milliseconds since epoch).
    /// </summary>
    private static double GetTimeOrigin()
    {
        // Calculate when the process started in Unix time
        var now = DateTimeOffset.UtcNow;
        var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - StartTicks) / TicksPerMs;
        var startTime = now.AddMilliseconds(-elapsedMs);
        return startTime.ToUnixTimeMilliseconds();
    }
}
