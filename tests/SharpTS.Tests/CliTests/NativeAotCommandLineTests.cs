using SharpTS.Cli;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CliTests;

public class NativeAotCommandLineTests
{
    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    [InlineData("osx-x64")]
    [InlineData("osx-arm64")]
    public void NativeProjectRecognizesRidsAndRequiresTheMatchingHost(string rid)
    {
        Assert.Contains(rid, NativeAotProjectOptions.SupportedRuntimeIdentifiers);
        var parsed = new CommandLineParser().Parse(
            ["--compile", "app.ts", "--emit-native-project", "native project", "--runtime", rid]);
        if (rid != NativeAotProjectOptions.HostRuntimeIdentifier)
        {
            var error = Assert.IsType<ParsedCommand.Error>(parsed);
            Assert.Equal(64, error.ExitCode);
            Assert.Contains("compiler host RID", error.Message);
            return;
        }
        var command = Assert.IsType<ParsedCommand.Compile>(parsed);
        Assert.Equal(OutputTarget.Dll, command.CompileOptions.Target);
        Assert.True(command.CompileOptions.UseReferenceAssemblies);
        Assert.Equal(new NativeAotProjectOptions("native project", rid), command.CompileOptions.NativeProject);
        Assert.Equal(Path.Combine("native project", "guest", "Guest.dll"), command.OutputFile);
    }

    public static IEnumerable<object[]> InvalidOptions()
    {
        yield return [new[] { "--emit-native-project" }];
        yield return [new[] { "--runtime", "win-x64" }];
        yield return [new[] { "--emit-native-project", "native" }];
        yield return [new[] { "--emit-native-project", "native", "--runtime", "unknown-x64" }];
        foreach (string[] conflict in new string[][]
        {
            new[] { "--hosted" }, ["--target", "exe"], ["-o", "other.dll"], ["--bundler", "sdk"],
            ["--pack"], ["--noEmit"], ["--emitDeclarationOnly"], ["--showConfig"],
            ["--gc-profile", "adaptive"], ["--gc-profile", "throughput"]
        })
            yield return [new[] { "--emit-native-project", "native", "--runtime", "win-x64" }.Concat(conflict).ToArray()];
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void IncompleteOrConflictingNativeModesAreRejected(string[] options)
    {
        var error = Assert.IsType<ParsedCommand.Error>(new CommandLineParser().Parse(
            new[] { "--compile", "app.ts" }.Concat(options).ToArray()));
        Assert.Equal(64, error.ExitCode);
    }

    [Fact]
    public void OrdinaryCompilationRetainsItsExistingDefaults()
    {
        var command = Assert.IsType<ParsedCommand.Compile>(new CommandLineParser().Parse(["--compile", "app.ts"]));
        Assert.Null(command.CompileOptions.NativeProject);
        Assert.False(command.CompileOptions.UseReferenceAssemblies);
        Assert.Equal("app.dll", command.OutputFile);
    }
}
