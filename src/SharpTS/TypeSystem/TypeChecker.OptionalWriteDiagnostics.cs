using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private TypeInfo OptionalPropertyWriteType(TypeInfo type, bool optional) =>
        optional && !_exactOptionalPropertyTypes ? CreateUnion(type, TypeInfo.Undefined.Shared) : type;

    private TypeCheckException DottedPropertyWriteError(TypeInfo receiver, Token name, TypeInfo target, TypeInfo source,
        string message, int? line = null)
    {
        if (_exactOptionalPropertyTypes && IncludesUndefined(source, new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance)) &&
            !IsCompatible(target, TypeInfo.Undefined.Shared) &&
            IntersectionWriteMemberOf(receiver, name, enforceAccessibility: false,
                new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance)) is { Optional: true })
            return new TypeCheckException(
                $"Type '{source}' is not assignable to type '{target}' with 'exactOptionalPropertyTypes: true'. Consider adding 'undefined' to the type of the target.",
                line ?? name.Line, tsCode: "TS2412");
        return new TypeCheckException(message, line, tsCode: "TS2322");
    }

    private bool IncludesUndefined(TypeInfo type, HashSet<TypeInfo> active)
    {
        if (!active.Add(type)) return false;
        try
        {
            return type switch
            {
                TypeInfo.Undefined => true,
                TypeInfo.Union union => union.FlattenedTypes.Any(part => IncludesUndefined(part, active)),
                TypeInfo.TypeParameter { Constraint: { } constraint } => IncludesUndefined(constraint, active),
                TypeInfo.RecursiveTypeAlias alias => IncludesUndefined(ExpandRecursiveTypeAlias(alias), active),
                _ => false
            };
        }
        finally { active.Remove(type); }
    }
}
