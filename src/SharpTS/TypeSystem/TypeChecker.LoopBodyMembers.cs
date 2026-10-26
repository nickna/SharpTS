using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private bool LoopBodyMemberOptional(TypeInfo type, string name) => type switch
    {
        TypeInfo.Record record => record.IsFieldOptional(name),
        TypeInfo.Interface iface => iface.GetAllOptionalMembers().Contains(name),
        TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } generic when FlattenInstantiatedInterface(generic) is { } iface =>
            iface.GetAllOptionalMembers().Contains(name),
        TypeInfo.Instance instance => LoopBodyInstanceMemberOptional(instance, name),
        _ => false
    };

    private bool LoopBodyInstanceMemberOptional(TypeInfo.Instance instance, string name)
    {
        bool complete = TryGetOrdinaryConstructorHierarchy(instance.ClassType, out var levels);
        var owner = levels.FirstOrDefault(level => level.Core.FieldTypes.ContainsKey(name) || level.Core.Methods.ContainsKey(name) ||
            level.Core.Getters.ContainsKey(name) || level.Core.Setters.ContainsKey(name));
        if (owner is not null) return owner.Core.OrdinaryOptionalMembers?.Contains((false, name)) == true;
        if (!complete) throw new UnprovedLoopBodyFlow();
        return false;
    }

    private TypeInfo? LoopBodyReadableMember(TypeInfo type, string name)
    {
        var member = GetMemberType(type, name);
        return member is null ? null : WithOptionalPropertyUndefined(member, LoopBodyMemberOptional(type, name));
    }

    private object? LoopBodyMemberSlot(LoopBodyEvaluation evaluation, Expr receiver, string member)
    {
        List<string> path = [member];
        while (true)
            switch (receiver)
            {
                case Expr.Grouping grouping: receiver = grouping.Expression; break;
                case Expr.Get get: path.Insert(0, get.Name.Lexeme); receiver = get.Object; break;
                case Expr.Variable variable:
                {
                    if (LoopBodySlot(evaluation, variable.Name.Lexeme) is not { } root) return null;
                    if (!evaluation.Graph.MemberSlots.TryGetValue(root, out var members))
                        evaluation.Graph.MemberSlots[root] = members = new(StringComparer.Ordinal);
                    string key = string.Join('.', path);
                    if (!members.TryGetValue(key, out var slot))
                    {
                        members[key] = slot = new();
                        evaluation.Graph.MemberPaths[slot] = (root, path.ToArray());
                    }
                    return slot;
                }
                default: return null;
            }
    }

    private TypeInfo DefaultLoopBodyValue(LoopBodyGraph graph, Dictionary<object, TypeInfo> values, object slot)
    {
        if (!graph.MemberPaths.TryGetValue(slot, out var member))
            return graph.Initial.GetValueOrDefault(slot) ?? UnprovedLoopBodyType;
        var type = values.GetValueOrDefault(member.Root) ?? graph.Initial.GetValueOrDefault(member.Root) ?? UnprovedLoopBodyType;
        foreach (string name in member.Path)
        {
            var members = LoopBodyAtoms(type).Select(value => LoopBodyReadableMember(value, name)).ToArray();
            if (members.Any(value => value is null)) return UnprovedLoopBodyType;
            type = UnionLoopBodyValues(members.OfType<TypeInfo>());
        }
        return type;
    }

    private void StoreLoopBodyMember(LoopBodyEvaluation evaluation, Expr receiverSyntax, string name,
        TypeInfo receiver, TypeInfo value)
    {
        if (InvalidateLoopBodyMember(evaluation, receiverSyntax, name) is not { } slot) return;
        var expected = LoopBodyAtoms(receiver).Select(type => GetMemberType(type, name) is { } memberType
            ? OptionalPropertyWriteType(memberType, LoopBodyMemberOptional(type, name)) : null).ToArray();
        if (expected.Any(type => type is null || type is TypeInfo.Any or TypeInfo.Unknown))
            evaluation.Values[slot] = UnprovedLoopBodyType;
        else
        {
            if (ReferenceEquals(value, UnprovedLoopBodyType)) throw new UnprovedLoopBodyFlow();
            if (expected.Any(type => !IsCompatible(type!, value))) throw new UnprovedLoopBodyFlow();
            evaluation.Values[slot] = value;
        }
    }

    private object? InvalidateLoopBodyMember(LoopBodyEvaluation evaluation, Expr receiverSyntax, string name)
    {
        if (LoopBodyMemberSlot(evaluation, receiverSyntax, name) is not { } slot) return null;
        var member = evaluation.Graph.MemberPaths[slot];
        evaluation.Writes.Add(member.Root);
        // A replacement may invalidate saved nested property facts; keep the written
        // location only. No class/interface metadata or unrelated binding is mutated.
        foreach (var child in evaluation.Graph.MemberSlots[member.Root])
            if (child.Key == string.Join('.', member.Path) || child.Key.StartsWith(string.Join('.', member.Path) + ".", StringComparison.Ordinal))
                evaluation.Values.Remove(child.Value);
        return slot;
    }

    private static bool HasUnprovedLoopBodyType(TypeInfo type)
    {
        HashSet<TypeInfo> seen = new(ReferenceEqualityComparer.Instance);
        return Visit(type);
        bool Visit(TypeInfo current)
        {
            if (ReferenceEquals(current, UnprovedLoopBodyType)) return true;
            if (!seen.Add(current)) return false;
            return current switch
            {
                TypeInfo.Array array => Visit(array.ElementType),
                TypeInfo.Tuple tuple => tuple.Elements.Any(element => Visit(element.Type)) || tuple.RestElementType is { } rest && Visit(rest),
                TypeInfo.Record record => record.Fields.Values.Any(Visit),
                TypeInfo.Union union => union.FlattenedTypes.Any(Visit),
                TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(Visit),
                TypeInfo.InstantiatedGeneric generic => generic.TypeArguments.Any(Visit),
                TypeInfo.Instance instance => Visit(instance.ClassType),
                _ => false
            };
        }
    }
}
