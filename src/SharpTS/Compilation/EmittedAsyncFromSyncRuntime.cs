using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional async-from-sync adapter and result-adoption helper metadata.</summary>
public sealed class EmittedAsyncFromSyncRuntime
{
    internal EmittedAsyncFromSyncRuntime() { }
    public bool IsComplete { get; private set; }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => SetHandle(ref _ctor, value);
    }

    private MethodBuilder? _adapt;
    public MethodBuilder Adapt
    {
        get => Require(_adapt);
        internal set => SetHandle(ref _adapt, value);
    }

    private MethodBuilder? _createResult;
    public MethodBuilder CreateResult
    {
        get => Require(_createResult);
        internal set => SetHandle(ref _createResult, value);
    }

    private MethodBuilder? _awaitResult;
    public MethodBuilder AwaitResult
    {
        get => Require(_awaitResult);
        internal set => SetHandle(ref _awaitResult, value);
    }

    private MethodBuilder? _awaitContinuation;
    public MethodBuilder AwaitContinuation
    {
        get => Require(_awaitContinuation);
        internal set => SetHandle(ref _awaitContinuation, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Async-from-sync adapter metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Async-from-sync adapter metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Async-from-sync adapter metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Ctor;
        _ = Adapt;
        _ = CreateResult;
        _ = AwaitResult;
        _ = AwaitContinuation;
        IsComplete = true;
    }
}
