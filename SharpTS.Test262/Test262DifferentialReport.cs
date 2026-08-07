using System.Globalization;
using System.Text;

namespace SharpTS.Test262;

public sealed record Test262TransitionCount(string Transition, int Count);

public sealed record Test262FolderCount(string Folder, int Count);

public sealed record Test262DifferentialEntry(
    string RelPath,
    string InterpretedBucket,
    string CompiledBucket)
{
    public Test262Outcome InterpretedOutcome => Test262Bucket.ParseOutcome(InterpretedBucket);

    public Test262Outcome CompiledOutcome => Test262Bucket.ParseOutcome(CompiledBucket);

    public string Transition => $"{InterpretedOutcome} -> {CompiledOutcome}";
}

public sealed class Test262DifferentialReport
{
    public static IReadOnlyList<Test262FolderCount> ClusterByFolder(
        IEnumerable<Test262DifferentialEntry> entries,
        int depth = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        return entries
            .GroupBy(entry => string.Join('/', entry.RelPath.Split('/').Take(depth)), StringComparer.Ordinal)
            .Select(group => new Test262FolderCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Folder, StringComparer.Ordinal)
            .ToList();
    }

    private Test262DifferentialReport(
        IReadOnlyList<Test262DifferentialEntry> entries,
        IReadOnlyList<string> interpretedOnly,
        IReadOnlyList<string> compiledOnly,
        Test262ModeSummary interpretedSummary,
        Test262ModeSummary compiledSummary,
        string? interpretedCorpusRevision,
        string? compiledCorpusRevision)
    {
        Entries = entries;
        InterpretedOnly = interpretedOnly;
        CompiledOnly = compiledOnly;
        InterpretedSummary = interpretedSummary;
        CompiledSummary = compiledSummary;
        InterpretedCorpusRevision = interpretedCorpusRevision;
        CompiledCorpusRevision = compiledCorpusRevision;
    }

    public IReadOnlyList<Test262DifferentialEntry> Entries { get; }

    public IReadOnlyList<string> InterpretedOnly { get; }

    public IReadOnlyList<string> CompiledOnly { get; }

    public Test262ModeSummary InterpretedSummary { get; }

    public Test262ModeSummary CompiledSummary { get; }

    public string? InterpretedCorpusRevision { get; }

    public string? CompiledCorpusRevision { get; }

    public IReadOnlyList<Test262DifferentialEntry> Divergences =>
        Entries.Where(entry => entry.InterpretedOutcome != entry.CompiledOutcome).ToList();

    public int AgreementCount => Entries.Count - Divergences.Count;

    public double AgreementPercentage => Entries.Count == 0
        ? 100
        : AgreementCount * 100.0 / Entries.Count;

    public IReadOnlyList<Test262TransitionCount> Histogram => Divergences
        .GroupBy(entry => entry.Transition, StringComparer.Ordinal)
        .Select(group => new Test262TransitionCount(group.Key, group.Count()))
        .OrderByDescending(item => item.Count)
        .ThenBy(item => item.Transition, StringComparer.Ordinal)
        .ToList();

    public IReadOnlyList<Test262DifferentialEntry> InterpreterDeficits => Divergences
        .Where(entry => entry.CompiledOutcome == Test262Outcome.Pass)
        .ToList();

    public IReadOnlyList<Test262DifferentialEntry> CompilerDeficits => Divergences
        .Where(entry => entry.InterpretedOutcome == Test262Outcome.Pass)
        .ToList();

    public IReadOnlyList<Test262DifferentialEntry> OtherDivergences => Divergences
        .Where(entry => entry.InterpretedOutcome != Test262Outcome.Pass &&
                        entry.CompiledOutcome != Test262Outcome.Pass)
        .ToList();

