namespace SharpTS.TypeScriptConformance;

/// <summary>The committed TypeScript baseline family requested by a conformance track.</summary>
public enum TypeScriptBaselineKind
{
    Errors,
    Types,
}

/// <summary>The result of resolving a configured TypeScript baseline variant.</summary>
public enum TypeScriptBaselineResolutionStatus
{
    Found,
    NoBaseline,
    Ambiguous,
}

/// <summary>
/// A deterministic baseline lookup result. <see cref="ExpectedPath"/> is always the conventional
/// unconfigured path and is useful in diagnostics when no compatible file exists.
/// </summary>
public sealed record TypeScriptBaselineResolution(
    TypeScriptBaselineResolutionStatus Status,
    string ExpectedPath,
    string? Path,
    IReadOnlyList<string> Candidates)
{
    public static TypeScriptBaselineResolution Found(string expectedPath, string path) =>
        new(TypeScriptBaselineResolutionStatus.Found, expectedPath, path, [path]);

    public static TypeScriptBaselineResolution NoBaseline(string expectedPath) =>
        new(TypeScriptBaselineResolutionStatus.NoBaseline, expectedPath, null, []);

    public static TypeScriptBaselineResolution Ambiguous(
        string expectedPath,
        IReadOnlyList<string> candidates) =>
        new(TypeScriptBaselineResolutionStatus.Ambiguous, expectedPath, null, candidates);
}

/// <summary>
/// Resolves plain and target/module-configured baselines using one algorithm for diagnostics and
/// inferred types. Axes that SharpTS does not model are deliberately rejected rather than guessed.
/// </summary>
public static class TypeScriptBaselineResolver
{
    public static TypeScriptBaselineResolution Resolve(
        string typescriptRoot,
        string testFilePath,
        TypeScriptConformanceMetadata metadata,
        TypeScriptBaselineKind kind)
    {
        string basename = System.IO.Path.GetFileNameWithoutExtension(testFilePath);
        string baselinesDir = TypeScriptConformancePaths.BaselinesDir(typescriptRoot);
        string suffix = kind switch
        {
            TypeScriptBaselineKind.Errors => ".errors.txt",
            TypeScriptBaselineKind.Types => ".types",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        string plain = System.IO.Path.Combine(baselinesDir, basename + suffix);
        if (File.Exists(plain))
            return TypeScriptBaselineResolution.Found(plain, plain);
        if (!Directory.Exists(baselinesDir))
            return TypeScriptBaselineResolution.NoBaseline(plain);

        string selectedTarget = SelectHarnessValue(metadata.Target, "es5");
        string selectedModule = SelectHarnessValue(metadata.Module, "esnext");
        int bestSpecificity = -1;
        var best = new List<string>();

        foreach (string path in Directory.EnumerateFiles(
                     baselinesDir,
                     $"{basename}(*){suffix}"))
        {
            if (!TryReadAxes(System.IO.Path.GetFileName(path), suffix, out var axes))
                continue;
            if (axes.Keys.Any(key => key is not "target" and not "module"))
                continue;
            if (axes.TryGetValue("target", out string? target) &&
                NormalizeTarget(target) != NormalizeTarget(selectedTarget))
                continue;
            if (axes.TryGetValue("module", out string? module) &&
                NormalizeModule(module) != NormalizeModule(selectedModule))
                continue;

            if (axes.Count > bestSpecificity)
            {
                bestSpecificity = axes.Count;
                best.Clear();
            }
            if (axes.Count == bestSpecificity)
                best.Add(path);
        }

        best.Sort(StringComparer.Ordinal);
        return best.Count switch
        {
            0 => TypeScriptBaselineResolution.NoBaseline(plain),
            1 => TypeScriptBaselineResolution.Found(plain, best[0]),
            _ => TypeScriptBaselineResolution.Ambiguous(plain, best),
        };
    }

    private static bool TryReadAxes(
        string filename,
        string suffix,
        out IReadOnlyDictionary<string, string> axes)
    {
        axes = new Dictionary<string, string>();
        int open = filename.IndexOf('(');
        int close = filename.LastIndexOf(')' + suffix, StringComparison.Ordinal);
        if (open < 0 || close <= open + 1)
            return false;

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in filename[(open + 1)..close]
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0)
                return false;
            if (!parsed.TryAdd(pair[0].ToLowerInvariant(), pair[1]))
                return false;
        }
        axes = parsed;
        return parsed.Count > 0;
    }

    private static string SelectHarnessValue(string? values, string defaultValue)
    {
        string selected = values?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?
            .ToLowerInvariant() ?? defaultValue;
        return selected == "*" ? "esnext" : selected;
    }

    private static string NormalizeTarget(string target) =>
        target.Trim().ToLowerInvariant() is "es6" ? "es2015" : target.Trim().ToLowerInvariant();

    private static string NormalizeModule(string module) =>
        module.Trim().ToLowerInvariant() is "es6" ? "es2015" : module.Trim().ToLowerInvariant();
}
