using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Discriminated-union assignability — tsc's <c>typeRelatedToDiscriminatedType</c>
/// (checker.ts): a source whose discriminant properties are unions of unit types can be
/// assignable to a union target even when it is assignable to NO single constituent, as long
/// as EVERY combination of its discriminant values lands on an assignable constituent.
/// `{ done: boolean, value: number }` is assignable to
/// `{ done: true, value: number } | { done: false, value: number }`.
/// </summary>
public partial class TypeChecker
{
    /// <summary>tsc caps the discriminant combination space at 25 (Example5 in the pinning test
    /// EXPECTS the 27-combination case to fail).</summary>
    private const int MaxDiscriminantCombinations = 25;

    /// <summary>
    /// True when <paramref name="source"/> (a non-union object-like or tuple) relates to the
    /// union <paramref name="target"/> through discriminant analysis. Called only after the
    /// plain some-constituent check has failed.
    /// </summary>
    private bool RelatedToDiscriminatedUnion(TypeInfo.Union target, TypeInfo source)
    {
        var targetTypes = target.FlattenedTypes;

        if (source is TypeInfo.Tuple sourceTuple)
            return TupleRelatedToDiscriminatedUnion(targetTypes, sourceTuple);

        var members = NamedMembersWithOptionality(source).ToList();
        if (members.Count == 0) return false;

        // Discriminant candidates: source properties whose type is a unit (or union of units),
        // present on EVERY target constituent, with a unit type in at least one of them.
        List<(string Name, List<TypeInfo> Values)> discriminants = [];
        foreach (var (name, type, _) in members)
        {
            var values = UnitConstituents(type);
            if (values is null) continue;
            bool onAll = true, anyUnit = false;
            foreach (var constituent in targetTypes)
            {
                if (GetMemberTypeWithOptionality(constituent, name) is not { } member) { onAll = false; break; }
                anyUnit |= UnitConstituents(member.Type) is not null;
            }
            if (onAll && anyUnit) discriminants.Add((name, values));
        }
        if (discriminants.Count == 0) return false;

        long combinations = 1;
        foreach (var (_, values) in discriminants)
        {
            combinations *= values.Count;
            if (combinations > MaxDiscriminantCombinations) return false;
        }

        // Walk the cartesian product of discriminant values; each combination must land on a
        // constituent that (a) accepts every discriminant value and (b) accepts the rest of the
        // source with its discriminants narrowed to the combination.
        var indices = new int[discriminants.Count];
        while (true)
        {
            bool matched = false;
            foreach (var constituent in targetTypes)
            {
                bool discriminantsAccepted = true;
                for (int d = 0; d < discriminants.Count; d++)
                {
                    var (name, values) = discriminants[d];
                    var combinationValue = values[indices[d]];
                    var member = GetMemberTypeWithOptionality(constituent, name)!.Value;
                    bool accepted = IsCompatible(member.Type, combinationValue) ||
                        // An `undefined` discriminant value satisfies an optional member.
                        (member.IsOptional && combinationValue is TypeInfo.Undefined);
                    if (!accepted) { discriminantsAccepted = false; break; }
                }
                if (!discriminantsAccepted) continue;

                if (IsCompatible(constituent, NarrowedSource(members, discriminants, indices)))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched) return false;

            int next = discriminants.Count - 1;
            while (next >= 0 && ++indices[next] == discriminants[next].Values.Count)
            {
                indices[next] = 0;
                next--;
            }
            if (next < 0) return true; // all combinations matched
        }
    }

    /// <summary>The source with its discriminant properties narrowed to one combination.</summary>
    private static TypeInfo.Record NarrowedSource(
        List<(string Name, TypeInfo Type, bool IsOptional)> members,
        List<(string Name, List<TypeInfo> Values)> discriminants,
        int[] indices)
    {
        Dictionary<string, TypeInfo> fields = [];
        HashSet<string> optionalFields = [];
        foreach (var (name, type, isOptional) in members)
        {
            fields[name] = type;
            if (isOptional) optionalFields.Add(name);
        }
        for (int d = 0; d < discriminants.Count; d++)
            fields[discriminants[d].Name] = discriminants[d].Values[indices[d]];
        return new TypeInfo.Record(
            fields.ToFrozenDictionary(),
            OptionalFields: optionalFields.Count > 0 ? optionalFields.ToFrozenSet() : null);
    }

    /// <summary>
    /// Tuple flavor: discriminant POSITIONS instead of property names
    /// (`["a" | "b", number]` vs `["a", number] | ["b", number] | ["c", string]`).
    /// </summary>
    private bool TupleRelatedToDiscriminatedUnion(IReadOnlyList<TypeInfo> targetTypes, TypeInfo.Tuple source)
    {
        if (targetTypes.Any(t => t is not TypeInfo.Tuple)) return false;

        List<(int Position, List<TypeInfo> Values)> discriminants = [];
        for (int i = 0; i < source.Elements.Count; i++)
        {
            var values = UnitConstituents(source.Elements[i].Type);
            if (values is null || values.Count < 2) continue;
            bool onAll = targetTypes.All(t => ((TypeInfo.Tuple)t).Elements.Count > i);
            if (onAll) discriminants.Add((i, values));
        }
        if (discriminants.Count == 0) return false;

        long combinations = 1;
        foreach (var (_, values) in discriminants)
        {
            combinations *= values.Count;
            if (combinations > MaxDiscriminantCombinations) return false;
        }

        var indices = new int[discriminants.Count];
        while (true)
        {
            bool matched = targetTypes.Any(constituent =>
                IsCompatible(constituent, NarrowedTuple(source, discriminants, indices)));
            if (!matched) return false;

            int next = discriminants.Count - 1;
            while (next >= 0 && ++indices[next] == discriminants[next].Values.Count)
            {
                indices[next] = 0;
                next--;
            }
            if (next < 0) return true;
        }
    }

    private static TypeInfo.Tuple NarrowedTuple(
        TypeInfo.Tuple source, List<(int Position, List<TypeInfo> Values)> discriminants, int[] indices)
    {
        var elements = new List<TypeInfo.TupleElement>(source.Elements);
        for (int d = 0; d < discriminants.Count; d++)
        {
            var (position, values) = discriminants[d];
            elements[position] = elements[position] with { Type = values[indices[d]] };
        }
        return source with { Elements = elements };
    }

    /// <summary>
    /// The unit-type constituents of a discriminant-capable type: literals, undefined/null, with
    /// `boolean` expanding to <c>true | false</c> (tsc models it as that union). Null when the
    /// type contains any non-unit constituent (then it cannot discriminate).
    /// </summary>
    private static List<TypeInfo>? UnitConstituents(TypeInfo type)
    {
        List<TypeInfo> result = [];
        foreach (var t in type is TypeInfo.Union u ? (IEnumerable<TypeInfo>)u.FlattenedTypes : [type])
        {
            switch (t)
            {
                case TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral
                    or TypeInfo.BigIntLiteral or TypeInfo.Undefined or TypeInfo.Null:
                    result.Add(t);
                    break;
                case TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_BOOLEAN }:
                    result.Add(new TypeInfo.BooleanLiteral(true));
                    result.Add(new TypeInfo.BooleanLiteral(false));
                    break;
                default:
                    return null;
            }
        }
        return result;
    }

    /// <summary>A constituent's member type and optionality, or null when absent.</summary>
    private (TypeInfo Type, bool IsOptional)? GetMemberTypeWithOptionality(TypeInfo constituent, string name)
    {
        foreach (var (memberName, type, isOptional) in NamedMembersWithOptionality(constituent))
            if (memberName == name) return (type, isOptional);
        return null;
    }
}
