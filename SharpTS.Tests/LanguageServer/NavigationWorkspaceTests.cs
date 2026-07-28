using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.LanguageServer;

public class NavigationWorkspaceTests
{
    [Fact]
    public void InitializationCapturesEveryWorkspaceFolder()
    {
        using var first = CliTestHelper.CreateTempDirectory();
        using var second = CliTestHelper.CreateTempDirectory();
        var context = new NavigationWorkspaceContext();

        context.Initialize(new InitializeParams
        {
            WorkspaceFolders = new Container<WorkspaceFolder>(
                new WorkspaceFolder
                {
                    Name = "second",
                    Uri = DocumentUri.FromFileSystemPath(second.Path),
                },
                new WorkspaceFolder
                {
                    Name = "first",
                    Uri = DocumentUri.FromFileSystemPath(first.Path),
                }),
        });

        string[] expected =
        [
            Path.GetFullPath(first.Path),
            Path.GetFullPath(second.Path),
        ];
        Assert.True(
            expected
                .Order(StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    context.SnapshotRoots(),
                    StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void CatalogDoesNotTreatDependencyConfigsAsWorkspaceProjects()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string configPath = directory.CreateFile(
            "tsconfig.json",
            """{ "files": [] }""");
        directory.CreateFile("node_modules/pkg/tsconfig.json", "{ not json");

        NavigationProjectCatalog catalog =
            NavigationProjectCatalog.Discover([directory.Path]);

        Assert.True(catalog.IsComplete);
        Assert.Equal(
            Path.GetFullPath(configPath),
            Assert.Single(catalog.ConfigPaths));
        Assert.Single(catalog.Projects);
    }

    [Fact]
    public void ProjectReferenceOutsideTheWorkspaceMakesTheBoundaryIncomplete()
    {
        using var workspace = CliTestHelper.CreateTempDirectory();
        using var external = CliTestHelper.CreateTempDirectory();
        external.CreateFile(
            "tsconfig.json",
            """
            {
              "files": [],
              "compilerOptions": { "composite": true }
            }
            """);
        string relative = Path.GetRelativePath(workspace.Path, external.Path)
            .Replace('\\', '/');
        workspace.CreateFile(
            "tsconfig.json",
            $$"""
            {
              "files": [],
              "references": [{ "path": "{{relative}}" }]
            }
            """);

        NavigationProjectCatalog catalog =
            NavigationProjectCatalog.Discover([workspace.Path]);

        Assert.False(catalog.IsComplete);
        Assert.Single(catalog.ConfigPaths);
        Assert.Single(catalog.Projects);
    }
}
