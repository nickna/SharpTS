using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class CalculatorHeadlessTests
{
    [Fact]
    public async Task CalculatorInteractionsPassInInterpretedAndCompiledModes()
    {
        TraceEvent[] interpreted = await RunAsync("interpreted");
        TraceEvent[] compiled = await RunAsync("compiled");

        Assert.Single(interpreted, item => item.Stage == "guest-init-end");
        Assert.Single(compiled, item => item.Stage == "guest-init-end");
        Assert.Contains(interpreted, item => item.Stage == "headless-window-shown");
        Assert.Contains(compiled, item => item.Stage == "headless-window-shown");
        Assert.Equal(
            interpreted.Count(item => item.Stage == "render-commit"),
            compiled.Count(item => item.Stage == "render-commit"));
    }

    private static async Task<TraceEvent[]> RunAsync(string mode)
    {
        string root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(root, "src", "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string conformanceRoot = Path.Combine(root, "tests", "gui-conformance", "SharpTS.Gui.Conformance.Tests", "obj", configuration, "net10.0", ".sharpts-gui-conformance");
        string stage = Path.Combine(Path.GetTempPath(), $"sharpts-calculator-{mode}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            CopyDirectory(hostSource, stage);
            GuiInterpretedTestAssets.Stage(root, configuration, stage);
            string guestDirectory = Path.Combine(stage, "Guest");
            Directory.CreateDirectory(guestDirectory);
            string calculatorSource = Path.Combine(root, "samples", "Calculator");
            File.Copy(Path.Combine(calculatorSource, "headless.tests.tsx"), Path.Combine(guestDirectory, "main.tsx"), true);
            foreach (string source in Directory.EnumerateFiles(calculatorSource, "*.ts*"))
            {
                string name = Path.GetFileName(source);
                if (name is "main.tsx" or "headless.tests.tsx" || name.Contains(".tests.", StringComparison.Ordinal)) continue;
                File.Copy(source, Path.Combine(guestDirectory, name), true);
            }
            File.Copy(Path.Combine(conformanceRoot, "Calculator.Headless.Guest.dll"), Path.Combine(stage, "SharpTS.Gui.Guest.dll"), true);

            string tracePath = Path.Combine(stage, $"{mode}.json");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = stage,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(stage, "SharpTS.Gui.Host.dll"));
            start.ArgumentList.Add("--mode");
            start.ArgumentList.Add(mode);
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--trace");
            start.ArgumentList.Add(tracePath);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the Calculator Headless host.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Calculator {mode} Headless run exceeded 30 seconds.");
            }

            string output = await stdout;
            string errors = await stderr;
            Assert.True(process.ExitCode == 0,
                $"Calculator {mode} Headless run failed with {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{errors}");

            using JsonDocument trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
            return trace.RootElement.EnumerateArray().Select(item => new TraceEvent(
                item.GetProperty("Stage").GetString()!,
                item.GetProperty("Detail").ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetProperty("Detail").GetString())).ToArray();
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "SharpTS.sln"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate the SharpTS repository root.");
    }

    private sealed record TraceEvent(string Stage, string? Detail);
}
