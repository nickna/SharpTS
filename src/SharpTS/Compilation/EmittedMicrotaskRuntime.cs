using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required shared FIFO microtask and Promise-job queue metadata.
/// Declarations support forward calls; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedMicrotaskRuntime
{
    internal EmittedMicrotaskRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _queueMicrotask;
    public MethodBuilder QueueMicrotask
    {
        get => Require(_queueMicrotask);
        internal set => Set(ref _queueMicrotask, value);
    }

    private MethodBuilder? _queuePromiseJob;
    public MethodBuilder QueuePromiseJob
    {
        get => Require(_queuePromiseJob);
        internal set => Set(ref _queuePromiseJob, value);
    }

    private MethodBuilder? _processMicrotasks;
    public MethodBuilder ProcessMicrotasks
    {
        get => Require(_processMicrotasks);
        internal set => Set(ref _processMicrotasks, value);
    }

    private MethodBuilder? _hasMicrotasks;
    public MethodBuilder HasMicrotasks
    {
        get => Require(_hasMicrotasks);
        internal set => Set(ref _hasMicrotasks, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Microtasks metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Microtasks metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = QueueMicrotask;
        _ = QueuePromiseJob;
        _ = ProcessMicrotasks;
        _ = HasMicrotasks;
        IsComplete = true;
    }
}
