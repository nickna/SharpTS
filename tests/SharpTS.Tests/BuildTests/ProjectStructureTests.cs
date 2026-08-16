using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.BuildTests;

/// <summary>Guards the role-based repository layout introduced by issue #1373.</summary>
public class ProjectStructureTests
{
    [Fact]
    public void CoreProject_IsBoundedToItsOwnSourceDirectory()
    {
        string repoRoot = RepoPaths.FindRepoRoot();
        string coreDirectory = Path.Combine(repoRoot, "src", "SharpTS");
        string coreProject = Path.Combine(coreDirectory, "SharpTS.csproj");

        Assert.True(File.Exists(coreProject), $"Core project was not found at {coreProject}.");
        Assert.Single(Directory.EnumerateFiles(coreDirectory, "*.csproj", SearchOption.AllDirectories));

        string projectText = File.ReadAllText(coreProject);
        Assert.DoesNotContain("GuardAgainstForeignProjectSources", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("<Compile Remove=\"SharpTS.", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void Projects_HaveDocumentedRoleBasedHomes()
    {
        string repoRoot = RepoPaths.FindRepoRoot();
        string[] expectedProjects =
        [
            "src/SharpTS/SharpTS.csproj",
            "src/SharpTS.Hosting/SharpTS.Hosting.csproj",
            "src/SharpTS.Gui/SharpTS.Gui.csproj",
            "src/SharpTS.Sdk/SharpTS.Sdk.csproj",
            "tests/SharpTS.Tests/SharpTS.Tests.csproj",
            "tests/conformance/SharpTS.Test262/SharpTS.Test262.csproj",
            "tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj",
            "tests/fixtures/SharpTS.Gui.Sdk.Consumer/SharpTS.Gui.Sdk.Consumer.csproj",
            "benchmarks/micro/SharpTS.Microbenchmarks/SharpTS.Microbenchmarks.csproj",
            "samples/Interop/SharpTS.Example.Interop.csproj",
        ];

        foreach (string relativePath in expectedProjects)
            Assert.True(File.Exists(Path.Combine(repoRoot, relativePath)), $"Missing {relativePath}.");

        Assert.Empty(Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void Solution_UsesLogicalFolders()
    {
        string solution = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "SharpTS.sln"));
        foreach (string folder in new[] { "Core", "Hosting", "GUI", "SDK", "Tests", "Benchmarks", "Samples" })
            Assert.Contains($"= \"{folder}\", \"{folder}\"", solution, StringComparison.Ordinal);
    }
}
