using System.Text;

namespace SharpTS.Test262;

/// <summary>
/// Reads and writes the committed baseline files. Format is deliberately
/// minimal: one line per test, <c>&lt;relative-path&gt; &lt;bucket&gt;</c>, sorted
/// by path ordinal. Skip reasons are appended as <c>:reason</c> on the bucket
/// so baseline diffs can tell <c>Skipped:skip-feature:X</c> apart from
/// <c>Skipped:async-done-deferred</c>.
/// </summary>
public static class Test262Baseline
{
    public const int FormatVersion = 1;

    public static string Header(string corpusRevision)
    {
        if (corpusRevision.Length != 40 || corpusRevision.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Corpus revision must be a 40-character Git SHA.", nameof(corpusRevision));
        return $"# SharpTS baseline-format={FormatVersion} suite=Test262 corpus={corpusRevision.ToLowerInvariant()} — do not hand-edit; regenerate with SHARPTS_TEST262_UPDATE_BASELINE=1.";
    }

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return dict;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var space = line.IndexOf(' ');
            if (space < 0) continue;
            var relPath = line[..space];
            var bucket = line[(space + 1)..].Trim();
            dict[relPath] = bucket;
        }
        return dict;
    }

    public static void Write(string path, IEnumerable<(string RelPath, string Bucket)> entries,
        string corpusRevision)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sorted = entries.OrderBy(e => e.RelPath, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder();
        sb.Append(Header(corpusRevision));
        sb.Append('\n');
        foreach (var (relPath, bucket) in sorted)
        {
            sb.Append(relPath);
            sb.Append(' ');
            sb.Append(bucket);
            sb.Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Encodes an outcome + optional skip reason into the single token we
    /// persist in the baseline. The skip reason matters: it tells new
    /// <c>Skipped:skip-feature:Proxy</c> entries apart from older
    /// <c>Skipped:negative-test-deferred</c>, which is the whole point of
    /// flagging "bucket changed" as a soft-fail.
    /// </summary>
    public static string EncodeBucket(Test262Result result) =>
        result.SkipReason is null
            ? result.Outcome.ToString()
            : $"{result.Outcome}:{result.SkipReason}";
}

/// <summary>
/// Sentinel buckets the differ applies to entries present on only one side.
/// </summary>
public enum BaselineChangeKind
{
    NewRegression,
    NewPass,
    BucketChanged,
    NewEntry,
    RemovedEntry,
}

public sealed record BaselineChange(
    BaselineChangeKind Kind,
    string RelPath,
    string? OldBucket,
    string NewBucket);

public sealed record BaselineDiff(
    IReadOnlyList<BaselineChange> NewRegressions,
    IReadOnlyList<BaselineChange> NewPasses,
    IReadOnlyList<BaselineChange> BucketChanges,
    IReadOnlyList<BaselineChange> NewEntries,
    IReadOnlyList<BaselineChange> RemovedEntries)
{
    public bool HasHardFailures => NewRegressions.Count > 0 || NewPasses.Count > 0;
}

public static class Test262BaselineDiffer
{
    /// <summary>
    /// Diffs a fresh run against the committed baseline. The two hard-fail
    /// cases are (a) a test that used to pass/skip now failing (regression)
    /// and (b) a test that used to fail now passing (forces the baseline to
    /// be regenerated so wins don't silently slip past review).
    /// </summary>
    public static BaselineDiff Diff(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> current)
    {
        var regressions = new List<BaselineChange>();
        var newPasses = new List<BaselineChange>();
        var bucketChanges = new List<BaselineChange>();
        var newEntries = new List<BaselineChange>();
        var removed = new List<BaselineChange>();

        foreach (var (path, curBucket) in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(path, out var baseBucket))
            {
                // New entry — treat as "needs baseline update" (same bar as new pass).
                newEntries.Add(new BaselineChange(BaselineChangeKind.NewEntry, path, null, curBucket));
                continue;
            }
            if (baseBucket == curBucket) continue;

            var wasGood = IsGood(baseBucket);
            var isGood = IsGood(curBucket);

            if (wasGood && !isGood)
                regressions.Add(new BaselineChange(BaselineChangeKind.NewRegression, path, baseBucket, curBucket));
            else if (!wasGood && isGood)
                newPasses.Add(new BaselineChange(BaselineChangeKind.NewPass, path, baseBucket, curBucket));
            else
                bucketChanges.Add(new BaselineChange(BaselineChangeKind.BucketChanged, path, baseBucket, curBucket));
        }

        foreach (var (path, baseBucket) in baseline.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!current.ContainsKey(path))
                removed.Add(new BaselineChange(BaselineChangeKind.RemovedEntry, path, baseBucket, ""));
        }

        return new BaselineDiff(regressions, newPasses, bucketChanges, newEntries, removed);
    }

    /// <summary>
    /// A "good" outcome is one we'd regress from. Pass obviously; Skipped
    /// too — a test that used to be skipped as unsupported shouldn't suddenly
    /// start crashing.
    /// </summary>
    private static bool IsGood(string bucket)
    {
        if (bucket.StartsWith("Pass", StringComparison.Ordinal)) return true;
        if (bucket.StartsWith("Skipped", StringComparison.Ordinal)) return true;
        return false;
    }
}
