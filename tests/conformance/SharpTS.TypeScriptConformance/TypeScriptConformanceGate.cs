using System.Text.Json;

namespace SharpTS.TypeScriptConformance;

internal static class TypeScriptConformanceGate
{
    internal static IReadOnlyDictionary<string, string> ReadBaseline(string path, string revision)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Committed TypeScript baseline is required; the gate never generates it.", path);
        string prefix = $"# SharpTS baseline-format=1 suite=TypeScript corpus={revision} ";
        if (File.ReadLines(path).FirstOrDefault()?.StartsWith(prefix, StringComparison.Ordinal) != true)
            throw new InvalidDataException("TypeScript baseline format/corpus revision does not match the acquired corpus.");
        var baseline = TypeScriptConformanceBaseline.Read(path);
        if (baseline.Count == 0)
            throw new InvalidDataException("Committed TypeScript baseline is empty.");
        return baseline;
    }

    internal static IReadOnlyDictionary<string, string> SelectBaseline(
        IReadOnlyDictionary<string, string> baseline, IEnumerable<string> paths, bool smoke)
    {
        var selected = paths.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
            throw new InvalidDataException("TypeScript conformance selection contains no tests.");
        // The smoke selection projects the existing baseline; it has no separate expectations.
        // Leave missing selected entries absent so the ordinary differ reports NewEntry.
        return smoke
            ? baseline.Where(pair => selected.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : baseline;
    }

    internal static bool HasChanges(TypeScriptConformanceBaselineDiff diff) =>
        diff.HasHardFailures || diff.BucketChanges.Count > 0 ||
        diff.NewEntries.Count > 0 || diff.RemovedEntries.Count > 0;

    internal static void RequireMeaningfulResults(IEnumerable<TypeScriptConformanceResult> results)
    {
        var checkedResults = results.Where(r => r.Outcome is
            TypeScriptConformanceOutcome.Pass or TypeScriptConformanceOutcome.Fail).ToList();
        if (checkedResults.Count == 0)
            throw new InvalidDataException("TypeScript selection executed no meaningful parser/checker comparisons.");
        if (!checkedResults.Any(r => r.ExpectedDiagnostics?.Count > 0))
            throw new InvalidDataException("TypeScript selection must exercise expected diagnostics as well as valid inputs.");
        if (!checkedResults.Any(r => r.ExpectedDiagnostics?.Count == 0))
            throw new InvalidDataException("TypeScript selection must exercise valid inputs as well as expected diagnostics.");
    }
}

internal sealed class TypeScriptConformanceReport : IDisposable
{
    private readonly string _directory;
    private readonly StreamWriter _cases;
    private readonly StreamWriter _diff;

    internal TypeScriptConformanceReport(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _cases = new StreamWriter(Path.Combine(directory, "cases.jsonl")) { AutoFlush = true };
        _diff = new StreamWriter(Path.Combine(directory, "diagnostic-diff.txt")) { AutoFlush = true };
    }

    internal void Record(string path, string? expectedBucket, TypeScriptConformanceResult result)
    {
        string actualBucket = TypeScriptConformanceBaseline.EncodeBucket(result);
        _cases.WriteLine(JsonSerializer.Serialize(new
        {
            path, expectedBucket, actualBucket, result.Message,
            result.ExpectedDiagnostics, result.ActualDiagnostics
        }));
        if (expectedBucket == actualBucket && result.Outcome == TypeScriptConformanceOutcome.Pass)
            return;
        _diff.WriteLine($"{path}: {expectedBucket ?? "<new>"} -> {actualBucket}");
        _diff.WriteLine(result.Message);
        _diff.WriteLine("  expected: " + JsonSerializer.Serialize(result.ExpectedDiagnostics));
        _diff.WriteLine("  actual:   " + JsonSerializer.Serialize(result.ActualDiagnostics));
    }

    internal void Complete(TypeScriptConformanceBaselineDiff diff, int count, double seconds)
    {
        foreach (var entry in diff.RemovedEntries)
            _diff.WriteLine($"{entry.RelPath}: {entry.OldBucket} -> <removed>");
        bool passed = !TypeScriptConformanceGate.HasChanges(diff);
        _diff.WriteLine($"Compared {count} cases in {seconds:F1}s. Baseline unchanged: {passed}.");
        File.WriteAllText(Path.Combine(_directory, "summary.json"), JsonSerializer.Serialize(new
        {
            completed = true, passed, count, seconds, diff
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        _cases.Dispose();
        _diff.Dispose();
    }
}
