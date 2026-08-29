using Xunit;

namespace SharpTS.TypeScriptConformance;

public class TypeScriptBaselineResolverTests
{
    [Theory]
    [InlineData(TypeScriptBaselineKind.Errors, "sample.errors.txt")]
    [InlineData(TypeScriptBaselineKind.Types, "sample.types")]
    public void Resolve_PrefersPlainBaseline(TypeScriptBaselineKind kind, string filename)
    {
        using TempTypeScriptRoot root = new();
        string expected = root.WriteBaseline(filename);
        root.WriteBaseline(kind == TypeScriptBaselineKind.Errors
            ? "sample(target=es5).errors.txt"
            : "sample(target=es5).types");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.ts",
            Metadata(""),
            kind);

        Assert.Equal(TypeScriptBaselineResolutionStatus.Found, result.Status);
        Assert.Equal(expected, result.Path);
    }

    [Theory]
    [InlineData(TypeScriptBaselineKind.Errors, ".errors.txt")]
    [InlineData(TypeScriptBaselineKind.Types, ".types")]
    public void Resolve_SelectsSameMostSpecificConfiguredVariant(
        TypeScriptBaselineKind kind,
        string suffix)
    {
        using TempTypeScriptRoot root = new();
        root.WriteBaseline("sample(target=es5)" + suffix);
        root.WriteBaseline("sample(module=es2020)" + suffix);
        string expected = root.WriteBaseline("sample(module=es2020,target=es5)" + suffix);
        TypeScriptConformanceMetadata metadata = Metadata(
            "// @target: esnext, es5\n// @module: esnext, commonjs, es2020\n");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.tsx",
            metadata,
            kind);

        Assert.Equal(TypeScriptBaselineResolutionStatus.Found, result.Status);
        Assert.Equal(expected, result.Path);
    }

    [Fact]
    public void Resolve_NormalizesWildcardModuleAndEs6Target()
    {
        using TempTypeScriptRoot root = new();
        string expected = root.WriteBaseline("sample(module=esnext,target=es2015).types");
        TypeScriptConformanceMetadata metadata = Metadata(
            "// @target: es6\n// @module: *\n");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.ts",
            metadata,
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.Found, result.Status);
        Assert.Equal(expected, result.Path);
    }

    [Fact]
    public void Resolve_UnsupportedAxisProducesNoBaseline()
    {
        using TempTypeScriptRoot root = new();
        root.WriteBaseline("sample(module=esnext,newLine=lf).types");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.ts",
            Metadata(""),
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.NoBaseline, result.Status);
        Assert.Null(result.Path);
        Assert.EndsWith("sample.types", result.ExpectedPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_MissingDirectoryProducesNoBaseline()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), $"missing-sharpts-ts-{Guid.NewGuid():N}");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            missingRoot,
            "sample.ts",
            Metadata(""),
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.NoBaseline, result.Status);
        Assert.Empty(result.Candidates);
        Assert.EndsWith("sample.types", result.ExpectedPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_EquallySpecificMatchesAreAmbiguousAndSorted()
    {
        using TempTypeScriptRoot root = new();
        string target = root.WriteBaseline("sample(target=es5).types");
        string module = root.WriteBaseline("sample(module=esnext).types");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.ts",
            Metadata(""),
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Path);
        Assert.Equal(new[] { module, target }.OrderBy(path => path, StringComparer.Ordinal), result.Candidates);
    }

    [Fact]
    public void Resolve_MalformedOrDuplicateAxesAreIgnored()
    {
        using TempTypeScriptRoot root = new();
        root.WriteBaseline("sample(target).types");
        root.WriteBaseline("sample(target=es5,target=es5).types");

        TypeScriptBaselineResolution result = TypeScriptBaselineResolver.Resolve(
            root.Path,
            "sample.ts",
            Metadata(""),
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.NoBaseline, result.Status);
    }

    private static TypeScriptConformanceMetadata Metadata(string source) =>
        TypeScriptConformanceMetadataParser.Parse("sample.ts", source);

    private sealed class TempTypeScriptRoot : IDisposable
    {
        public TempTypeScriptRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sharpts-baseline-resolver-{Guid.NewGuid():N}");
            Baselines = System.IO.Path.Combine(Path, "tests", "baselines", "reference");
            Directory.CreateDirectory(Baselines);
        }

        public string Path { get; }
        private string Baselines { get; }

        public string WriteBaseline(string filename)
        {
            string path = System.IO.Path.Combine(Baselines, filename);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
