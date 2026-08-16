using System.Collections.ObjectModel;
using System.Diagnostics;

namespace SharpTS.Diagnostics;

/// <summary>
/// Optional internal timing sink. Callers that do not supply one pay no timestamp cost.
/// </summary>
internal sealed class ExecutionTimingCollector
{
    private readonly List<ExecutionPhaseTiming> _timings = [];

    public T Measure<T>(string name, Func<T> action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = action();
            Complete(name, startedAt);
            return result;
        }
        catch
        {
            Fail(name, startedAt);
            throw;
        }
    }

    public void Measure(string name, Action action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            action();
            Complete(name, startedAt);
        }
        catch
        {
            Fail(name, startedAt);
            throw;
        }
    }

    public long Start() => Stopwatch.GetTimestamp();

    public void Complete(string name, long startedAt) =>
        _timings.Add(ExecutionPhaseTiming.Completed(name, Elapsed(startedAt)));

    public void Fail(string name, long startedAt) =>
        _timings.Add(ExecutionPhaseTiming.Failed(name, Elapsed(startedAt)));

    public void FailDuration(string name, double durationMs) =>
        _timings.Add(ExecutionPhaseTiming.Failed(name, durationMs));

    public IReadOnlyList<ExecutionPhaseTiming> Snapshot() =>
        new ReadOnlyCollection<ExecutionPhaseTiming>(_timings.ToArray());

    private static double Elapsed(long startedAt) =>
        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}
