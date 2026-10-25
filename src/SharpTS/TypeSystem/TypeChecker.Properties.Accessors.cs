using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private readonly record struct SelectedInstanceAccessor(
        TypeInfo DeclaringType, TypeInfo? GetterType, TypeInfo? SetterType,
        TypeInfo? RawGetterType, Dictionary<string, TypeInfo> Substitutions);

    private TypeInfo SelectedInstanceAccessorReadType(SelectedInstanceAccessor selected, string name) =>
        selected.RawGetterType is { } getter
            ? SubstituteConstructorMember(
                ResolveFieldStageGetter(selected.DeclaringType, isStatic: false, name, getter),
                selected.Substitutions)
            : selected.SetterType!;

    private bool TrySelectInstanceAccessor(
        TypeInfo.Instance receiver, string name, out SelectedInstanceAccessor selected)
    {
        Dictionary<string, TypeInfo> substitutions = [];
        HashSet<TypeInfo> visited = new(ReferenceEqualityComparer.Instance);
        for (TypeInfo? current = receiver.ResolvedClassType;
             current is not null && visited.Add(current); current = GetSuperclass(current))
        {
            if (current is TypeInfo.InstantiatedGeneric
                { GenericDefinition: TypeInfo.GenericClass generic } instantiated)
            {
                // Resolve every argument in the incoming scope before entering the
                // next owner's parameter scope (including renamed/repeated names).
                var arguments = instantiated.TypeArguments
                    .Select(argument => SubstituteConstructorMember(argument, substitutions)).ToList();
                substitutions = GenericClassSubs(generic, arguments);
            }

            var getter = GetGetters(current)?.GetValueOrDefault(name);
            var setter = GetSetters(current)?.GetValueOrDefault(name);
            if (getter is not null || setter is not null)
            {
                selected = new(current,
                    getter is null ? null : SubstituteConstructorMember(getter, substitutions),
                    setter is null ? null : SubstituteConstructorMember(setter, substitutions),
                    getter, substitutions);
                return true;
            }

            // A nearer ordinary member masks any farther accessor. Leave its
            // existing field/method typing and visibility policy to the caller.
            if (GetFieldTypes(current)?.ContainsKey(name) == true ||
                GetMethods(current)?.ContainsKey(name) == true)
                break;
        }
        selected = default;
        return false;
    }

    private bool TryGetAccessorSetterAccess(
        TypeInfo owner, bool isStatic, string name, out AccessModifier? access)
    {
        IReadOnlyDictionary<(bool IsStatic, string Name), AccessModifier?>? accessors =
            owner is TypeInfo.MutableClass mutable ? mutable.AccessorSetterAccess
                : ClassInfoAccessor.Get(owner,
                    cls => cls.Core.AccessorSetterAccess,
                    generic => generic.Core.AccessorSetterAccess);
        access = null;
        return accessors?.TryGetValue((isStatic, name), out access) == true;
    }

    private bool IsSameAccessorSource(int left, int right) => left != 0 && right != 0 &&
        (left == right || Members.IsSameSourceClass(left, right));

    private bool AccessorSourceDerivesFrom(TypeInfo source, int ownerId) =>
        InterfaceReceiverDerivesFrom(source, ownerId, new(ReferenceEqualityComparer.Instance));

    private bool InterfaceReceiverDerivesFrom(TypeInfo source, int ownerId, HashSet<TypeInfo> active)
    {
        if (!active.Add(source)) return false;
        try
        {
            return source switch
            {
                TypeInfo.TypeParameter { Constraint: { } constraint } => InterfaceReceiverDerivesFrom(constraint, ownerId, active),
                TypeInfo.MutableClass mutable => IsSameAccessorSource(mutable.DeclarationId, ownerId) ||
                    mutable.Superclass is { } parent && InterfaceReceiverDerivesFrom(parent, ownerId, active),
                TypeInfo.Intersection intersection => intersection.FlattenedTypes.Any(part => InterfaceReceiverDerivesFrom(part, ownerId, active)),
                TypeInfo.Union union => union.FlattenedTypes.All(part => InterfaceReceiverDerivesFrom(part, ownerId, active)),
                TypeInfo.Interface iface => iface.ClassOrigin is { } origin && InterfaceReceiverDerivesFrom(origin, ownerId, active) ||
                    iface.Extends?.Any(parent => InterfaceReceiverDerivesFrom(parent, ownerId, active)) == true,
                TypeInfo.InstantiatedGeneric generic when FlattenInstantiatedInterface(generic) is { } iface =>
                    InterfaceReceiverDerivesFrom(iface, ownerId, active),
                _ => EnumerateClassCores(source).Any(core => IsSameAccessorSource(core.DeclarationId, ownerId))
            };
        }
        finally { active.Remove(source); }
    }

    private void EnforceAccessorAccess(
        TypeInfo declaringType, TypeInfo receiverType, Token memberName, bool isStatic, bool isWrite)
    {
        AccessModifier access = (isStatic ? GetStaticFieldAccess(declaringType) : GetFieldAccess(declaringType))
            ?.GetValueOrDefault(memberName.Lexeme) ?? AccessModifier.Public;
        if (isWrite && TryGetAccessorSetterAccess(declaringType, isStatic, memberName.Lexeme,
                out var setterAccess) && setterAccess is { } writeAccess)
            access = writeAccess;
        if (access == AccessModifier.Public) return;

        int declaringId = SelectedSourceClassId(declaringType);
        int lexicalId = _currentClass?.Core.DeclarationId ?? 0;
        bool sameOwner = IsSameAccessorSource(lexicalId, declaringId);
        var declaringName = GetClassName(declaringType);
        if (access == AccessModifier.Private && !sameOwner)
            throw new TypeCheckException(
                $"Property '{memberName.Lexeme}' is private and only accessible within class '{declaringName}'.",
                memberName.Line, tsCode: "TS2341");
        if (access != AccessModifier.Protected) return;

        if (_currentClass is not { } lexicalClass || !AccessorSourceDerivesFrom(lexicalClass, declaringId))
            throw new TypeCheckException(
                $"Property '{memberName.Lexeme}' is protected and only accessible within class '{declaringName}' and its subclasses.",
                memberName.Line, tsCode: "TS2445");
        if (!isStatic && !sameOwner && !AccessorSourceDerivesFrom(receiverType, lexicalId))
            throw new TypeCheckException(
                $"Property '{memberName.Lexeme}' is protected and only accessible through an instance of class '{lexicalClass.Name}'.",
                memberName.Line, tsCode: "TS2446");
    }
}
