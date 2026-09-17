using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required strict and non-strict property/index deletion metadata for one compilation.</summary>
public sealed class EmittedObjectDeletionRuntime
{
    internal EmittedObjectDeletionRuntime() { }
    public bool IsComplete { get; private set; }

    private MethodBuilder? _property;
    public MethodBuilder Property
    {
        get => Require(_property);
        internal set => SetHandle(ref _property, value);
    }

    private MethodBuilder? _propertyStrict;
    public MethodBuilder PropertyStrict
    {
        get => Require(_propertyStrict);
        internal set => SetHandle(ref _propertyStrict, value);
    }

    private MethodBuilder? _compactDictionaryOrder;
    public MethodBuilder CompactDictionaryOrder
    {
        get => Require(_compactDictionaryOrder);
        internal set => SetHandle(ref _compactDictionaryOrder, value);
    }

    private MethodBuilder? _index;
    public MethodBuilder Index
    {
        get => Require(_index);
        internal set => SetHandle(ref _index, value);
    }

    private MethodBuilder? _indexStrict;
    public MethodBuilder IndexStrict
    {
        get => Require(_indexStrict);
        internal set => SetHandle(ref _indexStrict, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object deletion metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object deletion metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object deletion metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Property;
        _ = PropertyStrict;
        _ = CompactDictionaryOrder;
        _ = Index;
        _ = IndexStrict;
        IsComplete = true;
    }
}
