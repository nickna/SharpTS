using System.Runtime.CompilerServices;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Unhandled-rejection reporting for guest callbacks invoked by built-in code (#228),
/// wired to the process 'unhandledRejection' / 'rejectionHandled' events (#1080).
/// </summary>
/// <remarks>
/// When C# built-in code invokes a guest callback (timer callbacks, fs/dns/crypto
/// completion callbacks, EventEmitter listeners, http server callbacks), the
/// callback's return value is discarded — there is no guest-visible promise that
/// anyone could attach a rejection handler to. If the callback is async and
/// rejects, the rejection used to vanish completely: no output, no exit-code
/// signal, and any event-loop Refs the callback would have released stayed
/// leaked (the silent-hang symptom behind #207). These helpers give every such
/// invocation site Node's default behavior: report the rejection on stderr and
/// make the process exit nonzero — unless a process 'unhandledRejection'
/// listener is installed, which (like Node) suppresses the default and receives
/// (reason, promise) instead. A later .catch()/.then(_, onRejected) on a
/// reported promise fires 'rejectionHandled'.
/// </remarks>
public partial class Interpreter
{
    /// <summary>
    /// Promises already surfaced through 'unhandledRejection', eligible for a
    /// later 'rejectionHandled'. Weak-keyed so reporting keeps nothing alive.
    /// </summary>
    private static readonly ConditionalWeakTable<SharpTSPromise, object> _reportedRejections = new();

    /// <summary>
    /// True once any unhandled promise rejection has been reported. The CLI
    /// turns this into a nonzero process exit code after the event loop drains,
    /// matching Node's default unhandled-rejection behavior.
    /// </summary>
    public bool HadUnhandledRejection { get; private set; }

    /// <summary>
    /// Invokes a guest callback whose result the caller would otherwise discard,
    /// and reports the rejection if the callback was async and its promise faults.
    /// Built-in invocation sites should prefer this over calling
    /// <c>callback.Call(...)</c> directly and dropping the result.
    /// </summary>
    public void InvokeGuestCallback(ISharpTSCallable callback, List<object?> args)
    {
        ObserveDiscardedCallbackResult(callback.Call(this, args));
    }

    /// <summary>
    /// Observes a guest-callback result that the built-in caller is about to
    /// discard. If it is a promise (or raw task) that rejects, the rejection is
    /// reported as unhandled — synchronously if already faulted, otherwise via a
    /// continuation when it settles. Non-promise results are ignored.
    /// </summary>
    public void ObserveDiscardedCallbackResult(object? result)
    {
        SharpTSPromise? promise = result as SharpTSPromise;
        Task<object?>? task = result switch
        {
            SharpTSPromise p => p.Task,
            Task<object?> t => t,
            _ => null,
        };
        if (task == null) return;

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
            {
                ReportUnhandledRejection(task.Exception!.InnerException ?? task.Exception!, promise);
            }
            return;
        }

        task.ContinueWith(
            t => ReportUnhandledRejection(t.Exception!.InnerException ?? t.Exception!, promise),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Called when a rejection handler is attached (.catch / .then(_, onRejected))
    /// to a promise. If the promise was previously reported through
    /// 'unhandledRejection', emits 'rejectionHandled' (Node semantics).
    /// </summary>
    internal void NotifyRejectionHandlerAttached(SharpTSPromise promise)
    {
        if (!_reportedRejections.TryGetValue(promise, out _)) return;
        _reportedRejections.Remove(promise);

        try
        {
            EnqueueCallback(() =>
                SharpTSProcess.Instance.EmitWith(this, "rejectionHandled", promise));
        }
        catch
        {
            SharpTSProcess.Instance.EmitDirect("rejectionHandled", promise);
        }
    }

    private void ReportUnhandledRejection(Exception exception, SharpTSPromise? promise = null)
    {
        // A process-level listener suppresses the default (stderr + nonzero
        // exit) and receives (reason, promise) instead — Node semantics.
        if (SharpTSProcess.Instance.HasListenersInternal("unhandledRejection"))
        {
            object? reason = exception switch
            {
                SharpTSPromiseRejectedException rejected => rejected.Reason,
                ThrowException thrown => thrown.Value,
                _ => exception.Message,
            };
            if (promise != null)
                _reportedRejections.AddOrUpdate(promise, new object());

            try
            {
                EnqueueCallback(() =>
                    SharpTSProcess.Instance.EmitWith(this, "unhandledRejection", reason, promise ?? (object?)SharpTSUndefined.Instance));
            }
            catch
            {
                SharpTSProcess.Instance.EmitDirect("unhandledRejection", reason, promise ?? (object?)SharpTSUndefined.Instance);
            }
            return;
        }

        HadUnhandledRejection = true;
        _hostedUnhandledError?.Invoke(exception);

        string message = exception switch
        {
            SharpTSPromiseRejectedException rejected => rejected.Message,
            ThrowException thrown => SharpTSPromiseRejectedException.FormatReason(thrown.Value),
            _ => exception.Message,
        };

        try
        {
            Error.WriteLine($"Unhandled promise rejection: {message}");
            if (exception is SharpTSPromiseRejectedException { RejectionStack: { } stack })
            {
                Error.WriteLine(stack);
            }
        }
        catch
        {
            // Reporting must never take down the event loop (e.g. a closed
            // stderr writer during shutdown). The exit-code flag is already set.
        }
    }
}
