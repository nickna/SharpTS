using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public sealed partial class RuntimeFeatureDetector
{
    private static bool ContainsOpaqueType(TypeInfo type)
    {
        var visited = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);
        return Contains(type, 0);

        bool Contains(TypeInfo candidate, int depth)
        {
            if (depth >= 32) return true;
            if (!visited.Add(candidate)) return false;
            bool Member(TypeInfo? member) => member is not null && Contains(member, depth + 1);
            return candidate switch
            {
                TypeInfo.Any or TypeInfo.Unknown => true,
                TypeInfo.Record record => record.Fields.Values.Any(Member) || Member(record.StringIndexType) ||
                    Member(record.NumberIndexType) || Member(record.SymbolIndexType),
                TypeInfo.Interface iface => iface.Members.Values.Any(Member) || Member(iface.StringIndexType) ||
                    Member(iface.NumberIndexType) || Member(iface.SymbolIndexType) ||
                    iface.Extends?.Any(Member) == true,
                TypeInfo.InstantiatedGeneric { GenericDefinition: TypeInfo.GenericInterface } instance =>
                    !TryProjectMaterializationInterface(instance, out var projected) || Member(projected),
                TypeInfo.Union union => union.Types.Any(Member),
                TypeInfo.Intersection intersection => intersection.Types.Any(Member),
                TypeInfo.Array array => Member(array.ElementType),
                TypeInfo.Tuple tuple => tuple.Elements.Any(element => Member(element.Type)) || Member(tuple.RestElementType),
                TypeInfo.TypeParameter parameter => Member(parameter.Constraint),
                TypeInfo.Function function => function.ParamTypes.Any(Member) || Member(function.ReturnType),
                TypeInfo.GenericFunction function => function.ParamTypes.Any(Member) || Member(function.ReturnType),
                _ => false
            };
        }
    }
}
