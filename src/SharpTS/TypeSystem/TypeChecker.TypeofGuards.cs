namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private static TypeInfo UnionTypeofProjections(IEnumerable<TypeInfo?> projections)
    {
        var types = projections.OfType<TypeInfo>().Where(type => type is not TypeInfo.Never).ToList();
        return types.Count == 0 ? TypeInfo.Never.Shared : types.Count == 1 ? types[0] : new TypeInfo.Union(types);
    }

    private static bool IsStructuralTypeofDomain(TypeInfo type) => type is
        TypeInfo.Interface or TypeInfo.GenericInterface or TypeInfo.Record or TypeInfo.Object or
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface };

    private bool IsTypeofObjectDomain(TypeInfo type)
    {
        if (type is TypeInfo.Intersection intersection)
            return !intersection.FlattenedTypes.Any(IsPrimitiveType) &&
                !HasTypeofCallableFacet(type) && intersection.FlattenedTypes.Any(IsTypeofObjectDomain);
        return type is TypeInfo.Null or TypeInfo.Array or TypeInfo.Tuple or TypeInfo.Instance or TypeInfo.Object ||
            IsStructuralTypeofDomain(type) && !HasTypeofCallableFacet(type);
    }

    /// <summary>
    /// Inspects existing signatures without adapting them into invocation candidates.
    /// Guard narrowing retains the original interface/record and its declaration owners.
    /// </summary>
    private bool HasTypeofCallableFacet(TypeInfo type)
    {
        if (type is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or
            TypeInfo.GenericOverloadedFunction or TypeInfo.OverloadSet or TypeInfo.Class or
            TypeInfo.GenericClass or TypeInfo.MutableClass) return true;
        if (type is not (TypeInfo.Record or TypeInfo.Interface or TypeInfo.GenericInterface or
            TypeInfo.InstantiatedGeneric or TypeInfo.Intersection)) return false;
        HashSet<TypeInfo> visited = new(ReferenceEqualityComparer.Instance);
        return Visit(type);

        bool Visit(TypeInfo current)
        {
            current = RefreshCompletedInterfaceBinding(current);
            if (!visited.Add(current)) return false;
            return current switch
            {
                TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or
                    TypeInfo.GenericOverloadedFunction or TypeInfo.OverloadSet or TypeInfo.Class or
                    TypeInfo.GenericClass or TypeInfo.MutableClass => true,
                TypeInfo.Record record => record.CallSignatures is { Count: > 0 } ||
                    record.ConstructorSignatures is { Count: > 0 },
                TypeInfo.Interface iface => iface.HasFunctionTypeofFacet || iface.CallSignatures is { Count: > 0 } ||
                    iface.ConstructorSignatures is { Count: > 0 } || iface.Extends?.Any(Visit) == true,
                TypeInfo.GenericInterface generic => generic.CallSignatures is { Count: > 0 } ||
                    generic.ConstructorSignatures is { Count: > 0 } || generic.Extends?.Any(Visit) == true,
                TypeInfo.InstantiatedGeneric generic => Visit(generic.GenericDefinition),
                TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(Visit),
                _ => false
            };
        }
    }
}
