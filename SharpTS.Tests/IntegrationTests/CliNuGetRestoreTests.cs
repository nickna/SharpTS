using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Hermetic end-to-end tests for sharpts.json "packages" (issue #1197, NuGet restore):
/// every test's nuget.config clears all sources, points at the fixture's local folder
/// source, and redirects the global packages folder into the test directory — no
/// network, no user-cache pollution. TestPkg.Main depends on TestPkg.Base, exercising
/// transitive restore, loading, and copy closure.
/// </summary>
[Collection("ExternalAssembly")]
public class CliNuGetRestoreTests(ExternalAssemblyFixture fixture)
{
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(180);

    private const string MainTs = $$"""
        import { MainInfo } from "dotnet:{{ExternalAssemblyFixture.MainTypeName}}";
        console.log(MainInfo.describe());
        """;

    private TempTestDirectory CreateWorkspace()
    {
        var dir = CliTestHelper.CreateTempDirectory();
        fixture.WriteHermeticNuGetConfig(dir.Path);
        dir.CreateFile("sharpts.json", $$"""
            { "packages": { "{{ExternalAssemblyFixture.MainPackageId}}": "{{ExternalAssemblyFixture.PackageVersion}}" } }
            """);
        dir.CreateFile("main.ts", MainTs);
        return dir;
    }

    [Fact]
    public void Interp_RestoresAndRunsWithTransitiveDependency()
    {
        using var dir = CreateWorkspace();

        var result = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);

        Assert.Equal(0, result.ExitCode);
        // "on base-1.0" comes from TestPkg.Base — the transitive dependency loaded.
        Assert.Equal("main-1.0 on base-1.0\n", result.StandardOutput);
        Assert.True(File.Exists(dir.GetPath(".sharpts/restore.hash")));
        Assert.True(File.Exists(dir.GetPath(".sharpts/obj/project.assets.json")));
    }

    [Fact]
    public void SecondRun_SkipsRestoreViaHashGate()
    {
        using var dir = CreateWorkspace();
        var first = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);
        Assert.Equal(0, first.ExitCode);

        // A gated (skipped) restore never touches .sharpts/ — only a re-invoked restore
        // regenerates restore.csproj. Delete it: if the second run recreates it, the
        // hash gate failed.
        File.Delete(dir.GetPath(".sharpts/restore.csproj"));

        var second = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal("main-1.0 on base-1.0\n", second.StandardOutput);
        Assert.False(File.Exists(dir.GetPath(".sharpts/restore.csproj")),
            "restore ran again despite an unchanged package set");
    }

    [Fact]
    public void ChangedPackageSet_InvalidatesHashGate()
    {
        using var dir = CreateWorkspace();
        var first = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);
        Assert.Equal(0, first.ExitCode);

        // Adding a package changes the hash — restore must re-run and now include Base
        // as a direct reference too.
        dir.CreateFile("sharpts.json", $$"""
            {
              "packages": {
                "{{ExternalAssemblyFixture.MainPackageId}}": "{{ExternalAssemblyFixture.PackageVersion}}",
                "{{ExternalAssemblyFixture.BasePackageId}}": "{{ExternalAssemblyFixture.PackageVersion}}"
              }
            }
            """);

        var second = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal("main-1.0 on base-1.0\n", second.StandardOutput);
    }

    [Fact]
    public void Compiled_CopiesPackageClosureAndRunsStandalone()
    {
        using var dir = CreateWorkspace();
        Directory.CreateDirectory(dir.GetPath("out"));

        var compile = CliTestHelper.RunCli("--compile main.ts -o out/app.dll --verify", dir.Path, RestoreTimeout);

        Assert.Equal(0, compile.ExitCode);
        Assert.Contains("IL verification passed", compile.StandardOutput);
        // The used package AND its transitive dependency were co-located.
        Assert.True(File.Exists(dir.GetPath($"out/{ExternalAssemblyFixture.MainPackageId}.dll")));
        Assert.True(File.Exists(dir.GetPath($"out/{ExternalAssemblyFixture.BasePackageId}.dll")));
        Assert.False(File.Exists(dir.GetPath("out/SharpTS.dll")));

        var run = CliManifestTests.RunProgram(dir.GetPath("out/app.dll"));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("main-1.0 on base-1.0\n", run.StandardOutput);
    }

    [Fact]
    public void RestoreFailure_ExitsNonzeroNamingManifest()
    {
        using var dir = CreateWorkspace();
        dir.CreateFile("sharpts.json", """{ "packages": { "No.Such.Package": "9.9.9" } }""");

        var result = CliTestHelper.RunCli("main.ts", dir.Path, RestoreTimeout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("NuGet restore failed", result.StandardOutput);
        Assert.Contains("sharpts.json", result.StandardOutput);
    }
}
