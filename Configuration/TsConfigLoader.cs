using System.Text.Json;
using SharpTS.Parsing;

namespace SharpTS.Configuration;

/// <summary>
/// Discovers, parses and folds tsconfig.json — including its <c>extends</c> chain — into the
/// values SharpTS acts on.
/// </summary>
/// <remarks>
/// Deliberately mirrors <see cref="References.SharpTsManifestLoader"/>: the same upward walk,
/// the same temp/user-profile ceilings, the same JSON leniency (comments, trailing commas,
/// case-insensitive keys), and the same error policy — <b>a missing file is null (soft), a
/// malformed one is a hard error naming the path</b>. Silently ignoring a tsconfig the user
/// wrote would make strictness flags mysteriously not apply.
/// </remarks>
public static class TsConfigLoader
{
    public const string FileName = "tsconfig.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Finds the nearest tsconfig.json in <paramref name="startDirectory"/> or its parents and
    /// loads it with its <c>extends</c> chain. Returns null when none exists.
    /// </summary>
    /// <remarks>
    /// The upward walk stops (exclusive) at the system temp root and the user profile root — a
    /// tsconfig.json sitting there is ambient noise from unrelated tooling, not the config of the
    /// project being run. Each ceiling is still searched when it IS the start directory. Same
    /// policy as <see cref="References.SharpTsManifestLoader.FindAndLoad"/>.
    /// </remarks>
    public static TsConfigResult? FindAndLoad(string startDirectory)
    {
        var path = Discover(startDirectory);
        return path is null ? null : Load(path);
    }

    /// <summary>Returns the path of the nearest tsconfig.json, or null.</summary>
    public static string? Discover(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);

        var ceilings = new[]
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToArray();

