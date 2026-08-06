using System.Text.Json;
using System.Text.Json.Serialization;
using SharpTS.Diagnostics;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliTimingTests
{
    [Fact]
    public void Compile_TimingsJson_ProducesOneStableReportOnStdout()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig --timings-json",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.True(report.Success);
        Assert.True(double.IsFinite(report.TotalDurationMs));
        Assert.True(report.TotalDurationMs >= 0);
        Assert.Equal(
            [
                "resolveConfiguration", "loadReferences", "loadModules", "typeCheck",
                "analyzeDeadCode", "initializeCompiler", "prepareCompilation",
                "extractNamespaces", "emitRuntimeTypes", "analyzeClosures",
                "defineProgramStructure", "analyzeModuleBindings", "defineDeclarations",
                "collectFunctions", "emitFunctionBodies", "emitMethodBodies",
                "emitModuleInitializers", "emitEntryPoint", "finalizeTypes",
                "serializeAssembly", "generateRuntimeConfig"
            ],
            report.Timings.Select(timing => timing.Name));
        Assert.All(report.Timings, timing =>
        {
            Assert.Equal(ExecutionPhaseTiming.CompletedStatus, timing.Status);
            Assert.True(double.IsFinite(timing.DurationMs));
            Assert.True(timing.DurationMs >= 0);
        });
        Assert.DoesNotContain("Compiled to", result.StandardOutput);
    }

    [Fact]
    public void Compile_TimingsJson_FailureKeepsJsonCleanAndReportsPartialPhases()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("bad.ts", CliFixtures.TypeErrorScript);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig --timings-json",
            tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.False(report.Success);
        Assert.Equal(
            ["resolveConfiguration", "loadReferences", "loadModules", "typeCheck"],
            report.Timings.Select(timing => timing.Name));
        Assert.Equal(ExecutionPhaseTiming.FailedStatus, report.Timings[^1].Status);
        Assert.Contains("Error", result.StandardError);
    }

    [Fact]
    public void Compile_TimingsJson_NoEmitIncludesOnlyReachedFrontEndPhases()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig --noEmit --timings-json",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.Equal(
            ["resolveConfiguration", "loadReferences", "loadModules", "typeCheck"],
            report.Timings.Select(timing => timing.Name));
        Assert.False(File.Exists(tempDir.GetPath("hello.dll")));
    }

    [Fact]
    public void Compile_TimingsJson_BackendFailureReportsFailedSerialization()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);
        var missingOutput = tempDir.GetPath(Path.Combine("missing", "hello.dll"));

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" -o \"{missingOutput}\" --no-tsconfig --timings-json",
            tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.False(report.Success);
        Assert.Equal(ExecutionPhaseTiming.SerializeAssembly, report.Timings[^1].Name);
        Assert.Equal(ExecutionPhaseTiming.FailedStatus, report.Timings[^1].Status);
        Assert.Contains("Error", result.StandardError);
    }

    [Fact]
    public void Compile_TimingsJson_VerificationAndDeclarationsAreConditionalPhases()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig --declaration --verify --timings-json",
            tempDir.Path,
            TimeSpan.FromMinutes(2));

        Assert.Equal(0, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.Contains(report.Timings,
            timing => timing.Name == ExecutionPhaseTiming.EmitDeclarations);
        Assert.Contains(report.Timings,
            timing => timing.Name == ExecutionPhaseTiming.VerifyAssembly);
    }

    [Fact]
    public void Compile_TimingsReadable_IsWrittenToStderrEvenWhenQuiet()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig --noEmit --quiet --timings",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput);
        Assert.Contains("Compilation timings:", result.StandardError);
        Assert.Contains("resolveConfiguration", result.StandardError);
        Assert.Contains("total", result.StandardError);
    }

    [Fact]
    public void Compile_TimingsJson_ExeIncludesBundlingPhase()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli(
            $"-c \"{scriptPath}\" --no-tsconfig -t exe --timings-json",
            tempDir.Path,
            TimeSpan.FromMinutes(2));

        Assert.Equal(0, result.ExitCode);
        var report = Deserialize(result.StandardOutput);
        Assert.Contains(report.Timings,
            timing => timing.Name == ExecutionPhaseTiming.BundleExecutable);
        Assert.True(File.Exists(tempDir.GetPath("hello.exe")));
    }

    private static TimingReport Deserialize(string json) =>
        JsonSerializer.Deserialize(json, CliTimingTestJsonContext.Default.TimingReport)
        ?? throw new InvalidOperationException("Timing report was null.");
}

internal sealed record TimingReport(
    bool Success,
    double TotalDurationMs,
    ExecutionPhaseTiming[] Timings);

[JsonSerializable(typeof(TimingReport))]
internal sealed partial class CliTimingTestJsonContext : JsonSerializerContext;
