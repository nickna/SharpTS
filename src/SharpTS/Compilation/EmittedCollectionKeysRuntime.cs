using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required collection key identity declarations for one compilation.</summary>
public sealed class EmittedCollectionKeysRuntime
{
    internal EmittedCollectionKeysRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _comparerInstance;
    public FieldBuilder ComparerInstance
    {
        get => Require(_comparerInstance);
        internal set => SetHandle(ref _comparerInstance, value);
    }

    private FieldBuilder? _nullSentinel;
    public FieldBuilder NullSentinel
    {
        get => Require(_nullSentinel);
        internal set => SetHandle(ref _nullSentinel, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("CollectionKeys metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("CollectionKeys metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ComparerInstance;
        _ = NullSentinel;
        IsComplete = true;
    }
}
