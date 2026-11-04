using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

internal static class SourceTypeMemberOrder
{
    private static readonly ConditionalWeakTable<TypeInfo, IReadOnlyDictionary<string, int>> Orders = new();

    internal static void Capture(TypeInfo type, IEnumerable<string> names)
        => Orders.AddOrUpdate(type, names.Select((name, index) => (name, index))
            .GroupBy(item => item.name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal));

    internal static IEnumerable<KeyValuePair<string, TypeInfo>> Fields(TypeInfo.Record type)
        => Orders.TryGetValue(type, out var order)
            ? type.Fields.OrderBy(field => order.GetValueOrDefault(field.Key, int.MaxValue)).ThenBy(field => field.Key, StringComparer.Ordinal)
            : type.Fields.OrderBy(field => field.Key, StringComparer.Ordinal);

    internal static void Copy(TypeInfo original, TypeInfo replacement)
    {
        if (!ReferenceEquals(original, replacement) && Orders.TryGetValue(original, out var order))
            Orders.AddOrUpdate(replacement, order);
    }
}
