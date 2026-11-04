using SharpTS.Testing;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class CalculatorHeadlessTests(ITestOutputHelper testOutput)
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

    private async Task<TraceEvent[]> RunAsync(string mode)
    {
        string root = FindRepositoryRoot();
string hostSource = GuiConformanceBuildPaths.DesktopHostDirectory;
        string conformanceRoot = GuiConformanceBuildPaths.Assets;
        string stage = Path.Combine(Path.GetTempPath(), $"sharpts-calculator-{mode}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            CopyDirectory(hostSource, stage);
            GuiInterpretedTestAssets.Stage(root, stage);
            string guestDirectory = Path.Combine(stage, "Guest");
            Directory.CreateDirectory(guestDirectory);
            File.Copy(Path.Combine(root, "samples", "Calculator", "headless.tests.tsx"), Path.Combine(guestDirectory, "main.tsx"), true);
            File.Copy(Path.Combine(root, "samples", "Calculator", "CalculatorApp.tsx"), Path.Combine(guestDirectory, "CalculatorApp.tsx"), true);
            File.Copy(Path.Combine(root, "samples", "Calculator", "calculator.ts"), Path.Combine(guestDirectory, "calculator.ts"), true);
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

            var process = await TestProcess.RunAsync(start, TimeSpan.FromSeconds(30), $"Calculator {mode} Headless run");
            string output = process.StandardOutput;
            string errors = process.StandardError;
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
            await TestDirectory.TryDeleteAsync(stage, testOutput.WriteLine);
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
