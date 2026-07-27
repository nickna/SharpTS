using SharpTS.Compilation;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the AbortSignal Web API.
/// Wraps a .NET CancellationToken for native cancellation support.
/// </summary>
/// <remarks>
/// AbortSignal is NOT directly constructable. Instances are obtained from:
/// - AbortController.signal
/// - AbortSignal.abort(reason?)
/// - AbortSignal.timeout(ms)
/// - AbortSignal.any(signals)
/// </remarks>
public class SharpTSAbortSignal : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.AbortSignal;

    private readonly CancellationToken _token;
#pragma warning disable IDE0052 // deliberately unread: pins the CTS (and its timer) against GC while the signal is alive
    private CancellationTokenSource? _ownedCts; // owned by timeout/any factories
#pragma warning restore IDE0052
    private object? _reason;
    private readonly List<object> _listeners = [];
    private object? _onabort;
    private bool _reasonSet;

    /// <summary>
    /// Creates a signal wrapping an external token (from AbortController).
    /// </summary>
    internal SharpTSAbortSignal(CancellationToken token)
    {
        _token = token;
    }

    /// <summary>
    /// Creates a signal that owns its own CancellationTokenSource (for static factories).
    /// </summary>
    private SharpTSAbortSignal(CancellationTokenSource cts)
    {
        _ownedCts = cts;
        _token = cts.Token;
    }

    /// <summary>
    /// Whether the signal has been aborted.
    /// </summary>
    public bool Aborted => _token.IsCancellationRequested;

    /// <summary>
    /// The abort reason. Defaults to an AbortError DOMException-like message.
    /// </summary>
    public object? Reason => _reasonSet ? _reason : (Aborted ? "AbortError: The operation was aborted" : null);

    /// <summary>
    /// The underlying CancellationToken for integration with .NET async APIs (e.g., fetch).
    /// </summary>
    public CancellationToken Token => _token;

    /// <summary>
    /// The onabort event handler property.
    /// </summary>
    public object? OnAbort
    {
        get => _onabort;
        set => _onabort = value;
    }

    /// <summary>
    /// Sets the abort reason. Called by AbortController before cancellation.
    /// </summary>
    internal void SetReason(object? reason)
    {
        if (!_reasonSet)
        {
            _reason = reason;
            _reasonSet = true;
        }
    }

    /// <summary>
    /// Adds an event listener. Only "abort" event type is supported.
    /// </summary>
    public void AddEventListener(string type, object listener)
    {
        if (type == "abort")
        {
            _listeners.Add(listener);
        }
    }

    /// <summary>
    /// Removes an event listener by reference equality.
    /// </summary>
    public void RemoveEventListener(string type, object listener)
    {
        if (type == "abort")
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_listeners[i], listener))
                {
                    _listeners.RemoveAt(i);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Throws if the signal has been aborted.
    /// </summary>
    public void ThrowIfAborted()
    {
        if (Aborted)
        {
            throw new Exception(Reason?.ToString() ?? "AbortError: The operation was aborted");
        }
    }

    /// <summary>
    /// Fires the abort event, invoking all listeners and the onabort handler.
    /// Called directly by AbortController.Abort() to stay on the caller's thread.
    /// The interpreter parameter is optional — null for compiled code paths.
    /// </summary>
    internal void FireAbortEvent(Interp? interpreter = null)
    {
        // Snapshot listeners to handle modifications during iteration
        var snapshot = new List<object>(_listeners);

        foreach (var listener in snapshot)
        {
            InvokeListener(listener, interpreter);
        }

        // Also invoke onabort handler
        if (_onabort != null)
        {
            InvokeListener(_onabort, interpreter);
        }
    }

    /// <summary>
    /// Invokes a listener function (works for both interpreter and compiled code).
    /// </summary>
    private static void InvokeListener(object listener, Interp? interpreter)
    {
        // TSFunction from compiled code — no interpreter needed
        if (listener is TSFunction tsFunc)
        {
            tsFunc.Invoke([]);
            return;
        }

        // ISharpTSCallable from interpreter — needs interpreter
        if (listener is ISharpTSCallable callable)
        {
            callable.Call(interpreter!, []);
            return;
        }

        // BuiltInMethod
        if (listener is BuiltInMethod builtIn)
        {
            builtIn.Call(interpreter!, []);
            return;
        }

        // Reflection fallback for unknown callable types
        var invokeMethod = listener.GetType().GetMethod("Invoke");
        if (invokeMethod != null)
        {
            invokeMethod.Invoke(listener, [Array.Empty<object?>()]);
        }
    }

    #region Static Factories

    /// <summary>
    /// Returns an already-aborted signal with the given reason.
    /// </summary>
    public static SharpTSAbortSignal Abort(object? reason = null)
    {
        var cts = new CancellationTokenSource();
        var signal = new SharpTSAbortSignal(cts);
        signal.SetReason(reason ?? "AbortError: The operation was aborted");
        cts.Cancel();
        return signal;
    }

    /// <summary>
    /// Returns a signal that automatically aborts after the specified milliseconds.
    /// </summary>
    public static SharpTSAbortSignal Timeout(double ms)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(ms));
        var signal = new SharpTSAbortSignal(cts);
        signal.SetReason("TimeoutError: The operation was aborted due to timeout");
        return signal;
    }

    /// <summary>
    /// Returns a signal that aborts when any of the input signals abort.
    /// </summary>
    public static SharpTSAbortSignal Any(IEnumerable<SharpTSAbortSignal> signals)
    {
        var cts = new CancellationTokenSource();
        var compositeSignal = new SharpTSAbortSignal(cts);

        foreach (var signal in signals)
        {
            if (signal.Aborted)
            {
                // Already aborted - abort immediately with that signal's reason
                compositeSignal.SetReason(signal.Reason);
                cts.Cancel();
                return compositeSignal;
            }

            // Register to cancel when any input signal cancels
            var capturedSignal = signal;
            signal._token.Register(() =>
            {
                compositeSignal.SetReason(capturedSignal.Reason);
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            });
        }

        return compositeSignal;
    }

    #endregion

    /// <summary>
    /// Gets a member (method or property) by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "aborted" => Aborted,
            "reason" => Reason ?? SharpTSUndefined.Instance,
            "onabort" => _onabort ?? SharpTSUndefined.Instance,
            "throwIfAborted" => BuiltInMethod.CreateV2("throwIfAborted", 0, (_, _, _) =>
            {
                ThrowIfAborted();
                return RuntimeValue.Undefined;
            }),
            "addEventListener" => BuiltInMethod.CreateV2("addEventListener", 2, (_, _, args) =>
            {
                var type = args[0].ToObject()?.ToString() ?? "abort";
                var listener = args[1].ToObject() ?? throw new Exception("Runtime Error: addEventListener requires a listener function");
                AddEventListener(type, listener);
                return RuntimeValue.Undefined;
            }),
            "removeEventListener" => BuiltInMethod.CreateV2("removeEventListener", 2, (_, _, args) =>
            {
                var type = args[0].ToObject()?.ToString() ?? "abort";
                var listener = args[1].ToObject() ?? throw new Exception("Runtime Error: removeEventListener requires a listener function");
                RemoveEventListener(type, listener);
                return RuntimeValue.Undefined;
            }),
            _ => null
        };
    }

    public override string ToString() => Aborted ? "AbortSignal { aborted: true }" : "AbortSignal { aborted: false }";
}
