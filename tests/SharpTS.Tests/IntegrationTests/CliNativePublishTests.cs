using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliNativePublishTests
{
    private static string Options => $"--target native-aot --runtime {NativeAotProjectOptions.HostRuntimeIdentifier} -o output.bin --msbuild-errors";

    [Theory]
    [InlineData("Guest.exe")]
    [InlineData("guest.exe")]
    [InlineData("System.Console.exe")]
    public void ReservedWrapperOutputNamesRefuseBeforeOverwritingUserOutput(string name)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);"); directory.CreateFile(name, "prior output");
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile main.ts {Options.Replace("output.bin", name)}", directory.Path);
        Assert.NotEqual(0, result.ExitCode); Assert.Contains("SHARPTS409", result.StandardError + result.StandardOutput);
        Assert.Equal("prior output", File.ReadAllText(directory.GetPath(name)));
    }

    [Theory]
    [InlineData("eval(process.argv[2]);", "SHARPTS401")]
    [InlineData("import { createGzip } from 'node:zlib'; const zipper:any=createGzip(); console.log(zipper.bytesWritten); zipper.close();", "SHARPTS402")]
    public void UnsupportedFeaturesPreserveExistingNativeOutputBeforePublication(string source, string code)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", source); directory.CreateFile("output.bin", "prior output");
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile main.ts {Options}", directory.Path);
        Assert.NotEqual(0, result.ExitCode); Assert.Contains(code, result.StandardError + result.StandardOutput);
        Assert.Equal("prior output", File.ReadAllText(directory.GetPath("output.bin")));
        Assert.False(File.Exists(directory.GetPath(".output.bin.sharpts-native.json")));
    }

    [Fact]
    public void EffectiveDeclarationOnlyConfigurationRefusesBeforeNativePublication()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);"); directory.CreateFile("output.bin", "prior output");
        directory.CreateFile("base.json", "{\"compilerOptions\":{\"declaration\":true,\"emitDeclarationOnly\":true}}");
        directory.CreateFile("tsconfig.json", "{\"extends\":\"./base.json\"}");
        var result = CliTestHelper.RunCli($"--compile main.ts {Options}", directory.Path);
        Assert.NotEqual(0, result.ExitCode); Assert.Contains("emitDeclarationOnly", result.StandardError + result.StandardOutput);
        Assert.Equal("prior output", File.ReadAllText(directory.GetPath("output.bin")));
        Assert.False(File.Exists(directory.GetPath("main.d.ts")));
    }

    [Fact]
    public void MissingSourceSelectedSdkReportsNamedDiagnosticAndPreservesOutput()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.ts", "console.log(42);"); directory.CreateFile("output.bin", "prior output");
        directory.CreateFile("global.json", "{\"sdk\":{\"version\":\"99.0.100\",\"rollForward\":\"disable\"}}");
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile main.ts {Options}", directory.Path);
        Assert.NotEqual(0, result.ExitCode); Assert.Contains("SHARPTS407", result.StandardError + result.StandardOutput);
        Assert.Equal("prior output", File.ReadAllText(directory.GetPath("output.bin")));
    }
}
