using System.Collections.ObjectModel;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Required record marker and early record-layout registries for one compilation.</summary>
/// <remarks>
/// Registries remain empty when scalar/compact storage is not selected, preserving
/// optional layout lookups. Their read-only views are live during declaration and
/// become fixed at runtime completion. Guest record values remain mutable.
/// </remarks>
public sealed class EmittedRecordStorageRuntime
{
    internal EmittedRecordStorageRuntime()
    {
        ScalarInlineTypes = new ReadOnlyDictionary<int, TypeBuilder>(_scalarInlineTypes);
        ScalarInlineCtors = new ReadOnlyDictionary<int, ConstructorBuilder>(_scalarInlineCtors);
        ScalarInlineGetters = new ReadOnlyDictionary<(int Arity, int Index), MethodBuilder>(_scalarInlineGetters);
        TypedScalarTypes = new ReadOnlyDictionary<string, TypeBuilder>(_typedScalarTypes);
        TypedScalarCtors = new ReadOnlyDictionary<string, ConstructorBuilder>(_typedScalarCtors);
        TypedScalarValueFields = new ReadOnlyDictionary<(string Fingerprint, int Index), FieldBuilder>(_typedScalarValueFields);
        TypedScalarShapeFields = new ReadOnlyDictionary<string, FieldBuilder>(_typedScalarShapeFields);
        CompactTypes = new ReadOnlyDictionary<string, TypeBuilder>(_compactTypes);
        CompactCtors = new ReadOnlyDictionary<string, ConstructorBuilder>(_compactCtors);
        CompactValueFields = new ReadOnlyDictionary<(string Fingerprint, int Index), FieldBuilder>(_compactValueFields);
        CompactAnyMaterializedFields = new ReadOnlyDictionary<string, FieldBuilder>(_compactAnyMaterializedFields);
        CompactIsMaterializedGetters = new ReadOnlyDictionary<string, MethodBuilder>(_compactIsMaterializedGetters);
        CompactTryGetMaterializedDictionary = new ReadOnlyDictionary<string, MethodBuilder>(_compactTryGetMaterializedDictionary);
    }

    public bool IsComplete { get; private set; }

