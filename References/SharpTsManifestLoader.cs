using System.Text.Json;
using SharpTS.Configuration;

namespace SharpTS.References;

/// <summary>
/// Discovers and parses sharpts.json manifests.
/// </summary>
public static class SharpTsManifestLoader
{
    public const string FileName = "sharpts.json";

    private static JsonSerializerOptions JsonOptions => FileDiscovery.LenientJsonOptions;

    /// <summary>
    /// Finds and loads the nearest sharpts.json in <paramref name="startDirectory"/>
    /// or its parents. Walk policy (temp/user-profile ceilings) is
    /// <see cref="FileDiscovery.FindNearestFile"/>.
    /// </summary>
    /// <returns>The loaded manifest, or null when none exists.</returns>
    /// <exception cref="Exception">When a found manifest fails to parse — unlike
    /// discovery misses, a malformed manifest is a hard error naming the file.</exception>
    public static SharpTsManifest? FindAndLoad(string startDirectory)
    {
        var path = FileDiscovery.FindNearestFile(startDirectory, FileName);
        return path is null ? null : Load(path);
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
