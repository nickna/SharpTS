using System.Runtime.CompilerServices;

namespace SharpTS.TypeSystem;

/// <summary>Source alias provenance for still-deferred semantic types; never participates in type identity.</summary>
internal static class DeferredTypeAliasPresentation
{
    internal sealed record Alias(string Name, IReadOnlyList<TypeInfo> Arguments);
    private static readonly ConditionalWeakTable<TypeInfo, Alias> Aliases = new();

    internal static TypeInfo Retain(TypeInfo type, string name, IReadOnlyList<TypeInfo> arguments)
    {
        if (type is TypeInfo.MappedType or TypeInfo.ConditionalType)
            Aliases.AddOrUpdate(type, new(name, arguments.ToArray()));
        return type;
    }

    internal static Alias? Get(TypeInfo type) => Aliases.TryGetValue(type, out var alias) ? alias : null;

    internal static void Substitute(TypeInfo original, TypeInfo replacement, Func<TypeInfo, TypeInfo> substitute)
    {
        // A concrete conditional result has consumed the alias; its primitive or
        // structural type must display the checked result rather than stale syntax.
        if (!ReferenceEquals(original, replacement) && Get(original) is { } alias &&
            replacement is TypeInfo.MappedType or TypeInfo.ConditionalType)
            Retain(replacement, alias.Name, alias.Arguments.Select(substitute).ToArray());
    }
}
