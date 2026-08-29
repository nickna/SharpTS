using Xunit;

namespace SharpTS.TypeScriptConformance;

public class TypesBaselineParserTests
{
    [Fact]
    public void Parse_SingleFile_PreservesOrderTypesAndOccurrences()
    {
        string baseline = ReadFixture("single.types");
        TypeScriptConformanceFile source = new(
            "single.ts",
            "const x = 1, y = 2;\n\nconst total = x + x;\n\nlet shape: { left: string; right: number };");

        TypesBaselineDocument result = TypesBaselineParser.Parse(baseline, [source]);

        TypesBaselineFile file = Assert.Single(result.Files);
        Assert.Equal("single.ts", file.VirtualFileName);
        Assert.Equal(9, file.Observations.Count);
        Assert.Equal(["x", "1", "y", "2", "total", "x + x", "x", "x", "shape"],
            file.Observations.Select(observation => observation.SourceText));

        TypesBaselineObservation[] repeated = file.Observations
            .Where(observation => observation.SourceLine == 3 && observation.SourceText == "x")
            .ToArray();
        Assert.Equal([1, 2], repeated.Select(observation => observation.OccurrenceOrdinal));
        Assert.All(repeated, observation => Assert.Equal("const total = x + x;", observation.SourceLineText));

        TypesBaselineObservation shape = file.Observations[^1];
        Assert.Equal(5, shape.SourceLine);
        Assert.Equal("{ left: string; right: number; }", shape.ExpectedTypeText);
        Assert.Equal("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^", shape.Underline);
    }

    [Fact]
    public void Parse_MultiFile_NormalizesNamesAndResetsCoordinates()
    {
        string baseline = ReadFixture("multi.types");
        TypeScriptConformanceFile[] sources =
        [
            new("a.ts", "export const value = 1;"),
            new("./b.ts", "import { value } from \"./a\";\n\nconst copy = value;"),
        ];

        TypesBaselineDocument result = TypesBaselineParser.Parse(baseline, sources);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal(["a.ts", "b.ts"], result.Files.Select(file => file.VirtualFileName));
        Assert.Equal([1, 1], result.Files[0].Observations.Select(observation => observation.SourceLine));
        Assert.Equal([1, 3, 3], result.Files[1].Observations.Select(observation => observation.SourceLine));
        Assert.Equal(5, result.Observations.Count);
    }

    [Fact]
    public void Parse_UsesUnderlineColumnWhenSourceAndTypeContainColons()
    {
        const string nodeText = "flag ? left : right";
        string baseline =
            "=== edge.ts ===\n" +
            "const value = flag ? left : right;\n" +
            $">{nodeText} : {{ choose: (x: string) => string; fallback: string; }}\n" +
            $">{new string(' ', nodeText.Length)} : ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^\n";

        TypesBaselineObservation observation = Assert.Single(
            TypesBaselineParser.Parse(
                baseline,
                [new TypeScriptConformanceFile("edge.ts", "const value = flag ? left : right;")])
            .Observations);

        Assert.Equal(nodeText, observation.SourceText);
        Assert.Equal("{ choose: (x: string) => string; fallback: string; }", observation.ExpectedTypeText);
    }

    [Fact]
    public void Parse_AllowsWriterObservationWithoutUnderline()
    {
        const string baseline = "=== error.ts ===\ndeclare const missing: any;\n>missing : error\n";

        TypesBaselineObservation observation = Assert.Single(
            TypesBaselineParser.Parse(
                baseline,
                [new TypeScriptConformanceFile("error.ts", "declare const missing: any;")])
            .Observations);

        Assert.Equal("error", observation.ExpectedTypeText);
        Assert.Null(observation.Underline);
    }

    [Theory]
    [InlineData(">x : number\n", "outside a virtual-file section")]
    [InlineData("=== a.ts ===\n>x : number\n", "before its source line")]
    [InlineData("=== a.ts ===\nconst x = 1;\n>  : ^\n", "without a preceding")]
    [InlineData("=== missing.ts ===\n", "unknown virtual file")]
    public void Parse_RejectsMalformedOrUnanchoredContent(string baseline, string expectedMessage)
    {
        TypesBaselineParseException error = Assert.Throws<TypesBaselineParseException>(() =>
            TypesBaselineParser.Parse(
                baseline,
                [new TypeScriptConformanceFile("a.ts", "const x = 1;")]));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsSectionThatDoesNotEchoAllSource()
    {
        const string baseline = "=== a.ts ===\nconst x = 1;\n>x : 1\n>  : ^\n";

        TypesBaselineParseException error = Assert.Throws<TypesBaselineParseException>(() =>
            TypesBaselineParser.Parse(
                baseline,
                [new TypeScriptConformanceFile("a.ts", "const x = 1;\nconst y = 2;")]));

        Assert.Contains("ended before source line 2", error.Message);
    }

    [Fact]
    public void Parse_IsDeterministicAcrossLineEndingsAndBom()
    {
        string baseline = "\uFEFF" + ReadFixture("multi.types").Replace("\n", "\r\n", StringComparison.Ordinal);
        TypeScriptConformanceFile[] sources =
        [
            new("a.ts", "export const value = 1;"),
            new("b.ts", "import { value } from \"./a\";\n\nconst copy = value;"),
        ];

        TypesBaselineDocument first = TypesBaselineParser.Parse(baseline, sources);
        TypesBaselineDocument second = TypesBaselineParser.Parse(baseline, sources);

        Assert.Equal(
            first.Files.Select(file => file.VirtualFileName).ToArray(),
            second.Files.Select(file => file.VirtualFileName).ToArray());
        Assert.Equal(first.Observations.ToArray(), second.Observations.ToArray());
    }

    private static string ReadFixture(string name)
    {
        string projectDir = TypeScriptConformancePaths.TryFindProjectDir()
            ?? throw new InvalidOperationException("Could not find the TypeScript conformance project directory.");
        return File.ReadAllText(Path.Combine(projectDir, "Fixtures", "TypesBaselines", name));
    }
}
