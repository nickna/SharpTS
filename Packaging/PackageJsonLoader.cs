using System.Text.Json;
using SharpTS.Configuration;

namespace SharpTS.Packaging;

/// <summary>
/// Loads and parses package.json files for NuGet package metadata.
/// </summary>
public static class PackageJsonLoader
{
    private static JsonSerializerOptions JsonOptions => FileDiscovery.LenientJsonOptions;

    /// <summary>
    /// Attempts to find and load a package.json file in the specified directory or its parents.
    /// Walk policy (temp/user-profile ceilings, stop-directory exclusivity) is
    /// <see cref="FileDiscovery.FindNearestFile"/>.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from.</param>
    /// <param name="stopDirectory">Optional directory to stop searching at (exclusive). If specified, the search will not look in this directory or its parents.</param>
    /// <returns>Loaded PackageJson or null if not found.</returns>
    public static PackageJson? FindAndLoad(string startDirectory, string? stopDirectory = null)
    {
        var path = FileDiscovery.FindNearestFile(startDirectory, "package.json", stopDirectory);
        return path is null ? null : Load(path);
    }

    /// <summary>
    /// Loads a package.json file from the specified path.
    /// </summary>
    /// <param name="path">Path to the package.json file.</param>
    /// <returns>Loaded PackageJson.</returns>
    /// <exception cref="FileNotFoundException">If the file doesn't exist.</exception>
    /// <exception cref="JsonException">If the file contains invalid JSON.</exception>
    public static PackageJson Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"package.json not found at: {path}", path);

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PackageJson>(stream, JsonOptions)
            ?? throw new JsonException($"Failed to parse package.json at: {path}");
    }

    /// <summary>
    /// Attempts to load a package.json file, returning null on any error.
    /// </summary>
    public static PackageJson? TryLoad(string path)
    {
        try
        {
            return Load(path);
        }
        catch
        {
            return null;
        }
    }
}
