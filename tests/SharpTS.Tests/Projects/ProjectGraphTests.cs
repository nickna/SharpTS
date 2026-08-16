using SharpTS.Projects;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.Projects;

public class ProjectGraphTests
{
    [Fact]
    public void ReferencesAreOrderedBeforeConsumers()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string dependency = dir.CreateFile(
            "dependency/tsconfig.json",
            """{ "files": [], "compilerOptions": { "composite": true } }""");
        string consumer = dir.CreateFile(
            "tsconfig.json",
            """{ "files": [], "references": [{ "path": "dependency" }] }""");

        var graph = ProjectGraph.Load([consumer]);

        Assert.Equal(
            [Path.GetFullPath(dependency), Path.GetFullPath(consumer)],
            graph.Projects.Select(project => project.ConfigPath));
    }

    [Fact]
    public void ReferencedProjectsMustBeComposite()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("dependency/tsconfig.json", """{ "files": [] }""");
        string consumer = dir.CreateFile(
            "tsconfig.json",
            """{ "files": [], "references": [{ "path": "dependency" }] }""");

        var error = Assert.Throws<Exception>(() => ProjectGraph.Load([consumer]));

        Assert.Contains("composite", error.Message);
        Assert.Contains("dependency", error.Message);
    }

    [Fact]
    public void CircularReferencesReportTheCycle()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        string first = dir.CreateFile("a/tsconfig.json", """
            {
              "files": [],
              "compilerOptions": { "composite": true },
              "references": [{ "path": "../b" }]
            }
            """);
        dir.CreateFile("b/tsconfig.json", """
            {
              "files": [],
              "compilerOptions": { "composite": true },
              "references": [{ "path": "../a" }]
            }
            """);

        var error = Assert.Throws<Exception>(() => ProjectGraph.Load([first]));

        Assert.Contains("circular project reference", error.Message);
        Assert.Contains("tsconfig.json", error.Message);
    }
}
