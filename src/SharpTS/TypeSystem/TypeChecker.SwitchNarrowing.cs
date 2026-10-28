using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private static bool IsSwitchPrimitiveDomain(TypeInfo type) => type is
        TypeInfo.String or TypeInfo.BigInt or TypeInfo.Any or TypeInfo.Unknown or TypeInfo.Never or
        TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral or
        TypeInfo.Null or TypeInfo.Undefined or
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN };

    private static bool IsSwitchUnitType(TypeInfo type) => type is TypeInfo.StringLiteral or TypeInfo.NumberLiteral or
        TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral or TypeInfo.Null or TypeInfo.Undefined;

    private static TypeInfo NormalizeSwitchVariableType(TypeInfo type, TypeInfo source)
    {
        if (type is not TypeInfo.Union union || !union.FlattenedTypes.All(IsSwitchPrimitiveDomain)) return type;
        var members = union.FlattenedTypes.Where(member => member is not TypeInfo.Never).ToList();
        if (members.Any(member => member is TypeInfo.Any)) return TypeInfo.Any.Shared;
        if (members.Any(member => member is TypeInfo.Unknown)) return TypeInfo.Unknown.Shared;
        if (members.OfType<TypeInfo.BooleanLiteral>().Select(literal => literal.Value).Distinct().Count() == 2 &&
            !members.Contains(TypeInfo.Primitive.Boolean))
            members.Add(TypeInfo.Primitive.Boolean);
        members.RemoveAll(member => member switch
        {
            TypeInfo.StringLiteral => members.Any(other => other is TypeInfo.String),
            TypeInfo.NumberLiteral => members.Contains(TypeInfo.Primitive.Number),
            TypeInfo.BooleanLiteral => members.Contains(TypeInfo.Primitive.Boolean),
            TypeInfo.BigIntLiteral => members.Any(other => other is TypeInfo.BigInt),
            _ => false
        });
        var sourceMembers = source is TypeInfo.Union sourceUnion ? sourceUnion.FlattenedTypes : [source];
        return UnionTypeofProjections(members.OrderBy(IsSwitchUnitType).ThenBy(member =>
        {
            int index = sourceMembers.FindIndex(candidate => TypeInfoEqualityComparer.Instance.Equals(candidate, member));
            return index < 0 ? int.MaxValue : index;
        }));
    }

    private static TypeInfo NarrowSwitchVariableType(TypeInfo subject, TypeInfo?[] tests, bool unmatched)
    {
        static bool Inhabits(TypeInfo literal, TypeInfo type) => type switch
        {
            TypeInfo.String => literal is TypeInfo.StringLiteral,
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => literal is TypeInfo.NumberLiteral,
            TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => literal is TypeInfo.BooleanLiteral,
            TypeInfo.BigInt => literal is TypeInfo.BigIntLiteral,
            TypeInfo.Any or TypeInfo.Unknown => true,
            _ => TypeInfoEqualityComparer.Instance.Equals(literal, type)
        };

        static IEnumerable<TypeInfo> Members(TypeInfo type) =>
            type is TypeInfo.Union union ? union.FlattenedTypes : [type];

        if (!unmatched)
        {
            if (tests[0] is not { } test) return subject;
            var candidates = Members(test).ToArray();
            if (!candidates.All(IsSwitchUnitType) || !Members(subject).All(IsSwitchPrimitiveDomain)) return subject;
            return UnionTypeofProjections(candidates.Where(candidate =>
                Members(subject).Any(member => Inhabits(candidate, member))));
        }

        // A union-valued test matches only one of its possible values at runtime.
        // Only singleton tests prove a value absent on the unmatched path.
        var excluded = tests.OfType<TypeInfo>().Where(IsSwitchUnitType).ToArray();
        return UnionTypeofProjections(Members(subject).Select(member =>
        {
            if (excluded.Any(test => TypeInfoEqualityComparer.Instance.Equals(test, member)))
                return TypeInfo.Never.Shared;
            if (member is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN })
            {
                bool excludesTrue = excluded.OfType<TypeInfo.BooleanLiteral>().Any(test => test.Value);
                bool excludesFalse = excluded.OfType<TypeInfo.BooleanLiteral>().Any(test => !test.Value);
                if (excludesTrue && excludesFalse) return TypeInfo.Never.Shared;
                if (excludesTrue || excludesFalse) return new TypeInfo.BooleanLiteral(excludesFalse);
            }
            return member;
        }).OrderBy(IsSwitchUnitType));
    }
}
