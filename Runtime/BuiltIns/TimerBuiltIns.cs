using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides runtime implementation for timer functions (setTimeout, clearTimeout).
/// Uses Task.Delay for async delay implementation.
/// </summary>
public static class TimerBuiltIns
{
    /// <summary>
    /// Executes setTimeout: schedules a callback to run after a delay.
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

        // Ensure delay is non-negative
        int delay = Math.Max(0, (int)delayMs);

        // Start the async task
        var task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                if (!cts.IsCancellationRequested)
                {
                    // Execute callback on completion
                    callback.Call(interpreter, args);
                }
            }
            catch (TaskCanceledException)
            {
                // Timeout was cancelled - this is expected behavior
            }
        }, cts.Token);

        timeout.Task = task;
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
