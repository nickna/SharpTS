using Xunit;

namespace SharpTS.Test262;

public sealed class DifferentialReportTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void Report_mode_parses_its_environment_switch(string? value, bool expected)
    {
        Assert.Equal(expected, Test262ReportMode.IsEnabled(value));
    }

    [Fact]
    public void Report_mode_generates_from_a_project_baseline_directory()
    {
        var directory = Directory.CreateTempSubdirectory("sharpts-report-mode-");
        try
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            var baselines = Directory.CreateDirectory(Path.Combine(directory.FullName, "baselines"));
            Test262Baseline.Write(Path.Combine(baselines.FullName, "interpreted.txt"), [("a.js", "Fail")], revision);
            Test262Baseline.Write(Path.Combine(baselines.FullName, "compiled.txt"), [("a.js", "Pass")], revision);

            var output = Test262ReportMode.Generate(directory.FullName);

            Assert.True(File.Exists(output));
            Assert.Contains("| 1 | Fail → Pass |", File.ReadAllText(output));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Differential_entry_exposes_outcomes_and_transition()
    {
        var entry = new Test262DifferentialEntry("a.js", "Fail", "Pass");

        Assert.Equal(Test262Outcome.Fail, entry.InterpretedOutcome);
        Assert.Equal(Test262Outcome.Pass, entry.CompiledOutcome);
        Assert.Equal("Fail -> Pass", entry.Transition);
    }

    [Fact]
    public void Report_aligns_entries_present_in_both_modes()
    {
        var interpreted = Baseline(("b.js", "Fail"), ("a.js", "Pass"), ("only-i.js", "Pass"));
        var compiled = Baseline(("a.js", "Fail"), ("b.js", "Pass"), ("only-c.js", "Pass"));

        var report = Test262DifferentialReport.Create(interpreted, compiled);

        Assert.Collection(
            report.Entries,
            entry => Assert.Equal("a.js", entry.RelPath),
            entry => Assert.Equal("b.js", entry.RelPath));
        Assert.Equal(["only-i.js"], report.InterpretedOnly);
        Assert.Equal(["only-c.js"], report.CompiledOnly);
        Assert.Equal(3, report.InterpretedSummary.Total);
        Assert.Equal(3, report.CompiledSummary.Total);
    }

    [Fact]
    public void Report_excludes_matching_outcomes_from_divergences()
    {
        var interpreted = Baseline(("same.js", "Pass"), ("skip.js", "Skipped:left"), ("diff.js", "Fail"));
        var compiled = Baseline(("same.js", "Pass"), ("skip.js", "Skipped:right"), ("diff.js", "Pass"));

        var report = Test262DifferentialReport.Create(interpreted, compiled);

        var divergence = Assert.Single(report.Divergences);
        Assert.Equal("diff.js", divergence.RelPath);
        Assert.Equal(2, report.AgreementCount);
        Assert.Equal(200.0 / 3, report.AgreementPercentage, precision: 10);
    }

    [Fact]
    public void Empty_report_has_complete_agreement()
    {
        var report = Test262DifferentialReport.Create(Baseline(), Baseline());

        Assert.Equal(100, report.AgreementPercentage);
    }

    [Fact]
    public void Report_builds_a_stable_transition_histogram()
    {
        var interpreted = Baseline(("a.js", "Fail"), ("b.js", "Fail"), ("c.js", "Pass"));
        var compiled = Baseline(("a.js", "Pass"), ("b.js", "Pass"), ("c.js", "RuntimeError"));

        var report = Test262DifferentialReport.Create(interpreted, compiled);

        Assert.Equal(
            [new("Fail -> Pass", 2), new("Pass -> RuntimeError", 1)],
            report.Histogram);
        Assert.Equal(["a.js", "b.js"], report.InterpreterDeficits.Select(entry => entry.RelPath));
        Assert.Equal(["c.js"], report.CompilerDeficits.Select(entry => entry.RelPath));
    }

    [Fact]
    public void Report_keeps_nonpass_transitions_in_the_other_bucket()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("a.js", "Fail")),
            Baseline(("a.js", "RuntimeError")));

        Assert.Equal(["a.js"], report.OtherDivergences.Select(entry => entry.RelPath));
        Assert.Empty(report.InterpreterDeficits);
        Assert.Empty(report.CompilerDeficits);
    }

    [Fact]
    public void Report_clusters_entries_by_stable_folder_prefix()
    {
        var entries = new[]
        {
            new Test262DifferentialEntry("test/built-ins/Object/a.js", "Fail", "Pass"),
            new Test262DifferentialEntry("test/built-ins/Object/b.js", "Fail", "Pass"),
            new Test262DifferentialEntry("test/built-ins/Array/a.js", "Fail", "Pass"),
        };

        Assert.Equal(
            [new("test/built-ins/Object", 2), new("test/built-ins/Array", 1)],
            Test262DifferentialReport.ClusterByFolder(entries));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Test262DifferentialReport.ClusterByFolder(entries, 0));
    }

    [Fact]
    public void Markdown_starts_with_mode_coverage_and_agreement()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("a.js", "Pass"), ("b.js", "Skipped:reason")),
            Baseline(("a.js", "Pass"), ("b.js", "Fail")));

        var markdown = report.ToMarkdown();

        Assert.StartsWith("# Test262 Differential Report", markdown);
        Assert.Contains("| Interpreted | 1 | 1 | 1 | 100.0% |", markdown);
        Assert.Contains("| Compiled | 1 | 2 | 0 | 50.0% |", markdown);
        Assert.Contains("Outcome agreement: **1/2 (50.0%)**.", markdown);
        Assert.Contains("| 1 | Skipped → Fail |", markdown);
    }

    [Fact]
    public void Markdown_clusters_track_a_interpreter_deficits()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("test/built-ins/Object/a.js", "Fail"), ("test/built-ins/Object/b.js", "RuntimeError")),
            Baseline(("test/built-ins/Object/a.js", "Pass"), ("test/built-ins/Object/b.js", "Pass")));

        var markdown = report.ToMarkdown();

        Assert.Contains("## Track A — interpreter deficits (2)", markdown);
        Assert.Contains("| 2 | `test/built-ins/Object` |", markdown);
    }

    [Fact]
    public void Markdown_clusters_track_b_compiler_deficits()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("test/built-ins/Array/a.js", "Pass")),
            Baseline(("test/built-ins/Array/a.js", "Fail")));

        var markdown = report.ToMarkdown();

        Assert.Contains("## Track B — compiler deficits (1)", markdown);
        Assert.Contains("| 1 | `test/built-ins/Array` |", markdown);
    }

    [Fact]
    public void Markdown_lists_other_divergences()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("test/built-ins/Object/a.js", "Fail")),
            Baseline(("test/built-ins/Object/a.js", "RuntimeError")));

        var markdown = report.ToMarkdown();

        Assert.Contains("## Other divergences (1)", markdown);
        Assert.Contains("| `test/built-ins/Object/a.js` | Fail → RuntimeError |", markdown);
    }

    [Fact]
    public void Markdown_lists_mode_specific_coverage_gaps()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(("both.js", "Pass"), ("only-i.js", "Fail")),
            Baseline(("both.js", "Pass"), ("only-c.js", "Fail")));

        var markdown = report.ToMarkdown();

        Assert.Contains("## Coverage gaps (2)", markdown);
        Assert.Contains("- Interpreted only: `only-i.js`", markdown);
        Assert.Contains("- Compiled only: `only-c.js`", markdown);
    }

    [Fact]
    public void Markdown_warns_when_baseline_corpus_revisions_differ()
    {
        var report = Test262DifferentialReport.Create(
            Baseline(), Baseline(), "aaaa", "bbbb");

        Assert.Contains(
            "> Warning: baseline corpus mismatch (interpreted `aaaa`, compiled `bbbb`).",
            report.ToMarkdown());
    }

    [Fact]
    public void Report_loads_both_baseline_files_and_their_revision()
    {
        var directory = Directory.CreateTempSubdirectory("sharpts-differential-");
        try
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            var interpretedPath = Path.Combine(directory.FullName, "interpreted.txt");
            var compiledPath = Path.Combine(directory.FullName, "compiled.txt");
            Test262Baseline.Write(interpretedPath, [("a.js", "Fail")], revision);
            Test262Baseline.Write(compiledPath, [("a.js", "Pass")], revision);

            var report = Test262DifferentialReport.CreateFromFiles(interpretedPath, compiledPath);

            Assert.Equal(revision, report.InterpretedCorpusRevision);
            Assert.Single(report.InterpreterDeficits);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Report_writes_markdown_and_creates_its_directory()
    {
        var directory = Directory.CreateTempSubdirectory("sharpts-differential-");
        try
        {
            var path = Path.Combine(directory.FullName, "nested", "report.md");
            Test262DifferentialReport.Create(Baseline(), Baseline()).WriteMarkdown(path);

            Assert.StartsWith("# Test262 Differential Report", File.ReadAllText(path));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Wide_sweep_artifacts_write_mode_snapshots_then_a_differential_report()
    {
        var directory = Directory.CreateTempSubdirectory("sharpts-wide-sweep-");
        try
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";
            var interpreted = Test262WideSweepArtifacts.Write(
                directory.FullName,
                Test262ExecutionMode.Interpreted,
                Baseline(("a.js", "Pass"), ("b.js", "Fail")),
                revision);

            Assert.True(File.Exists(interpreted.SnapshotPath));
            Assert.Null(interpreted.DifferentialReportPath);

            var compiled = Test262WideSweepArtifacts.Write(
                directory.FullName,
                Test262ExecutionMode.Compiled,
                Baseline(("a.js", "Fail"), ("b.js", "Pass")),
                revision);

            Assert.NotNull(compiled.DifferentialReportPath);
            Assert.True(File.Exists(compiled.DifferentialReportPath));
            var markdown = File.ReadAllText(compiled.DifferentialReportPath);
            Assert.Contains("| 1 | Pass → Fail |", markdown);
            Assert.Contains("| 1 | Fail → Pass |", markdown);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
