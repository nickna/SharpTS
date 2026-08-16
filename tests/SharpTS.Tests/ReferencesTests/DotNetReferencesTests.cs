using SharpTS.References;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.ReferencesTests;

/// <summary>
/// Unit tests for the shared reference resolve/load entry point (issue #1197).
/// Resolve is pure path work (dummy files suffice); Load tests use the built
/// fixture DLL because they load into the test AppDomain.
/// </summary>
[Collection("ExternalAssembly")]
public class DotNetReferencesTests(ExternalAssemblyFixture fixture)
{
    [Fact]
    public void Resolve_NoManifestNoFlags_IsEmpty()
    {
        using var dir = CliTestHelper.CreateTempDirectory();

        var set = DotNetReferences.Resolve(dir.Path, []);

        Assert.True(set.IsEmpty);
        Assert.Null(set.ManifestPath);
    }

    [Fact]
    public void Resolve_CliBeforeManifest_DedupedByFullPath()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var libA = dir.CreateFile("libs/a.dll", "not really a dll");
        var libB = dir.CreateFile("libs/b.dll", "not really a dll");
        dir.CreateFile("sharpts.json", """{ "references": ["./libs/a.dll", "./libs/b.dll"] }""");

        var set = DotNetReferences.Resolve(dir.Path, [libA]);

        // CLI ref first; the manifest's duplicate of a.dll folds into it.
        Assert.Equal(2, set.References.Count);
        Assert.Equal(Path.GetFullPath(libA), set.References[0].Path);
        Assert.Equal(ReferenceOrigin.Cli, set.References[0].Origin);
        Assert.Equal(Path.GetFullPath(libB), set.References[1].Path);
        Assert.Equal(ReferenceOrigin.Manifest, set.References[1].Origin);
    }

    [Fact]
    public void Resolve_ManifestRelativePathsResolveAgainstManifestDirectory()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("libs/a.dll", "x");
        dir.CreateFile("sharpts.json", """{ "references": ["./libs/a.dll"] }""");
        Directory.CreateDirectory(dir.GetPath("src"));

        // Start the walk from a nested dir: the entry resolves against the manifest's
        // directory, not the start directory.
        var set = DotNetReferences.Resolve(dir.GetPath("src"), []);

        Assert.Equal(Path.GetFullPath(dir.GetPath("libs/a.dll")), Assert.Single(set.References).Path);
    }

    [Fact]
    public void Resolve_MissingManifestReference_ErrorNamesManifestEntryAndResolvedPath()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", """{ "references": ["./nope/missing.dll"] }""");

        var ex = Assert.ThrowsAny<Exception>(() => DotNetReferences.Resolve(dir.Path, []));
        Assert.Contains("sharpts.json", ex.Message);
        Assert.Contains("./nope/missing.dll", ex.Message);
        Assert.Contains(Path.GetFullPath(dir.GetPath("nope/missing.dll")), ex.Message);
    }

    [Fact]
    public void Resolve_MissingCliReference_ErrorMentionsFlag()
    {
        using var dir = CliTestHelper.CreateTempDirectory();

        var ex = Assert.ThrowsAny<Exception>(() =>
            DotNetReferences.Resolve(dir.Path, [dir.GetPath("missing.dll")]));
        Assert.Contains("-r/--reference", ex.Message);
    }

    [Fact]
    public void Load_LoadsAssemblyAndIsIdempotent()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var dll = dir.GetPath("SharpTsExternalFixture.dll");
        File.Copy(fixture.GreeterDllPath, dll);
        dir.CreateFile("sharpts.json", """{ "references": ["./SharpTsExternalFixture.dll"] }""");

        var first = DotNetReferences.Load(dir.Path, []);
        var second = DotNetReferences.Load(dir.Path, []);

        Assert.Single(first.References);
        Assert.Single(second.References);
        // The type is now resolvable through the registry's AppDomain scan.
        Assert.NotNull(SharpTS.Runtime.DotNet.DotNetTypeRegistry.Resolve(ExternalAssemblyFixture.GreeterTypeName));
    }

    [Fact]
    public void Load_CorruptDll_AggregatedErrorNamesFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        var bad = dir.CreateFile("bad.dll", "this is not a PE file");
        dir.CreateFile("sharpts.json", """{ "references": ["./bad.dll"] }""");

        var ex = Assert.ThrowsAny<Exception>(() => DotNetReferences.Load(dir.Path, []));
        Assert.Contains("could not be loaded", ex.Message);
        Assert.Contains(Path.GetFullPath(bad), ex.Message);
    }
}
