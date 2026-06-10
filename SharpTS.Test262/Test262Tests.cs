using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Test262;

/// <summary>
/// Milestone 2: subset coverage with a committed baseline.
///
/// Split into two classes (<see cref="Test262InterpretedTests"/>,
/// <see cref="Test262CompiledTests"/>) so xUnit treats them as separate
/// collections that can run concurrently. The earlier
/// <c>[CollectionDefinition(DisableParallelization = true)]</c> existed to
/// serialize a process-wide <c>Console.SetOut/restore</c> window; the
/// AsyncLocal-scoped redirector in <see cref="Test262Runner"/> removed that
/// constraint.
///
/// Flow:
///   1. Enumerate every <c>.js</c> under the subset folders.
///   2. Run each file through <see cref="Test262Runner"/>.
///   3. Compare outcomes to <c>baselines/{interpreted|compiled}.txt</c>.
///   4. Fail the fact if any regression (good → bad) or new pass (bad → good)
///      is detected; soft-report bucket changes.
///
/// Env-var switches:
///   <c>SHARPTS_TEST262_UPDATE_BASELINE=1</c> — write baselines instead of diffing.
///   <c>SHARPTS_TEST262_WIDE_SWEEP=1</c>      — use <c>wide-sweep.json</c>;
///                                              write <c>wide-sweep-report.md</c> instead of diffing.
/// </summary>
public abstract class Test262TestsBase
{
    protected readonly ITestOutputHelper _output;

    protected Test262TestsBase(ITestOutputHelper output) => _output = output;

    protected void RunBaseline(Test262ExecutionMode mode)
    {
        var test262Root = Test262Paths.TryFindRoot();
        var projectDir = Test262Paths.TryFindProjectDir();
        if (test262Root is null || projectDir is null)
        {
            _output.WriteLine("external/test262 or SharpTS.Test262/ not found — run `git submodule update --init external/test262`");
            return;
        }

        var wideSweep = GetBool("SHARPTS_TEST262_WIDE_SWEEP");
        var updateBaseline = GetBool("SHARPTS_TEST262_UPDATE_BASELINE");

        var configDir = Path.Combine(projectDir, "config");
        var configFile = Path.Combine(configDir, wideSweep ? "wide-sweep.json" : "subset.json");
        var config = Test262Config.Load(configFile);
        var skipFeatures = config.LoadSkipFeatures(configDir);

        var modeFolders = config.GetFoldersForMode(mode);
        var files = EnumerateTestFiles(test262Root, modeFolders);
        _output.WriteLine($"[{mode}] enumerated {files.Count} test files from {modeFolders.Count} folders");

        var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<Test262Outcome, int>();

        // Both modes use the persistent worker pool (issue #109): tests run in
        // dotnet subprocesses fed from a shared queue, so pathological tests
        // can't accumulate memory in (or crash) the testhost, and the regen
        // parallelizes across workers. Interpreted mode previously ran serial
        // in-process; that was both the regen's longest pole (~6x slower than
        // pooled) and fragile — a guest test could take down the entire run
        // with a CLR internal error.
        var workerDll = Test262Paths.TryFindWorkerDll();

        var started = DateTime.UtcNow;
        if (workerDll is not null)
        {
            var skipFeaturesFile = ResolveSkipFeaturesPath(configDir, config);
            var batched = new BatchedSubprocessRunner(
                test262Root, mode, config.Timeout, skipFeaturesFile, workerDll);
            var byAbs = batched.RunAll(
                files.Select(f => f.AbsPath).ToList(),
                progress: (done, total) =>
                {
                    if (done % 1000 == 0 || done == total)
                        _output.WriteLine($"[{mode}] batch progress: {done}/{total}");
                });
            foreach (var (relPath, absPath) in files)
            {
                if (!byAbs.TryGetValue(absPath, out var bucket))
                    bucket = "RuntimeError:worker-crashed";
                current[relPath] = bucket;
                var outcome = ParseOutcomeFromBucket(bucket);
                counts.TryGetValue(outcome, out var c);
                counts[outcome] = c + 1;
            }
        }
        else
        {
            _output.WriteLine($"[{mode}] worker DLL not found — falling back to in-process (build SharpTS.Test262.Worker for issue #109 batched mode)");
            var runner = new Test262Runner(test262Root, config.Timeout, skipFeatures);
            foreach (var (relPath, absPath) in files)
            {
                var result = runner.RunOne(absPath, mode);
                current[relPath] = Test262Baseline.EncodeBucket(result);
                counts.TryGetValue(result.Outcome, out var c);
                counts[result.Outcome] = c + 1;
            }
        }
        var elapsed = DateTime.UtcNow - started;

        var summary = FormatSummary(counts, files.Count, elapsed);
        _output.WriteLine(summary);

        if (wideSweep)
        {
            var reportPath = Path.Combine(projectDir, "wide-sweep-report.md");
            WriteWideSweepReport(reportPath, mode, config, counts, current, elapsed);
            _output.WriteLine($"[{mode}] wrote {reportPath}");
            return;
        }

        var baselinePath = Path.Combine(projectDir, "baselines",
            mode == Test262ExecutionMode.Interpreted ? "interpreted.txt" : "compiled.txt");

        if (updateBaseline || !File.Exists(baselinePath))
        {
            Test262Baseline.Write(baselinePath, current.Select(kv => (kv.Key, kv.Value)));
            _output.WriteLine($"[{mode}] wrote baseline → {baselinePath}");
            return;
        }

        var baseline = Test262Baseline.Read(baselinePath);
        var diff = Test262BaselineDiffer.Diff(baseline, current);
        LogDiff(mode, diff);

        if (diff.HasHardFailures || diff.NewEntries.Count > 0 || diff.RemovedEntries.Count > 0)
        {
            Assert.Fail(
                $"[{mode}] baseline drift: " +
                $"{diff.NewRegressions.Count} regressions, {diff.NewPasses.Count} new passes, " +
                $"{diff.NewEntries.Count} new entries, {diff.RemovedEntries.Count} removed entries. " +
                $"Re-run with SHARPTS_TEST262_UPDATE_BASELINE=1 to update.");
        }
    }

