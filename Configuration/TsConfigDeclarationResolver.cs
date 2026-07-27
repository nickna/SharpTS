using System.Text.Json;

namespace SharpTS.Configuration;

/// <summary>Resolves explicitly selected TypeScript libs and visible <c>@types</c> packages.</summary>
internal static class TsConfigDeclarationResolver
{
    public static IReadOnlyList<string> Resolve(
        string configDirectory,
        IReadOnlyList<string>? libs,
        IReadOnlyList<string>? types,
        IReadOnlyList<string>? typeRoots)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var result = new HashSet<string>(comparer);

        // lib.*.d.ts inputs are provided by ModuleResolver's pinned compiler
        // library graph. Keeping them out of this physical-file list means a
        // project does not need an npm-installed copy of TypeScript.

        var roots = typeRoots?.ToArray() ?? FindVisibleTypeRoots(configDirectory).ToArray();
        if (types is not null)
        {
            foreach (string type in types)
            {
                // Configuration loading records the selection even when packages
                // are not installed yet. Program loading produces the diagnostic.
                try { result.Add(ResolveTypePackage(type, roots)); }
                catch (Exception) { }
            }
        }
        else
        {
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(directory);
                    if (name.StartsWith('.'))
                        continue;
                    string? entry = TryFindDeclarationEntry(directory);
                    if (entry is not null)
                        result.Add(entry);
                }
            }
        }

        return result.OrderBy(p => p, comparer).ToArray();
    }

    public static string ResolveLibReference(string containingFile, string name) =>
        ResolveLib(Path.GetDirectoryName(Path.GetFullPath(containingFile))!, name);

    public static string ResolveTypeReference(
        string containingFile,
        string name,
        IReadOnlyList<string>? typeRoots)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(containingFile))!;
        var roots = typeRoots?.ToArray() ?? FindVisibleTypeRoots(directory).ToArray();
        return ResolveTypePackage(name, roots);
    }

    private static string ResolveLib(string configDirectory, string name)
    {
        string fileName = name.StartsWith("lib.", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"lib.{name}";
        if (!fileName.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            fileName += ".d.ts";

        foreach (string directory in Ancestors(configDirectory))
        {
            string candidate = Path.Combine(directory, "node_modules", "typescript", "lib", fileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new Exception(
            $"Error: tsconfig.json: cannot resolve lib '{name}'. Install the 'typescript' package " +
            $"so '{fileName}' is available under node_modules/typescript/lib.");
    }

    private static string ResolveTypePackage(string type, IReadOnlyList<string> roots)
    {
        string packageDirectoryName = type.StartsWith('@')
            ? type[1..].Replace("/", "__", StringComparison.Ordinal)
            : type;

        foreach (string root in roots)
        {
            string directory = Path.Combine(root, packageDirectoryName);
            string? entry = TryFindDeclarationEntry(directory);
            if (entry is not null)
                return entry;
        }

        throw new Exception(
            $"Error: tsconfig.json: cannot find type definition package '{type}' in: " +
            string.Join(", ", roots));
    }

    private static string? TryFindDeclarationEntry(string packageDirectory)
    {
        if (!Directory.Exists(packageDirectory))
            return null;

        string packageJson = Path.Combine(packageDirectory, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                using var document = JsonDocument.Parse(
                    File.ReadAllText(packageJson),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = document.RootElement;
                foreach (string key in new[] { "types", "typings" })
                {
                    if (root.TryGetProperty(key, out var property) &&
                        property.ValueKind == JsonValueKind.String)
                    {
                        string candidate = Path.GetFullPath(
                            Path.Combine(packageDirectory, property.GetString()!));
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }
            catch (JsonException)
            {
                // Module loading will produce the actionable package error if this package is imported.
            }
        }

        string index = Path.Combine(packageDirectory, "index.d.ts");
        return File.Exists(index) ? Path.GetFullPath(index) : null;
    }

    private static IEnumerable<string> FindVisibleTypeRoots(string startDirectory)
    {
        string[] ceilings =
            new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar))
            .ToArray();
        bool isStartDirectory = true;
        foreach (string directory in Ancestors(startDirectory))
        {
            string normalized = directory.TrimEnd(Path.DirectorySeparatorChar);
            if (!isStartDirectory && ceilings.Any(ceiling =>
                    string.Equals(normalized, ceiling, StringComparison.OrdinalIgnoreCase)))
            {
                yield break;
            }

            string root = Path.Combine(directory, "node_modules", "@types");
            if (Directory.Exists(root))
                yield return Path.GetFullPath(root);
            isStartDirectory = false;
        }
    }

    private static IEnumerable<string> Ancestors(string startDirectory)
    {
        string? directory = Path.GetFullPath(startDirectory);
        while (directory is not null)
        {
            yield return directory;
            directory = Path.GetDirectoryName(directory);
        }
    }
}
