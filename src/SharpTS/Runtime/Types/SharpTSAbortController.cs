using SharpTS.TypeSystem;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of the AbortController Web API.
/// Wraps a .NET CancellationTokenSource for native cancellation support.
/// </summary>
public class SharpTSAbortController : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.AbortController;

    private readonly CancellationTokenSource _cts;
    private readonly SharpTSAbortSignal _signal;

    public SharpTSAbortController()
    {
        _cts = new CancellationTokenSource();
        _signal = new SharpTSAbortSignal(_cts.Token);
    }

    /// <summary>
    /// The associated AbortSignal.
    /// </summary>
    public SharpTSAbortSignal Signal => _signal;

    /// <summary>
    /// Aborts the signal with an optional reason.
    /// Second and subsequent calls are no-ops.
    /// The interpreter parameter is optional — null for compiled code.
    /// </summary>
    public void Abort(object? reason = null, Interp? interpreter = null)
    {
        if (_cts.IsCancellationRequested)
            return;

        _signal.SetReason(reason ?? "AbortError: The operation was aborted");
        _cts.Cancel();
        _signal.FireAbortEvent(interpreter);
    }

    public override string ToString() => "AbortController {}";
}
