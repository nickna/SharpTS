using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

/// <summary>Source alias provenance, independent of structural type identity.</summary>
internal static class DeferredTypeAliasPresentation
{
    internal sealed record Alias(string Name, IReadOnlyList<TypeInfo> Arguments, object? Declaration = null);
    private static readonly ConditionalWeakTable<TypeInfo, Alias> Aliases = new();

    internal static TypeInfo Retain(TypeInfo type, string name, IReadOnlyList<TypeInfo> arguments)
    {
        if (type is TypeInfo.MappedType or TypeInfo.ConditionalType)
            Aliases.AddOrUpdate(type, new(name, arguments.ToArray()));
        return type;
    }

    internal static Alias? Get(TypeInfo type) => Aliases.TryGetValue(type, out var alias) ? alias : null;

    internal static TypeInfo RetainSourceAlias(TypeInfo type, string name, object declaration)
    {
        if (type is not TypeInfo.Union || Get(type) is not null) return type;
        // A source alias must never rename a shared or structurally equal type.
        var owned = type with { };
        Aliases.Add(owned, new(name, [], declaration));
        return owned;
    }

    internal static void Substitute(TypeInfo original, TypeInfo replacement, Func<TypeInfo, TypeInfo> substitute)
    {
        // A concrete conditional result has consumed the alias; its primitive or
        // structural type must display the checked result rather than stale syntax.
        if (!ReferenceEquals(original, replacement) && Get(original) is { } alias &&
            replacement is TypeInfo.MappedType or TypeInfo.ConditionalType)
            Retain(replacement, alias.Name, alias.Arguments.Select(substitute).ToArray());
        if (!ReferenceEquals(original, replacement) && Get(original) is { Declaration: not null } source &&
            replacement is TypeInfo.Union)
            Aliases.AddOrUpdate(replacement, source with { Arguments = source.Arguments.Select(substitute).ToArray() });
    }
}
