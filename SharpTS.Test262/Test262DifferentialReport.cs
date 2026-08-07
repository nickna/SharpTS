namespace SharpTS.Test262;

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
    private Test262DifferentialReport(IReadOnlyList<Test262DifferentialEntry> entries) =>
        Entries = entries;

    public IReadOnlyList<Test262DifferentialEntry> Entries { get; }

    public IReadOnlyList<Test262DifferentialEntry> Divergences =>
        Entries.Where(entry => entry.InterpretedOutcome != entry.CompiledOutcome).ToList();

    public static Test262DifferentialReport Create(
        IReadOnlyDictionary<string, string> interpreted,
        IReadOnlyDictionary<string, string> compiled)
    {
        var entries = interpreted.Keys
            .Intersect(compiled.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new Test262DifferentialEntry(path, interpreted[path], compiled[path]))
            .ToList();
        return new Test262DifferentialReport(entries);
    }
}
