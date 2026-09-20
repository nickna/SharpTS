using System.Collections.ObjectModel;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Program-wide cache of generated value-type "shape" structs for promoted object-literal locals (#862).
/// Mirrors the role of <c>DisplayClasses</c>: defined once after analysis, shared across every emit
/// context, and finalized (<c>CreateType()</c>) at the end of compilation. Keyed two ways — by the
/// shape's <see cref="TypeSystem.ObjectShapeInfo.CanonicalKey"/> (so the declaration site resolves the
/// generated type from the <see cref="TypeMap"/> mark) and by the generated CLR <see cref="System.Type"/>
/// (so the property get/set fast paths recognise a promoted local purely from its slot type — the same
/// CLR-type-gated, shadow-safe resolution used by promoted typed-array locals).
/// </summary>
public sealed class ObjectShapeRegistry
{
    private readonly Dictionary<string, ObjectShapeTypeInfo> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ObjectShapeTypeInfo> _byClrType = new();
    private HashSet<string>? _selectedKeys;

    public ObjectShapeRegistry()
    {
        ByKey = new ReadOnlyDictionary<string, ObjectShapeTypeInfo>(_byKey);
        ByClrType = new ReadOnlyDictionary<Type, ObjectShapeTypeInfo>(_byClrType);
    }

    /// <summary>Canonical shape key → generated shape type info.</summary>
    public IReadOnlyDictionary<string, ObjectShapeTypeInfo> ByKey { get; }

    /// <summary>Generated CLR type (the <see cref="TypeBuilder"/>) → shape type info, for use-site lookup.</summary>
    public IReadOnlyDictionary<Type, ObjectShapeTypeInfo> ByClrType { get; }

    public bool IsComplete { get; private set; }

    internal void BeginDeclarations(IEnumerable<string> canonicalKeys)
    {
        if (_selectedKeys is not null)
            throw new InvalidOperationException("Object shape declarations have already begun.");
        ArgumentNullException.ThrowIfNull(canonicalKeys);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in canonicalKeys)
        {
            ArgumentNullException.ThrowIfNull(key);
            keys.Add(key);
        }
        _selectedKeys = keys;
    }

    internal void Declare(string canonicalKey, ObjectShapeTypeInfo info)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(canonicalKey);
        ArgumentNullException.ThrowIfNull(info);
        if (!_selectedKeys!.Contains(canonicalKey))
            throw new InvalidOperationException("The object shape was not selected for this compilation.");
        if (_byKey.ContainsKey(canonicalKey) || _byClrType.ContainsKey(info.ClrType))
            throw new InvalidOperationException("The object shape key or generated type is already declared.");
        _byKey.Add(canonicalKey, info);
        _byClrType.Add(info.ClrType, info);
    }

    internal void CompleteDeclarations()
    {
        EnsureMutable();
        if (_byKey.Count != _selectedKeys!.Count)
            throw new InvalidOperationException("Not all selected object shapes have been declared.");
        IsComplete = true;
    }

    private void EnsureMutable()
    {
        if (_selectedKeys is null || IsComplete)
            throw new InvalidOperationException("Object shape declarations are not open.");
    }
}

/// <summary>
/// One generated shape struct: its CLR type, the ordered fields (name + primitive kind), and the
/// <see cref="FieldBuilder"/> for each field name (used to emit <c>ldfld</c>/<c>stfld</c>).
/// </summary>
public sealed class ObjectShapeTypeInfo
{
    internal ObjectShapeTypeInfo(TypeBuilder clrType,
        IEnumerable<(string Name, TokenType Kind)> fields,
        IDictionary<string, FieldBuilder> fieldBuilders, FieldBuilder keyMetadataField)
    {
        ArgumentNullException.ThrowIfNull(clrType);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(fieldBuilders);
        ArgumentNullException.ThrowIfNull(keyMetadataField);
        var ordered = fields.ToArray();
        var builders = new Dictionary<string, FieldBuilder>(fieldBuilders, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, kind) in ordered)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (!names.Add(name) || !builders.TryGetValue(name, out var field)
                || field is null || field.DeclaringType != clrType || field.IsStatic
                || kind is not (TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN or TokenType.TYPE_STRING))
                throw new InvalidOperationException("Object shape fields must be unique declarations on their generated type.");
        }
        if (builders.Count != ordered.Length || keyMetadataField.DeclaringType != clrType || !keyMetadataField.IsStatic)
            throw new InvalidOperationException("Object shape metadata does not match its generated type.");
        ClrType = clrType;
        Fields = Array.AsReadOnly(ordered);
        FieldBuilders = new ReadOnlyDictionary<string, FieldBuilder>(builders);
        KeyMetadataField = keyMetadataField;
    }

    /// <summary>The generated value type (a <see cref="TypeBuilder"/> during emit; finalized later).</summary>
    public Type ClrType { get; }

    /// <summary>Ordered fields (name + kind), matching the literal's property order.</summary>
    public IReadOnlyList<(string Name, TokenType Kind)> Fields { get; }

    /// <summary>Field name → its <see cref="FieldBuilder"/> on <see cref="ClrType"/>.</summary>
    public IReadOnlyDictionary<string, FieldBuilder> FieldBuilders { get; }

    /// <summary>
    /// Cached ordered enumerable string keys for direct <c>Object.keys</c> materialization.
    /// The emitted array is internal to the generated assembly and copied into every mutable result.
    /// </summary>
    public FieldBuilder KeyMetadataField { get; }
}
