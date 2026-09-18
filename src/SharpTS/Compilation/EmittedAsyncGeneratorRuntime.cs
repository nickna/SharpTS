using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional async-generator interface and independently gated continuation/adapter metadata.</summary>
public sealed class EmittedAsyncGeneratorRuntime
{
    internal EmittedAsyncGeneratorRuntime() { }
    public bool IsComplete { get; private set; }

    // Async Generator interface ($IAsyncGenerator extends IAsyncEnumerator<object> with async Return/Throw)
    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private MethodBuilder? _next;
    public MethodBuilder Next
    {
        get => Require(_next);
        internal set => SetHandle(ref _next, value);
    }

    private MethodBuilder? _return;
    public MethodBuilder Return
    {
        get => Require(_return);
        internal set => SetHandle(ref _return, value);
    }

    private MethodBuilder? _throw;
    public MethodBuilder Throw
    {
        get => Require(_throw);
        internal set => SetHandle(ref _throw, value);
    }

    public EmittedAsyncGeneratorContinuationRuntime? Continuations { get; private set; }

    public EmittedAsyncGeneratorContinuationRuntime RequireContinuations() => Continuations
        ?? throw new InvalidOperationException("Async generator continuation was not enabled for this compilation.");

    internal void BeginContinuationsEmission()
    {
        EnsureMutable();
        if (Continuations is not null)
            throw new InvalidOperationException("Async generator continuation emission has already started.");
        Continuations = new EmittedAsyncGeneratorContinuationRuntime();
    }

    public EmittedAsyncFromSyncRuntime? FromSync { get; private set; }

    public EmittedAsyncFromSyncRuntime RequireFromSync() => FromSync
        ?? throw new InvalidOperationException("Async-from-sync adapter was not enabled for this compilation.");

    internal void BeginFromSyncEmission()
    {
        EnsureMutable();
        if (FromSync is not null)
            throw new InvalidOperationException("Async-from-sync adapter emission has already started.");
        FromSync = new EmittedAsyncFromSyncRuntime();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Async generator metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Async generator metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Async generator metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Next;
        _ = Return;
        _ = Throw;
        if (Continuations is { IsComplete: false })
            throw new InvalidOperationException("Async generator continuation emission has not completed.");
        if (FromSync is { IsComplete: false })
            throw new InvalidOperationException("Async-from-sync adapter emission has not completed.");
        IsComplete = true;
    }
}
