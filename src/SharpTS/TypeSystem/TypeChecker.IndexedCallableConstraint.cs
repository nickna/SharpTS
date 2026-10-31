namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    // A generic indexed expression retains T[K] as its checked source fact.
    // Its constraint supplies a callable signature only for checking invocation.
    private TypeInfo IndexedCallableConstraint(TypeInfo type)
    {
        if (type is not TypeInfo.IndexedAccess indexed) return type;
        TypeInfo owner = indexed.ObjectType;
        HashSet<TypeInfo> visited = new(ReferenceEqualityComparer.Instance);
        while (owner is TypeInfo.TypeParameter { Constraint: { } constraint } && visited.Add(owner))
            owner = constraint;
        if (ReferenceEquals(owner, indexed.ObjectType)) return type;
        var resolved = ResolveIndexedAccess(new TypeInfo.IndexedAccess(owner, indexed.IndexType), []);
        // An explicit any member in the constraint permits a call without
        // changing the expression's retained T[K] source fact. A non-callable
        // concrete constraint still reaches the ordinary call diagnostic.
        return resolved is TypeInfo.IndexedAccess ? type : resolved;
    }
}
