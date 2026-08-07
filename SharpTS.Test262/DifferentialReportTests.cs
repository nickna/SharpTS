using Xunit;

namespace SharpTS.Test262;

public sealed class DifferentialReportTests
{
    [Fact]
    public void Mode_summary_counts_each_outcome()
    {
        var baseline = Baseline(
            ("a.js", "Pass"),
            ("b.js", "Fail"),
            ("c.js", "Skipped:reason"),
            ("d.js", "Fail"));

        var summary = Test262ModeSummary.Create(baseline);

        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.Count(Test262Outcome.Pass));
        Assert.Equal(2, summary.Count(Test262Outcome.Fail));
        Assert.Equal(1, summary.Count(Test262Outcome.Skipped));
        Assert.Equal(0, summary.Count(Test262Outcome.Timeout));
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(3, summary.Executed);
    }

    private static IReadOnlyDictionary<string, string> Baseline(
        params (string Path, string Bucket)[] entries) =>
        entries.ToDictionary(entry => entry.Path, entry => entry.Bucket, StringComparer.Ordinal);
}
