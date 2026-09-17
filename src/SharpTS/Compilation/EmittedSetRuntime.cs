using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Set operation and bound-method declarations for one compilation.</summary>
public sealed class EmittedSetRuntime
{
    internal EmittedSetRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _boundMethodType;
    public TypeBuilder BoundMethodType
    {
        get => Require(_boundMethodType);
        internal set => SetHandle(ref _boundMethodType, value);
    }

    private ConstructorBuilder? _boundMethodConstructor;
    public ConstructorBuilder BoundMethodConstructor
    {
        get => Require(_boundMethodConstructor);
        internal set => SetHandle(ref _boundMethodConstructor, value);
    }

    private MethodBuilder? _boundMethodInvoke;
    public MethodBuilder BoundMethodInvoke
    {
        get => Require(_boundMethodInvoke);
        internal set => SetHandle(ref _boundMethodInvoke, value);
    }

    private FieldBuilder? _boundReceiverField;
    public FieldBuilder BoundReceiverField
    {
        get => Require(_boundReceiverField);
        internal set => SetHandle(ref _boundReceiverField, value);
    }

    private FieldBuilder? _boundNameField;
    public FieldBuilder BoundNameField
    {
        get => Require(_boundNameField);
        internal set => SetHandle(ref _boundNameField, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _createFromArray;
    public MethodBuilder CreateFromArray
    {
        get => Require(_createFromArray);
        internal set => SetHandle(ref _createFromArray, value);
    }

    private MethodBuilder? _size;
    public MethodBuilder Size
    {
        get => Require(_size);
        internal set => SetHandle(ref _size, value);
    }

    private MethodBuilder? _add;
    public MethodBuilder Add
    {
        get => Require(_add);
        internal set => SetHandle(ref _add, value);
    }

    private MethodBuilder? _has;
    public MethodBuilder Has
    {
        get => Require(_has);
        internal set => SetHandle(ref _has, value);
    }

    private MethodBuilder? _delete;
    public MethodBuilder Delete
    {
        get => Require(_delete);
        internal set => SetHandle(ref _delete, value);
    }

    private MethodBuilder? _clear;
    public MethodBuilder Clear
    {
        get => Require(_clear);
        internal set => SetHandle(ref _clear, value);
    }

    private MethodBuilder? _keys;
    public MethodBuilder Keys
    {
        get => Require(_keys);
        internal set => SetHandle(ref _keys, value);
    }

    private MethodBuilder? _values;
    public MethodBuilder Values
    {
        get => Require(_values);
        internal set => SetHandle(ref _values, value);
    }

    private MethodBuilder? _entries;
    public MethodBuilder Entries
    {
        get => Require(_entries);
        internal set => SetHandle(ref _entries, value);
    }

    private MethodBuilder? _forEach;
    public MethodBuilder ForEach
    {
        get => Require(_forEach);
        internal set => SetHandle(ref _forEach, value);
    }

    private MethodBuilder? _union;
    public MethodBuilder Union
    {
        get => Require(_union);
        internal set => SetHandle(ref _union, value);
    }

    private MethodBuilder? _intersection;
    public MethodBuilder Intersection
    {
        get => Require(_intersection);
        internal set => SetHandle(ref _intersection, value);
    }

    private MethodBuilder? _difference;
    public MethodBuilder Difference
    {
        get => Require(_difference);
        internal set => SetHandle(ref _difference, value);
    }

    private MethodBuilder? _symmetricDifference;
    public MethodBuilder SymmetricDifference
    {
        get => Require(_symmetricDifference);
        internal set => SetHandle(ref _symmetricDifference, value);
    }

    private MethodBuilder? _isSubsetOf;
    public MethodBuilder IsSubsetOf
    {
        get => Require(_isSubsetOf);
        internal set => SetHandle(ref _isSubsetOf, value);
    }

    private MethodBuilder? _isSupersetOf;
    public MethodBuilder IsSupersetOf
    {
        get => Require(_isSupersetOf);
        internal set => SetHandle(ref _isSupersetOf, value);
    }

    private MethodBuilder? _isDisjointFrom;
    public MethodBuilder IsDisjointFrom
    {
        get => Require(_isDisjointFrom);
        internal set => SetHandle(ref _isDisjointFrom, value);
    }

    private ConstructorBuilder? _iteratorConstructor;
    public ConstructorBuilder IteratorConstructor
    {
        get => Require(_iteratorConstructor);
        internal set => SetHandle(ref _iteratorConstructor, value);
    }

    private MethodBuilder? _getProperty;
    public MethodBuilder GetProperty
    {
        get => Require(_getProperty);
        internal set => SetHandle(ref _getProperty, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Set metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Set metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = BoundMethodType;
        _ = BoundMethodConstructor;
        _ = BoundMethodInvoke;
        _ = BoundReceiverField;
        _ = BoundNameField;
        _ = Create;
        _ = CreateFromArray;
        _ = Size;
        _ = Add;
        _ = Has;
        _ = Delete;
        _ = Clear;
        _ = Keys;
        _ = Values;
        _ = Entries;
        _ = ForEach;
        _ = Union;
        _ = Intersection;
        _ = Difference;
        _ = SymmetricDifference;
        _ = IsSubsetOf;
        _ = IsSupersetOf;
        _ = IsDisjointFrom;
        _ = IteratorConstructor;
        _ = GetProperty;
        IsComplete = true;
    }
}
