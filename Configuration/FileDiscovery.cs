using System.Text.Json;

namespace SharpTS.Configuration;

/// <summary>
/// Shared upward config/manifest-file discovery used by <see cref="TsConfigLoader"/>,
/// <see cref="Packaging.PackageJsonLoader"/>, and <see cref="References.SharpTsManifestLoader"/>,
/// so all three loaders apply one walk policy.
/// </summary>
internal static class FileDiscovery
{
    /// <summary>
    /// The lenient parse options every config/manifest loader uses: case-insensitive keys,
    /// comments, and trailing commas allowed. Shared read-only instance — never mutate.
    /// </summary>
    internal static readonly JsonSerializerOptions LenientJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Returns the full path of the nearest <paramref name="fileName"/> in
    /// <paramref name="startDirectory"/> or its parents, or null.
    /// </summary>
    /// <remarks>
    /// The upward walk stops (exclusive) at the system temp root and the user profile root — a
    /// config file sitting there is ambient noise from unrelated tooling, not part of the project
    /// being run. Each ceiling is still searched when it IS the start directory; the walk only
    /// stops when it ASCENDS into a ceiling. An explicit <paramref name="stopDirectory"/> is
    /// always exclusive: neither it nor its parents are searched, even as the start directory.
    /// Path comparison is case-insensitive (ceilings are OS well-known directories).
    /// </remarks>
    internal static string? FindNearestFile(string startDirectory, string fileName, string? stopDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory);
        var stopDirFullName = stopDirectory != null
            ? NormalizeDir(new DirectoryInfo(stopDirectory).FullName)
            : null;

        var ceilings = new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => NormalizeDir(Path.GetFullPath(p)))
            .ToArray();

        bool isStartDirectory = true;
        while (dir != null)
        {
            var currentPath = NormalizeDir(dir.FullName);

            if (stopDirFullName != null &&
                string.Equals(currentPath, stopDirFullName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!isStartDirectory &&
                ceilings.Any(c => string.Equals(currentPath, c, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            isStartDirectory = false;
            dir = dir.Parent;
        }

        return null;
    }

    private static string NormalizeDir(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
