using SharpTS.References;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.ReferencesTests;

/// <summary>
/// Unit tests for sharpts.json discovery and parsing (issue #1197).
/// </summary>
public class SharpTsManifestLoaderTests
{
    [Fact]
    public void FindAndLoad_FindsManifestInStartDirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", """{ "references": ["./libs/a.dll"] }""");

        var manifest = SharpTsManifestLoader.FindAndLoad(dir.Path);

        Assert.NotNull(manifest);
        Assert.Equal(Path.GetFullPath(dir.GetPath("sharpts.json")), manifest.ManifestPath);
        Assert.Equal(Path.GetFullPath(dir.Path), Path.GetFullPath(manifest.ManifestDirectory));
        Assert.Equal(["./libs/a.dll"], manifest.References!);
    }

    [Fact]
    public void FindAndLoad_WalksUpFromNestedDirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", """{ "packages": { "Some.Pkg": "1.2.3" } }""");
        Directory.CreateDirectory(dir.GetPath("src/nested"));

        var manifest = SharpTsManifestLoader.FindAndLoad(dir.GetPath("src/nested"));

        Assert.NotNull(manifest);
        Assert.Equal("1.2.3", manifest.Packages!["Some.Pkg"]);
    }

    [Fact]
    public void FindAndLoad_ReturnsNullWhenNoManifest()
    {
        // The walk from a fresh temp dir ascends only as far as the temp-root ceiling,
        // so an ambient manifest elsewhere on the machine can't leak in.
        using var dir = CliTestHelper.CreateTempDirectory();

        Assert.Null(SharpTsManifestLoader.FindAndLoad(dir.Path));
    }

    [Fact]
    public void Load_AcceptsCommentsTrailingCommasAndAnyKeyCase()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("sharpts.json", """
            {
              // assemblies for dotnet: imports
              "References": [
                "./libs/a.dll",
              ],
              "PACKAGES": { "Newtonsoft.Json": "13.0.3", },
            }
            """);

        var manifest = SharpTsManifestLoader.Load(path);

        Assert.Equal(["./libs/a.dll"], manifest.References!);
        Assert.Equal("13.0.3", manifest.Packages!["Newtonsoft.Json"]);
    }

    [Fact]
    public void Load_MalformedJsonIsHardErrorNamingTheFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var path = dir.CreateFile("sharpts.json", "{ not json ");

        var ex = Assert.ThrowsAny<Exception>(() => SharpTsManifestLoader.Load(path));
        Assert.Contains("sharpts.json", ex.Message);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Load_MissingFileThrows()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        Assert.Throws<FileNotFoundException>(() => SharpTsManifestLoader.Load(dir.GetPath("sharpts.json")));
    }
}
