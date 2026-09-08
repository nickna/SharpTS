using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Mirror of <c>SharpTS.Test262.Test262BaselineCollection</c>. Type checking
/// itself is thread-safe enough to parallelize, but the runner shares some
/// state (caches inside TypeChecker partials) we haven't audited — keep the
/// baseline fact serial to avoid surprise. Flip later if perf warrants.
/// </summary>
[CollectionDefinition("TypeScriptConformanceBaseline", DisableParallelization = true)]
public class TypeScriptConformanceBaselineCollection { }

/// <summary>
/// #85: subset coverage with a committed baseline.
///
/// Flow:
///   1. Enumerate every <c>.ts</c>/<c>.tsx</c> under the configured subset folders.
///   2. Run each through <see cref="TypeScriptConformanceRunner"/>.
///   3. Compare outcomes to <c>baselines/interpreted.txt</c>.
///   4. Fail the fact on regression (good→bad) or new pass (bad→good);
///      fail on other bucket changes and selection drift as well.
///
/// Env switch:
///   <c>SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1</c> — write the baseline
///   instead of diffing. Use after intentional changes.
/// </summary>
[Collection("TypeScriptConformanceBaseline")]
public class TypeScriptConformanceTests
{
    private readonly ITestOutputHelper _output;

    public TypeScriptConformanceTests(ITestOutputHelper output) => _output = output;

    [Trait("Category", "Corpus")]
    [Fact]
    public async Task InterpretedBaseline()
    {
        string? gateProfile = Environment.GetEnvironmentVariable("SHARPTS_TSCONFORMANCE_GATE_PROFILE");
        bool gate = !string.IsNullOrEmpty(gateProfile);
        if (gate && gateProfile is not ("smoke" or "full"))
            throw new InvalidDataException($"Unknown TypeScript gate profile: {gateProfile}");
        if (gate && GetBool("SHARPTS_TSCONFORMANCE_UPDATE_BASELINE"))
            throw new InvalidOperationException("Baseline updates are forbidden in the TypeScript CI gate.");
        bool smoke = gateProfile == "smoke";
        var root = TypeScriptConformancePaths.RequireRoot();
        var projectDir = TypeScriptConformancePaths.RequireProjectDir();
        var baselinePath = Path.Combine(projectDir, "baselines", "interpreted.txt");
        if (!GetBool("SHARPTS_TSCONFORMANCE_UPDATE_BASELINE"))
            SharpTS.Conformance.ConformanceInputs.RequireBaseline(baselinePath);

        var configDir = Path.Combine(projectDir, "config");
        var configFile = Path.Combine(configDir, smoke ? "smoke.json" : "subset.json");
        var config = TypeScriptConformanceConfig.Load(configFile);
        if (config.TimeoutSeconds <= 0)
            throw new InvalidDataException("TypeScript per-case timeout must be positive.");
        var skipDirectives = config.LoadSkipDirectives(configDir);
        var skipTests = config.LoadSkipTests(configDir);

        var files = EnumerateTestFiles(root, config.Folders, config.Files ?? []);
        if (files.Count == 0)
            throw new InvalidDataException("TypeScript conformance selection contains no tests.");
        IReadOnlyDictionary<string, string>? gateBaseline = gate
            ? TypeScriptConformanceGate.SelectBaseline(
                TypeScriptConformanceGate.ReadBaseline(baselinePath, TypeScriptConformancePaths.GetCorpusRevision(root)),
                files.Select(file => file.RelPath), smoke)
            : null;
        string? reportDirectory = Environment.GetEnvironmentVariable("SHARPTS_TSCONFORMANCE_REPORT_DIR");
        using var report = string.IsNullOrEmpty(reportDirectory) ? null : new TypeScriptConformanceReport(reportDirectory);
        _output.WriteLine(
            $"enumerated {files.Count} test files from {config.Folders.Count} folder(s) " +
            $"and {config.Files?.Count ?? 0} explicit file(s)");

        var runner = new TypeScriptConformanceRunner(root, skipDirectives, skipTests);
        var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<TypeScriptConformanceOutcome, int>();
        var results = new List<TypeScriptConformanceResult>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(smoke ? 120 : 600);
        foreach (var (relPath, absPath) in files)
        {
            // A background task bounds the wait even if the checker hangs.
            // Gate mode aborts below because the checker has no cooperative cancellation.
            var remaining = budget - stopwatch.Elapsed;
            if (gate && remaining <= TimeSpan.Zero)
                throw new TimeoutException($"TypeScript {gateProfile} gate exceeded its {budget.TotalSeconds}s execution budget.");
            var timeout = gate && remaining < config.Timeout ? remaining : config.Timeout;
            var runTask = Task.Run(() => runner.RunOne(absPath));
            TypeScriptConformanceResult result;
            var completed = await Task.WhenAny(
                runTask, Task.Delay(timeout));
            if (completed != runTask)
            {
                result = new TypeScriptConformanceResult(
                    TypeScriptConformanceOutcome.TypeCheckError,
                    $"Timed out after {config.TimeoutSeconds}s.",
                    null);
                _output.WriteLine($"timeout: {relPath}");
            }
            else
            {
                result = await runTask;
            }
            if (result.Outcome == TypeScriptConformanceOutcome.Fail &&
                GetBool("SHARPTS_TSCONFORMANCE_DUMP_FAILURES"))
            {
                _output.WriteLine($"mismatch: {relPath}: {result.Message}");
            }
            if (result.Outcome == TypeScriptConformanceOutcome.ParseError)
                _output.WriteLine($"parse error: {relPath}: {result.Message}");
            current[relPath] = TypeScriptConformanceBaseline.EncodeBucket(result);
            results.Add(result);
            string? expectedBucket = null;
            gateBaseline?.TryGetValue(relPath, out expectedBucket);
            report?.Record(relPath, expectedBucket, result);
            // A timed-out checker cannot be cancelled cooperatively. Stop the gate
            // instead of starting concurrent checks alongside that abandoned task.
            if (gate && completed != runTask)
                throw new TimeoutException($"TypeScript case timed out: {relPath}. See diagnostic-diff.txt.");
            counts.TryGetValue(result.Outcome, out var c);
            counts[result.Outcome] = c + 1;
        }
        var elapsed = stopwatch.Elapsed;

        _output.WriteLine(FormatSummary(counts, files.Count, elapsed));

        var updateBaseline = GetBool("SHARPTS_TSCONFORMANCE_UPDATE_BASELINE");

        if (updateBaseline)
        {
            // Sandboxed test hosts may be allowed to write only to an
            // artifact directory. Let CI/agents redirect the mechanical
            // output and copy it into the source tree afterward.
            string baselineOutputPath =
                Environment.GetEnvironmentVariable("SHARPTS_TSCONFORMANCE_BASELINE_OUTPUT")
                ?? baselinePath;
            if (baselineOutputPath == "-")
            {
                _output.WriteLine("baseline-header:" + TypeScriptConformanceBaseline.Header(
                    TypeScriptConformancePaths.GetCorpusRevision(root)));
                foreach (var (path, bucket) in current)
                    _output.WriteLine($"baseline-entry:{path} {bucket}");
                _output.WriteLine("wrote baseline → stdout");
            }
            else
            {
                TypeScriptConformanceBaseline.Write(
                    baselineOutputPath, current.Select(kv => (kv.Key, kv.Value)),
                    TypeScriptConformancePaths.GetCorpusRevision(root));
                _output.WriteLine($"wrote baseline → {baselineOutputPath}");
            }
            return;
        }

        if (!File.Exists(baselinePath))
            throw new FileNotFoundException("Committed TypeScript baseline is missing. Updates require SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1.", baselinePath);
        var baseline = gateBaseline ?? TypeScriptConformanceBaseline.Read(baselinePath);
        var diff = TypeScriptConformanceBaselineDiffer.Diff(baseline, current);
        LogDiff(diff);
        if (gate)
            TypeScriptConformanceGate.RequireMeaningfulResults(results);
        report?.Complete(diff, files.Count, elapsed.TotalSeconds);

        if (TypeScriptConformanceGate.HasChanges(diff))
        {
            Assert.Fail(
                $"baseline drift: " +
                $"{diff.NewRegressions.Count} regressions, {diff.NewPasses.Count} new passes, " +
                $"{diff.BucketChanges.Count} bucket changes, " +
                $"{diff.NewEntries.Count} new entries, {diff.RemovedEntries.Count} removed entries. " +
                $"Re-run with SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1 to update.");
        }
    }

