using System.Text.Json;

namespace SharpTS.Packaging;

/// <summary>
/// Loads and parses package.json files for NuGet package metadata.
/// </summary>
public static class PackageJsonLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Attempts to find and load a package.json file in the specified directory or its parents.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from.</param>
    /// <returns>Loaded PackageJson or null if not found.</returns>
    public static PackageJson? FindAndLoad(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        while (dir != null)
        {
            var packageJsonPath = Path.Combine(dir.FullName, "package.json");
            if (File.Exists(packageJsonPath))
            {
                return Load(packageJsonPath);
            }
            dir = dir.Parent;
        }

        return null;
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
