using SharpTS.Configuration;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Discovers configured TypeScript projects inside the initialized LSP workspace.
/// </summary>
internal sealed record NavigationProjectCatalog(
    IReadOnlyList<TsConfigResult> Projects,
    IReadOnlyList<string> ConfigPaths,
    bool IsComplete)
{
    private static readonly HashSet<string> SkippedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".claude",
            ".codex",
            "bin",
            "bower_components",
            "jspm_packages",
            "node_modules",
            "obj",
        };

    public static NavigationProjectCatalog Discover(
        IReadOnlyList<string> workspaceRoots)
    {
        var roots = workspaceRoots
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .Order(PathComparer)
            .ToArray();
        if (roots.Length == 0)
            return new NavigationProjectCatalog([], [], IsComplete: false);

        var (discovered, scanComplete) = FindConfigPaths(roots);
        var pending = new Queue<string>(discovered);
        var visited = new HashSet<string>(PathComparer);
        var configPaths = new List<string>();
        var projects = new List<TsConfigResult>();
        bool isComplete = scanComplete;

        while (pending.TryDequeue(out string? configPath))
        {
            string full = Path.GetFullPath(configPath);
            if (!visited.Add(full))
                continue;

            configPaths.Add(full);
            TsConfigResult project;
            try
            {
                project = TsConfigLoader.Load(full);
                projects.Add(project);
            }
            catch
            {
                isComplete = false;
                continue;
            }

            foreach (string reference in project.ProjectReferences)
            {
                if (roots.Any(root => IsWithin(reference, root)))
                    pending.Enqueue(reference);
                else
                    isComplete = false;
            }
        }

        return new NavigationProjectCatalog(
            projects.OrderBy(project => project.ConfigPath, PathComparer).ToArray(),
            configPaths.Order(PathComparer).ToArray(),
            isComplete);
    }

    private static (IReadOnlyList<string> Paths, bool IsComplete) FindConfigPaths(
        IReadOnlyList<string> roots)
    {
        var paths = new HashSet<string>(PathComparer);
        var visitedDirectories = new HashSet<string>(PathComparer);
        var pending = new Queue<string>(roots);
        bool isComplete = true;

        while (pending.TryDequeue(out string? directory))
        {
            string fullDirectory = Path.GetFullPath(directory);
            if (!visitedDirectories.Add(fullDirectory))
                continue;
            if (!Directory.Exists(fullDirectory))
            {
                isComplete = false;
                continue;
            }

            try
            {
                foreach (string config in Directory.EnumerateFiles(
                             fullDirectory,
                             TsConfigLoader.FileName,
                             SearchOption.TopDirectoryOnly))
                {
                    paths.Add(Path.GetFullPath(config));
                }

                foreach (string child in Directory.EnumerateDirectories(
                             fullDirectory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (SkippedDirectoryNames.Contains(Path.GetFileName(child)))
                        continue;
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    pending.Enqueue(child);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                isComplete = false;
            }
        }

        return (paths.Order(PathComparer).ToArray(), isComplete);
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
