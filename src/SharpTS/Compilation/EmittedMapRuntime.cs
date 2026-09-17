using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Map operation and bound-method declarations for one compilation.</summary>
public sealed class EmittedMapRuntime
{
    internal EmittedMapRuntime() { }

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

    private MethodBuilder? _normalizeKey;
    public MethodBuilder NormalizeKey
    {
        get => Require(_normalizeKey);
        internal set => SetHandle(ref _normalizeKey, value);
    }

    private MethodBuilder? _denormalizeKey;
    public MethodBuilder DenormalizeKey
    {
        get => Require(_denormalizeKey);
        internal set => SetHandle(ref _denormalizeKey, value);
    }

    private MethodBuilder? _create;
    public MethodBuilder Create
    {
        get => Require(_create);
        internal set => SetHandle(ref _create, value);
    }

    private MethodBuilder? _createFromEntries;
    public MethodBuilder CreateFromEntries
    {
        get => Require(_createFromEntries);
        internal set => SetHandle(ref _createFromEntries, value);
    }

    private MethodBuilder? _size;
    public MethodBuilder Size
    {
        get => Require(_size);
        internal set => SetHandle(ref _size, value);
    }

    private MethodBuilder? _get;
    public MethodBuilder Get
    {
        get => Require(_get);
        internal set => SetHandle(ref _get, value);
    }

    private MethodBuilder? _set;
    public MethodBuilder Set
    {
        get => Require(_set);
        internal set => SetHandle(ref _set, value);
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

    private MethodBuilder? _groupBy;
    public MethodBuilder GroupBy
    {
        get => Require(_groupBy);
        internal set => SetHandle(ref _groupBy, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Map metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Map metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = BoundMethodType;
        _ = BoundMethodConstructor;
        _ = BoundMethodInvoke;
        _ = BoundReceiverField;
        _ = BoundNameField;
        _ = NormalizeKey;
        _ = DenormalizeKey;
        _ = Create;
        _ = CreateFromEntries;
        _ = Size;
        _ = Get;
        _ = Set;
        _ = Has;
        _ = Delete;
        _ = Clear;
        _ = Keys;
        _ = Values;
        _ = Entries;
        _ = ForEach;
        _ = IteratorConstructor;
        _ = GetProperty;
        _ = GroupBy;
        IsComplete = true;
    }
}
