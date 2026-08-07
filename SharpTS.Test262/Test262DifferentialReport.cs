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
