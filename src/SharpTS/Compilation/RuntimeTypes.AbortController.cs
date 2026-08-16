using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region AbortController / AbortSignal Support

    /// <summary>
    /// Creates a new AbortController.
    /// </summary>
    public static object CreateAbortController()
    {
        return new SharpTSAbortController();
    }

    /// <summary>
    /// Calls abort on an AbortController with an optional reason.
    /// Returns undefined (null).
    /// </summary>
    public static object? AbortControllerAbort(object? controller, object? reason)
    {
        if (controller is SharpTSAbortController ac)
        {
            ac.Abort(reason);
        }
        return null;
    }

    /// <summary>
    /// Gets the signal from an AbortController.
    /// </summary>
    public static object? AbortControllerGetSignal(object? controller)
    {
        if (controller is SharpTSAbortController ac)
        {
            return ac.Signal;
        }
        return null;
    }

    /// <summary>
    /// Gets the aborted property from an AbortSignal.
    /// </summary>
    public static bool AbortSignalGetAborted(object? signal)
    {
        if (signal is SharpTSAbortSignal s)
        {
            return s.Aborted;
        }
        return false;
    }

    /// <summary>
    /// Gets the reason property from an AbortSignal.
    /// </summary>
    public static object? AbortSignalGetReason(object? signal)
    {
        if (signal is SharpTSAbortSignal s)
        {
            return s.Reason;
        }
        return null;
    }

    /// <summary>
    /// Gets the onabort property from an AbortSignal.
    /// </summary>
    public static object? AbortSignalGetOnAbort(object? signal)
    {
        if (signal is SharpTSAbortSignal s)
        {
            return s.OnAbort;
        }
        return null;
    }

    /// <summary>
    /// Sets the onabort property on an AbortSignal.
    /// </summary>
    public static void AbortSignalSetOnAbort(object? signal, object? handler)
    {
        if (signal is SharpTSAbortSignal s)
        {
            s.OnAbort = handler;
        }
    }

    /// <summary>
    /// Calls throwIfAborted on an AbortSignal.
    /// </summary>
    public static void AbortSignalThrowIfAborted(object? signal)
    {
        if (signal is SharpTSAbortSignal s)
        {
            s.ThrowIfAborted();
        }
    }

    /// <summary>
    /// Adds an event listener to an AbortSignal. Returns undefined.
    /// </summary>
    public static object? AbortSignalAddEventListener(object? signal, string type, object? listener)
    {
        if (signal is SharpTSAbortSignal s && listener != null)
        {
            s.AddEventListener(type, listener);
        }
        return null;
    }

    /// <summary>
    /// Removes an event listener from an AbortSignal. Returns undefined.
    /// </summary>
    public static object? AbortSignalRemoveEventListener(object? signal, string type, object? listener)
    {
        if (signal is SharpTSAbortSignal s && listener != null)
        {
            s.RemoveEventListener(type, listener);
        }
        return null;
    }

    /// <summary>
    /// Static factory: AbortSignal.abort(reason?)
    /// </summary>
    public static object AbortSignalAbort(object? reason)
    {
        return SharpTSAbortSignal.Abort(reason);
    }

    /// <summary>
    /// Static factory: AbortSignal.timeout(ms)
    /// </summary>
    public static object AbortSignalTimeout(double ms)
    {
        return SharpTSAbortSignal.Timeout(ms);
    }

    /// <summary>
    /// Static factory: AbortSignal.any(signals)
    /// </summary>
    public static object AbortSignalAny(object? signals)
    {
        IEnumerable<SharpTSAbortSignal>? abortSignals = null;

        if (signals is SharpTSArray arr)
        {
            abortSignals = arr
                .Where(e => e is SharpTSAbortSignal)
                .Cast<SharpTSAbortSignal>();
        }
        else if (signals is List<object?> list)
        {
            abortSignals = list
                .Where(e => e is SharpTSAbortSignal)
                .Cast<SharpTSAbortSignal>();
        }

        if (abortSignals != null)
            return SharpTSAbortSignal.Any(abortSignals);

        throw new Exception("Runtime Error: AbortSignal.any requires an array of AbortSignal instances");
    }

    /// <summary>
    /// AbortSignal.any(signals) for compiled mode where signals are dict-based.
    /// Creates a composite signal that aborts when any input signal aborts.
    /// Each signal is a Dictionary{string, object?} with _token, _cts, _reason, _reasonSet, _listeners, _onabort.
    /// </summary>
    public static object AbortSignalAnyCompiled(object? signals)
    {
        var cts = new System.Threading.CancellationTokenSource();
        var signal = new Dictionary<string, object?>
        {
            ["_token"] = cts.Token,
            ["_cts"] = cts,
            ["_reason"] = null,
            ["_reasonSet"] = false,
            ["_listeners"] = new List<object?>(),
            ["_onabort"] = null
        };

        var inputSignals = new List<Dictionary<string, object?>>();
        if (signals is List<object?> list)
        {
            foreach (var item in list)
            {
                if (item is Dictionary<string, object?> dict)
                    inputSignals.Add(dict);
            }
        }

        // Check for already-aborted signals first
        foreach (var s in inputSignals)
        {
            if (s.TryGetValue("_token", out var tokenObj)
                && tokenObj is System.Threading.CancellationToken token
                && token.IsCancellationRequested)
            {
                CopyReason(s, signal);
                cts.Cancel();
                return signal;
            }
        }

        // Register callbacks for non-aborted signals
        foreach (var s in inputSignals)
        {
            if (s.TryGetValue("_token", out var tokenObj)
                && tokenObj is System.Threading.CancellationToken token)
            {
                var captured = s;
                token.Register(() =>
                {
                    if (!cts.IsCancellationRequested)
                    {
                        CopyReason(captured, signal);
                        cts.Cancel();
                    }
                });
            }
        }

        return signal;

        static void CopyReason(Dictionary<string, object?> source, Dictionary<string, object?> target)
        {
            if (source.TryGetValue("_reasonSet", out var rs) && rs is true)
            {
                target["_reasonSet"] = true;
                target["_reason"] = source.TryGetValue("_reason", out var r) ? r : null;
            }
            else
            {
                target["_reasonSet"] = true;
                target["_reason"] = "AbortError: The operation was aborted";
            }
        }
    }

    #endregion
}