    private static bool GetBool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return v is "1" or "true" or "TRUE";
    }

    /// <summary>
    /// Walks each configured folder for TypeScript source files. Sorted by relative
    /// path so baselines don't flap between runs.
    /// </summary>
    internal static List<(string RelPath, string AbsPath)> EnumerateTestFiles(
        string typescriptRoot,
        IReadOnlyList<string> folders,
        IReadOnlyList<string> explicitFiles)
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var absFolder = Path.Combine(typescriptRoot, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absFolder))
                throw new DirectoryNotFoundException($"Configured TypeScript conformance folder does not exist: {folder}");
            foreach (var file in Directory.EnumerateFiles(absFolder, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetExtension(path) is ".ts" or ".tsx"))
            {
                var rel = Path.GetRelativePath(typescriptRoot, file).Replace('\\', '/');
                results[rel] = file;
            }
        }

        foreach (var explicitFile in explicitFiles)
        {
            var normalized = explicitFile.Replace('/', Path.DirectorySeparatorChar);
            var absolute = Path.Combine(typescriptRoot, normalized);
            if (!File.Exists(absolute))
                throw new FileNotFoundException(
                    $"Configured TypeScript conformance file does not exist: {explicitFile}",
                    absolute);
            if (Path.GetExtension(absolute) is not (".ts" or ".tsx"))
                throw new InvalidDataException(
                    $"Configured TypeScript conformance file must be .ts or .tsx: {explicitFile}");

            var relative = Path.GetRelativePath(typescriptRoot, absolute).Replace('\\', '/');
            results[relative] = absolute;
        }

        return results
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value))
            .ToList();
    }

    private static string FormatSummary(
        Dictionary<TypeScriptConformanceOutcome, int> counts, int total, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.Append($"summary: {total} tests in {elapsed.TotalSeconds:F1}s → ");
        var parts = counts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}");
        sb.Append(string.Join(", ", parts));
        return sb.ToString();
    }

    private void LogDiff(TypeScriptConformanceBaselineDiff diff)
    {
        void Dump(string label, IReadOnlyList<TypeScriptConformanceBaselineChange> list, int maxShown = 20)
        {
            if (list.Count == 0) return;
            _output.WriteLine($"{label}: {list.Count}");
            foreach (var c in list.Take(maxShown))
                _output.WriteLine($"  {c.RelPath}: {c.OldBucket ?? "-"} → {c.NewBucket}");
            if (list.Count > maxShown)
                _output.WriteLine($"  ... and {list.Count - maxShown} more");
        }
        Dump("regressions", diff.NewRegressions);
        Dump("new passes", diff.NewPasses);
        Dump("new entries", diff.NewEntries);
        Dump("removed entries", diff.RemovedEntries);
        Dump("bucket changes", diff.BucketChanges);
    }
}
