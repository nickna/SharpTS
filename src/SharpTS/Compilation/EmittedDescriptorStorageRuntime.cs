using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Required descriptor, prototype and extensibility storage declarations for one compilation.</summary>
public sealed class EmittedDescriptorStorageRuntime
{
    internal EmittedDescriptorStorageRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _getEnumerableExtraKeys;
    /// <summary>$PropertyDescriptorStore.GetEnumerableExtraKeys(obj, dict) — returns enumerable PDS keys NOT already in dict (i.e. accessor-only own properties created via Object.defineProperty). Used by GetKeys/GetValues/GetEntries to surface §10.1.11.1 OrdinaryOwnPropertyKeys-correct results.</summary>
    public MethodBuilder GetEnumerableExtraKeys
    {
        get => Require(_getEnumerableExtraKeys);
        internal set => Set(ref _getEnumerableExtraKeys, value);
    }

    private MethodBuilder? _getAllExtraKeys;
    /// <summary>$PropertyDescriptorStore.GetAllExtraKeys(obj, dict) — like the Enumerable variant but does NOT filter by the Enumerable bit. Used by Object.getOwnPropertyNames (ECMA-262 §20.1.2.10) which returns both enumerable AND non-enumerable own string-keyed properties.</summary>
    public MethodBuilder GetAllExtraKeys
    {
        get => Require(_getAllExtraKeys);
        internal set => Set(ref _getAllExtraKeys, value);
    }

    private Type? _stateType;
    public Type StateType
    {
        get => Require(_stateType);
        internal set => Set(ref _stateType, value);
    }

    private PropertyInfo? _stateIsFrozen;
    public PropertyInfo StateIsFrozen
    {
        get => Require(_stateIsFrozen);
        internal set => Set(ref _stateIsFrozen, value);
    }

    private PropertyInfo? _stateIsSealed;
    public PropertyInfo StateIsSealed
    {
        get => Require(_stateIsSealed);
        internal set => Set(ref _stateIsSealed, value);
    }

    private PropertyInfo? _stateIsExtensible;
    public PropertyInfo StateIsExtensible
    {
        get => Require(_stateIsExtensible);
        internal set => Set(ref _stateIsExtensible, value);
    }

    private Type? _prototypeInfoType;
    public Type PrototypeInfoType
    {
        get => Require(_prototypeInfoType);
        internal set => Set(ref _prototypeInfoType, value);
    }

    private PropertyInfo? _prototypeValue;
    public PropertyInfo PrototypeValue
    {
        get => Require(_prototypeValue);
        internal set => Set(ref _prototypeValue, value);
    }

    private Type? _descriptorType;
    public Type DescriptorType
    {
        get => Require(_descriptorType);
        internal set => Set(ref _descriptorType, value);
    }

    private ConstructorInfo? _descriptorConstructor;
    public ConstructorInfo DescriptorConstructor
    {
        get => Require(_descriptorConstructor);
        internal set => Set(ref _descriptorConstructor, value);
    }

    private PropertyInfo? _descriptorValue;
    public PropertyInfo DescriptorValue
    {
        get => Require(_descriptorValue);
        internal set => Set(ref _descriptorValue, value);
    }

    private PropertyInfo? _descriptorGetter;
    public PropertyInfo DescriptorGetter
    {
        get => Require(_descriptorGetter);
        internal set => Set(ref _descriptorGetter, value);
    }

    private PropertyInfo? _descriptorSetter;
    public PropertyInfo DescriptorSetter
    {
        get => Require(_descriptorSetter);
        internal set => Set(ref _descriptorSetter, value);
    }

    private PropertyInfo? _descriptorWritable;
    public PropertyInfo DescriptorWritable
    {
        get => Require(_descriptorWritable);
        internal set => Set(ref _descriptorWritable, value);
    }

    private PropertyInfo? _descriptorEnumerable;
    public PropertyInfo DescriptorEnumerable
    {
        get => Require(_descriptorEnumerable);
        internal set => Set(ref _descriptorEnumerable, value);
    }

