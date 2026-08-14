namespace SharpTS.Test262;

public sealed record Test262WideSweepWriteResult(
    string SnapshotPath,
    string? DifferentialReportPath);

/// <summary>
/// Persists the path-level results from a wide Test262 sweep. The snapshots
/// are intentionally gitignored diagnostic artifacts; once both execution
/// modes exist for the same corpus revision, a differential report is written
/// beside the project so a sweep can be triaged without rerunning either mode.
/// </summary>
public static class Test262WideSweepArtifacts
{
    private static readonly object WriteLock = new();

    public static Test262WideSweepWriteResult Write(
        string projectDirectory,
        Test262ExecutionMode mode,
        IReadOnlyDictionary<string, string> results,
        string corpusRevision)
    {
        lock (WriteLock)
        {
            var snapshotDirectory = Path.Combine(projectDirectory, "wide-sweep-baselines");
            var snapshotPath = Path.Combine(snapshotDirectory,
                mode == Test262ExecutionMode.Interpreted ? "interpreted.txt" : "compiled.txt");
            Test262Baseline.Write(
                snapshotPath,
                results.Select(entry => (entry.Key, entry.Value)),
                corpusRevision);

            var interpretedPath = Path.Combine(snapshotDirectory, "interpreted.txt");
            var compiledPath = Path.Combine(snapshotDirectory, "compiled.txt");
            if (!File.Exists(interpretedPath) || !File.Exists(compiledPath))
                return new Test262WideSweepWriteResult(snapshotPath, null);

            var interpretedRevision = Test262Baseline.ReadCorpusRevision(interpretedPath);
            var compiledRevision = Test262Baseline.ReadCorpusRevision(compiledPath);
            if (interpretedRevision != compiledRevision)
                return new Test262WideSweepWriteResult(snapshotPath, null);

            var reportPath = Path.Combine(projectDirectory, "wide-sweep-report.md");
            Test262DifferentialReport.CreateFromFiles(interpretedPath, compiledPath)
                .WriteMarkdown(reportPath);
            return new Test262WideSweepWriteResult(snapshotPath, reportPath);
        }
    }
}
