namespace SharpTS.Execution;

/// <summary>
/// One completed or failed stage in an execution pipeline.
/// </summary>
/// <param name="Name">Stable, architecture-level phase identifier.</param>
/// <param name="DurationMs">Precise wall-clock duration in milliseconds.</param>
/// <param name="Status"><c>completed</c> or <c>failed</c>.</param>
public sealed record ExecutionPhaseTiming(
    string Name,
    double DurationMs,
    string Status)
{
    public const string CompletedStatus = "completed";
    public const string FailedStatus = "failed";

    internal static ExecutionPhaseTiming Completed(string name, double durationMs) =>
        new(name, Math.Max(0, durationMs), CompletedStatus);

    internal static ExecutionPhaseTiming Failed(string name, double durationMs) =>
        new(name, Math.Max(0, durationMs), FailedStatus);
}