    private static bool GetBool(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return v is "1" or "true" or "TRUE";
    }

    /// <summary>
    /// Resolves the absolute path to the skip-features file the worker should
    /// load. Mirrors <see cref="Test262Config.LoadSkipFeatures"/>'s resolution
    /// (relative paths are anchored to the config dir).
    /// </summary>
    private static string? ResolveSkipFeaturesPath(string configDir, Test262Config config)
    {
        if (string.IsNullOrEmpty(config.SkipFeaturesFile)) return null;
        return Path.IsPathRooted(config.SkipFeaturesFile)
            ? config.SkipFeaturesFile
            : Path.Combine(configDir, config.SkipFeaturesFile);
    }

    /// <summary>
    /// Recovers the <see cref="Test262Outcome"/> from an encoded bucket string
    /// like <c>"Pass"</c>, <c>"Skipped:async-done-deferred"</c>, or
    /// <c>"RuntimeError:worker-crashed"</c>. Used to populate the per-mode
    /// outcome histogram when results come from the worker subprocess.
    /// </summary>
    private static Test262Outcome ParseOutcomeFromBucket(string bucket)
    {
        var colon = bucket.IndexOf(':');
        var name = colon < 0 ? bucket : bucket[..colon];
        return Enum.TryParse<Test262Outcome>(name, out var outcome) ? outcome : Test262Outcome.RuntimeError;
    }

    /// <summary>
    /// Walks each configured folder for <c>*.js</c> files, excluding the
    /// Test262 convention for non-test fixtures (suffix <c>_FIXTURE.js</c>).
    /// </summary>
    private static List<(string RelPath, string AbsPath)> EnumerateTestFiles(
        string test262Root, IReadOnlyList<string> folders)
    {
        var results = new List<(string, string)>();
        foreach (var folder in folders)
        {
            var absFolder = Path.Combine(test262Root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absFolder)) continue;
            foreach (var file in Directory.EnumerateFiles(absFolder, "*.js", SearchOption.AllDirectories))
            {
                if (file.EndsWith("_FIXTURE.js", StringComparison.Ordinal)) continue;
                var rel = Path.GetRelativePath(test262Root, file).Replace('\\', '/');
                results.Add((rel, file));
            }
        }
        // Stable enumeration order so baselines don't flap between runs.
        results.Sort((a, b) => StringComparer.Ordinal.Compare(a.Item1, b.Item1));
        return results;
    }

    private static string FormatSummary(Dictionary<Test262Outcome, int> counts, int total, TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.Append($"summary: {total} tests in {elapsed.TotalSeconds:F1}s → ");
        var parts = counts.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value}");
        sb.Append(string.Join(", ", parts));
        return sb.ToString();
    }

    private void LogDiff(Test262ExecutionMode mode, BaselineDiff diff)
    {
        void Dump(string label, IReadOnlyList<BaselineChange> list, int maxShown = 20)
        {
            if (list.Count == 0) return;
            _output.WriteLine($"[{mode}] {label}: {list.Count}");
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

    private static void WriteWideSweepReport(
        string path,
        Test262ExecutionMode mode,
        Test262Config config,
        Dictionary<Test262Outcome, int> counts,
        IReadOnlyDictionary<string, string> current,
        TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Test262 wide-sweep report — {mode}");
        sb.AppendLine();
        sb.AppendLine($"- generated: {DateTime.UtcNow:u}");
        sb.AppendLine($"- folders: {string.Join(", ", config.Folders)}");
        sb.AppendLine($"- timeout: {config.TimeoutSeconds}s");
        sb.AppendLine($"- elapsed: {elapsed.TotalSeconds:F1}s");
        sb.AppendLine($"- total: {current.Count}");
        sb.AppendLine();
        sb.AppendLine("## Outcomes");
        sb.AppendLine();
        sb.AppendLine("| Bucket | Count |");
        sb.AppendLine("|--------|------:|");
        foreach (var kv in counts.OrderBy(kv => kv.Key))
            sb.AppendLine($"| {kv.Key} | {kv.Value} |");
        File.AppendAllText(path, sb.ToString());
    }
}

public class Test262InterpretedTests : Test262TestsBase
{
    public Test262InterpretedTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void InterpretedBaseline() => RunBaseline(Test262ExecutionMode.Interpreted);
}

public class Test262CompiledTests : Test262TestsBase
{
    public Test262CompiledTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CompiledBaseline() => RunBaseline(Test262ExecutionMode.Compiled);
}
