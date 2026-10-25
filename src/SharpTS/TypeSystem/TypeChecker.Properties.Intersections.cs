using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record IntersectionWriteMember(TypeInfo Type, bool Readonly, bool Optional);

    private TypeInfo.Record IntersectionWriteTarget(TypeInfo.Intersection receiver, IEnumerable<string> names,
        int line, bool enforceAccessibility, string missingCode = "TS2339")
    {
        Dictionary<string, TypeInfo> fields = [];
        HashSet<string> readonlyFields = [], optionalFields = [];
        foreach (string name in names)
        {
            var token = new Token(TokenType.IDENTIFIER, name, null, line);
            if (receiver.FlattenedTypes.Any(IsImpossibleIntersectionConstraint) ||
                IntersectionWriteMemberOf(receiver, token, enforceAccessibility,
                    new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance)) is not { } member)
                throw new TypeCheckException($"Property '{name}' does not exist on type '{receiver}'.", line, tsCode: missingCode);
            fields[name] = member.Type;
            if (member.Readonly) readonlyFields.Add(name);
            if (member.Optional) optionalFields.Add(name);
        }
        return new(fields.ToFrozenDictionary(), OptionalFields: optionalFields.ToFrozenSet(StringComparer.Ordinal),
            ReadonlyFields: readonlyFields.ToFrozenSet(StringComparer.Ordinal));
    }

    private static bool IsImpossibleIntersectionConstraint(TypeInfo type) => type switch
    {
        TypeInfo.Never or TypeInfo.Null or TypeInfo.Undefined => true,
        TypeInfo.TypeParameter { Constraint: { } constraint } => IsImpossibleIntersectionConstraint(constraint),
        TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(IsImpossibleIntersectionConstraint),
        TypeInfo.Union union => union.FlattenedTypes.All(IsImpossibleIntersectionConstraint),
        _ => false
    };

    private IntersectionWriteMember? IntersectionWriteMemberOf(TypeInfo type, Token name, bool enforceAccessibility,
        HashSet<TypeInfo> active)
    {
        if (!active.Add(type)) return null;
        TypeInfo original = type;
        try
        {
            type = RefreshCompletedInterfaceBinding(type);
            if (type is TypeInfo.TypeParameter parameter)
                return parameter.Constraint is { } constraint
                    ? IntersectionWriteMemberOf(constraint, name, enforceAccessibility, active) : null;
            if (type is TypeInfo.RecursiveTypeAlias alias)
                return IntersectionWriteMemberOf(ExpandRecursiveTypeAlias(alias), name, enforceAccessibility, active);
            if (type is TypeInfo.MappedType)
                return IntersectionWriteMemberOf(ResolveMappedTypeForAccess(type), name, enforceAccessibility, active);
            if (type is TypeInfo.InstantiatedGeneric generic && FlattenInstantiatedInterface(generic) is { } flattened)
                return IntersectionWriteMemberOf(flattened, name, enforceAccessibility, active);
            if (type is TypeInfo.Intersection intersection)
            {
                var members = intersection.FlattenedTypes
                    .Select(part => IntersectionWriteMemberOf(part, name, enforceAccessibility, active))
                    .OfType<IntersectionWriteMember>().ToList();
                return members.Count == 0 ? null : new(SimplifyIntersection(members.Select(member => member.Type)
                        .Distinct(TypeInfoEqualityComparer.Instance).ToList()),
                    members.All(member => member.Readonly), members.All(member => member.Optional));
            }
            if (type is TypeInfo.Union union)
            {
                var members = union.FlattenedTypes
                    .Select(part => IntersectionWriteMemberOf(part, name, enforceAccessibility, active)).ToList();
                return members.Any(member => member is null) ? null : new(
                    members.Select(member => member!.Type).Aggregate(CreateUnion),
                    members.Any(member => member!.Readonly), members.Any(member => member!.Optional));
            }
            if (type is TypeInfo.Record record && record.Fields.TryGetValue(name.Lexeme, out var field))
                return Slot(field, record.IsFieldReadonly(name.Lexeme) || record.IsGetterOnly(name.Lexeme),
                    record.IsFieldOptional(name.Lexeme));
            if (type is TypeInfo.Interface iface)
                foreach (var member in iface.GetAllMembers())
                    if (member.Key == name.Lexeme)
                        return Slot(member.Value, iface.IsMemberReadonly(name.Lexeme),
                            iface.GetAllOptionalMembers().Contains(name.Lexeme));
            bool isStatic = type is not TypeInfo.Instance;
            TypeInfo classType = type is TypeInfo.Instance instance ? instance.ResolvedClassType : type;
            if (TryGetOrdinaryConstructorHierarchy(classType, out var levels) &&
                OrdinaryConstructorMembers(levels, isStatic).TryGetValue(name.Lexeme, out var selected))
            {
                var owner = new TypeInfo.Class(selected.DeclaringCore);
                if (isStatic)
                {
                    var setter = selected.DeclaringCore.StaticSetters?.GetValueOrDefault(name.Lexeme);
                    bool getterOnly = selected.DeclaringCore.StaticGetters?.ContainsKey(name.Lexeme) == true && setter is null;
                    if (enforceAccessibility)
                    {
                        if (setter is not null || getterOnly)
                            EnforceAccessorAccess(owner, classType, name, isStatic: true, isWrite: setter is not null);
                        else EnforceStaticMemberAccess(owner, name);
                    }
                    var substitutions = levels.First(level => ReferenceEquals(level.Core, selected.DeclaringCore)).Substitutions;
                    return Slot(setter is null ? selected.Type : SubstituteConstructorMember(setter, substitutions),
                        getterOnly || selected.DeclaringCore.StaticReadonlyFields?.Contains(name.Lexeme) == true, selected.IsOptional);
                }
                if (TrySelectInstanceAccessor((TypeInfo.Instance)type, name.Lexeme, out var accessor))
                {
                    if (enforceAccessibility)
                        EnforceAccessorAccess(accessor.DeclaringType, classType, name,
                            isStatic: false, isWrite: accessor.SetterType is not null);
                    return Slot(accessor.SetterType ?? accessor.GetterType!, accessor.SetterType is null, false);
                }
                if (enforceAccessibility)
                    EnforceIntersectionMemberAccess(selected.Access, owner, classType, name);
                return Slot(selected.Type, selected.DeclaringCore.ReadonlyFields.Contains(name.Lexeme), selected.IsOptional);
            }
            return null;
        }
        finally { active.Remove(original); }

        IntersectionWriteMember Slot(TypeInfo value, bool isReadonly, bool optional) =>
            new(optional && !_exactOptionalPropertyTypes ? CreateUnion(value, TypeInfo.Undefined.Shared) : value,
                isReadonly, optional);
    }

    private void EnforceIntersectionMemberAccess(AccessModifier access, TypeInfo owner, TypeInfo receiver, Token name)
    {
        if (access == AccessModifier.Public) return;
        int ownerId = SelectedSourceClassId(owner);
        int lexicalId = _currentClass?.Core.DeclarationId ?? 0;
        bool sameOwner = IsSameAccessorSource(lexicalId, ownerId);
        if (access == AccessModifier.Private && !sameOwner)
            throw new TypeCheckException($"Property '{name.Lexeme}' is private and only accessible within class '{GetClassName(owner)}'.",
                name.Line, tsCode: "TS2341");
        if (access == AccessModifier.Protected && !sameOwner)
        {
            if (_currentClass is null || !AccessorSourceDerivesFrom(_currentClass, ownerId))
                throw new TypeCheckException($"Property '{name.Lexeme}' is protected and only accessible within class '{GetClassName(owner)}' and its subclasses.",
                    name.Line, tsCode: "TS2445");
            if (!AccessorSourceDerivesFrom(receiver, lexicalId))
                throw new TypeCheckException($"Property '{name.Lexeme}' is protected and only accessible through an instance of class '{_currentClass.Name}'.",
                    name.Line, tsCode: "TS2446");
        }
    }
}
