using System.Collections.Frozen;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public sealed partial class RuntimeFeatureDetector
{
    private readonly HashSet<HashSet<string>> _structuralMaterializationViews =
        new(HashSet<string>.CreateSetComparer());

    private static bool TryProjectMaterializationInterface(TypeInfo.InstantiatedGeneric instantiated,
        out TypeInfo projected)
    {
        bool complete = true;
        projected = Project(instantiated, new Dictionary<string, TypeInfo>(StringComparer.Ordinal), 0);
        return complete;

        TypeInfo Project(TypeInfo type, IReadOnlyDictionary<string, TypeInfo> substitutions, int depth)
        {
            // Recursive generic surfaces need an open-world fallback rather than a partial
            // required-key proof. This projection is only for materialization analysis.
            if (!complete || depth >= 32)
            {
                complete = false;
                return type;
            }

            TypeInfo Sub(TypeInfo member) => Project(member, substitutions, depth + 1);
            switch (type)
            {
                case TypeInfo.TypeParameter parameter:
                    if (substitutions.TryGetValue(parameter.Name, out var argument) &&
                        !ReferenceEquals(argument, parameter))
                        return argument;
                    return parameter.Constraint is { } constraint ? Sub(constraint) : parameter;
                case TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface generic } instance:
                {
                    if (instance.TypeArguments.Count < generic.TypeParams.Count)
                    {
                        complete = false;
                        return type;
                    }
                    var scoped = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                    for (int i = 0; i < generic.TypeParams.Count; i++)
                        scoped[generic.TypeParams[i].Name] = Sub(instance.TypeArguments[i]);
                    TypeInfo Member(TypeInfo member) => Project(member, scoped, depth + 1);
                    return new TypeInfo.Interface(generic.Name,
                        generic.Members.ToFrozenDictionary(pair => pair.Key, pair => Member(pair.Value)),
                        generic.OptionalMembers,
                        generic.StringIndexType is { } strings ? Member(strings) : null,
                        generic.NumberIndexType is { } numbers ? Member(numbers) : null,
                        generic.SymbolIndexType is { } symbols ? Member(symbols) : null,
                        generic.Extends?.Select(parent => (TypeInfo.Interface)Member(parent))
                            .ToFrozenSet<TypeInfo.Interface>(ReferenceEqualityComparer.Instance));
                }
                case TypeInfo.Record record:
                    return record with
                    {
                        Fields = record.Fields.ToFrozenDictionary(pair => pair.Key, pair => Sub(pair.Value)),
                        StringIndexType = record.StringIndexType is { } recordStrings ? Sub(recordStrings) : null,
                        NumberIndexType = record.NumberIndexType is { } recordNumbers ? Sub(recordNumbers) : null,
                        SymbolIndexType = record.SymbolIndexType is { } recordSymbols ? Sub(recordSymbols) : null
                    };
                case TypeInfo.Interface iface:
                    return iface with
                    {
                        Members = iface.Members.ToFrozenDictionary(pair => pair.Key, pair => Sub(pair.Value)),
                        StringIndexType = iface.StringIndexType is { } interfaceStrings ? Sub(interfaceStrings) : null,
                        NumberIndexType = iface.NumberIndexType is { } interfaceNumbers ? Sub(interfaceNumbers) : null,
                        SymbolIndexType = iface.SymbolIndexType is { } interfaceSymbols ? Sub(interfaceSymbols) : null,
                        Extends = iface.Extends?.Select(parent => (TypeInfo.Interface)Sub(parent))
                            .ToFrozenSet<TypeInfo.Interface>(ReferenceEqualityComparer.Instance)
                    };
                case TypeInfo.Array array:
                    return array with { ElementType = Sub(array.ElementType) };
                case TypeInfo.Tuple tuple:
                    return tuple with
                    {
                        Elements = tuple.Elements.Select(element => element with { Type = Sub(element.Type) }).ToList(),
                        RestElementType = tuple.RestElementType is { } rest ? Sub(rest) : null
                    };
                case TypeInfo.Union union:
                    return new TypeInfo.Union(union.Types.Select(Sub).ToList());
                case TypeInfo.Intersection intersection:
                    return new TypeInfo.Intersection(intersection.Types.Select(Sub).ToList());
                case TypeInfo.IndexedAccess or TypeInfo.ConditionalType or TypeInfo.MappedType or
                    TypeInfo.RecursiveTypeAlias or TypeInfo.SpreadType or TypeInfo.GenericInterface:
                    complete = false;
                    return type;
                default:
                    return type;
            }
        }
    }

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
            case TypeInfo.Instance { ResolvedClassType: TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericClass generic } }:
                // Required property names belong to the original class core and
                // do not depend on its substituted value types.
                CollectRequired(new TypeInfo.Instance(new TypeInfo.Class(generic.Core)), names, visited, depth + 1);
                break;
            case TypeInfo.Instance { ResolvedClassType: TypeInfo.Class cls }:
                foreach (var name in cls.FieldTypes.Keys.Concat(cls.Getters.Keys).Concat(cls.Setters.Keys))
                    if (cls.Core.OrdinaryOptionalMembers?.Contains((false, name)) != true && !IsPrototypeMember(name))
                        names.Add(name);
                if (cls.Superclass is not null)
                    CollectRequired(new TypeInfo.Instance(cls.Superclass), names, visited, depth + 1);
                break;
            case TypeInfo.Instance { ResolvedClassType: TypeInfo.MutableClass cls }:
                foreach (var name in cls.FieldTypes.Keys.Concat(cls.Getters.Keys).Concat(cls.Setters.Keys))
                    if (!cls.OrdinaryOptionalMembers.Contains((false, name)) && !IsPrototypeMember(name))
                        names.Add(name);
                if (cls.Superclass is not null)
                    CollectRequired(new TypeInfo.Instance(cls.Superclass), names, visited, depth + 1);
                break;
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
