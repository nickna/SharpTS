using System.Text.Json;

namespace SharpTS.Configuration;

/// <summary>
/// Shared upward config/manifest-file discovery used by <see cref="TsConfigLoader"/>,
/// <see cref="Packaging.PackageJsonLoader"/>, and <see cref="References.SharpTsManifestLoader"/>,
/// so all three loaders apply one walk policy. Ambient walks elsewhere (module resolution's
/// node_modules/@types/package.json probes) share the same ceilings via
/// <see cref="AmbientParent"/>.
/// </summary>
internal static class FileDiscovery
{

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

        var ceilings = WalkCeilings();

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

    /// <summary>
    /// The parent of <paramref name="directory"/> for ambient upward walks, or null when
    /// ascending would enter a ceiling (system temp root or user profile root). The caller's
    /// start directory is always probed — even when it is itself a ceiling — matching the
    /// <see cref="FindNearestFile"/> policy: files directly under a ceiling are ambient noise
    /// from unrelated tooling, not part of the program being resolved.
    /// </summary>
    internal static string? AmbientParent(string directory)
    {
        string? parent = Path.GetDirectoryName(NormalizeDir(directory));
        if (string.IsNullOrEmpty(parent))
            return null;
        string normalized = NormalizeDir(Path.GetFullPath(parent));
        return WalkCeilings().Any(c => string.Equals(normalized, c, StringComparison.OrdinalIgnoreCase))
            ? null
            : parent;
    }

    // Recomputed per call: tests and embedders may redirect TMP/TEMP at runtime.
    private static string[] WalkCeilings() =>
        new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => NormalizeDir(Path.GetFullPath(p)))
            .ToArray();

    private static string NormalizeDir(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
