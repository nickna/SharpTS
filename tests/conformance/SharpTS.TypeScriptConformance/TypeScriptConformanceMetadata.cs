namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Header directives extracted from a TypeScript conformance test, plus the
/// virtual files declared by <c>// @filename:</c> markers.
///
/// Tests use the <c>// @&lt;key&gt;: &lt;value&gt;</c> syntax instead of YAML
/// frontmatter (the JS-spec equivalent is <see cref="SharpTS.Test262.Test262Metadata"/>).
/// Directives can appear anywhere in the source — the parser scans the full
/// file. <c>RawDirectives</c> contains every directive seen (lower-cased keys);
/// the typed fields are denormalized convenience for the most common keys.
/// </summary>
public sealed record TypeScriptConformanceMetadata(
    string TestPath,
    string? Target,
    string? Module,
    string? Jsx,
    bool Strict,
    bool? NoImplicitAny,
    bool? StrictNullChecks,
    bool NoEmit,
    IReadOnlyList<string> Lib,
    IReadOnlyDictionary<string, string> RawDirectives,
    IReadOnlyList<TypeScriptConformanceFile> Files)
{
    /// <summary>True if the test declared any of the given directive keys (case-insensitive).</summary>
    public bool HasAnyDirective(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (RawDirectives.ContainsKey(key.ToLowerInvariant())) return true;
        }
        return false;
    }
}

/// <summary>One virtual file declared by a <c>// @filename:</c> marker.
/// Single-file tests produce one <c>TypeScriptConformanceFile</c> named after
/// the test's basename.</summary>
public sealed record TypeScriptConformanceFile(string Name, string Body);
