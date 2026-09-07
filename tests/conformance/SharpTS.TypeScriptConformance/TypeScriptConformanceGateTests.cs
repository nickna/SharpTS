using Xunit;

namespace SharpTS.TypeScriptConformance;

public sealed class TypeScriptConformanceGateTests : IDisposable
{
    private const string Revision = "050880ce59e30b356b686bd3144efe24f875ebc8";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sharpts-tsgate-{Guid.NewGuid():N}");

    public TypeScriptConformanceGateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MissingBaselineFailsWithoutCreatingExpectations()
    {
        string path = Path.Combine(_root, "missing.txt");
        Assert.Throws<FileNotFoundException>(() => TypeScriptConformanceGate.ReadBaseline(path, Revision));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("# legacy baseline")]
    [InlineData("# SharpTS baseline-format=2 suite=TypeScript corpus=050880ce59e30b356b686bd3144efe24f875ebc8 ")]
    [InlineData("# SharpTS baseline-format=1 suite=TypeScript corpus=0000000000000000000000000000000000000000 ")]
    public void IncompatibleBaselineFails(string header)
    {
        string path = Path.Combine(_root, "baseline.txt");
        File.WriteAllText(path, header + "\ntest.ts Pass\n");
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.ReadBaseline(path, Revision));
    }

    [Fact]
    public void EmptyBaselineFails()
    {
        string path = Path.Combine(_root, "baseline.txt");
        File.WriteAllText(path, TypeScriptConformanceBaseline.Header(Revision));
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.ReadBaseline(path, Revision));
    }

    [Fact]
    public void SmokeProjectsCommittedExpectationsAndDetectsUnbaselinedCases()
    {
        var baseline = new Dictionary<string, string> { ["selected.ts"] = "Pass", ["broader.ts"] = "Pass" };
        var selected = TypeScriptConformanceGate.SelectBaseline(baseline, ["selected.ts", "new.ts"], smoke: true);
        var diff = TypeScriptConformanceBaselineDiffer.Diff(selected,
            new Dictionary<string, string> { ["selected.ts"] = "Pass", ["new.ts"] = "Pass" });
        Assert.Empty(diff.RemovedEntries);
        Assert.Equal("new.ts", Assert.Single(diff.NewEntries).RelPath);
        Assert.True(TypeScriptConformanceGate.HasChanges(diff));
        Assert.Same(baseline, TypeScriptConformanceGate.SelectBaseline(baseline, ["selected.ts"], smoke: false));
    }

    [Theory]
    [InlineData("Pass", "Fail")]
    [InlineData("Fail", "Pass")]
    [InlineData("Fail", "TypeCheckError")]
    [InlineData("Skipped:one", "Skipped:two")]
    [InlineData("Pass", "Skipped:explicitly-skipped")]
    public void EveryUnexpectedBucketChangeFails(string before, string after)
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["test.ts"] = before },
            new Dictionary<string, string> { ["test.ts"] = after });
        Assert.True(TypeScriptConformanceGate.HasChanges(diff));
    }

    [Fact]
    public void RemovedCaseFails()
    {
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["test.ts"] = "Pass" }, new Dictionary<string, string>());
        Assert.True(TypeScriptConformanceGate.HasChanges(diff));
    }

    [Fact]
    public void MissingSelectionAndMissingCorpusFolderFail()
    {
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.SelectBaseline(
            new Dictionary<string, string>(), [], smoke: true));
        Assert.Throws<DirectoryNotFoundException>(() => TypeScriptConformanceTests.EnumerateTestFiles(
            _root, ["missing-folder"], []));
        Assert.Throws<FileNotFoundException>(() => TypeScriptConformanceTests.EnumerateTestFiles(
            _root, [], ["missing.ts"]));
    }

    [Fact]
    public void EmptySkippedAndCrashedRunsAreNotMeaningful()
    {
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.RequireMeaningfulResults([]));
        foreach (var outcome in new[] { TypeScriptConformanceOutcome.Skipped,
            TypeScriptConformanceOutcome.ParseError, TypeScriptConformanceOutcome.TypeCheckError,
            TypeScriptConformanceOutcome.HarnessError })
        {
            Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.RequireMeaningfulResults(
                [new TypeScriptConformanceResult(outcome, null, null)]));
        }
    }

    [Fact]
    public void MeaningfulSelectionRequiresValidAndDiagnosticInputs()
    {
        var valid = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Pass, null, null, [], []);
        var error = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Pass, null, null,
            [new BaselineDiagnostic(1, "TS2322")], [new BaselineDiagnostic(1, "TS2322")]);
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.RequireMeaningfulResults([valid]));
        Assert.Throws<InvalidDataException>(() => TypeScriptConformanceGate.RequireMeaningfulResults([error]));
        TypeScriptConformanceGate.RequireMeaningfulResults([valid, error]);
    }

    [Fact]
    public void RegressionReportIncludesCaseAndExpectedActualDiagnostics()
    {
        var result = new TypeScriptConformanceResult(TypeScriptConformanceOutcome.Fail,
            "missing: (1, TS2322)", null, [new BaselineDiagnostic(1, "TS2322")], []);
        var diff = TypeScriptConformanceBaselineDiffer.Diff(
            new Dictionary<string, string> { ["test.ts"] = "Pass" },
            new Dictionary<string, string> { ["test.ts"] = "Fail" });
        using (var report = new TypeScriptConformanceReport(_root))
        {
            report.Record("test.ts", "Pass", result);
            report.Complete(diff, 1, 0.1);
        }
        string diagnosticDiff = File.ReadAllText(Path.Combine(_root, "diagnostic-diff.txt"));
        Assert.Contains("test.ts: Pass -> Fail", diagnosticDiff);
        Assert.Contains("missing: (1, TS2322)", diagnosticDiff);
        Assert.Contains("actual:   []", diagnosticDiff);
        Assert.Contains("\"passed\": false", File.ReadAllText(Path.Combine(_root, "summary.json")));
        Assert.Contains("TS2322", File.ReadAllText(Path.Combine(_root, "cases.jsonl")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
