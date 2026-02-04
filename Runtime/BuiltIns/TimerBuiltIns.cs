using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides runtime implementation for timer functions (setTimeout, setInterval, clearTimeout, clearInterval).
/// Uses virtual timers that are processed on the main thread during loop iterations,
/// ensuring deterministic execution and avoiding thread scheduling issues across platforms.
/// </summary>
public static class TimerBuiltIns
{
    /// <summary>
    /// Executes setTimeout: schedules a callback to run after a delay.
    /// Uses virtual timers that are processed on the main thread during loop iterations,
    /// avoiding thread scheduling issues on macOS.
    /// </summary>
    /// <param name="interpreter">The interpreter instance for callback execution.</param>
    /// <param name="callback">The callback function to execute.</param>
    /// <param name="delayMs">The delay in milliseconds (defaults to 0).</param>
    /// <param name="args">Additional arguments to pass to the callback.</param>
    /// <returns>A SharpTSTimeout handle that can be used with clearTimeout.</returns>
    public static SharpTSTimeout SetTimeout(Interpreter interpreter, ISharpTSCallable callback, double delayMs, List<object?> args)
    {
        var cts = new CancellationTokenSource();
        var timeout = new SharpTSTimeout(cts);
        timeout.AttachRefTracking(interpreter.Ref, interpreter.Unref);

        // Ensure delay is non-negative
        int delay = Math.Max(0, (int)delayMs);

        // Schedule a virtual timer that will be checked and executed on the main thread
        var virtualTimer = interpreter.ScheduleTimer(delay, 0, () =>
        {
            if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
            {
                try
                {
                    callback.Call(interpreter, args);
                }
                finally
                {
                    timeout.ReleaseRef();
                }
            }
            else
            {
                timeout.ReleaseRef();
            }
        }, isInterval: false);

        // Link cancellation to virtual timer
        cts.Token.Register(() => virtualTimer.IsCancelled = true);

        // Register timer with interpreter for cleanup on disposal
        interpreter.RegisterTimer(timeout);

        return timeout;
    }

    /// <summary>
    /// Cancels a pending timeout.
    /// Safe to call with null/undefined - follows Node.js behavior.
    /// </summary>
    /// <param name="handle">The timeout handle to cancel (can be null).</param>
    public static void ClearTimeout(object? handle)
    {
        if (handle is SharpTSTimeout timeout)
        {
            timeout.Cancel();
        }
        // If handle is null/undefined/wrong type, silently ignore (Node.js behavior)
    }

    /// <summary>
    /// Executes setInterval: schedules callback to run repeatedly after each delay.
    /// Uses virtual timers that are processed on the main thread during loop iterations,
    /// avoiding thread scheduling issues on macOS.
    /// </summary>
    /// <param name="interpreter">The interpreter instance for callback execution.</param>
    /// <param name="callback">The callback function to execute.</param>
    /// <param name="delayMs">The delay in milliseconds between executions.</param>
    /// <param name="args">Additional arguments to pass to the callback.</param>
    /// <returns>A SharpTSTimeout handle that can be used with clearInterval.</returns>
    public static SharpTSTimeout SetInterval(Interpreter interpreter, ISharpTSCallable callback, double delayMs, List<object?> args)
    {
        var cts = new CancellationTokenSource();
        var interval = new SharpTSTimeout(cts);
        interval.AttachRefTracking(interpreter.Ref, interpreter.Unref);
        int delay = Math.Max(0, (int)delayMs);

        // Schedule a virtual interval timer that will be checked and executed on the main thread
        var virtualTimer = interpreter.ScheduleTimer(delay, delay, () =>
        {
            if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
            {
                callback.Call(interpreter, args);
            }
        }, isInterval: true);

        // Link cancellation to virtual timer
        cts.Token.Register(() => virtualTimer.IsCancelled = true);

        // Register timer with interpreter for cleanup on disposal
        interpreter.RegisterTimer(interval);

        return interval;
    }

    /// <summary>
    /// Cancels a pending interval.
    /// Safe to call with null/undefined - follows Node.js behavior.
    /// </summary>
    /// <param name="handle">The interval handle to cancel (can be null).</param>
    public static void ClearInterval(object? handle)
    {
        if (handle is SharpTSTimeout interval)
        {
            interval.Cancel();
        }
        // If handle is null/undefined/wrong type, silently ignore (Node.js behavior)
    }

    /// <summary>
    /// Gets a member (method or property) from a SharpTSTimeout instance.
    /// </summary>
    /// <param name="timeout">The timeout instance.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member value or method.</returns>
    public static object? GetMember(SharpTSTimeout timeout, string name)
    {
        return name switch
        {
            "ref" => new BuiltInMethod("ref", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.Ref();
                return receiver;
            }),
            "unref" => new BuiltInMethod("unref", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.Unref();
                return receiver;
            }),
            "hasRef" => timeout.HasRef,
            "toString" => new BuiltInMethod("toString", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.ToString();
                return receiver?.ToString() ?? "null";
            }),
            _ => null
        };
    }
}
