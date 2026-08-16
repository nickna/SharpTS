using System.Text;
using System.Text.RegularExpressions;

namespace SharpTS.Configuration;

/// <summary>
/// Expands a tsconfig's <c>files</c>/<c>include</c>/<c>exclude</c> fields into root files.
/// Imported files are intentionally not filtered here; the module resolver adds those later.
/// </summary>
internal static class TsConfigFileMatcher
{
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string>? files,
        IReadOnlyList<string>? includes,
        IReadOnlyList<string>? excludes,
        string configDirectory,
        string? outDir,
        bool allowJs)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var roots = new HashSet<string>(comparer);

        // `files` entries are never filtered by `exclude`.
        if (files is not null)
        {
            foreach (string file in files)
            {
                string full = Path.GetFullPath(file);
                if (!File.Exists(full))
                    throw new Exception($"Error: tsconfig.json: file '{full}' listed in 'files' does not exist.");
                if (IsSupportedSource(full, allowJs))
                    roots.Add(full);
            }
        }

        // An explicit `files: []` with no include means exactly no roots. When neither field
        // appears, TypeScript's implicit include is **/*.
        IReadOnlyList<string> effectiveIncludes = includes
            ?? (files is null ? [Path.Combine(configDirectory, "**", "*")] : []);

        var effectiveExcludes = excludes is null
            ? new List<string>
            {
                Path.Combine(configDirectory, "node_modules"),
                Path.Combine(configDirectory, "bower_components"),
                Path.Combine(configDirectory, "jspm_packages"),
            }
            : [.. excludes];
        if (excludes is null && outDir is not null)
            effectiveExcludes.Add(outDir);

        var excludeMatchers = effectiveExcludes
            .Select(pattern => CreateMatcher(pattern, matchDirectoryDescendants: true))
            .ToArray();
        foreach (string include in effectiveIncludes)
        {
            string pattern = NormalizeInclude(include);
            foreach (string candidate in EnumerateCandidates(pattern))
            {
                string full = Path.GetFullPath(candidate);
                if (!IsSupportedSource(full, allowJs))
                    continue;
                if (excludeMatchers.Any(m => m(full)))
                    continue;
                roots.Add(full);
            }
        }

        return roots.OrderBy(p => p, comparer).ToArray();
    }

    private static bool IsSupportedSource(string path, bool allowJs)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            return true;

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".ts" or ".tsx" or ".mts" or ".cts")
            return true;
        return allowJs && extension is ".js" or ".jsx" or ".mjs" or ".cjs";
    }

    private static string NormalizeInclude(string include)
    {
        string full = Path.GetFullPath(include);
        if (ContainsWildcard(full))
            return full;
        if (File.Exists(full) || Path.HasExtension(full))
            return full;
        return Path.Combine(full, "**", "*");
    }

    private static IEnumerable<string> EnumerateCandidates(string pattern)
    {
        if (!ContainsWildcard(pattern))
        {
            if (File.Exists(pattern))
                yield return pattern;
            yield break;
        }

        string root = SearchRoot(pattern);
        if (!Directory.Exists(root))
            yield break;

        var matches = CreateMatcher(pattern);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (string file in Directory.EnumerateFiles(root, "*", enumeration))
        {
            if (matches(file))
                yield return file;
        }
    }

    private static string SearchRoot(string pattern)
    {
        int wildcard = pattern.IndexOfAny(['*', '?']);
        string prefix = wildcard < 0 ? pattern : pattern[..wildcard];
        bool endsAtDirectoryBoundary =
            prefix.EndsWith(Path.DirectorySeparatorChar) ||
            prefix.EndsWith(Path.AltDirectorySeparatorChar);
        if (endsAtDirectoryBoundary)
            return Path.GetFullPath(prefix);

        string trimmed = prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? root = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(root))
            root = Path.GetPathRoot(Path.GetFullPath(pattern));
        return root ?? Directory.GetCurrentDirectory();
    }

    private static Func<string, bool> CreateMatcher(
        string pattern,
        bool matchDirectoryDescendants = false)
    {
        string full = Path.GetFullPath(pattern);
        string finalSegment = Path.GetFileName(full);
        if (matchDirectoryDescendants &&
            !Path.HasExtension(finalSegment) &&
            !ContainsWildcard(finalSegment))
        {
            full = Path.Combine(full, "**", "*");
        }
        if (!ContainsWildcard(full) && !Path.HasExtension(full))
            full = Path.Combine(full, "**", "*");

        string normalized = full.Replace('\\', '/').TrimEnd('/');
        var regex = new StringBuilder("^");
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (c == '*' && i + 1 < normalized.Length && normalized[i + 1] == '*')
            {
                i++;
                if (i + 1 < normalized.Length && normalized[i + 1] == '/')
                {
                    i++;
                    regex.Append("(?:.*/)?");
                }
                else
                {
                    regex.Append(".*");
                }
            }
            else if (c == '*')
            {
                regex.Append("[^/]*");
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        regex.Append('$');

        var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
        if (OperatingSystem.IsWindows())
            options |= RegexOptions.IgnoreCase;
        var matcher = new Regex(regex.ToString(), options);
        return path => matcher.IsMatch(Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/'));
    }

    private static bool ContainsWildcard(string value) =>
        value.IndexOfAny(['*', '?']) >= 0;
}
