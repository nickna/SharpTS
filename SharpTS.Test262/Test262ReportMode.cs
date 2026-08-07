namespace SharpTS.Test262;

public static class Test262ReportMode
{
    public const string EnvironmentVariable = "SHARPTS_TEST262_DIFFERENTIAL_REPORT";

    public static bool IsEnabled(string? value) => value is "1" or "true" or "TRUE";

    public static bool IsEnabled() => IsEnabled(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static string Generate(string projectDirectory, string? outputPath = null)
    {
        var baselineDirectory = Path.Combine(projectDirectory, "baselines");
        var interpretedPath = Path.Combine(baselineDirectory, "interpreted.txt");
        var compiledPath = Path.Combine(baselineDirectory, "compiled.txt");
        if (!File.Exists(interpretedPath) || !File.Exists(compiledPath))
            throw new FileNotFoundException("Both interpreted and compiled Test262 baselines are required.");

        outputPath ??= Path.Combine(projectDirectory, "differential-report.md");
        Test262DifferentialReport.CreateFromFiles(interpretedPath, compiledPath)
            .WriteMarkdown(outputPath);
        return outputPath;
    }
}
