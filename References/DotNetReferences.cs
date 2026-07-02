using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.References;

/// <summary>
/// The single entry point for third-party assembly references (issue #1197):
/// discovers the sharpts.json manifest, restores its NuGet packages, and resolves
/// the full set of reference DLL paths from CLI -r flags + manifest references +
/// package runtime assets.
///
/// Two call shapes:
/// <list type="bullet">
/// <item><see cref="Resolve"/> — paths only, no loading. Used by the language server,
/// which inspects types through a MetadataLoadContext and must never execute
/// workspace code.</item>
/// <item><see cref="Load"/> — <see cref="Resolve"/> + <c>Assembly.LoadFrom</c> into the
/// default load context, so every existing resolution seam (interpreter, compiler,
/// --gen-decl, @DotNetType) finds the types via its AppDomain scan. Used by run,
/// compile, --gen-decl, and REPL modes before any module loading begins.</item>
/// </list>
/// </summary>
public static class DotNetReferences
{
    /// <summary>
    /// Discovers the manifest (upward walk from <paramref name="startDirectory"/>),
    /// restores packages when present, and resolves all reference paths.
    /// Load order: CLI -r (resolved against the current directory) → manifest
    /// references (resolved against the manifest directory) → package runtime
    /// closures; first occurrence wins de-duplication.
    /// </summary>
    /// <exception cref="Exception">Missing reference DLL, malformed manifest, or
    /// failed restore — all with messages naming the offending file/entry.</exception>
    public static ReferenceSet Resolve(string startDirectory, IReadOnlyList<string> cliReferences)
    {
        var manifest = SharpTsManifestLoader.FindAndLoad(startDirectory);
        if (manifest == null && cliReferences.Count == 0)
            return ReferenceSet.Empty;

        var seen = new HashSet<string>(PathComparer);
        var references = new List<ResolvedReference>();

        foreach (var cliRef in cliReferences)
        {
            string fullPath = Path.GetFullPath(cliRef);
            if (!File.Exists(fullPath))
            {
                throw new Exception(
                    $"Error: reference '{cliRef}' (from -r/--reference) not found" +
                    (fullPath != cliRef ? $" (resolved to '{fullPath}')." : "."));
            }
            if (seen.Add(fullPath))
                references.Add(new ResolvedReference(fullPath, ReferenceOrigin.Cli));
        }

        Dictionary<string, IReadOnlyList<string>>? packageClosures = null;
        if (manifest != null)
        {
            foreach (var entry in manifest.References ?? [])
            {
                string fullPath = Path.GetFullPath(Path.Combine(manifest.ManifestDirectory, entry));
                if (!File.Exists(fullPath))
                {
                    throw new Exception(
                        $"Error: sharpts.json ('{manifest.ManifestPath}'): reference '{entry}' not found " +
                        $"(resolved to '{fullPath}'). Check the path or remove the entry.");
                }
                if (seen.Add(fullPath))
                    references.Add(new ResolvedReference(fullPath, ReferenceOrigin.Manifest));
            }

            if (manifest.Packages is { Count: > 0 })
            {
                var restore = NuGetRestorer.Restore(manifest);
                packageClosures = restore.PackageClosures;
                foreach (var asset in restore.RuntimeAssets)
                {
                    if (seen.Add(asset.Path))
                        references.Add(new ResolvedReference(asset.Path, ReferenceOrigin.Package, asset.PackageId));
                }
            }
        }

        return new ReferenceSet(manifest?.ManifestPath, references, packageClosures);
    }

    /// <summary>
    /// <see cref="Resolve"/> + loads every resolved assembly into the default load
    /// context. Must run before module loading / type checking so the AppDomain
    /// scans in DotNetTypeRegistry, ILCompiler, and DiscoveryGenerator see the types.
    /// Idempotent: re-loading the same path returns the already-loaded assembly.
    /// </summary>
    public static ReferenceSet Load(string startDirectory, IReadOnlyList<string> cliReferences)
    {
        var set = Resolve(startDirectory, cliReferences);
        if (set.IsEmpty) return set;

        List<string>? failures = null;
        foreach (var reference in set.References)
        {
            try
            {
                var assembly = Assembly.LoadFrom(reference.Path);
                if (assembly.GetCustomAttribute<ReferenceAssemblyAttribute>() != null)
                {
                    (failures ??= []).Add(
                        $"'{reference.Path}' is a reference assembly (metadata only, no executable code). " +
                        "Point at the implementation assembly (e.g. bin/, not obj/ref/).");
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
            {
                (failures ??= []).Add($"'{reference.Path}' could not be loaded: {ex.Message}");
            }
        }

        if (failures != null)
        {
            string origin = set.ManifestPath != null ? $" (manifest: '{set.ManifestPath}')" : "";
            throw new Exception(
                $"Error: failed to load reference assembl{(failures.Count == 1 ? "y" : "ies")}{origin}:\n  " +
                string.Join("\n  ", failures));
        }

        return set;
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
