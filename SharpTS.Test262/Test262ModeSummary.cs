namespace SharpTS.Test262;

/// <summary>Aggregate outcome counts for one Test262 execution mode.</summary>
public sealed record Test262ModeSummary(
    int Total,
    IReadOnlyDictionary<Test262Outcome, int> Outcomes)
{
    public int Skipped => Count(Test262Outcome.Skipped);

    public int Executed => Total - Skipped;

    public double PassPercentage => Executed == 0
        ? 0
        : Count(Test262Outcome.Pass) * 100.0 / Executed;

    public int Count(Test262Outcome outcome) =>
        Outcomes.TryGetValue(outcome, out var count) ? count : 0;

    public static Test262ModeSummary Create(IReadOnlyDictionary<string, string> baseline)
    {
        var outcomes = baseline.Values
            .GroupBy(Test262Bucket.ParseOutcome)
            .ToDictionary(group => group.Key, group => group.Count());
        return new Test262ModeSummary(baseline.Count, outcomes);
    }
}
