using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required captured iterator-next lookup and invocation metadata.</summary>
public sealed class EmittedIteratorRecordRuntime
{
    internal EmittedIteratorRecordRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _nextMethod;
    public MethodBuilder NextMethod
    {
        get => Require(_nextMethod);
        internal set => SetHandle(ref _nextMethod, value);
    }

    private MethodBuilder? _invokeNext;
    public MethodBuilder InvokeNext
    {
        get => Require(_invokeNext);
        internal set => SetHandle(ref _invokeNext, value);
    }

    private MethodBuilder? _invokeNextWithSent;
    public MethodBuilder InvokeNextWithSent
    {
        get => Require(_invokeNextWithSent);
        internal set => SetHandle(ref _invokeNextWithSent, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Iterator record metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Iterator record metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Iterator record metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = NextMethod;
        _ = InvokeNext;
        _ = InvokeNextWithSent;
        IsComplete = true;
    }
}
