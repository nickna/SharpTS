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

internal static class HistoricalRuntimeDeploymentTests
{
    public static void AssertDeployment(string file, string source, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(file, source);
        // Preserve historical snippets where current declarations reject
        // sumPrecise availability or unchecked descriptor reads. The noLib probes
        // retain the originals; typed controls also check default declarations.
        var libModes = file is "issue1935-original.ts" or "issue1936-original.ts" or "issue1959-original.ts" or "issue1960-original.ts"
            ? new[] { true } : new[] { false, true };
        foreach (bool noLib in libModes)
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
        try
        {
            Assert.True(process.WaitForExit(30000),
                "Standalone execution exceeded the preserved 30-second deadline.");
            Assert.True(Task.WaitAll([stdout, stderr], 5000), "Redirected output did not drain.");
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(stderr.Result);
            return stdout.Result.Replace("\r\n", "\n");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.WaitForExit(5000);
            }
        }
    }

    private static string RunHosted(string dll)
    {
        var dispatcher = new DeterministicHostDispatcher();
        var sink = new RecordingErrorSink();
        using var output = new StringWriter();
        using var capture = AsyncLocalConsoleRedirector.WithOut(output);
        using var runtime = SharpTSHostedAssembly.CreateRuntime(
            Assembly.Load(File.ReadAllBytes(dll)), dispatcher, new RecordingLifetime(), sink);
        Task initialization = runtime.InitializeAsync();
        dispatcher.RunUntil(() => initialization.IsCompleted, timeout: TimeSpan.FromSeconds(30));
        initialization.GetAwaiter().GetResult();
        Assert.Empty(sink.Errors);
        return output.ToString().Replace("\r\n", "\n");
    }
}
