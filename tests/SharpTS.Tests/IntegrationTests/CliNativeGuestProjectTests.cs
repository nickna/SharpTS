using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using SharpTS.Compilation;
using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliNativeGuestProjectTests
{
    [Fact]
    public void ExportedGuestFacadeBuildsAndPreservesArgumentsAndExitCode()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(process.argv.slice(2).join('|')); process.exitCode = 7;");
        var export = CliTestHelper.RunCli("--no-tsconfig --compile main.ts --emit-native-project \"native project\" --runtime win-x64 --verify --debug", directory.Path);
        Assert.True(export.ExitCode == 0, export.StandardOutput + export.StandardError);
        string native = directory.GetPath("native project");
        Assert.True(File.Exists(Path.Combine(native, "guest", "Guest.pdb")));
        Assert.False(File.Exists(Path.Combine(native, "guest", "SharpTS.dll")));
        Assert.False(File.Exists(Path.Combine(native, "native-project.pending")));
        var project = XDocument.Load(Path.Combine(native, "NativeApp.csproj"));
        Assert.Equal("true", project.Descendants("PublishAot").Single().Value);
        Assert.Equal("true", project.Descendants("IlcTreatWarningsAsErrors").Single().Value);
        Assert.Equal("true", project.Descendants("ILLinkTreatWarningsAsErrors").Single().Value);
        Assert.Empty(project.Descendants("NoWarn"));
        Assert.Empty(project.Descendants("TrimmerRootAssembly"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(native, "native-project.json")));
        Assert.Equal("experimental-native-guest-v1", manifest.RootElement.GetProperty("profile").GetString());
        Assert.Equal("None", manifest.RootElement.GetProperty("runtimeRequirements").GetString());

        // This portable managed build verifies the facade contract on every CI OS.
        // Actual NativeAOT publish/execute is measured separately with its platform toolchain.
        var build = RunDotnet(native, "build", "NativeApp.csproj", "-c", "Release", "-m:2", "-nr:false",
            "-p:PublishAot=false", "-p:SelfContained=false", "-p:RuntimeIdentifier=");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var run = RunDotnet(native, Path.Combine(native, "bin", "Release", "net10.0", "NativeApp.dll"), "first", "two words");
        Assert.Equal("first|two words\n", run.StandardOutput.Replace("\r\n", "\n"));
        Assert.Equal(7, run.ExitCode);
    }

    [Theory]
    [InlineData("eval(process.argv[2]);", "eval")]
    [InlineData("new Proxy({}, {});", "Proxy")]
    [InlineData("new Intl.NumberFormat();", "Intl")]
    [InlineData("import * as vm from 'vm'; new vm.Script('1');", "vm")]
    public void RuntimeDependentFeaturesRefuseBeforeWritingProject(string source, string feature)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli("--no-tsconfig --compile main.ts --emit-native-project native --runtime win-x64 --msbuild-errors", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SHARPTS401", result.StandardOutput + result.StandardError);
        Assert.Contains(feature, result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(directory.GetPath("native")));
    }

    [Fact]
    public void StaticallyLoweredEvalDoesNotRequireTheRuntimeBridge()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(eval('40 + 2')); ");
        var result = CliTestHelper.RunCli("--no-tsconfig --compile main.ts --emit-native-project native --runtime win-x64 --verify", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        using var manifest = JsonDocument.Parse(File.ReadAllText(directory.GetPath("native/native-project.json")));
        Assert.Equal("None", manifest.RootElement.GetProperty("runtimeRequirements").GetString());
    }

    [Fact]
    public void ExternalReferencesAndOccupiedDestinationsRemainUntouched()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);");
        string reference = typeof(ILCompiler).Assembly.Location;
        var external = CliTestHelper.RunCli($"--no-tsconfig --compile main.ts --emit-native-project native --runtime win-x64 -r \"{reference}\" --msbuild-errors", directory.Path);
        Assert.Contains("SHARPTS402", external.StandardOutput + external.StandardError);
        Assert.False(Directory.Exists(directory.GetPath("native")));
        directory.CreateFile("occupied/keep.txt", "user content");
        var occupied = CliTestHelper.RunCli("--no-tsconfig --compile main.ts --emit-native-project occupied --runtime win-x64 --msbuild-errors", directory.Path);
        Assert.Contains("SHARPTS403", occupied.StandardOutput + occupied.StandardError);
        Assert.Equal("user content", File.ReadAllText(directory.GetPath("occupied/keep.txt")));
        Assert.Single(Directory.GetFileSystemEntries(directory.GetPath("occupied")));
    }

    private static TestProcessResult RunDotnet(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return TestProcess.Run(start, TimeSpan.FromSeconds(90), "Native guest facade SDK build/execution");
    }
}
