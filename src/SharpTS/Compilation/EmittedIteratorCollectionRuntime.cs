using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required, forward-declared iterable collection methods.</summary>
public sealed class EmittedIteratorCollectionRuntime
{
    internal EmittedIteratorCollectionRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _toList;
    // Converts any iterable to List<object>
    public MethodBuilder ToList
    {
        get => Require(_toList);
        internal set => SetHandle(ref _toList, value);
    }

    private MethodBuilder? _intoList;
    public MethodBuilder IntoList
    {
        get => Require(_intoList);
        internal set => SetHandle(ref _intoList, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Iterator collection metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Iterator collection metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Iterator collection metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ToList;
        _ = IntoList;
        IsComplete = true;
    }
}
