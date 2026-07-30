using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpTS.Configuration;

namespace SharpTS.Projects;

/// <summary>SharpTS-owned incremental project state. The format is intentionally not tsc's.</summary>
internal static class ProjectBuildState
{
    private const int FormatVersion = 2;

    private static readonly string ToolVersion =
        typeof(ProjectBuildState).Assembly.GetName().Version?.ToString() ?? "unknown";

    // internal (not private) so the source-generated ProjectBuildStateContext below can bind it.
    internal sealed record State(
        int Version,
        string ToolVersion,
        string ConfigPath,
        string OptionsKey,
        Dictionary<string, string> Inputs,
        Dictionary<string, string> Outputs);

    public static bool IsUpToDate(TsConfigResult project, string optionsKey)
    {
        if (!File.Exists(project.BuildInfoFile))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize(
                File.ReadAllText(project.BuildInfoFile), ProjectBuildStateContext.Default.State);
            if (state is null ||
                state.Version != FormatVersion ||
                !string.Equals(state.ToolVersion, ToolVersion, StringComparison.Ordinal) ||
                !string.Equals(state.OptionsKey, optionsKey, StringComparison.Ordinal) ||
                !PathEquals(state.ConfigPath, project.ConfigPath))
            {
                return false;
            }

            foreach (string currentRoot in project.RootFiles.Concat(project.DeclarationFiles))
            {
                if (!state.Inputs.Keys.Any(path => PathEquals(path, currentRoot)))
                    return false;
            }

            foreach (var (path, expectedHash) in state.Inputs)
            {
                if (!File.Exists(path) || !string.Equals(Hash(path), expectedHash, StringComparison.Ordinal))
                    return false;
            }
            foreach (var (path, expectedHash) in state.Outputs)
            {
                if (!File.Exists(path) || !string.Equals(Hash(path), expectedHash, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Write(
        TsConfigResult project,
        string optionsKey,
        IEnumerable<string> inputPaths,
        IEnumerable<string>? outputPaths = null)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var inputs = inputPaths
            .Where(File.Exists)
            .Append(project.ConfigPath)
            .Concat(project.ExtendsChain)
            .Distinct(comparer)
            .ToDictionary(path => Path.GetFullPath(path), Hash, comparer);
        var outputs = (outputPaths ?? [])
            .Where(File.Exists)
            .Distinct(comparer)
            .ToDictionary(path => Path.GetFullPath(path), Hash, comparer);

        string? directory = Path.GetDirectoryName(project.BuildInfoFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var state = new State(FormatVersion, ToolVersion, project.ConfigPath, optionsKey, inputs, outputs);
        File.WriteAllText(
            project.BuildInfoFile,
            JsonSerializer.Serialize(state, ProjectBuildStateContext.Default.State));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

/// <summary>
/// Source-generated serializer context for the .tsbuildinfo state file (#1324 Phase 1 — see
/// <see cref="Configuration.TsConfigJsonContext"/> for the rationale). WriteIndented matches the
/// previous runtime options; reads keep the default strict parsing this format always had.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProjectBuildState.State))]
internal sealed partial class ProjectBuildStateContext : JsonSerializerContext;
