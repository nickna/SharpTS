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
    private Test262DifferentialReport(
        IReadOnlyList<Test262DifferentialEntry> entries,
        IReadOnlyList<string> interpretedOnly,
        IReadOnlyList<string> compiledOnly)
    {
        Entries = entries;
        InterpretedOnly = interpretedOnly;
        CompiledOnly = compiledOnly;
    }

    public IReadOnlyList<Test262DifferentialEntry> Entries { get; }

    public IReadOnlyList<string> InterpretedOnly { get; }

    public IReadOnlyList<string> CompiledOnly { get; }

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
        var interpretedOnly = interpreted.Keys
            .Except(compiled.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        var compiledOnly = compiled.Keys
            .Except(interpreted.Keys, StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        return new Test262DifferentialReport(entries, interpretedOnly, compiledOnly);
    }
}
