using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the <c>primitive:perf</c> primitive module.
/// Exposes a single <c>now()</c> method returning high-resolution ms since first
/// call. The rest of the perf_hooks surface (mark, measure, entries, observer)
/// lives in <c>stdlib/node/perf_hooks.ts</c> as pure TypeScript.
/// </summary>
public static class PerfPrimitiveInterpreter
{
    private static readonly long _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    private static readonly double _ticksPerMs = System.Diagnostics.Stopwatch.Frequency / 1000.0;

    private static readonly BuiltInMethod _now = new("now", 0, 0, Now);

    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["now"] = _now,
        };
    }

    private static object? Now(Interp interpreter, object? receiver, List<object?> args)
    {
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - _startTicks;
        return elapsed / _ticksPerMs;
    }
}
