// The packaged host and guest deliberately share hosted ABI 1.
#pragma warning disable SHARPTS_HOSTING001

using System.Text.Json;

namespace SharpTS.Gui.Host;

internal static class DepsAssetValidator
{
    public static IReadOnlyList<string> Validate(string publishDirectory)
    {
        // MSBuild's Windows Exec quoting preserves a literal trailing quote when
        // PublishDir itself ends in a backslash. Normalize that harmless shell
        // artifact before resolving the directory.
        string fullDirectory = Path.GetFullPath(publishDirectory.Trim().Trim('"'));
        var failures = new List<string>();
        string[] depsFiles = Directory.GetFiles(fullDirectory, "*.deps.json", SearchOption.TopDirectoryOnly);
        string? depsPath = depsFiles.FirstOrDefault(path =>
            !string.Equals(Path.GetFileName(path), "SharpTS.Gui.Host.deps.json", StringComparison.OrdinalIgnoreCase))
            ?? depsFiles.SingleOrDefault();
        if (depsPath is null)
            return [$"missing deps file in '{fullDirectory}'"];

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        string? runtimeTarget = document.RootElement
            .GetProperty("runtimeTarget")
            .GetProperty("name")
            .GetString();
        var targets = document.RootElement.GetProperty("targets");
        if (runtimeTarget == null || !targets.TryGetProperty(runtimeTarget, out var selectedTarget))
        {
            failures.Add($"deps file does not contain selected runtime target '{runtimeTarget}'");
            return failures;
        }

        foreach (var library in selectedTarget.EnumerateObject())
        {
            foreach (string groupName in new[] { "runtime", "native", "resources" })
            {
                if (!library.Value.TryGetProperty(groupName, out var assets))
                    continue;
                foreach (var asset in assets.EnumerateObject())
                {
                    if (!AssetExists(fullDirectory, asset.Name, groupName))
                        failures.Add($"missing selected {groupName} asset '{asset.Name}' ({library.Name})");
                }
            }
        }

        string[] requiredContent =
        [
            "SharpTS.Gui.Guest.dll",
            "SharpTS.Hosting.Abstractions.dll",
            Path.Combine(".sharpts", "app.json"),
            Path.Combine(".sharpts", "tsconfig.json"),
            Path.Combine(".sharpts", "node_modules", "@sharpts", "gui", "index.ts"),
            Path.Combine(".sharpts", "node_modules", "@sharpts", "gui", "jsx-runtime.ts"),
            Path.Combine(".sharpts", "node_modules", "@sharpts", "gui", "jsx-dev-runtime.ts"),
            Path.Combine(".sharpts", "node_modules", "@sharpts", "gui", "internal-testing.ts")
        ];
        foreach (string relativePath in requiredContent)
            if (!File.Exists(Path.Combine(fullDirectory, relativePath)))
                failures.Add($"missing required SharpTS GUI content '{relativePath}'");

        string manifestPath = Path.Combine(fullDirectory, ".sharpts", "app.json");
        if (File.Exists(manifestPath))
        {
            GuiAppManifest? manifest = JsonSerializer.Deserialize<GuiAppManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null)
            {
                failures.Add("SharpTS GUI application manifest is invalid");
            }
            else
            {
                if (manifest.HostedAbiVersion != SharpTS.Hosting.SharpTSHostedAbi.CurrentVersion)
                    failures.Add($"unsupported hosted ABI {manifest.HostedAbiVersion}");
                foreach (string relativePath in new[] { manifest.EntryPath, manifest.CompiledAssembly })
                    if (!File.Exists(ResolveContainedPath(fullDirectory, relativePath)))
                        failures.Add($"manifest payload is missing '{relativePath}'");
            }
        }

        return failures;
    }

    private static bool AssetExists(string root, string asset, string group)
    {
        string normalized = asset.Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(root, normalized)))
            return true;
        if (File.Exists(Path.Combine(root, Path.GetFileName(normalized))))
            return true;
        if (group == "resources")
        {
            string? culture = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrEmpty(culture) &&
                File.Exists(Path.Combine(root, Path.GetFileName(culture), Path.GetFileName(normalized))))
                return true;
        }
        return false;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SharpTS GUI manifest path escapes the application: {relativePath}");
        return candidate;
    }
}
