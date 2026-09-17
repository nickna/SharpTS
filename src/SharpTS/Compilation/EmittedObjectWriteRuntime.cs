using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required named, computed, field and strict writing metadata for one compilation.</summary>
public sealed class EmittedObjectWriteRuntime
{
    internal EmittedObjectWriteRuntime() { }
    public bool IsComplete { get; private set; }
    private bool _propertyBodyEmitted;

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

    private MethodBuilder? _fieldsProperty;
    public MethodBuilder FieldsProperty
    {
        get => Require(_fieldsProperty);
        internal set => SetHandle(ref _fieldsProperty, value);
    }

    private MethodBuilder? _fieldsPropertyStrict;
    public MethodBuilder FieldsPropertyStrict
    {
        get => Require(_fieldsPropertyStrict);
        internal set => SetHandle(ref _fieldsPropertyStrict, value);
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

    internal void MarkPropertyBodyEmitted()
    {
        EnsureMutable();
        _ = Property;
        if (_propertyBodyEmitted)
            throw new InvalidOperationException("Object write Property body emission is already complete.");
        _propertyBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("Object write metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("Object write metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Object write metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Property;
        _ = PropertyStrict;
        _ = FieldsProperty;
        _ = FieldsPropertyStrict;
        _ = Index;
        _ = IndexStrict;
        if (!_propertyBodyEmitted)
            throw new InvalidOperationException("Object write Property body has not been emitted.");
        IsComplete = true;
    }
}
