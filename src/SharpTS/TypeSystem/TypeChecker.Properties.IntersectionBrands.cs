using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record IntersectionWriteBrand(int DeclarationId = 0, ClassMetadataCore? Core = null)
    {
        public bool IsPrivate => DeclarationId != 0 || Core is not null;
    }

    private sealed record IntersectionWriteBrandShape(
        Dictionary<string, List<IntersectionWriteBrand>> Members, bool Impossible = false);

    private static bool InterfaceHasNominalIntersectionMembers(TypeInfo.Interface type)
    {
        Stack<TypeInfo.Interface> pending = new();
        HashSet<TypeInfo.Interface> visited = new(ReferenceEqualityComparer.Instance);
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (current.ClassOrigin is not null || current.MemberBrands?.Values.Any(brand => brand.Access != AccessModifier.Public) == true)
                return true;
            foreach (var parent in current.Extends ?? []) pending.Push(parent);
        }
        return false;
    }

    private bool HasImpossibleIntersectionWriteBrands(TypeInfo type)
    {
        HashSet<TypeInfo> active = new(ReferenceEqualityComparer.Instance);
        return Shape(type).Impossible;

        IntersectionWriteBrandShape Shape(TypeInfo current)
        {
            ThrowIfCancellationRequested();
            if (!active.Add(current)) return new([]);
            TypeInfo original = current;
            try
            {
                current = RefreshCompletedInterfaceBinding(current);
                if (current is TypeInfo.TypeParameter parameter)
                    return parameter.Constraint is { } constraint ? Shape(constraint) : new([]);
                if (current is TypeInfo.RecursiveTypeAlias alias) return Shape(ExpandRecursiveTypeAlias(alias));
                if (current is TypeInfo.MappedType) return Shape(ResolveMappedTypeForAccess(current));
                if (current is TypeInfo.InstantiatedGeneric generic && FlattenInstantiatedInterface(generic) is { } flattened)
                    return Shape(flattened);
                if (current is TypeInfo.Never or TypeInfo.Null or TypeInfo.Undefined) return new([], true);
                if (current is TypeInfo.Intersection intersection)
                {
                    IntersectionWriteBrandShape result = new([]);
                    foreach (var part in intersection.FlattenedTypes)
                    {
                        var next = Shape(part);
                        if (next.Impossible) return next;
                        foreach (var (name, brands) in next.Members)
                        {
                            if (!result.Members.TryGetValue(name, out var previous)) result.Members[name] = brands;
                            else
                            {
                                var common = previous.Where(brand => brands.Any(other => SameBrand(brand, other))).ToList();
                                if (common.Count == 0) return new([], true);
                                result.Members[name] = common;
                            }
                        }
                    }
                    foreach (var part in intersection.FlattenedTypes)
                        if (UnionArmsConflict(part, result, new(ReferenceEqualityComparer.Instance))) return new([], true);
                    return result;
                }
                if (current is TypeInfo.Union union)
                {
                    var branches = union.FlattenedTypes.Select(Shape).Where(shape => !shape.Impossible).ToList();
                    if (branches.Count == 0) return new([], true);
                    Dictionary<string, List<IntersectionWriteBrand>> members = [];
                    // Only a declaration present in every live union branch is guaranteed.
                    // Keeping per-name alternatives avoids an exponential branch product and
                    // cannot reject a union with a viable, non-conflicting constituent.
                    foreach (string name in branches[0].Members.Keys.Where(name => branches.All(branch => branch.Members.ContainsKey(name))))
                    {
                        List<IntersectionWriteBrand> brands = [];
                        foreach (var brand in branches.SelectMany(branch => branch.Members[name]))
                            if (!brands.Any(previous => SameBrand(previous, brand))) brands.Add(brand);
                        members[name] = brands;
                    }
                    return new(members);
                }
                Dictionary<string, List<IntersectionWriteBrand>> declarations = [];
                if (current is TypeInfo.Record record)
                    foreach (string name in record.Fields.Keys) declarations[name] = [new()];
                else if (current is TypeInfo.Interface iface)
                {
                    if (iface.ClassOrigin is { } origin) return Shape(new TypeInfo.Instance(origin));
                    foreach (string name in iface.GetAllMembers().Select(member => member.Key).Distinct(StringComparer.Ordinal))
                    {
                        TryGetInterfaceMemberBrand(iface, name, out var access, out int owner);
                        // A source-less private declaration provides no identity proof.
                        if (access != AccessModifier.Private || owner != 0)
                            declarations[name] = [access == AccessModifier.Private ? new(owner) : new()];
                    }
                    foreach (var parent in iface.Extends ?? [])
                    {
                        var inherited = Shape(parent);
                        if (inherited.Impossible) return inherited;
                        foreach (var (name, brands) in inherited.Members)
                        {
                            if (!declarations.TryGetValue(name, out var existing)) declarations[name] = brands;
                            else
                            {
                                var common = existing.Where(brand => brands.Any(other => SameBrand(brand, other))).ToList();
                                if (common.Count == 0) return new([], true);
                                declarations[name] = common;
                            }
                        }
                    }
                }
                else
                {
                    TypeInfo classType = current is TypeInfo.Instance instance ? instance.ResolvedClassType : current;
                    if (TryGetOrdinaryConstructorHierarchy(classType, out var levels))
                        foreach (var (name, member) in OrdinaryConstructorMembers(levels, current is not TypeInfo.Instance))
                            declarations[name] = [member.Access == AccessModifier.Private
                                ? new(member.DeclaringCore.DeclarationId, member.DeclaringCore) : new()];
                }
                return new(declarations);
            }
            finally { active.Remove(original); }
        }

        bool SameBrand(IntersectionWriteBrand left, IntersectionWriteBrand right) =>
            !left.IsPrivate && !right.IsPrivate || left.IsPrivate && right.IsPrivate &&
                (IsSameAccessorSource(left.DeclarationId, right.DeclarationId) ||
                 left.Core is not null && ReferenceEquals(left.Core, right.Core));

        bool UnionArmsConflict(TypeInfo current, IntersectionWriteBrandShape context, HashSet<TypeInfo> visited)
        {
            if (!visited.Add(current)) return false;
            try
            {
                return current switch
                {
                    TypeInfo.TypeParameter { Constraint: { } constraint } => UnionArmsConflict(constraint, context, visited),
                    TypeInfo.RecursiveTypeAlias alias => UnionArmsConflict(ExpandRecursiveTypeAlias(alias), context, visited),
                    TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(part => UnionArmsConflict(part, context, visited)),
                    // Compare each complete arm against the guaranteed enclosing context.
                    // Do not distribute several unions or materialize their cross-product.
                    TypeInfo.Union union => union.FlattenedTypes.All(arm => ConflictsWithContext(Shape(arm), context)),
                    _ => false
                };
            }
            finally { visited.Remove(current); }
        }

        bool ConflictsWithContext(IntersectionWriteBrandShape arm, IntersectionWriteBrandShape context) =>
            arm.Impossible || arm.Members.Any(member => context.Members.TryGetValue(member.Key, out var expected) &&
                !member.Value.Any(brand => expected.Any(other => SameBrand(brand, other))));
    }
}
