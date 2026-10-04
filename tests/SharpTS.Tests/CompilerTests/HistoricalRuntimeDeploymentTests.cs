#pragma warning disable SHARPTS_HOSTING001

using System.Diagnostics;
using System.Reflection;
using SharpTS.Hosting;
using SharpTS.Tests.Hosting;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("ExternalProcessTests")]
public sealed class HistoricalRuntimeDeploymentTests
{
    [Theory]
    [MemberData(nameof(HistoricalRuntimeRegressionTests.Cases), MemberType = typeof(HistoricalRuntimeRegressionTests))]
    public void Isolated_PreservedSourcesExecuteStandaloneAndHosted(string file, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(file, source);
        foreach (bool noLib in new[] { false, true })
        foreach (bool hosted in new[] { false, true })
        {
            string output = directory.GetPath($"guest-{noLib}-{hosted}.dll");
            var compile = CliTestHelper.RunCli(
                $"--no-tsconfig {(noLib ? "--noLib" : "")} --compile \"{path}\" -o \"{output}\" --standalone --verify {(hosted ? "--target dll --hosted" : "")}",
                directory.Path, TimeSpan.FromSeconds(30));
            Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
            Assert.Contains("IL verification passed.", compile.StandardOutput);
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
            HistoricalRuntimeRegressionTests.AssertReferenceOutput(file, expected,
                hosted ? RunHosted(output) : RunStandalone(output));
        }
    }

    private static string RunStandalone(string dll)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!
        };
        start.ArgumentList.Add(dll);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        bool completed = process.WaitForExit(30000);
        if (!completed) process.Kill(entireProcessTree: true);
        Assert.True(completed, "Standalone execution exceeded the preserved 30-second deadline.");
        Assert.True(Task.WaitAll([stdout, stderr], 5000), "Redirected output did not drain.");
        Assert.Equal(0, process.ExitCode);
        Assert.Empty(stderr.Result);
        return stdout.Result.Replace("\r\n", "\n");
    }

    private static string RunHosted(string dll)
    {
        lock (TestHarness.ConsoleLock)
        {
            var dispatcher = new DeterministicHostDispatcher();
            var sink = new RecordingErrorSink();
            var priorOut = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                using var runtime = SharpTSHostedAssembly.CreateRuntime(
                    Assembly.Load(File.ReadAllBytes(dll)), dispatcher, new RecordingLifetime(), sink);
                Task initialization = runtime.InitializeAsync();
                dispatcher.RunUntil(() => initialization.IsCompleted, timeout: TimeSpan.FromSeconds(30));
                initialization.GetAwaiter().GetResult();
                Assert.Empty(sink.Errors);
                return output.ToString().Replace("\r\n", "\n");
            }
            finally { Console.SetOut(priorOut); }
        }
    }
}
