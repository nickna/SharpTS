using Xunit;

namespace SharpTS.TypeScriptConformance;

public class TypesBaselineIntegrationTests
{
    [Fact]
    public void CurrentDiagnosticPasses_WithCompatibleTypesBaseline_ParseSuccessfully()
    {
        string? root = TypeScriptConformancePaths.TryFindRoot();
        string? projectDir = TypeScriptConformancePaths.TryFindProjectDir();
        if (root is null || projectDir is null) return;

        string baselineIndex = Path.Combine(projectDir, "baselines", "interpreted.txt");
        int parsedCount = 0;
        foreach (string entry in File.ReadLines(baselineIndex)
                     .Where(line => line.Length > 0 && !line.StartsWith('#')))
        {
            string[] parts = entry.Split(' ', 2);
            if (parts.Length != 2 || parts[1] != "Pass")
                continue;

            const string prefix = "tests/cases/conformance/";
            Assert.StartsWith(prefix, parts[0], StringComparison.Ordinal);
            string relativePath = parts[0][prefix.Length..];
            string testPath = Path.Combine(
                TypeScriptConformancePaths.ConformanceDir(root),
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            TypeScriptConformanceMetadata metadata = TypeScriptConformanceMetadataParser.Parse(
                testPath,
                File.ReadAllText(testPath));
            TypeScriptBaselineResolution resolution = TypeScriptBaselineResolver.Resolve(
                root,
                testPath,
                metadata,
                TypeScriptBaselineKind.Types);

            if (resolution.Status == TypeScriptBaselineResolutionStatus.NoBaseline)
                continue;
            Assert.True(
                resolution.Status == TypeScriptBaselineResolutionStatus.Found,
                $"{relativePath}: {resolution.Status}: {string.Join(", ", resolution.Candidates)}");
            try
            {
                TypesBaselineParser.Parse(File.ReadAllText(resolution.Path!), metadata.Files);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to parse the resolved types baseline for {relativePath}: {exception.Message}",
                    exception);
            }
            parsedCount++;
        }

        // 531 current diagnostic passes have a basename-matching .types family. Thirteen expose
        // only a configuration variant outside the runner's selected target/module world, so the
        // strict resolver correctly leaves those as NoBaseline rather than guessing.
        Assert.Equal(518, parsedCount);
    }

    [Theory]
    [InlineData("es2019/globalThisTypeIndexAccess.ts")]
    [InlineData("es2021/logicalAssignment/logicalAssignment9.ts")]
    [InlineData("types/conditional/inferTypes1.ts")]
    [InlineData("types/conditional/inferTypesWithExtends2.ts")]
    [InlineData("types/keyof/keyofIntersection.ts")]
    [InlineData("types/literal/literalTypes1.ts")]
    [InlineData("types/typeRelationships/assignmentCompatibility/intersectionIncludingPropFromGlobalAugmentation.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/stringLiteralTypeIsSubtypeOfString.ts")]
    [InlineData("types/typeRelationships/subtypesAndSuperTypes/subtypesOfUnion.ts")]
    [InlineData("es2017/es2017DateAPIs.ts")]
    [InlineData("es2020/constructBigint.ts")]
    public void NamedPilotBaseline_ResolvesAndParses(string relativePath)
    {
        string? root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;

        (TypeScriptBaselineResolution resolution, TypesBaselineDocument document) =
            ResolveAndParse(root, relativePath);

        Assert.Equal(TypeScriptBaselineResolutionStatus.Found, resolution.Status);
        Assert.EndsWith(".types", resolution.Path!, StringComparison.Ordinal);
        Assert.NotEmpty(document.Files);
        Assert.NotEmpty(document.Observations);
    }

    [Fact]
    public void PinnedMultiFileBaseline_ResolvesAndParsesEverySection()
    {
        string? root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;

        (_, TypesBaselineDocument document) = ResolveAndParse(
            root,
            "es2019/globalThisVarDeclaration.ts");

        Assert.True(document.Files.Count > 1);
        Assert.All(document.Files, file => Assert.NotEmpty(file.Observations));
    }

    [Theory]
    [InlineData("es2020/modules/exportAsNamespace_exportAssignment.ts")]
    [InlineData("es2020/modules/exportAsNamespace_missingEmitHelpers.ts")]
    [InlineData("es2020/modules/exportAsNamespace_nonExistent.ts")]
    public void CurrentDiagnosticPassWithoutTypesFamily_IsNoBaseline(string relativePath)
    {
        string? root = TypeScriptConformancePaths.TryFindRoot();
        if (root is null) return;

        string testPath = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        TypeScriptConformanceMetadata metadata = TypeScriptConformanceMetadataParser.Parse(
            testPath,
            File.ReadAllText(testPath));

        TypeScriptBaselineResolution resolution = TypeScriptBaselineResolver.Resolve(
            root,
            testPath,
            metadata,
            TypeScriptBaselineKind.Types);

        Assert.Equal(TypeScriptBaselineResolutionStatus.NoBaseline, resolution.Status);
    }

    private static (TypeScriptBaselineResolution Resolution, TypesBaselineDocument Document)
        ResolveAndParse(string root, string relativePath)
    {
        string testPath = Path.Combine(
            TypeScriptConformancePaths.ConformanceDir(root),
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        TypeScriptConformanceMetadata metadata = TypeScriptConformanceMetadataParser.Parse(
            testPath,
            File.ReadAllText(testPath));
        TypeScriptBaselineResolution resolution = TypeScriptBaselineResolver.Resolve(
            root,
            testPath,
            metadata,
            TypeScriptBaselineKind.Types);
        Assert.Equal(TypeScriptBaselineResolutionStatus.Found, resolution.Status);

        TypesBaselineDocument document = TypesBaselineParser.Parse(
            File.ReadAllText(resolution.Path!),
            metadata.Files);
        return (resolution, document);
    }
}
