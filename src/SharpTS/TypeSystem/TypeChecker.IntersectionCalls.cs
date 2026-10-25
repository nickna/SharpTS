namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private static TypeInfo IntersectionCallTarget(TypeInfo type, int depth = 0)
    {
        if (depth >= 32) return type;
        if (type is TypeInfo.Intersection intersection)
        {
            // Additional value/property constraints do not remove a constituent's call
            // signatures. Keep the original signature objects for overload/editor identity.
            var facets = intersection.FlattenedTypes.Where(part => part is TypeInfo.Function
                or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or TypeInfo.GenericOverloadedFunction
                or TypeInfo.OverloadSet).ToList();
            if (facets.Count == 1) return facets[0];
            if (facets.Count > 1 && facets.All(part => part is not TypeInfo.GenericOverloadedFunction))
            {
                List<TypeInfo> signatures = [];
                foreach (var facet in facets) AddCallableSignature(signatures, facet);
                return CreateInterfaceOverload(signatures);
            }
        }
        else if (type is TypeInfo.Union union)
        {
            var branches = union.FlattenedTypes.Select(part => IntersectionCallTarget(part, depth + 1)).ToList();
            // A callable union must still accept each argument in every live branch.
            // Do not turn union alternatives into overloads or discard noncallable arms.
            if (branches.All(part => part is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction
                    or TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Never) &&
                branches.Where((part, index) => !ReferenceEquals(part, union.FlattenedTypes[index])).Any())
                return branches.Aggregate(CreateUnion);
        }
        return type;
    }
}
