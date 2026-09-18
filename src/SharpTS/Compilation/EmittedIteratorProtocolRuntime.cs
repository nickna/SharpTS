using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required iterator lookup, result reading, closing and dynamic protocol metadata.</summary>
public sealed class EmittedIteratorProtocolRuntime
{
    internal EmittedIteratorProtocolRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _function;
    // Returns iterator function or $Undefined when absent
    public MethodBuilder Function
    {
        get => Require(_function);
        internal set => SetHandle(ref _function, value);
    }

    private MethodBuilder? _invokeNext;
    // Calls next() on iterator (no sent value)
    public MethodBuilder InvokeNext
    {
        get => Require(_invokeNext);
        internal set => SetHandle(ref _invokeNext, value);
    }

    private MethodBuilder? _done;
    // Extracts done from result
    public MethodBuilder Done
    {
        get => Require(_done);
        internal set => SetHandle(ref _done, value);
    }

    private MethodBuilder? _value;
    // Extracts value from result
    public MethodBuilder Value
    {
        get => Require(_value);
        internal set => SetHandle(ref _value, value);
    }

    private MethodBuilder? _close;
    // IteratorClose(iterator, preserveThrowCompletion)
    public MethodBuilder Close
    {
        get => Require(_close);
        internal set => SetHandle(ref _close, value);
    }

    private MethodBuilder? _call;
    // Dynamic JS iterator-protocol bridge (.next()/.return()) for any-typed
    // receivers that are bare IEnumerator<object> — array .values()/.keys()/
    // .entries() return these. See EmitIteratorProtocolCall.
    public MethodBuilder Call
    {
        get => Require(_call);
        internal set => SetHandle(ref _call, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Iterator protocol metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Iterator protocol metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Iterator protocol metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Function;
        _ = InvokeNext;
        _ = Done;
        _ = Value;
        _ = Close;
        _ = Call;
        IsComplete = true;
    }
}