    private Type? _markerInterface;
    public Type MarkerInterface
    {
        get => _markerInterface ?? throw new InvalidOperationException("Record storage metadata 'MarkerInterface' has not been declared.");
        internal set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(value);
            _markerInterface = value;
        }
    }

    public EmittedScalarRecordRuntime? Scalars { get; private set; }

    public EmittedScalarRecordRuntime RequireScalars() => Scalars
        ?? throw new InvalidOperationException("Scalar record storage was not enabled for this compilation.");

    internal void BeginScalarEmission()
    {
        EnsureMutable();
        if (Scalars is not null)
            throw new InvalidOperationException("Scalar record storage emission has already started.");
        Scalars = new EmittedScalarRecordRuntime();
    }

    // Counts belong to the layout declaration, including its last field. Inferring
    // an arity from registered fields alone would miss an omitted final field.
    private readonly Dictionary<string, int> _typedScalarFieldCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _compactFieldCounts = new(StringComparer.Ordinal);

    private readonly Dictionary<int, TypeBuilder> _scalarInlineTypes = [];
    public IReadOnlyDictionary<int, TypeBuilder> ScalarInlineTypes { get; }

    internal void AddScalarInlineTypes(int key, TypeBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        _scalarInlineTypes.Add(key, value);
    }

    private readonly Dictionary<int, ConstructorBuilder> _scalarInlineCtors = [];
    public IReadOnlyDictionary<int, ConstructorBuilder> ScalarInlineCtors { get; }

    internal void AddScalarInlineCtors(int key, ConstructorBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        _scalarInlineCtors.Add(key, value);
    }

    private readonly Dictionary<(int Arity, int Index), MethodBuilder> _scalarInlineGetters = [];
    public IReadOnlyDictionary<(int Arity, int Index), MethodBuilder> ScalarInlineGetters { get; }

    internal void AddScalarInlineGetters((int Arity, int Index) key, MethodBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        _scalarInlineGetters.Add(key, value);
    }

    private readonly Dictionary<string, TypeBuilder> _typedScalarTypes = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, TypeBuilder> TypedScalarTypes { get; }

    internal void AddTypedScalarTypes(string key, TypeBuilder value, int fieldCount)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        if (fieldCount < 1)
            throw new ArgumentOutOfRangeException(nameof(fieldCount));
        if (_typedScalarTypes.ContainsKey(key))
            throw new ArgumentException("The record layout has already been declared.", nameof(key));
        _typedScalarFieldCounts.Add(key, fieldCount);
        _typedScalarTypes.Add(key, value);
    }

    private readonly Dictionary<string, ConstructorBuilder> _typedScalarCtors = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ConstructorBuilder> TypedScalarCtors { get; }

    internal void AddTypedScalarCtors(string key, ConstructorBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _typedScalarCtors.Add(key, value);
    }

    private readonly Dictionary<(string Fingerprint, int Index), FieldBuilder> _typedScalarValueFields = [];
    public IReadOnlyDictionary<(string Fingerprint, int Index), FieldBuilder> TypedScalarValueFields { get; }

    internal void AddTypedScalarValueFields((string Fingerprint, int Index) key, FieldBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        _typedScalarValueFields.Add(key, value);
    }

    private readonly Dictionary<string, FieldBuilder> _typedScalarShapeFields = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FieldBuilder> TypedScalarShapeFields { get; }

    internal void AddTypedScalarShapeFields(string key, FieldBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _typedScalarShapeFields.Add(key, value);
    }

    private readonly Dictionary<string, TypeBuilder> _compactTypes = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, TypeBuilder> CompactTypes { get; }

    internal void AddCompactTypes(string key, TypeBuilder value, int fieldCount)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        if (fieldCount is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(fieldCount));
        if (_compactTypes.ContainsKey(key))
            throw new ArgumentException("The record layout has already been declared.", nameof(key));
        _compactFieldCounts.Add(key, fieldCount);
        _compactTypes.Add(key, value);
    }

    private readonly Dictionary<string, ConstructorBuilder> _compactCtors = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, ConstructorBuilder> CompactCtors { get; }

    internal void AddCompactCtors(string key, ConstructorBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _compactCtors.Add(key, value);
    }

    private readonly Dictionary<(string Fingerprint, int Index), FieldBuilder> _compactValueFields = [];
    public IReadOnlyDictionary<(string Fingerprint, int Index), FieldBuilder> CompactValueFields { get; }

    internal void AddCompactValueFields((string Fingerprint, int Index) key, FieldBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        _compactValueFields.Add(key, value);
    }

    private readonly Dictionary<string, FieldBuilder> _compactAnyMaterializedFields = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FieldBuilder> CompactAnyMaterializedFields { get; }

    internal void AddCompactAnyMaterializedFields(string key, FieldBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _compactAnyMaterializedFields.Add(key, value);
    }

    private readonly Dictionary<string, MethodBuilder> _compactIsMaterializedGetters = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> CompactIsMaterializedGetters { get; }

    internal void AddCompactIsMaterializedGetters(string key, MethodBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _compactIsMaterializedGetters.Add(key, value);
    }

    private readonly Dictionary<string, MethodBuilder> _compactTryGetMaterializedDictionary = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, MethodBuilder> CompactTryGetMaterializedDictionary { get; }

    internal void AddCompactTryGetMaterializedDictionary(string key, MethodBuilder value)
    {
        EnsureRegistryMutable();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(key);
        _compactTryGetMaterializedDictionary.Add(key, value);
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Record storage metadata emission is already complete.");
    }

    private void EnsureRegistryMutable()
    {
        EnsureMutable();
        _ = RequireScalars();
    }

    private static void RequireKeys<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values, IEnumerable<TKey> expected, string name)
        where TKey : notnull
    {
        var keys = expected.ToArray();
        if (values.Count != keys.Length || keys.Any(key => !values.ContainsKey(key)))
            throw new InvalidOperationException($"Record storage registry '{name}' is incomplete or inconsistent.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = MarkerInterface;
        if (Scalars is { } scalars)
        {
            scalars.ValidateDeclarations();
            RequireKeys(ScalarInlineTypes, Enumerable.Range(1, 4), nameof(ScalarInlineTypes));
            RequireKeys(ScalarInlineCtors, Enumerable.Range(1, 4), nameof(ScalarInlineCtors));
            RequireKeys(ScalarInlineGetters, Enumerable.Range(1, 4)
                .SelectMany(arity => Enumerable.Range(0, arity).Select(index => (arity, index))), nameof(ScalarInlineGetters));
            RequireKeys(TypedScalarTypes, _typedScalarFieldCounts.Keys, nameof(TypedScalarTypes));
            RequireKeys(TypedScalarCtors, _typedScalarFieldCounts.Keys, nameof(TypedScalarCtors));
            RequireKeys(TypedScalarShapeFields, _typedScalarFieldCounts.Keys, nameof(TypedScalarShapeFields));
            RequireKeys(TypedScalarValueFields, _typedScalarFieldCounts
                .SelectMany(pair => Enumerable.Range(0, pair.Value).Select(index => (pair.Key, index))), nameof(TypedScalarValueFields));
            RequireKeys(CompactTypes, _compactFieldCounts.Keys, nameof(CompactTypes));
            RequireKeys(CompactCtors, _compactFieldCounts.Keys, nameof(CompactCtors));
            RequireKeys(CompactAnyMaterializedFields, _compactFieldCounts.Keys, nameof(CompactAnyMaterializedFields));
            RequireKeys(CompactIsMaterializedGetters, _compactFieldCounts.Keys, nameof(CompactIsMaterializedGetters));
            RequireKeys(CompactTryGetMaterializedDictionary, _compactFieldCounts.Keys, nameof(CompactTryGetMaterializedDictionary));
            RequireKeys(CompactValueFields, _compactFieldCounts
                .SelectMany(pair => Enumerable.Range(0, pair.Value).Select(index => (pair.Key, index))), nameof(CompactValueFields));
            // Freeze only after all registries and both owners have validated, so
            // an incomplete declaration can be repaired without a half-frozen owner.
            scalars.CompleteEmission();
        }
        IsComplete = true;
    }
}
