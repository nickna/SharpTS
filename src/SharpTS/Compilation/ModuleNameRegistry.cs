using System.Collections.ObjectModel;

namespace SharpTS.Compilation;

/// <summary>Allocates collision-free emitted prefixes from canonical module paths.</summary>
internal static class ModuleNameRegistry
{
    public static IReadOnlyDictionary<string, string> Collect(IEnumerable<string> modulePaths)
    {
        var groups = modulePaths.Distinct(StringComparer.Ordinal)
            .GroupBy(path => CompilationContext.SanitizeModuleName(Path.GetFileNameWithoutExtension(path)), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var used = new HashSet<string>(groups.Select(group => group.Key), StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var paths = group.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (paths.Length == 1)
            {
                names.Add(paths[0], group.Key);
                continue;
            }
            int suffix = 0;
            foreach (var path in paths)
            {
                string name;
                do { name = $"{group.Key}__{suffix++}"; } while (!used.Add(name));
                names.Add(path, name);
            }
        }
        return new ReadOnlyDictionary<string, string>(names);
    }
}
