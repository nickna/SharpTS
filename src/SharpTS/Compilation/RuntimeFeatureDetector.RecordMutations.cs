using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public sealed partial class RuntimeFeatureDetector
{
    private readonly HashSet<HashSet<string>> _structuralMaterializationViews =
        new(HashSet<string>.CreateSetComparer());

    private void RecordStructuralMaterializationView(TypeInfo type)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        CollectRequired(type, required, new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance), 0);
        // An optional/index-only or opaque view can refer to any emitted carrier.
        if (required.Count == 0)
            _set.PotentiallyMaterializesUnknownCompactObjectRecordShape = true;
        else
            _structuralMaterializationViews.Add(required);
    }

    private void ExpandStructuralMaterializationShapes()
    {
        if (_set.PotentiallyMaterializesUnknownCompactObjectRecordShape) return;
        // A structural view does not identify a carrier's full layout or field order.
        // Finish after the walk so writes also protect literals encountered later.
        foreach (var (fingerprint, shape) in _set.CompactObjectRecordShapes)
            if (_structuralMaterializationViews.Any(required =>
                    required.All(name => shape.Fields.Any(field => field.Key == name))))
                _set.PotentiallyMaterializedCompactObjectRecordShapes.Add(fingerprint);
    }

    private static bool ChangesStructuralView(TypeInfo? source, TypeInfo? target)
    {
        if (source is null || target is null || ReferenceEquals(source, target)) return false;
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        var targetNames = new HashSet<string>(StringComparer.Ordinal);
        CollectRequired(source, sourceNames, new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance), 0);
        CollectRequired(target, targetNames, new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance), 0);
        // Assertions can add requirements that the actual object does not satisfy.
        // Retain its source view so those assertions cannot hide a carrier mutation.
        return !sourceNames.SetEquals(targetNames);
    }

    private static void CollectRequired(TypeInfo type, HashSet<string> names,
        HashSet<TypeInfo> visited, int depth)
    {
        if (depth >= 32 || !visited.Add(type)) return;
        switch (type)
        {
            case TypeInfo.Record record:
                foreach (var name in record.Fields.Keys)
                    if (!record.IsFieldOptional(name) && !IsPrototypeMember(name)) names.Add(name);
                break;
            case TypeInfo.Interface itf:
                foreach (var name in itf.Members.Keys)
                    if (!itf.OptionalMembers.Contains(name) && !IsPrototypeMember(name)) names.Add(name);
                if (itf.Extends is not null)
                    foreach (var parent in itf.Extends) CollectRequired(parent, names, visited, depth + 1);
                break;
            case TypeInfo.Intersection intersection:
                foreach (var part in intersection.FlattenedTypes) CollectRequired(part, names, visited, depth + 1);
                break;
            case TypeInfo.TypeParameter { Constraint: { } constraint }:
                CollectRequired(constraint, names, visited, depth + 1);
                break;
        }
    }

    // These requirements may be satisfied by Object.prototype rather than own slots.
    private static bool IsPrototypeMember(string name) => name is "constructor" or "__proto__"
        or "toString" or "toLocaleString" or "valueOf" or "hasOwnProperty" or "isPrototypeOf"
        or "propertyIsEnumerable" or "__defineGetter__" or "__defineSetter__"
        or "__lookupGetter__" or "__lookupSetter__";
}
