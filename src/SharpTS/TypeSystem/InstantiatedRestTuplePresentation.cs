using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

/// <summary>Records a generic rest parameter's actual tuple instantiation without changing type identity.</summary>
internal static class InstantiatedRestTuplePresentation
{
    private static readonly ConditionalWeakTable<TypeInfo, object> Expanded = new();
    private static readonly object Marker = new();

    internal static bool HasExpandedRest(TypeInfo signature) => Expanded.TryGetValue(signature, out _);

    internal static void Capture(TypeInfo original, TypeInfo replacement)
    {
        if (RestParameter(replacement) is TypeInfo.Tuple &&
            (RestParameter(original) is TypeInfo.TypeParameter || HasExpandedRest(original)))
            Expanded.AddOrUpdate(replacement, Marker);
    }

    private static TypeInfo? RestParameter(TypeInfo type) => type switch
    {
        TypeInfo.Function { HasRestParam: true } function => function.ParamTypes.LastOrDefault(),
        TypeInfo.GenericFunction { HasRestParam: true } function => function.ParamTypes.LastOrDefault(),
        _ => null,
    };
}
