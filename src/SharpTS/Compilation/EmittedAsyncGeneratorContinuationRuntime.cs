using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional async-generator await continuation and next-result builder metadata.</summary>
public sealed class EmittedAsyncGeneratorContinuationRuntime
{
    internal EmittedAsyncGeneratorContinuationRuntime() { }
    public bool IsComplete { get; private set; }

    // Async Generator await continuation helper
    private MethodBuilder? _awaitContinue;
    public MethodBuilder AwaitContinue
    {
        get => Require(_awaitContinue);
        internal set => SetHandle(ref _awaitContinue, value);
    }

    // Async Generator next-result builder: awaits a MoveNextAsync ValueTask<bool> and produces the
    // { value, done } Task<object> for next(), so next() never blocks the event-loop thread (#631/#542).
    private MethodBuilder? _buildResult;
    public MethodBuilder BuildResult
    {
        get => Require(_buildResult);
        internal set => SetHandle(ref _buildResult, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Async generator continuation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Async generator continuation metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Async generator continuation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = AwaitContinue;
        _ = BuildResult;
        IsComplete = true;
    }
}
