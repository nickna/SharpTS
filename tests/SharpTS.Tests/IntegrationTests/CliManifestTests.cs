using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end CLI tests for the sharpts.json reference manifest and the global -r flag
/// (issue #1197): interpreter + compiled resolution of dotnet: imports from a
/// third-party DLL, co-location of referenced DLLs next to compiled output, standalone
/// execution, --gen-decl discovery, and the error surfaces.
/// </summary>
[Collection("ExternalAssembly")]
public class CliManifestTests(ExternalAssemblyFixture fixture)
{
    private const string MainTs = $$"""
        import { Greeter } from "dotnet:{{ExternalAssemblyFixture.GreeterTypeName}}";
        console.log(Greeter.hello("World"));
        const g = new Greeter();
        console.log(g.add(2, 40));
        """;

    private TempTestDirectory CreateWorkspace(bool withManifest = true)
    {
        var dir = CliTestHelper.CreateTempDirectory();
        Directory.CreateDirectory(dir.GetPath("libs"));
        File.Copy(fixture.GreeterDllPath, dir.GetPath($"libs/{ExternalAssemblyFixture.GreeterAssemblyName}.dll"));
        if (withManifest)
            dir.CreateFile("sharpts.json",
                $$"""{ "references": ["./libs/{{ExternalAssemblyFixture.GreeterAssemblyName}}.dll"] }""");
        dir.CreateFile("main.ts", MainTs);
        return dir;
    }

    [Fact]
    public void Interp_ResolvesDotnetImportFromManifestDll()
    {
        using var dir = CreateWorkspace();

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello, World!\n42\n", result.StandardOutput);
    }

    [Fact]
    public void Interp_ManifestDiscoveredByUpwardWalkFromEntryScript()
    {
        using var dir = CreateWorkspace();
        Directory.CreateDirectory(dir.GetPath("src/app"));
        dir.CreateFile("src/app/entry.ts", MainTs);

        var result = CliTestHelper.RunCli("src/app/entry.ts", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello, World!\n42\n", result.StandardOutput);
    }

    [Fact]
    public void Interp_ReferenceFlagWorksWithoutManifest()
    {
        using var dir = CreateWorkspace(withManifest: false);

        var result = CliTestHelper.RunCli(
            $"main.ts -r libs/{ExternalAssemblyFixture.GreeterAssemblyName}.dll", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello, World!\n42\n", result.StandardOutput);
    }

    [Fact]
    public void Compiled_CopiesReferencedDllAndRunsStandalone()
    {
        using var dir = CreateWorkspace();
        Directory.CreateDirectory(dir.GetPath("out"));

        var compile = CliTestHelper.RunCli("--compile main.ts -o out/app.dll --verify", dir.Path,
            TimeSpan.FromSeconds(120));

        Assert.Equal(0, compile.ExitCode);
        Assert.Contains("IL verification passed", compile.StandardOutput);
        // The referenced DLL was co-located next to the output (hard metadata reference).
        Assert.True(File.Exists(dir.GetPath($"out/{ExternalAssemblyFixture.GreeterAssemblyName}.dll")));
        // No SharpTS.dll: dotnet: imports compile to direct IL, fully standalone.
        Assert.False(File.Exists(dir.GetPath("out/SharpTS.dll")));

        var run = RunProgram(dir.GetPath("out/app.dll"));
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Hello, World!\n42\n", run.StandardOutput);
    }

    [Fact]
    public void Compiled_AssemblyRefTableHasFixtureButNotSharpTS()
    {
        using var dir = CreateWorkspace();
        Directory.CreateDirectory(dir.GetPath("out"));
        var compile = CliTestHelper.RunCli("--compile main.ts -o out/app.dll", dir.Path,
            TimeSpan.FromSeconds(120));
        Assert.Equal(0, compile.ExitCode);

        using var stream = File.OpenRead(dir.GetPath("out/app.dll"));
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var refNames = metadata.AssemblyReferences
            .Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name))
            .ToList();

        Assert.Contains(ExternalAssemblyFixture.GreeterAssemblyName, refNames);
        Assert.DoesNotContain("SharpTS", refNames);
    }

    [Fact]
    public void GenDecl_ReferenceFlagEnablesTypeDiscovery()
    {
        using var dir = CreateWorkspace(withManifest: false);

        var result = CliTestHelper.RunCli(
            $"--gen-decl {ExternalAssemblyFixture.GreeterTypeName} -r libs/{ExternalAssemblyFixture.GreeterAssemblyName}.dll",
            dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"import {{ Greeter }} from \"dotnet:{ExternalAssemblyFixture.GreeterTypeName}\";",
            result.StandardOutput);
    }

    [Fact]
    public void GenDecl_ManifestDiscoveredFromWorkingDirectory()
    {
        using var dir = CreateWorkspace();

        var result = CliTestHelper.RunCli(
            $"--gen-decl {ExternalAssemblyFixture.GreeterTypeName}", dir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("static hello(name: string): string", result.StandardOutput);
    }

    [Fact]
    public void MissingManifestReference_ExitsNonzeroNamingEntry()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", """{ "references": ["./nope/missing.dll"] }""");
        dir.CreateFile("main.ts", "console.log(1);");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sharpts.json", result.StandardOutput);
        Assert.Contains("./nope/missing.dll", result.StandardOutput);
    }

    [Fact]
    public void MalformedManifest_ExitsNonzeroNamingFile()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("sharpts.json", "{ not json ");
        dir.CreateFile("main.ts", "console.log(1);");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("sharpts.json", result.StandardOutput);
        Assert.Contains("not valid JSON", result.StandardOutput);
    }

    [Fact]
    public void PathEmbeddedSpecifier_RejectedWithManifestHint()
    {
        // The shared resolution algorithm rejects it for every consumer (interp, compiler, LSP).
        var ex = Assert.ThrowsAny<Exception>(() =>
            DotNetImports.ResolveExportType("./libs/MyLib.dll#Greeter", "Greeter"));

        Assert.StartsWith("Module Error:", ex.Message);
        Assert.Contains("sharpts.json", ex.Message);
        Assert.Contains("-r", ex.Message);
    }

    [Theory]
    [InlineData("libs/MyLib.dll")]
    [InlineData(@"libs\MyLib.dll")]
    [InlineData("MyLib.exe")]
    [InlineData("MyLib.dll#Widget")]
    public void PathLikeSpecifiers_AllRejected(string specifier)
    {
        var ex = Assert.ThrowsAny<Exception>(() => DotNetImports.ResolveExportType(specifier, "Widget"));
        Assert.Contains("not assembly paths", ex.Message);
    }

    [Fact]
    public void PathEmbeddedSpecifier_RejectedEndToEnd()
    {
        using var dir = CliTestHelper.CreateTempDirectory();
        dir.CreateFile("main.ts", """import { Greeter } from "dotnet:./libs/MyLib.dll#Greeter";""");

        var result = CliTestHelper.RunCli("main.ts", dir.Path);

        Assert.Contains("Module Error", result.StandardOutput);
        Assert.Contains("sharpts.json", result.StandardOutput);
    }

    internal static CliTestHelper.CliResult RunProgram(string dllPath, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(dllPath)!
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Compiled program timed out: {dllPath}");
        }
        return new CliTestHelper.CliResult(
            process.ExitCode,
            CliTestHelper.NormalizeOutput(stdout),
            CliTestHelper.NormalizeOutput(stderr));
    }
}
