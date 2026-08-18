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
///      soft-report bucket changes.
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

    [Fact]
    public async Task InterpretedBaseline()
    {
        var root = TypeScriptConformancePaths.TryFindRoot();
        var projectDir = TypeScriptConformancePaths.TryFindProjectDir();
        if (root is null || projectDir is null)
        {
            _output.WriteLine("external/typescript or tests/conformance/SharpTS.TypeScriptConformance/ not found — run `git submodule update --init external/typescript`");
            return;
        }

        var configDir = Path.Combine(projectDir, "config");
        var configFile = Path.Combine(configDir, "subset.json");
        var config = TypeScriptConformanceConfig.Load(configFile);
        var skipDirectives = config.LoadSkipDirectives(configDir);
        var skipTests = config.LoadSkipTests(configDir);

        var files = EnumerateTestFiles(root, config.Folders);
        _output.WriteLine($"enumerated {files.Count} test files from {config.Folders.Count} folder(s)");

        var runner = new TypeScriptConformanceRunner(root, skipDirectives, skipTests);
        var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<TypeScriptConformanceOutcome, int>();

        var started = DateTime.UtcNow;
        foreach (var (relPath, absPath) in files)
        {
            // A checker hang must not pin the entire corpus job. The config
            // has always carried a per-test timeout; enforce it here and keep
            // classifying subsequent files. The worker is a background
            // ThreadPool task, so the test host can still terminate cleanly.
            var runTask = Task.Run(() => runner.RunOne(absPath));
            TypeScriptConformanceResult result;
            var completed = await Task.WhenAny(
                runTask, Task.Delay(TimeSpan.FromSeconds(config.TimeoutSeconds)));
            if (completed != runTask)
            {
                result = new TypeScriptConformanceResult(
                    TypeScriptConformanceOutcome.TypeCheckError,
                    $"Timed out after {config.TimeoutSeconds}s.",
                    "timeout");
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
            counts.TryGetValue(result.Outcome, out var c);
            counts[result.Outcome] = c + 1;
        }
        var elapsed = DateTime.UtcNow - started;

        _output.WriteLine(FormatSummary(counts, files.Count, elapsed));

        var baselinePath = Path.Combine(projectDir, "baselines", "interpreted.txt");
        var updateBaseline = GetBool("SHARPTS_TSCONFORMANCE_UPDATE_BASELINE");

        if (updateBaseline || !File.Exists(baselinePath))
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

        var baseline = TypeScriptConformanceBaseline.Read(baselinePath);
        var diff = TypeScriptConformanceBaselineDiffer.Diff(baseline, current);
        LogDiff(diff);

        if (diff.HasHardFailures || diff.NewEntries.Count > 0 || diff.RemovedEntries.Count > 0)
        {
            Assert.Fail(
                $"baseline drift: " +
                $"{diff.NewRegressions.Count} regressions, {diff.NewPasses.Count} new passes, " +
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
    private static List<(string RelPath, string AbsPath)> EnumerateTestFiles(
        string typescriptRoot, IReadOnlyList<string> folders)
    {
        var results = new List<(string, string)>();
        foreach (var folder in folders)
        {
            var absFolder = Path.Combine(typescriptRoot, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absFolder)) continue;
            foreach (var file in Directory.EnumerateFiles(absFolder, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetExtension(path) is ".ts" or ".tsx"))
            {
                var rel = Path.GetRelativePath(typescriptRoot, file).Replace('\\', '/');
                results.Add((rel, file));
            }
        }
        results.Sort((a, b) => StringComparer.Ordinal.Compare(a.Item1, b.Item1));
        return results;
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
        Dump("bucket changes (soft)", diff.BucketChanges);
    }
}
