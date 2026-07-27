namespace SharpTS.Configuration;

/// <summary>
/// TypeScript module resolution strategies understood by SharpTS.
/// </summary>
public enum ModuleResolutionMode
{
    /// <summary>TypeScript's pre-Node resolver. Bare names resolve only through paths/baseUrl.</summary>
    Classic,

    /// <summary>Legacy Node resolution. Package exports/imports maps are not consulted.</summary>
    Node10,

    /// <summary>Node's dual ESM/CommonJS resolver, including package exports/imports.</summary>
    Node16,

    /// <summary>Node16 behavior with the modern NodeNext spelling.</summary>
    NodeNext,

    /// <summary>Bundler-oriented resolution with paths and package exports/imports.</summary>
    Bundler,
}

/// <summary>
/// Fully resolved, path-absolute module resolution configuration.
/// </summary>
public sealed record ModuleResolutionOptions(
    ModuleResolutionMode Mode,
    string? BaseUrl,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Paths,
    IReadOnlyList<string>? TypeRoots = null)
{
    public static ModuleResolutionOptions Default { get; } =
        new(ModuleResolutionMode.Node16, null,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public bool UsesPackageMaps =>
        Mode is ModuleResolutionMode.Node16 or ModuleResolutionMode.NodeNext or ModuleResolutionMode.Bundler;
}
