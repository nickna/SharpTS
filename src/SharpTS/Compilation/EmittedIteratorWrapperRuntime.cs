using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required custom-iterator adapter type, constructor and sent-value metadata.</summary>
public sealed class EmittedIteratorWrapperRuntime
{
    internal EmittedIteratorWrapperRuntime() { }
    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private ConstructorBuilder? _ctor;
    public ConstructorBuilder Ctor
    {
        get => Require(_ctor);
        internal set => SetHandle(ref _ctor, value);
    }

    private MethodBuilder? _moveNextWithSent;
    // $IteratorWrapper.MoveNextWithSent(sent) (#503)
    public MethodBuilder MoveNextWithSent
    {
        get => Require(_moveNextWithSent);
        internal set => SetHandle(ref _moveNextWithSent, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Iterator wrapper metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Iterator wrapper metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Iterator wrapper metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Ctor;
        _ = MoveNextWithSent;
        IsComplete = true;
    }
}
