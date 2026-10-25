using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private sealed record IntersectionReadMember(TypeInfo Type, bool Named);

    private IntersectionReadMember? IntersectionReadMemberOf(TypeInfo type, Token name, bool enforceAccessibility,
        HashSet<TypeInfo> active, TypeInfo receiver)
    {
        if (!active.Add(type)) return null;
        TypeInfo original = type;
        try
        {
            type = RefreshCompletedInterfaceBinding(type);
            if (type is TypeInfo.TypeParameter parameter)
                return parameter.Constraint is not (null or TypeInfo.Any or TypeInfo.Unknown)
                    ? IntersectionReadMemberOf(parameter.Constraint, name, enforceAccessibility, active, receiver) : null;
            if (type is TypeInfo.RecursiveTypeAlias alias)
                return IntersectionReadMemberOf(ExpandRecursiveTypeAlias(alias), name, enforceAccessibility, active, receiver);
            if (type is TypeInfo.MappedType)
                return IntersectionReadMemberOf(ResolveMappedTypeForAccess(type), name, enforceAccessibility, active, receiver);
            if (type is TypeInfo.InstantiatedGeneric generic && FlattenInstantiatedInterface(generic) is { } flattened)
                return IntersectionReadMemberOf(flattened, name, enforceAccessibility, active, receiver);
            if (type is TypeInfo.Intersection intersection)
            {
                var members = intersection.FlattenedTypes.Select(part =>
                    IntersectionReadMemberOf(part, name, enforceAccessibility, active, receiver)).OfType<IntersectionReadMember>().ToList();
                // A declared property wins over another constituent's index-signature fallback.
                var named = members.Where(member => member.Named).ToList();
                if (named.Count > 0) members = named;
                return members.Count == 0 ? null : new(CombineIntersectionReadTypes(members.Select(member => member.Type)), named.Count > 0);
            }
            if (type is TypeInfo.Union union)
            {
                var members = union.FlattenedTypes.Select(part =>
                    IntersectionReadMemberOf(part, name, enforceAccessibility, active, receiver)).ToList();
                return members.Count == 0 || members.Any(member => member is null) ? null
                    : new(members.Select(member => member!.Type).Aggregate(CreateUnion), members.Any(member => member!.Named));
            }
            if (type is TypeInfo.Record record && record.Fields.TryGetValue(name.Lexeme, out var field))
                return new(WithOptionalPropertyUndefined(field, record.IsFieldOptional(name.Lexeme)), true);
            if (type is TypeInfo.Interface iface)
            {
                foreach (var member in iface.GetAllMembers())
                    if (member.Key == name.Lexeme)
                    {
                        if (enforceAccessibility)
                        {
                            TryGetInterfaceMemberBrand(iface, name.Lexeme, out var access, out int owner);
                            EnforceIntersectionMemberAccess(access, owner, iface.Name, receiver, name);
                        }
                        return new(WithOptionalPropertyUndefined(member.Value,
                            iface.GetAllOptionalMembers().Contains(name.Lexeme)), true);
                    }
                if (iface.ClassOrigin is { } origin &&
                    IntersectionReadMemberOf(new TypeInfo.Instance(origin), name, enforceAccessibility, active, receiver) is { } inheritedClass)
                    return inheritedClass;
                foreach (var parent in iface.Extends ?? [])
                    if (IntersectionReadMemberOf(parent, name, enforceAccessibility, active, receiver) is { Named: true } inherited)
                        return inherited;
            }
            TypeInfo classType = type is TypeInfo.Instance instance ? instance.ResolvedClassType : type;
            bool isStatic = type is not TypeInfo.Instance;
            TryGetOrdinaryConstructorHierarchy(classType, out var levels);
            var declaring = levels.FirstOrDefault(level => isStatic
                ? level.Core.StaticProperties.ContainsKey(name.Lexeme) || level.Core.StaticMethods.ContainsKey(name.Lexeme)
                : level.Core.FieldTypes.ContainsKey(name.Lexeme) || level.Core.Methods.ContainsKey(name.Lexeme) && name.Lexeme != "constructor"
                    || level.Core.Getters.ContainsKey(name.Lexeme) || level.Core.Setters.ContainsKey(name.Lexeme)
                    || name.Lexeme == "constructor" && level.Core.ComputedConstructorMethodType is not null);
            if (declaring is not null)
            {
                if (type is TypeInfo.Instance actualInstance && TrySelectInstanceAccessor(actualInstance, name.Lexeme, out var accessor))
                {
                    if (enforceAccessibility) EnforceAccessorAccess(accessor.DeclaringType, receiver, name, isStatic: false, isWrite: false);
                    return new(SelectedInstanceAccessorReadType(accessor, name.Lexeme), true);
                }
                if (enforceAccessibility)
                {
                    var core = declaring.Core;
                    bool field = isStatic ? core.StaticProperties.ContainsKey(name.Lexeme)
                        : core.FieldTypes.ContainsKey(name.Lexeme) || core.Getters.ContainsKey(name.Lexeme) || core.Setters.ContainsKey(name.Lexeme);
                    var access = (isStatic, field) switch
                    {
                        (true, true) => core.StaticFieldAccessMap.GetValueOrDefault(name.Lexeme, AccessModifier.Public),
                        (true, false) => core.StaticMethodAccessMap.GetValueOrDefault(name.Lexeme, AccessModifier.Public),
                        (false, true) => core.FieldAccess.GetValueOrDefault(name.Lexeme, AccessModifier.Public),
                        _ => name.Lexeme == "constructor" ? core.ComputedConstructorMethodAccess
                            : core.MethodAccess.GetValueOrDefault(name.Lexeme, AccessModifier.Public)
                    };
                    EnforceIntersectionMemberAccess(access, core.DeclarationId, core.Name, receiver, name);
                }
                return new(isStatic ? CheckGetOnClass(type, name, enforceAccessibility: false)
                    : CheckGetOnInstance((TypeInfo.Instance)type, name, enforceAccessibility: false), true);
            }
            // Unknown generic contributions must not erase an independently known slot.
            // Keep index/prototype/dynamic fallbacks only when no constituent declares it.
            try { return new(CheckGetOnType(type, name), false); }
            catch (TypeCheckException error) when (error.Diagnostic.TsCode is "TS2339" or "TS2551") { return null; }
        }
        finally { active.Remove(original); }
    }

    private TypeInfo CombineIntersectionReadTypes(IEnumerable<TypeInfo> members)
    {
        int budget = 64;
        return Combine(members.Distinct(TypeInfoEqualityComparer.Instance).ToList());

        TypeInfo Combine(List<TypeInfo> types)
        {
            if (types.Count == 1) return types[0];
            if (types.All(type => type is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction or TypeInfo.OverloadSet))
            {
                List<TypeInfo> signatures = [];
                foreach (var type in types) AddCallableSignature(signatures, type);
                return CreateInterfaceOverload(signatures);
            }
            var simplified = SimplifyIntersection(types);
            if (simplified is not TypeInfo.Intersection || --budget < 0) return simplified;
            // Optional slots add undefined to their read type. It cannot survive an
            // intersection with a required callable/object slot; two optional slots
            // still retain their shared undefined branch below.
            if (types.Any(type => type is TypeInfo.Null or TypeInfo.Undefined) &&
                types.Any(type => type is TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction
                    or TypeInfo.OverloadSet or TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance
                    or TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.Array or TypeInfo.Tuple))
                return TypeInfo.Never.Shared;
            for (int i = 0; i < types.Count; i++)
                if (types[i] is TypeInfo.Union union)
                {
                    List<TypeInfo> alternatives = [];
                    foreach (var part in union.FlattenedTypes)
                    {
                        var branch = types.ToList();
                        branch[i] = part;
                        alternatives.Add(Combine(branch));
                    }
                    return alternatives.Count == 0 ? TypeInfo.Never.Shared : alternatives.Aggregate(CreateUnion);
                }
            if (types.All(type => type is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Primitive
                    or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigInt or TypeInfo.BigIntLiteral
                    or TypeInfo.Null or TypeInfo.Undefined or TypeInfo.Symbol or TypeInfo.UniqueSymbol))
            {
                TypeInfo result = types[0];
                foreach (var next in types.Skip(1))
                    if (IsCompatible(result, next)) result = next;
                    else if (!IsCompatible(next, result)) return TypeInfo.Never.Shared;
                return result;
            }
            return simplified;
        }
    }

    private TypeInfo? ReadLiteralIntersectionIndex(TypeInfo.Intersection receiver, TypeInfo index, Expr.GetIndex syntax)
    {
        if (!TryGetLiteralPropertyKeys(index, out var names)) return null;
        if (HasImpossibleIntersectionWriteBrands(receiver) || receiver.FlattenedTypes.Any(IsImpossibleIntersectionConstraint))
            return TypeInfo.Never.Shared;
        List<TypeInfo> members = [];
        foreach (string name in names)
        {
            var token = new Token(TokenType.IDENTIFIER, name, null,
                TryGetExprLine(syntax.Index) ?? TryGetExprLine(syntax.Object) ?? _currentStatementLine ?? 0);
            if (IntersectionReadMemberOf(receiver, token, false,
                new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance), receiver) is not { } member) return null;
            members.Add(member.Type);
        }
        return members.Aggregate(CreateUnion);
    }
}
