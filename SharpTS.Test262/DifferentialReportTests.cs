using Xunit;

namespace SharpTS.Test262;

public sealed class DifferentialReportTests
{
    [Fact]
    public void Differential_entry_exposes_outcomes_and_transition()
    {
        var entry = new Test262DifferentialEntry("a.js", "Fail", "Pass");

        Assert.Equal(Test262Outcome.Fail, entry.InterpretedOutcome);
        Assert.Equal(Test262Outcome.Pass, entry.CompiledOutcome);
        Assert.Equal("Fail -> Pass", entry.Transition);
    }

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
        Assert.Equal(100.0 / 3, summary.PassPercentage, precision: 10);
    }

    [Fact]
    public void Mode_summary_reports_zero_percent_when_every_test_is_skipped()
    {
        var summary = Test262ModeSummary.Create(Baseline(("a.js", "Skipped:reason")));

        Assert.Equal(0, summary.PassPercentage);
    }

    private static IReadOnlyDictionary<string, string> Baseline(
        params (string Path, string Bucket)[] entries) =>
        entries.ToDictionary(entry => entry.Path, entry => entry.Bucket, StringComparer.Ordinal);
}