        bool isStartDirectory = true;
        while (dir != null)
        {
            var currentPath = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!isStartDirectory &&
                ceilings.Any(c => string.Equals(currentPath, c, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            isStartDirectory = false;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves a <c>-p</c>/<c>--project</c> argument, which tsc accepts as either a config file
    /// or a directory containing one.
    /// </summary>
    /// <exception cref="Exception">When the path names nothing usable.</exception>
    public static string ResolveProjectPath(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);

        if (Directory.Exists(full))
        {
            string inDir = Path.Combine(full, FileName);
            if (File.Exists(inDir)) return inDir;

            throw new Exception($"Error: -p/--project: no {FileName} in '{full}'.");
        }

        if (File.Exists(full)) return full;

        throw new Exception($"Error: -p/--project: '{projectPath}' does not exist (resolved to '{full}').");
    }

    /// <summary>
    /// Loads an explicit tsconfig.json and folds its <c>extends</c> chain.
    /// </summary>
    /// <exception cref="Exception">Malformed JSON, an unresolvable or circular <c>extends</c>.</exception>
    public static TsConfigResult Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new FileNotFoundException($"{FileName} not found at: {full}", full);

        var chain = new List<(string Path, TsConfigJson Json)>();
        var onStack = new List<string>();
        LoadInto(full, chain, onStack);

        return Fold(chain);
    }

    /// <summary>
    /// Depth-first over the <c>extends</c> chain so bases land in <paramref name="chain"/>
    /// before the files that derive from them.
    /// </summary>
    private static void LoadInto(string path, List<(string, TsConfigJson)> chain, List<string> onStack)
    {
        string full = Path.GetFullPath(path);

        int existing = onStack.FindIndex(p => string.Equals(p, full, PathComparison));
        if (existing >= 0)
        {
            var cycle = string.Join(" -> ", onStack.Skip(existing).Append(full).Select(Path.GetFileName));
            throw new Exception($"Error: {FileName}: circular 'extends' chain: {cycle}");
        }

        var json = ParseFile(full);
        onStack.Add(full);

        foreach (var spec in ReadExtends(json, full))
            LoadInto(ResolveExtendsTarget(spec, full), chain, onStack);

        onStack.RemoveAt(onStack.Count - 1);
        chain.Add((full, json));
    }

    private static TsConfigJson ParseFile(string full)
    {
        try
        {
            using var stream = File.OpenRead(full);
            return JsonSerializer.Deserialize<TsConfigJson>(stream, JsonOptions)
                ?? throw new Exception($"Error: {FileName} ('{full}') is empty or null.");
        }
        catch (JsonException ex)
        {
            throw new Exception($"Error: {FileName} ('{full}') is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Reads <c>extends</c> in both its string and (tsc 5+) array forms.</summary>
    private static IEnumerable<string> ReadExtends(TsConfigJson json, string declaringFile)
    {
        if (json.Extends is not { } element) yield break;

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        throw new Exception($"Error: {FileName} ('{declaringFile}'): 'extends' array must contain only strings.");
                    yield return item.GetString()!;
                }
                break;
            default:
                throw new Exception($"Error: {FileName} ('{declaringFile}'): 'extends' must be a string or an array of strings.");
        }
    }

    /// <summary>
    /// Resolves one <c>extends</c> specifier relative to the file that declared it — tsc's rule,
    /// and what makes relative paths inside an extended config resolve against that config.
    /// </summary>
    private static string ResolveExtendsTarget(string spec, string declaringFile)
    {
        string declaringDir = Path.GetDirectoryName(declaringFile)!;

        bool isRelative = spec.StartsWith("./", StringComparison.Ordinal)
            || spec.StartsWith("../", StringComparison.Ordinal)
            || spec.StartsWith(".\\", StringComparison.Ordinal)
            || spec.StartsWith("..\\", StringComparison.Ordinal)
            || Path.IsPathRooted(spec);

        if (isRelative)
        {
            string candidate = Path.GetFullPath(Path.Combine(declaringDir, spec));
            if (File.Exists(candidate)) return candidate;
            if (!Path.HasExtension(candidate) && File.Exists(candidate + ".json")) return candidate + ".json";

            throw new Exception(
                $"Error: {FileName} ('{declaringFile}'): cannot resolve 'extends' target '{spec}' " +
                $"(looked for '{candidate}').");
        }

        // Bare specifier: an honest subset of node resolution — walk up looking in node_modules.
        var dir = new DirectoryInfo(declaringDir);
        while (dir != null)
        {
            string baseDir = Path.Combine(dir.FullName, "node_modules", spec);
            foreach (var candidate in new[] { baseDir, baseDir + ".json", Path.Combine(baseDir, FileName) })
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            dir = dir.Parent;
        }

        throw new Exception(
            $"Error: {FileName} ('{declaringFile}'): cannot resolve 'extends' target '{spec}' " +
            "in any node_modules directory.");
    }

    /// <summary>
    /// Folds the chain base-first so a deriving file's keys win, and resolves every path-valued
    /// option against the directory of the file that declared it.
    /// </summary>
    private static TsConfigResult Fold(List<(string Path, TsConfigJson Json)> chain)
    {
        var strictness = new StrictnessOptions();
        bool? checkJs = null, preserveConstEnums = null, emitDecoratorMetadata = null;
        DecoratorMode? decoratorMode = null;
        string? outDir = null, entryFile = null;
        var warnings = new List<string>();

        foreach (var (path, json) in chain)
        {
            string dir = Path.GetDirectoryName(path)!;
            warnings.AddRange(TsConfigKeyCatalog.Diagnose(path, json));

            var opts = json.CompilerOptions;
            if (opts is not null)
            {
                var layer = opts.ToStrictnessOptions();
                strictness = new StrictnessOptions
                {
                    Strict = layer.Strict ?? strictness.Strict,
                    StrictNullChecks = layer.StrictNullChecks ?? strictness.StrictNullChecks,
                    StrictFunctionTypes = layer.StrictFunctionTypes ?? strictness.StrictFunctionTypes,
                    NoImplicitAny = layer.NoImplicitAny ?? strictness.NoImplicitAny,
                };

                checkJs = opts.CheckJs ?? checkJs;
                preserveConstEnums = opts.PreserveConstEnums ?? preserveConstEnums;
                emitDecoratorMetadata = opts.EmitDecoratorMetadata ?? emitDecoratorMetadata;

                // `decorators` (Stage 3) wins over `experimentalDecorators` (Legacy) when both
                // are set — the same precedence the CLI's last-wins switch gives the arguments
                // SharpTS.Sdk/Sdk/Sdk.targets emits, so MSBuild and the CLI agree.
                if (opts.ExperimentalDecorators == true) decoratorMode = Parsing.DecoratorMode.Legacy;
                if (opts.Decorators == true) decoratorMode = Parsing.DecoratorMode.Stage3;

                if (!string.IsNullOrWhiteSpace(opts.OutDir))
                    outDir = Path.GetFullPath(Path.Combine(dir, opts.OutDir!));
            }

            // `files`/`include`/`exclude` replace rather than merge (tsc's rule).
            if (json.Files is { Length: > 0 })
                entryFile = Path.GetFullPath(Path.Combine(dir, json.Files[0]));
        }

        var leaf = chain[^1];
        return new TsConfigResult(
            ConfigPath: leaf.Path,
            ExtendsChain: chain.Select(c => c.Path).ToArray(),
            Strictness: strictness,
            CheckJs: checkJs,
            PreserveConstEnums: preserveConstEnums,
            DecoratorMode: decoratorMode,
            EmitDecoratorMetadata: emitDecoratorMetadata,
            OutDir: outDir,
            EntryFile: entryFile,
            Warnings: warnings);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

/// <summary>
/// A tsconfig.json (plus its <c>extends</c> chain) folded into the values SharpTS acts on.
/// Every option stays nullable so the CLI layer above can still win per key.
/// </summary>
/// <param name="ConfigPath">Absolute path of the tsconfig.json that was loaded.</param>
/// <param name="ExtendsChain">Every file in the chain, base first, ending with <paramref name="ConfigPath"/>.</param>
/// <param name="EntryFile">
/// <c>files[0]</c>, absolute. Used as the entry point only for <c>-p</c>/<c>--project</c> with no
/// script argument — mirroring <c>ReadTsConfigTask</c> so the CLI and MSBuild pick the same file.
/// </param>
/// <param name="Warnings">Unknown/inapplicable-key notes, already formatted for display.</param>
public sealed record TsConfigResult(
    string ConfigPath,
    IReadOnlyList<string> ExtendsChain,
    StrictnessOptions Strictness,
    bool? CheckJs,
    bool? PreserveConstEnums,
    DecoratorMode? DecoratorMode,
    bool? EmitDecoratorMetadata,
    string? OutDir,
    string? EntryFile,
    IReadOnlyList<string> Warnings);
