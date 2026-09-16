using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required object storage declarations for one compilation.</summary>
/// <remarks>The generated $Object must stay in sync with SharpTS.Runtime.Types.SharpTSObject.</remarks>
public sealed class EmittedObjectStorageRuntime
{
    internal EmittedObjectStorageRuntime() { }

    public bool IsComplete { get; private set; }

    private Type? _type;
    public Type Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private ConstructorBuilder? _constructor;
    public ConstructorBuilder Constructor
    {
        get => Require(_constructor);
        internal set => Set(ref _constructor, value);
    }

    private MethodBuilder? _fieldsGetter;
    public MethodBuilder FieldsGetter
    {
        get => Require(_fieldsGetter);
        internal set => Set(ref _fieldsGetter, value);
    }

    private MethodBuilder? _freeze;
    public MethodBuilder Freeze
    {
        get => Require(_freeze);
        internal set => Set(ref _freeze, value);
    }

    private MethodBuilder? _seal;
    public MethodBuilder Seal
    {
        get => Require(_seal);
        internal set => Set(ref _seal, value);
    }

    private MethodBuilder? _preventExtensions;
    public MethodBuilder PreventExtensions
    {
        get => Require(_preventExtensions);
        internal set => Set(ref _preventExtensions, value);
    }

    private MethodBuilder? _getProperty;
    public MethodBuilder GetProperty
    {
        get => Require(_getProperty);
        internal set => Set(ref _getProperty, value);
    }

    private MethodBuilder? _setProperty;
    public MethodBuilder SetProperty
    {
        get => Require(_setProperty);
        internal set => Set(ref _setProperty, value);
    }

    private MethodBuilder? _setPropertyStrict;
    public MethodBuilder SetPropertyStrict
    {
        get => Require(_setPropertyStrict);
        internal set => Set(ref _setPropertyStrict, value);
    }

    private MethodBuilder? _hasProperty;
    public MethodBuilder HasProperty
    {
        get => Require(_hasProperty);
        internal set => Set(ref _hasProperty, value);
    }

    private MethodBuilder? _deleteProperty;
    public MethodBuilder DeleteProperty
    {
        get => Require(_deleteProperty);
        internal set => Set(ref _deleteProperty, value);
    }

    private MethodBuilder? _deletePropertyStrict;
    public MethodBuilder DeletePropertyStrict
    {
        get => Require(_deletePropertyStrict);
        internal set => Set(ref _deletePropertyStrict, value);
    }

    private MethodBuilder? _defineGetter;
    public MethodBuilder DefineGetter
    {
        get => Require(_defineGetter);
        internal set => Set(ref _defineGetter, value);
    }

    private MethodBuilder? _defineSetter;
    public MethodBuilder DefineSetter
    {
        get => Require(_defineSetter);
        internal set => Set(ref _defineSetter, value);
    }

    private MethodBuilder? _hasGetter;
    public MethodBuilder HasGetter
    {
        get => Require(_hasGetter);
        internal set => Set(ref _hasGetter, value);
    }

    private MethodBuilder? _hasSetter;
    public MethodBuilder HasSetter
    {
        get => Require(_hasSetter);
        internal set => Set(ref _hasSetter, value);
    }

    private MethodBuilder? _getGettersDictionary;
    public MethodBuilder GetGettersDictionary
    {
        get => Require(_getGettersDictionary);
        internal set => Set(ref _getGettersDictionary, value);
    }

    private MethodBuilder? _getSettersDictionary;
    public MethodBuilder GetSettersDictionary
    {
        get => Require(_getSettersDictionary);
        internal set => Set(ref _getSettersDictionary, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Required object storage metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Required object storage metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = Constructor;
        _ = FieldsGetter;
        _ = Freeze;
        _ = Seal;
        _ = PreventExtensions;
        _ = GetProperty;
        _ = SetProperty;
        _ = SetPropertyStrict;
        _ = HasProperty;
        _ = DeleteProperty;
        _ = DeletePropertyStrict;
        _ = DefineGetter;
        _ = DefineSetter;
        _ = HasGetter;
        _ = HasSetter;
        _ = GetGettersDictionary;
        _ = GetSettersDictionary;
        IsComplete = true;
    }
}
