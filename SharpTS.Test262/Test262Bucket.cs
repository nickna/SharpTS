namespace SharpTS.Test262;

/// <summary>
/// Helpers for the encoded outcome buckets stored in Test262 baselines.
/// </summary>
public static class Test262Bucket
{
    public static Test262Outcome ParseOutcome(string bucket)
    {
        var colon = bucket.IndexOf(':');
        var name = colon < 0 ? bucket : bucket[..colon];
        return Enum.TryParse<Test262Outcome>(name, out var outcome)
            ? outcome
            : Test262Outcome.RuntimeError;
    }
}
