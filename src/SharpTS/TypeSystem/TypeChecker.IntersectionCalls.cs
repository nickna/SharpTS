namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private List<TypeInfo.CallSignature> CallableObjectSignatures(TypeInfo type)
    {
        List<TypeInfo.CallSignature> signatures = [];
        HashSet<TypeInfo> visited = new(ReferenceEqualityComparer.Instance);
        HashSet<TypeInfo.CallSignature> seen = new(ReferenceEqualityComparer.Instance);
        Collect(type);
        return signatures;

        void Collect(TypeInfo current)
        {
            current = RefreshCompletedInterfaceBinding(current);
            if (!visited.Add(current)) return;
            var own = current switch
            {
                TypeInfo.Record record => record.CallSignatures,
                TypeInfo.Interface iface => iface.CallSignatures,
                TypeInfo.GenericInterface generic => generic.CallSignatures,
                _ => null
            };
            foreach (var signature in own ?? [])
                if (seen.Add(signature)) signatures.Add(signature);
            var parents = current switch
            {
                TypeInfo.Interface iface => iface.Extends,
                TypeInfo.GenericInterface generic => generic.Extends,
                _ => null
            };
            foreach (var parent in parents ?? []) Collect(parent);
        }
    }

    private TypeInfo IntersectionCallTarget(TypeInfo type, out IReadOnlyDictionary<TypeInfo, TypeInfo>? originals)
    {
        Dictionary<TypeInfo, TypeInfo> adapted = new(ReferenceEqualityComparer.Instance);
        TypeInfo result = Normalize(type, 0);
        originals = adapted.Count == 0 ? null : adapted;
        return result;

        TypeInfo Normalize(TypeInfo current, int depth)
        {
            if (depth >= 32) return current;
            if (current is TypeInfo.Intersection intersection)
            {
                // Additional value/property constraints do not remove a constituent's call
                // signatures. Keep the original signature objects for overload/editor identity.
                var facets = intersection.FlattenedTypes.Where(part => part is TypeInfo.Function
                    or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or TypeInfo.GenericOverloadedFunction
                    or TypeInfo.OverloadSet or TypeInfo.Record { IsCallable: true } ||
                    part is TypeInfo.Interface iface && CallableObjectSignatures(iface).Count > 0 ||
                    part is TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface generic } &&
                        CallableObjectSignatures(generic).Count > 0).ToList();
                if (facets.Count == 1 && facets[0] is not TypeInfo.InstantiatedGeneric) return facets[0];
                if (facets.Count > 0 && facets.All(part => part is not TypeInfo.GenericOverloadedFunction))
                {
                    List<TypeInfo> signatures = [];
                    foreach (var facet in facets)
                    {
                        if (TryNormalizeCallableObject(facet, out var callable, out var sourceSignatures))
                        {
                            foreach (var (signature, original) in sourceSignatures!) adapted[signature] = original;
                            AddCallableSignature(signatures, callable);
                        }
                        else AddCallableSignature(signatures, facet);
                    }
                    return signatures.Any(adapted.ContainsKey)
                        ? signatures.Count == 1 ? signatures[0] : new TypeInfo.OverloadSet(signatures)
                        : CreateInterfaceOverload(signatures);
                }
            }
            else if (current is TypeInfo.Union union)
            {
                var branches = union.FlattenedTypes.Select(part => Normalize(part, depth + 1)).ToList();
                // A callable union must still accept each argument in every live branch.
                // Do not turn union alternatives into overloads or discard noncallable arms.
                if (branches.All(part => part is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction
                        or TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Never) &&
                    branches.Where((part, index) => !ReferenceEquals(part, union.FlattenedTypes[index])).Any())
                    return branches.Aggregate(CreateUnion);
            }
            return current;
        }
    }

    private static TypeInfo IntersectionCallCandidateSurface(TypeInfo type, IReadOnlyDictionary<TypeInfo, TypeInfo>? originals)
    {
        if (originals is null) return type;
        if (originals.TryGetValue(type, out var original)) return original;
        return type switch
        {
            TypeInfo.OverloadedFunction overloaded => new TypeInfo.OverloadSet(overloaded.Signatures
                .Select(signature => IntersectionCallCandidateSurface(signature, originals)).ToList()),
            TypeInfo.OverloadSet overloaded => new TypeInfo.OverloadSet(overloaded.Signatures
                .Select(signature => IntersectionCallCandidateSurface(signature, originals)).ToList()),
            TypeInfo.Union union => new TypeInfo.Union(union.FlattenedTypes
                .Select(part => IntersectionCallCandidateSurface(part, originals)).ToList()),
            _ => type
        };
    }
}
