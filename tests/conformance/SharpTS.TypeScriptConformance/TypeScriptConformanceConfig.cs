using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Subset configuration loaded from a JSON file. Describes which folders
/// under <c>tests/cases/conformance/</c> to enumerate, the per-test timeout,
/// and paths to the skip-directives + skip-tests sidecars.
///
/// Mirrors <c>SharpTS.Test262.Test262Config</c> shape; the differences are
/// (a) skip-directives instead of skip-features (TS uses different metadata)
/// and (b) skip-tests-by-path is first-class because TS conformance tests
/// can crash the runner individually and we need the escape hatch.
/// </summary>
public sealed record TypeScriptConformanceConfig(
    [property: JsonPropertyName("folders")] IReadOnlyList<string> Folders,
    [property: JsonPropertyName("timeoutSeconds")] int TimeoutSeconds,
    [property: JsonPropertyName("skipDirectivesFile")] string? SkipDirectivesFile,
    [property: JsonPropertyName("skipTestsFile")] string? SkipTestsFile)
{
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TypeScriptConformanceConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<TypeScriptConformanceConfig>(json, Options)
            ?? throw new InvalidDataException($"Failed to deserialize {path}");
    }

    /// <summary>
    /// Reads the skip-directives file (one directive name per line, lower-cased,
    /// '#' comments allowed). Returns empty set if not configured or missing.
    /// </summary>
    public HashSet<string> LoadSkipDirectives(string configDir) =>
        LoadLineList(SkipDirectivesFile, configDir, lowercase: true);

    /// <summary>
    /// Reads the skip-tests file (one test path per line, relative to the
    /// conformance corpus root, '#' comments allowed). Returns empty set if
    /// not configured or missing.
    /// </summary>
    public HashSet<string> LoadSkipTests(string configDir) =>
        LoadLineList(SkipTestsFile, configDir, lowercase: false);

    private static HashSet<string> LoadLineList(string? fileName, string configDir, bool lowercase)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(fileName)) return set;

        var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(configDir, fileName);
        if (!File.Exists(path)) return set;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;
            set.Add(lowercase ? line.ToLowerInvariant() : line);
        }
        return set;
    }
}
