using System.Text.Json;

namespace SharpTS.References;

/// <summary>
/// Discovers and parses sharpts.json manifests.
/// </summary>
public static class SharpTsManifestLoader
{
    public const string FileName = "sharpts.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Finds and loads the nearest sharpts.json in <paramref name="startDirectory"/>
    /// or its parents.
    /// </summary>
    /// <remarks>
    /// The upward walk stops (exclusive) at the system temp root and the user profile
    /// root — a sharpts.json sitting in those directories is ambient noise from
    /// unrelated tooling, not the manifest of the project being run. Each ceiling is
    /// still searched when it IS the start directory. (Same policy as
    /// <see cref="Packaging.PackageJsonLoader.FindAndLoad"/>.)
    /// </remarks>
    /// <returns>The loaded manifest, or null when none exists.</returns>
    /// <exception cref="Exception">When a found manifest fails to parse — unlike
    /// discovery misses, a malformed manifest is a hard error naming the file.</exception>
    public static SharpTsManifest? FindAndLoad(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        var ceilings = new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        bool isStartDirectory = true;
        while (dir != null)
        {
            var currentPath = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Stop when the walk ASCENDS into a ceiling directory; only search
            // a ceiling when the caller started there.
            if (!isStartDirectory &&
                ceilings.Any(c => string.Equals(currentPath, c, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var manifestPath = Path.Combine(dir.FullName, FileName);
            if (File.Exists(manifestPath))
            {
                return Load(manifestPath);
            }
            isStartDirectory = false;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Loads a sharpts.json from an explicit path. Malformed JSON is a hard error
    /// (the user asked for this manifest to apply; silently ignoring it would make
    /// every dotnet: import fail with a misleading "type not found").
    /// </summary>
    public static SharpTsManifest Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"sharpts.json not found at: {path}", path);

        SharpTsManifest? manifest;
        try
        {
            using var stream = File.OpenRead(path);
            manifest = JsonSerializer.Deserialize<SharpTsManifest>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Error: sharpts.json ('{path}') is not valid JSON: {ex.Message}");
        }

        if (manifest == null)
            throw new Exception($"Error: sharpts.json ('{path}') is empty or null.");

        manifest.ManifestPath = Path.GetFullPath(path);
        return manifest;
    }
}
