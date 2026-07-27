using System.Text.Json;

namespace SharpTS.Modules;

/// <summary>
/// Implements the Node.js package exports/imports resolution algorithm.
/// See: https://nodejs.org/api/packages.html#package-entry-points
/// </summary>
public static class ExportsResolver
{
    /// <summary>
    /// Conditions applied when resolving an ESM <c>import</c> against an exports map.
    /// Excludes <c>"require"</c> to match Node's resolution for ESM — otherwise a dual
    /// <c>{ require, import }</c> entry whose <c>require</c> key is listed first would
    /// always win regardless of how the caller is importing, and any package shipping
    /// divergent ESM/CJS entries (e.g. an ESM wrapper around CJS internals like uuid@9)
    /// would serve the wrong file.
    /// </summary>
    public static readonly string[] EsmConditions = ["node", "import", "default"];

    /// <summary>
    /// Conditions applied when resolving a CommonJS <c>require()</c> call against an exports map.
    /// Excludes <c>"import"</c> for the same reason <see cref="EsmConditions"/> excludes
    /// <c>"require"</c>: Node treats these as mutually exclusive per call site.
    /// </summary>
    public static readonly string[] CjsConditions = ["node", "require", "default"];

    public static readonly string[] TypeEsmConditions = ["types", "node", "import", "default"];
    public static readonly string[] TypeCjsConditions = ["types", "node", "require", "default"];

    /// <summary>
    /// Legacy combined condition set retained for callers that don't know their import
    /// kind. Prefer <see cref="EsmConditions"/> or <see cref="CjsConditions"/>.
    /// </summary>
    public static readonly string[] DefaultConditions = ["node", "require", "import", "default"];

    /// <summary>
    /// Resolves a subpath against a package's "exports" field.
    /// </summary>
    /// <param name="exports">The raw "exports" JsonElement from package.json</param>
    /// <param name="subpath">The subpath to resolve (e.g., "." or "./utils")</param>
    /// <param name="conditions">Condition names to match (e.g., ["types", "import", "default"])</param>
    /// <returns>The resolved relative path (e.g., "./src/index.ts"), or null if not found/blocked</returns>
    public static string? ResolvePackageExports(JsonElement exports, string subpath, string[] conditions)
    {
        // String or Array → shorthand for "." entry
        if (exports.ValueKind == JsonValueKind.String || exports.ValueKind == JsonValueKind.Array)
        {
            if (subpath == ".")
                return PackageTargetResolve(exports, patternMatch: null, isPattern: false, conditions);
            return null;
        }

        if (exports.ValueKind == JsonValueKind.Null)
            return null;

        if (exports.ValueKind != JsonValueKind.Object)
            return null;

        // Determine if this is a subpath map (keys start with ".") or conditions object
        bool hasSubpathKeys = false;
        bool hasConditionKeys = false;
        foreach (var prop in exports.EnumerateObject())
        {
            if (prop.Name.StartsWith('.'))
                hasSubpathKeys = true;
            else
                hasConditionKeys = true;
        }

        if (hasConditionKeys && !hasSubpathKeys)
        {
            // No-dot keys = conditions object → shorthand for "." entry
            if (subpath == ".")
                return PackageTargetResolve(exports, patternMatch: null, isPattern: false, conditions);
            return null;
        }

        if (hasSubpathKeys)
        {
            return ResolveSubpathExports(exports, subpath, conditions);
        }

        return null;
    }

    /// <summary>
    /// Resolves a specifier against a package's "imports" field.
    /// </summary>
    /// <param name="imports">The raw "imports" JsonElement from package.json</param>
    /// <param name="specifier">The full import specifier (e.g., "#utils")</param>
    /// <param name="conditions">Condition names to match</param>
    /// <returns>The resolved relative path, or null if not found</returns>
    public static string? ResolvePackageImports(JsonElement imports, string specifier, string[] conditions)
    {
        if (imports.ValueKind != JsonValueKind.Object)
            return null;

        // Exact match
        if (imports.TryGetProperty(specifier, out var exact))
        {
            return PackageTargetResolve(exact, patternMatch: null, isPattern: false, conditions);
        }

        // Wildcard pattern match (longest prefix wins)
        return MatchWildcardPattern(imports, specifier, conditions);
    }

    /// <summary>
    /// Resolves a single target value (string/object/array/null).
    /// </summary>
    internal static string? PackageTargetResolve(JsonElement target, string? patternMatch, bool isPattern, string[] conditions)
    {
        switch (target.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = target.GetString()!;
                // Target must start with "./" to be valid
                if (!value.StartsWith("./"))
                    return null;

                if (isPattern && patternMatch != null)
                {
                    // Substitute * with the pattern match
                    return value.Replace("*", patternMatch);
                }
                return value;
            }

            case JsonValueKind.Object:
            {
                // Iterate in insertion order; match first condition
                foreach (var prop in target.EnumerateObject())
                {
                    if (Array.IndexOf(conditions, prop.Name) >= 0)
                    {
                        var result = PackageTargetResolve(prop.Value, patternMatch, isPattern, conditions);
                        if (result != null)
                            return result;
                    }
                }
                return null;
            }

            case JsonValueKind.Array:
            {
                // Try each element as a fallback chain
                foreach (var element in target.EnumerateArray())
                {
                    var result = PackageTargetResolve(element, patternMatch, isPattern, conditions);
                    if (result != null)
                        return result;
                }
                return null;
            }

            case JsonValueKind.Null:
                // Explicitly blocked
                return null;

            default:
                return null;
        }
    }

    private static string? ResolveSubpathExports(JsonElement exports, string subpath, string[] conditions)
    {
        // Exact match first
        if (exports.TryGetProperty(subpath, out var exact))
        {
            return PackageTargetResolve(exact, patternMatch: null, isPattern: false, conditions);
        }

        // Wildcard pattern match (longest prefix wins)
        return MatchWildcardPattern(exports, subpath, conditions);
    }

    private static string? MatchWildcardPattern(JsonElement map, string key, string[] conditions)
    {
        string? bestPattern = null;
        int bestPrefixLen = -1;

        foreach (var prop in map.EnumerateObject())
        {
            var starIdx = prop.Name.IndexOf('*');
            if (starIdx < 0)
                continue;

            var prefix = prop.Name[..starIdx];
            var suffix = prop.Name[(starIdx + 1)..];

            if (key.StartsWith(prefix) && key.EndsWith(suffix) &&
                key.Length >= prefix.Length + suffix.Length)
            {
                if (prefix.Length > bestPrefixLen)
                {
                    bestPrefixLen = prefix.Length;
                    bestPattern = prop.Name;
                }
            }
        }

        if (bestPattern != null)
        {
            var starIdx = bestPattern.IndexOf('*');
            var prefix = bestPattern[..starIdx];
            var suffix = bestPattern[(starIdx + 1)..];
            var patternMatch = key[prefix.Length..^(suffix.Length > 0 ? suffix.Length : 0)];

            if (map.TryGetProperty(bestPattern, out var target))
            {
                return PackageTargetResolve(target, patternMatch, isPattern: true, conditions);
            }
        }

        return null;
    }
}