    private PropertyInfo? _descriptorConfigurable;
    public PropertyInfo DescriptorConfigurable
    {
        get => Require(_descriptorConfigurable);
        internal set => Set(ref _descriptorConfigurable, value);
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

    private MethodBuilder? _isExtensible;
    public MethodBuilder IsExtensible
    {
        get => Require(_isExtensible);
        internal set => Set(ref _isExtensible, value);
    }

    private MethodBuilder? _isFrozen;
    public MethodBuilder IsFrozen
    {
        get => Require(_isFrozen);
        internal set => Set(ref _isFrozen, value);
    }

    private MethodBuilder? _isSealed;
    public MethodBuilder IsSealed
    {
        get => Require(_isSealed);
        internal set => Set(ref _isSealed, value);
    }

    private MethodBuilder? _canAddProperty;
    public MethodBuilder CanAddProperty
    {
        get => Require(_canAddProperty);
        internal set => Set(ref _canAddProperty, value);
    }

    private MethodBuilder? _tryGetGetter;
    public MethodBuilder TryGetGetter
    {
        get => Require(_tryGetGetter);
        internal set => Set(ref _tryGetGetter, value);
    }

    private MethodBuilder? _tryGetSetter;
    public MethodBuilder TryGetSetter
    {
        get => Require(_tryGetSetter);
        internal set => Set(ref _tryGetSetter, value);
    }

    private MethodBuilder? _isWritable;
    public MethodBuilder IsWritable
    {
        get => Require(_isWritable);
        internal set => Set(ref _isWritable, value);
    }

    private MethodBuilder? _setPrototype;
    public MethodBuilder SetPrototype
    {
        get => Require(_setPrototype);
        internal set => Set(ref _setPrototype, value);
    }

    private MethodBuilder? _getPrototype;
    public MethodBuilder GetPrototype
    {
        get => Require(_getPrototype);
        internal set => Set(ref _getPrototype, value);
    }

    private MethodBuilder? _hasPrototypeEntry;
    public MethodBuilder HasPrototypeEntry
    {
        get => Require(_hasPrototypeEntry);
        internal set => Set(ref _hasPrototypeEntry, value);
    }

    private MethodBuilder? _defineProperty;
    public MethodBuilder DefineProperty
    {
        get => Require(_defineProperty);
        internal set => Set(ref _defineProperty, value);
    }

    private MethodBuilder? _deleteProperty;
    public MethodBuilder DeleteProperty
    {
        get => Require(_deleteProperty);
        internal set => Set(ref _deleteProperty, value);
    }

    private MethodBuilder? _getPropertyDescriptor;
    public MethodBuilder GetPropertyDescriptor
    {
        get => Require(_getPropertyDescriptor);
        internal set => Set(ref _getPropertyDescriptor, value);
    }

    private MethodBuilder? _hasPropertyDescriptors;
    public MethodBuilder HasPropertyDescriptors
    {
        get => Require(_hasPropertyDescriptors);
        internal set => Set(ref _hasPropertyDescriptors, value);
    }

    private MethodBuilder? _hasIndexedOwnProperty;
    public MethodBuilder HasIndexedOwnProperty
    {
        get => Require(_hasIndexedOwnProperty);
        internal set => Set(ref _hasIndexedOwnProperty, value);
    }

    private MethodBuilder? _getStaticShadow;
    public MethodBuilder GetStaticShadow
    {
        get => Require(_getStaticShadow);
        internal set => Set(ref _getStaticShadow, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Required descriptor storage metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Required descriptor storage metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = GetEnumerableExtraKeys;
        _ = GetAllExtraKeys;
        _ = StateType;
        _ = StateIsFrozen;
        _ = StateIsSealed;
        _ = StateIsExtensible;
        _ = PrototypeInfoType;
        _ = PrototypeValue;
        _ = DescriptorType;
        _ = DescriptorConstructor;
        _ = DescriptorValue;
        _ = DescriptorGetter;
        _ = DescriptorSetter;
        _ = DescriptorWritable;
        _ = DescriptorEnumerable;
        _ = DescriptorConfigurable;
        _ = Freeze;
        _ = Seal;
        _ = PreventExtensions;
        _ = IsExtensible;
        _ = IsFrozen;
        _ = IsSealed;
        _ = CanAddProperty;
        _ = TryGetGetter;
        _ = TryGetSetter;
        _ = IsWritable;
        _ = SetPrototype;
        _ = GetPrototype;
        _ = HasPrototypeEntry;
        _ = DefineProperty;
        _ = DeleteProperty;
        _ = GetPropertyDescriptor;
        _ = HasPropertyDescriptors;
        _ = HasIndexedOwnProperty;
        _ = GetStaticShadow;
        IsComplete = true;
    }
}
