using System.Text;

namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Reads and writes the committed baseline file. Format mirrors
/// <c>SharpTS.Test262.Test262Baseline</c> exactly: one line per test,
/// <c>&lt;relative-path&gt; &lt;bucket&gt;</c>, sorted by path. Skip reasons
/// are appended as <c>:reason</c> (e.g.
/// <c>Skipped:directive:experimentaldecorators</c>) so the differ can tell
/// skip policies apart.
/// </summary>
public static class TypeScriptConformanceBaseline
{
    public const int FormatVersion = 1;

    public static string Header(string corpusRevision)
    {
        if (corpusRevision.Length != 40 || corpusRevision.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Corpus revision must be a 40-character Git SHA.", nameof(corpusRevision));
        return $"# SharpTS baseline-format={FormatVersion} suite=TypeScript corpus={corpusRevision.ToLowerInvariant()} — do not hand-edit; regenerate with SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1.";
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
        string content = sb.ToString();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, content);
                break;
            }
            catch (Exception ex) when (
                attempt < 5
                && ex is IOException or UnauthorizedAccessException)
            {
                // Windows indexers/AV can briefly hold a just-read baseline.
                // A bounded retry keeps regeneration deterministic.
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    /// <summary>
    /// Encodes outcome + optional skip reason into the persisted token.
    /// </summary>
    public static string EncodeBucket(TypeScriptConformanceResult result) =>
        result.SkipReason is null
            ? result.Outcome.ToString()
            : $"{result.Outcome}:{result.SkipReason}";
}

public enum TypeScriptConformanceBaselineChangeKind
{
    NewRegression,
    NewPass,
    BucketChanged,
    NewEntry,
    RemovedEntry,
}

public sealed record TypeScriptConformanceBaselineChange(
    TypeScriptConformanceBaselineChangeKind Kind,
    string RelPath,
    string? OldBucket,
    string NewBucket);

public sealed record TypeScriptConformanceBaselineDiff(
    IReadOnlyList<TypeScriptConformanceBaselineChange> NewRegressions,
    IReadOnlyList<TypeScriptConformanceBaselineChange> NewPasses,
    IReadOnlyList<TypeScriptConformanceBaselineChange> BucketChanges,
    IReadOnlyList<TypeScriptConformanceBaselineChange> NewEntries,
    IReadOnlyList<TypeScriptConformanceBaselineChange> RemovedEntries)
{
    public bool HasHardFailures => NewRegressions.Count > 0 || NewPasses.Count > 0;
}

public static class TypeScriptConformanceBaselineDiffer
{
    /// <summary>
    /// Diffs a fresh run against the committed baseline. Hard-fails on
    /// regression (good→bad) or new-pass (bad→good); the latter forces
    /// baseline updates through review so wins don't slip past silently.
    /// </summary>
    public static TypeScriptConformanceBaselineDiff Diff(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> current)
    {
        var regressions = new List<TypeScriptConformanceBaselineChange>();
        var newPasses = new List<TypeScriptConformanceBaselineChange>();
        var bucketChanges = new List<TypeScriptConformanceBaselineChange>();
        var newEntries = new List<TypeScriptConformanceBaselineChange>();
        var removed = new List<TypeScriptConformanceBaselineChange>();

        foreach (var (path, curBucket) in current.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(path, out var baseBucket))
            {
                newEntries.Add(new TypeScriptConformanceBaselineChange(
                    TypeScriptConformanceBaselineChangeKind.NewEntry, path, null, curBucket));
                continue;
            }
            if (baseBucket == curBucket) continue;

            var wasGood = IsGood(baseBucket);
            var isGood = IsGood(curBucket);

            if (wasGood && !isGood)
                regressions.Add(new TypeScriptConformanceBaselineChange(
                    TypeScriptConformanceBaselineChangeKind.NewRegression, path, baseBucket, curBucket));
            else if (!wasGood && isGood)
                newPasses.Add(new TypeScriptConformanceBaselineChange(
                    TypeScriptConformanceBaselineChangeKind.NewPass, path, baseBucket, curBucket));
            else
                bucketChanges.Add(new TypeScriptConformanceBaselineChange(
                    TypeScriptConformanceBaselineChangeKind.BucketChanged, path, baseBucket, curBucket));
        }

        foreach (var (path, baseBucket) in baseline.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!current.ContainsKey(path))
                removed.Add(new TypeScriptConformanceBaselineChange(
                    TypeScriptConformanceBaselineChangeKind.RemovedEntry, path, baseBucket, ""));
        }

        return new TypeScriptConformanceBaselineDiff(regressions, newPasses, bucketChanges, newEntries, removed);
    }

    /// <summary>
    /// A "good" outcome is one we'd regress from. Pass obviously; Skipped too
    /// — a test that used to be skipped as unsupported shouldn't suddenly
    /// start crashing the runner.
    /// </summary>
    private static bool IsGood(string bucket)
    {
        if (bucket.StartsWith("Pass", StringComparison.Ordinal)) return true;
        if (bucket.StartsWith("Skipped", StringComparison.Ordinal)) return true;
        return false;
    }
}
