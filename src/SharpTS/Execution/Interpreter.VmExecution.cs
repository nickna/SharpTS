using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private VmExecutionHost? _vmExecutionHost;
    private CancellationTokenRegistration _vmHostShutdown;
    private VmEvaluationLease? _vmEvaluation;
    private bool _vmOwnedEvaluation;

    internal VmEvaluationLease? VmEvaluation => _vmEvaluation;
    internal CancellationToken CurrentVmTimeoutToken => _vmTimeoutToken;
    internal void CheckVmExecutionCancellation() => ThrowIfExecutionCancelled();
    internal SynchronizationContext VmSynchronizationContext =>
        _eventLoopSyncContext ??= new InterpreterSynchronizationContext(EnqueuePromiseJob);

    internal VmExecutionHost GetVmExecutionHost()
    {
        if (_vmEvaluation is { IsDisposed: false } evaluation) return evaluation.Host;
        if (_vmExecutionHost is not null) return _vmExecutionHost;
        var host = new VmExecutionHost(EnqueuePromiseJob, EnqueueCallback,
            EnqueueAfterPromiseCheckpoint, Ref, Unref, ThrowIfExecutionCancelled);
        _vmExecutionHost = host;
        _vmHostShutdown = ShutdownToken.Register(static state => ((VmExecutionHost)state!).Dispose(), host);
        return host;
    }

    internal void MarkVmOwnedEvaluation() => _vmOwnedEvaluation = true;

    internal void AttachVmEvaluation(VmEvaluationLease evaluation)
    {
        _vmEvaluation = evaluation;
        _vmOwnedEvaluation = true;
        _hostedAcceptingWork = true;
        _hostedWorkAvailable = evaluation.NotifyWork;
        _hostedTimerChanged = evaluation.RefreshTimer;
    }

    internal bool HasVmCallbacks => !_isDisposed && _callbackQueue.Count != 0;

    internal bool TryExecuteOneVmMacrotask() => TryExecuteOneOwnedMacrotask(false, out _);
}
