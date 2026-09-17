using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Shape-field declarations owned by the generated Program type.</summary>
/// <remarks>
/// Unlike runtime metadata, this registry stays writable through guest method and
/// entry-point emission. Completion freezes declarations, never guest field values.
/// </remarks>
public sealed class EmittedJsonShapeRegistry
{
    private readonly Dictionary<string, FieldBuilder> _fields = new(StringComparer.Ordinal);
    private TypeBuilder? _programType;

    internal EmittedJsonShapeRegistry() => Fields = new ReadOnlyDictionary<string, FieldBuilder>(_fields);

    public IReadOnlyDictionary<string, FieldBuilder> Fields { get; }
    public bool IsComplete { get; private set; }

    internal FieldBuilder GetOrDefine(string fingerprint, TypeBuilder programType, Type objectType)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(programType);
        ArgumentNullException.ThrowIfNull(objectType);
        if (_programType is not null && !ReferenceEquals(_programType, programType))
            throw new InvalidOperationException("JSON shape fields must belong to the same Program type.");
        if (_fields.TryGetValue(fingerprint, out var field))
            return field;
        field = programType.DefineField(
            $"$jsonShape_{_fields.Count}", objectType, FieldAttributes.Assembly | FieldAttributes.Static);
        _fields.Add(fingerprint, field);
        _programType = programType;
        return field;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("JSON shape field emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        IsComplete = true;
    }
}