    public static Test262DifferentialReport Create(
        IReadOnlyDictionary<string, string> interpreted,
        IReadOnlyDictionary<string, string> compiled,
        string? interpretedCorpusRevision = null,
        string? compiledCorpusRevision = null)
    {
        var entries = interpreted.Keys
            .Intersect(compiled.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new Test262DifferentialEntry(path, interpreted[path], compiled[path]))
            .ToList();
        var interpretedOnly = interpreted.Keys
            .Except(compiled.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        var compiledOnly = compiled.Keys
            .Except(interpreted.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        return new Test262DifferentialReport(
            entries,
            interpretedOnly,
            compiledOnly,
            Test262ModeSummary.Create(interpreted),
            Test262ModeSummary.Create(compiled),
            interpretedCorpusRevision,
            compiledCorpusRevision);
    }

    public static Test262DifferentialReport CreateFromFiles(
        string interpretedPath,
        string compiledPath) =>
        Create(
            Test262Baseline.Read(interpretedPath),
            Test262Baseline.Read(compiledPath),
            Test262Baseline.ReadCorpusRevision(interpretedPath),
            Test262Baseline.ReadCorpusRevision(compiledPath));

    public string ToMarkdown()
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# Test262 Differential Report");
        markdown.AppendLine();
        AppendCorpusRevision(markdown);
        markdown.AppendLine("| Mode | Pass | Executed | Skipped | Pass rate |");
        markdown.AppendLine("|---|---:|---:|---:|---:|");
        AppendMode(markdown, "Interpreted", InterpretedSummary);
        AppendMode(markdown, "Compiled", CompiledSummary);
        markdown.AppendLine();
        markdown.AppendLine($"Outcome agreement: **{AgreementCount:N0}/{Entries.Count:N0} ({AgreementPercentage.ToString("F1", CultureInfo.InvariantCulture)}%)**.");
        markdown.AppendLine();
        markdown.AppendLine("## Divergence histogram");
        markdown.AppendLine();
        markdown.AppendLine("| Count | Interpreted → compiled |");
        markdown.AppendLine("|---:|---|");
        foreach (var item in Histogram)
            markdown.AppendLine($"| {item.Count} | {item.Transition.Replace(" -> ", " → ", StringComparison.Ordinal)} |");
        AppendClusters(markdown, "Track A — interpreter deficits", InterpreterDeficits);
        AppendClusters(markdown, "Track B — compiler deficits", CompilerDeficits);
        AppendEntries(markdown, "Other divergences", OtherDivergences);
        AppendCoverageGaps(markdown);
        return markdown.ToString();
    }

    public void WriteMarkdown(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, ToMarkdown());
    }

    private static void AppendMode(StringBuilder markdown, string mode, Test262ModeSummary summary) =>
        markdown.AppendLine($"| {mode} | {summary.Count(Test262Outcome.Pass)} | {summary.Executed} | {summary.Skipped} | {summary.PassPercentage.ToString("F1", CultureInfo.InvariantCulture)}% |");

    private void AppendCorpusRevision(StringBuilder markdown)
    {
        if (InterpretedCorpusRevision is not null && InterpretedCorpusRevision == CompiledCorpusRevision)
            markdown.AppendLine($"Corpus revision: `{InterpretedCorpusRevision}`.");
        else if (InterpretedCorpusRevision is not null || CompiledCorpusRevision is not null)
            markdown.AppendLine($"> Warning: baseline corpus mismatch (interpreted `{InterpretedCorpusRevision ?? "unknown"}`, compiled `{CompiledCorpusRevision ?? "unknown"}`).");
        else
            markdown.AppendLine("Corpus revision: unavailable.");
        markdown.AppendLine();
    }

    private static void AppendClusters(
        StringBuilder markdown,
        string title,
        IReadOnlyList<Test262DifferentialEntry> entries)
    {
        markdown.AppendLine();
        markdown.AppendLine($"## {title} ({entries.Count})");
        markdown.AppendLine();
        markdown.AppendLine("| Count | Folder |");
        markdown.AppendLine("|---:|---|");
        foreach (var item in ClusterByFolder(entries))
            markdown.AppendLine($"| {item.Count} | `{item.Folder}` |");
    }

    private static void AppendEntries(
        StringBuilder markdown,
        string title,
        IReadOnlyList<Test262DifferentialEntry> entries)
    {
        markdown.AppendLine();
        markdown.AppendLine($"## {title} ({entries.Count})");
        markdown.AppendLine();
        markdown.AppendLine("| Test | Interpreted → compiled |");
        markdown.AppendLine("|---|---|");
        foreach (var entry in entries)
            markdown.AppendLine($"| `{entry.RelPath}` | {entry.Transition.Replace(" -> ", " → ", StringComparison.Ordinal)} |");
    }

    private void AppendCoverageGaps(StringBuilder markdown)
    {
        markdown.AppendLine();
        markdown.AppendLine($"## Coverage gaps ({InterpretedOnly.Count + CompiledOnly.Count})");
        markdown.AppendLine();
        foreach (var path in InterpretedOnly)
            markdown.AppendLine($"- Interpreted only: `{path}`");
        foreach (var path in CompiledOnly)
            markdown.AppendLine($"- Compiled only: `{path}`");
        if (InterpretedOnly.Count == 0 && CompiledOnly.Count == 0)
            markdown.AppendLine("None.");
    }
}
