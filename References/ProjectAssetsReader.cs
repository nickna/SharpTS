using System.Text.Json;

namespace SharpTS.References;

/// <summary>
/// Reads the parts of a NuGet <c>project.assets.json</c> the reference story needs:
/// each package's runtime DLL assets (absolute paths into the package folders) and
/// the package dependency edges, from which per-package transitive closures are
/// computed for the compiled-output copy step.
/// </summary>
internal static class ProjectAssetsReader
{
    public static RestoreResult Read(string assetsPath, string manifestPath, string targetFramework)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = doc.RootElement;

        // Package install roots (usually just ~/.nuget/packages/, but nuget.config
        // can redirect globalPackagesFolder — hermetic tests rely on that).
        var packageFolders = new List<string>();
        if (root.TryGetProperty("packageFolders", out var folders))
        {
            foreach (var folder in folders.EnumerateObject())
                packageFolders.Add(folder.Name);
        }

        // libraries: "Id/Version" -> { "path": "id/version", ... }
        var libraryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var lib in libraries.EnumerateObject())
            {
                if (lib.Value.TryGetProperty("path", out var path))
                    libraryPaths[lib.Name] = path.GetString() ?? "";
            }
        }

        if (!root.TryGetProperty("targets", out var targets))
            throw new Exception($"Error: '{assetsPath}' has no targets section (corrupt restore for '{manifestPath}'?).");

        // Exact-TFM target preferred; fall back to the first target defensively
        // (single-TFM restore project, so there is only ever one in practice).
        JsonElement target = default;
        bool found = false;
        foreach (var t in targets.EnumerateObject())
        {
            if (!found || t.Name == targetFramework)
            {
                target = t.Value;
                found = true;
                if (t.Name == targetFramework) break;
            }
        }
        if (!found)
            throw new Exception($"Error: '{assetsPath}' has no restore targets (corrupt restore for '{manifestPath}'?).");

        var assetsByPackage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var dependencyEdges = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in target.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("type", out var type) || type.GetString() != "package")
                continue;

            string packageId = entry.Name.Split('/')[0];
            var assets = assetsByPackage[packageId] = [];
            var deps = dependencyEdges[packageId] = [];

            if (entry.Value.TryGetProperty("runtime", out var runtime))
            {
                foreach (var asset in runtime.EnumerateObject())
                {
                    // "_._" placeholders mark intentionally-empty asset groups.
                    if (asset.Name.EndsWith("_._", StringComparison.Ordinal)) continue;
                    string? absolute = ResolveAssetPath(entry.Name, asset.Name, libraryPaths, packageFolders);
                    if (absolute != null) assets.Add(absolute);
                }
            }

            if (entry.Value.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dep in dependencies.EnumerateObject())
                    deps.Add(dep.Name);
            }
        }

        // Flat runtime-asset list in deterministic order.
        var runtimeAssets = new List<PackageAsset>();
        foreach (var packageId in assetsByPackage.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var asset in assetsByPackage[packageId].OrderBy(a => a, StringComparer.Ordinal))
                runtimeAssets.Add(new PackageAsset(asset, packageId));
        }

        // Per-package transitive closure of runtime assets.
        var closures = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var packageId in assetsByPackage.Keys)
        {
            var closure = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(packageId);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!visited.Add(current)) continue;
                if (assetsByPackage.TryGetValue(current, out var assets))
                    closure.AddRange(assets);
                if (dependencyEdges.TryGetValue(current, out var deps))
                {
                    foreach (var dep in deps) queue.Enqueue(dep);
                }
            }
            closures[packageId] = closure;
        }

        return new RestoreResult(runtimeAssets, closures);
    }

    private static string? ResolveAssetPath(
        string libraryKey, string assetRelativePath,
        Dictionary<string, string> libraryPaths, List<string> packageFolders)
    {
        if (!libraryPaths.TryGetValue(libraryKey, out var libraryPath)) return null;

        string relative = Path.Combine(
            libraryPath.Replace('/', Path.DirectorySeparatorChar),
            assetRelativePath.Replace('/', Path.DirectorySeparatorChar));

        foreach (var folder in packageFolders)
        {
            string candidate = Path.GetFullPath(Path.Combine(folder, relative));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
