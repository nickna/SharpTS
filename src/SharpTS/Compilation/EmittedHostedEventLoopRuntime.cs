using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional event-loop hooks and storage, emitted only for hosted output.
/// Declarations support forward references; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedHostedEventLoopRuntime
{
    internal EmittedHostedEventLoopRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _configure;
    public MethodBuilder Configure
    {
        get => Require(_configure);
        internal set => Set(ref _configure, value);
    }

    private MethodBuilder? _getRuntime;
    public MethodBuilder GetRuntime
    {
        get => Require(_getRuntime);
        internal set => Set(ref _getRuntime, value);
    }

    private MethodBuilder? _prepareAwait;
    public MethodBuilder PrepareAwait
    {
        get => Require(_prepareAwait);
        internal set => Set(ref _prepareAwait, value);
    }

    private MethodBuilder? _tryRunOne;
    public MethodBuilder TryRunOne
    {
        get => Require(_tryRunOne);
        internal set => Set(ref _tryRunOne, value);
    }

    private MethodBuilder? _hasQueuedCallbacks;
    public MethodBuilder HasQueuedCallbacks
    {
        get => Require(_hasQueuedCallbacks);
        internal set => Set(ref _hasQueuedCallbacks, value);
    }

    private MethodBuilder? _reject;
    public MethodBuilder Reject
    {
        get => Require(_reject);
        internal set => Set(ref _reject, value);
    }

    private MethodBuilder? _clear;
    public MethodBuilder Clear
    {
        get => Require(_clear);
        internal set => Set(ref _clear, value);
    }

    private FieldBuilder? _runtimeField;
    public FieldBuilder RuntimeField
    {
        get => Require(_runtimeField);
        internal set => Set(ref _runtimeField, value);
    }

    private FieldBuilder? _acceptingField;
    public FieldBuilder AcceptingField
    {
        get => Require(_acceptingField);
        internal set => Set(ref _acceptingField, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Hosted event-loop metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Hosted event-loop metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Configure;
        _ = GetRuntime;
        _ = PrepareAwait;
        _ = TryRunOne;
        _ = HasQueuedCallbacks;
        _ = Reject;
        _ = Clear;
        _ = RuntimeField;
        _ = AcceptingField;
        IsComplete = true;
    }
}
