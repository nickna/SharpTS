namespace SharpTS.References;

/// <summary>Where a resolved reference DLL came from.</summary>
public enum ReferenceOrigin
{
    /// <summary>CLI -r/--reference flag.</summary>
    Cli,
    /// <summary>The sharpts.json "references" list.</summary>
    Manifest,
    /// <summary>A restored NuGet package's runtime assets.</summary>
    Package
}

/// <summary>One resolved reference assembly path.</summary>
/// <param name="Path">Absolute path to the DLL.</param>
/// <param name="Origin">Which surface supplied it.</param>
/// <param name="PackageId">NuGet package id for <see cref="ReferenceOrigin.Package"/> origins.</param>
public sealed record ResolvedReference(string Path, ReferenceOrigin Origin, string? PackageId = null);

/// <summary>
/// The full set of third-party reference assemblies for one invocation:
/// CLI -r flags + sharpts.json references + restored NuGet package assets,
/// de-duplicated, in deterministic load order (CLI first, so a per-invocation
/// override wins the AppDomain first-match scan).
/// </summary>
public sealed class ReferenceSet
{
    public static readonly ReferenceSet Empty = new(null, []);

    /// <summary>Absolute path of the discovered manifest, or null when none.</summary>
    public string? ManifestPath { get; }

    /// <summary>Resolved references in load order.</summary>
    public IReadOnlyList<ResolvedReference> References { get; }

    private readonly Dictionary<string, IReadOnlyList<string>> _packageClosures;

    public ReferenceSet(
        string? manifestPath,
        IReadOnlyList<ResolvedReference> references,
        Dictionary<string, IReadOnlyList<string>>? packageClosures = null)
    {
        ManifestPath = manifestPath;
        References = references;
        _packageClosures = packageClosures ?? [];
    }

    public bool IsEmpty => References.Count == 0;

    /// <summary>
    /// The runtime-asset closure (this package plus its transitive package
    /// dependencies) for a <see cref="ReferenceOrigin.Package"/> reference,
    /// from the restore assets graph. Empty for non-package origins.
    /// </summary>
    public IReadOnlyList<string> RuntimeClosureFor(ResolvedReference reference)
    {
        if (reference.PackageId != null &&
            _packageClosures.TryGetValue(reference.PackageId, out var closure))
        {
            return closure;
        }
        return [];
    }

    /// <summary>Finds the reference entry for an assembly file path, if any.</summary>
    public ResolvedReference? FindByPath(string assemblyPath)
    {
        foreach (var reference in References)
        {
            if (string.Equals(reference.Path, assemblyPath, PathComparison))
                return reference;
        }
        return null;
    }

    internal static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
