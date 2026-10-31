using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.TypeScriptConformance;

public class TypeScriptTypesConformanceTests(ITestOutputHelper output)
{
    private const string PinnedRevision = "050880ce59e30b356b686bd3144efe24f875ebc8";

    [Fact]
    public async Task TypesPilotBaseline()
    {
        string root = TypeScriptConformancePaths.TryFindRoot()
            ?? throw new DirectoryNotFoundException("The pinned TypeScript corpus is required for the type pilot.");
        string project = TypeScriptConformancePaths.TryFindProjectDir()
            ?? throw new DirectoryNotFoundException("TypeScript conformance project source was not found.");
        Assert.Equal(PinnedRevision, TypeScriptConformancePaths.GetCorpusRevision(root));
        var config = TypeScriptConformanceConfig.Load(Path.Combine(project, "config", "types-subset.json"));
        Assert.Empty(config.Folders);
        Assert.InRange(config.Files!.Count, 10, 12);
        string header = $"# SharpTS baseline-format=1 suite=TypeScriptTypes corpus={PinnedRevision}";
        var lines = new List<string> { header };
        var runner = new TypeScriptTypesConformanceRunner(root);
        string? reportDirectory = Environment.GetEnvironmentVariable("SHARPTS_TSTYPES_REPORT_DIR");
        if (reportDirectory is not null) Directory.CreateDirectory(reportDirectory);
        foreach (string relative in config.Files.Order(StringComparer.Ordinal))
        {
            var result = await Task.Run(() => runner.RunOne(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
                .WaitAsync(config.Timeout);
            string summary = $"{relative} {result.Outcome} matches={result.Matches}" +
                $" mismatches={Count(TypeObservationDifferenceKind.TypeTextMismatch)}" +
                $" missing={Count(TypeObservationDifferenceKind.MissingSharpTSObservation)}" +
                $" extra={Count(TypeObservationDifferenceKind.ExtraSharpTSObservation)}" +
                $" unsupported={Count(TypeObservationDifferenceKind.UnsupportedSharpTSType)}" +
                $" sha256={Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result))))}";
            lines.Add(summary);
            output.WriteLine(summary);
            if (result.Message is not null) output.WriteLine(result.Message);
            if (Environment.GetEnvironmentVariable("SHARPTS_TSTYPES_DUMP_FAILURES") == "1")
                foreach (var difference in result.Differences)
                    output.WriteLine($"{difference.Kind} {difference.Key.File}:{difference.Key.Line} {difference.Key.SourceText}#{difference.Key.Ordinal}: expected {difference.Expected}; actual {difference.Actual}; {difference.Detail}");
            if (reportDirectory is not null)
                await File.WriteAllTextAsync(Path.Combine(reportDirectory, relative.Replace('/', '_') + ".json"),
                    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true,
                        Converters = { new JsonStringEnumConverter() } }) + "\n");
            Assert.NotEqual(TypeScriptTypesOutcome.HarnessError, result.Outcome);
            int Count(TypeObservationDifferenceKind kind) => result.Differences.Count(difference => difference.Kind == kind);
        }
        string current = string.Join("\n", lines) + "\n";
        string baselinePath = Path.Combine(project, "baselines", "types.txt");
        if (Environment.GetEnvironmentVariable("SHARPTS_TSTYPES_UPDATE_BASELINE") == "1")
        {
            await File.WriteAllTextAsync(baselinePath, current);
            return;
        }
        Assert.True(File.Exists(baselinePath), "Missing type baseline. Generate it explicitly with SHARPTS_TSTYPES_UPDATE_BASELINE=1.");
        string expected = (await File.ReadAllTextAsync(baselinePath)).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.StartsWith(header + "\n", expected, StringComparison.Ordinal);
        Assert.Equal(expected, current);
    }

    [Fact]
    public void StructuredComparisonNeverNormalizesAwayLiteralOrAnyDifferences()
    {
        var expected = new[] { Expected("value", "\"literal\""), Expected("other", "number") };
        var result = TypeScriptTypesConformanceRunner.Compare(expected,
            [new(new("main.ts", 1, "value", 1), "string"), new(new("main.ts", 1, "other", 1), "any")]);
        Assert.Equal(TypeScriptTypesOutcome.Mismatch, result.Outcome);
        Assert.Equal(2, result.Differences.Count);
        Assert.All(result.Differences, difference => Assert.Equal(TypeObservationDifferenceKind.TypeTextMismatch, difference.Kind));
    }

    [Fact]
    public void MissingExtraUnsupportedAndAmbiguousObservationsStayVisible()
    {
        var expected = new[] { Expected("missing", "number"), Expected("unknownShape", "number") };
        var actual = new[] { new ActualTypeObservation(new("main.ts", 1, "extra", 1), "number"),
            new ActualTypeObservation(new("main.ts", 1, "unknownShape", 1), null, new("Custom", "unsupported")) };
        var result = TypeScriptTypesConformanceRunner.Compare(expected, actual);
        Assert.Contains(result.Differences, difference => difference.Kind == TypeObservationDifferenceKind.MissingSharpTSObservation);
        Assert.Contains(result.Differences, difference => difference.Kind == TypeObservationDifferenceKind.ExtraSharpTSObservation);
        Assert.Contains(result.Differences, difference => difference.Kind == TypeObservationDifferenceKind.UnsupportedSharpTSType);
        Assert.Equal(TypeScriptTypesOutcome.HarnessError,
            TypeScriptTypesConformanceRunner.Compare([], [actual[0], actual[0]]).Outcome);
    }

    [Fact]
    public void MultiFileProgramUsesVirtualCoordinatesAndReportsMissingBaselines()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-types-" + Guid.NewGuid().ToString("N"));
        string sources = TypeScriptConformancePaths.ConformanceDir(root);
        string references = TypeScriptConformancePaths.BaselinesDir(root);
        Directory.CreateDirectory(sources);
        Directory.CreateDirectory(references);
        try
        {
            string path = Path.Combine(sources, "multi.ts");
            File.WriteAllText(path, "// @noLib: true\n// @filename: a.ts\nexport const value = 1;\n" +
                "// @filename: b.ts\nimport { value } from \"./a\";\n\nconst copy = value;\n");
            var runner = new TypeScriptTypesConformanceRunner(root);
            Assert.Equal(TypeScriptTypesOutcome.NoBaseline, runner.RunOne(path).Outcome);
            File.Copy(Path.Combine(TypeScriptConformancePaths.TryFindProjectDir()!, "Fixtures", "TypesBaselines", "multi.types"),
                Path.Combine(references, "multi.types"));
            var result = runner.RunOne(path);
            Assert.NotEqual(TypeScriptTypesOutcome.HarnessError, result.Outcome);
            Assert.True(result.Matches > 0);
            Assert.All(result.Differences, difference => Assert.Contains(difference.Key.File, new[] { "a.ts", "b.ts" }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static TypesBaselineObservation Expected(string text, string type) => new("main.ts", 1, text, text, 1, type, null);
}
