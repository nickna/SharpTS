using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class SharpPaintHeadlessTests
{
    [Fact]
    public async Task ModelTestsPassThroughSharpTSInterpreter()
    {
        string root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string compiler = Path.Combine(root, "src", "SharpTS", "bin", configuration, "net10.0", "SharpTS.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(compiler);
        start.ArgumentList.Add(Path.Combine(root, "samples", "SharpPaint", "document.tests.ts"));
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the SharpPaint model tests.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0,
            $"SharpPaint model tests failed.{Environment.NewLine}{await stdout}{Environment.NewLine}{await stderr}");
        Assert.Contains("SharpPaint model tests passed.", await stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InteractionsPassInInterpretedAndCompiledModes()
    {
        TraceEvent[] interpreted = await RunAsync("interpreted");
        TraceEvent[] compiled = await RunAsync("compiled");

        Assert.Single(interpreted, item => item.Stage == "guest-init-end");
        Assert.Single(compiled, item => item.Stage == "guest-init-end");
        Assert.Contains(interpreted, item => item.Stage == "headless-window-shown");
        Assert.Contains(compiled, item => item.Stage == "headless-window-shown");
        Assert.Equal(
            interpreted.Count(item => item.Stage == "headless-window-shown"),
            compiled.Count(item => item.Stage == "headless-window-shown"));
        Assert.True(interpreted.Count(item => item.Stage == "render-commit") >= 8);
        Assert.True(compiled.Count(item => item.Stage == "render-commit") >= 8);
    }

    [Fact]
    public async Task InterpretedSmokeCloseIgnoresQueuedMetricsRenderAfterDisposal()
    {
        TraceEvent[] events = await RunAsync(
            "interpreted",
            entryPoint: "main.tsx",
            smokeClose: true);

        Assert.Single(events, item => item.Stage == "guest-init-end");
        Assert.Contains(events, item => item.Stage == "headless-window-shown");
        Assert.Contains(events, item => item.Stage == "unmount");
    }

    private static async Task<TraceEvent[]> RunAsync(
        string mode,
        string entryPoint = "headless.tests.tsx",
        bool smokeClose = false)
    {
        string root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(root, "src", "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string conformanceRoot = Path.Combine(root, "tests", "gui-conformance", "SharpTS.Gui.Conformance.Tests", "obj", configuration, "net10.0", ".sharpts-gui-conformance");
        string stage = Path.Combine(Path.GetTempPath(), $"sharpts-sharpaint-{mode}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            CopyDirectory(hostSource, stage);
            GuiInterpretedTestAssets.Stage(root, configuration, stage);
            string guestDirectory = Path.Combine(stage, "Guest");
            Directory.CreateDirectory(guestDirectory);
            foreach (string file in new[] { entryPoint, "SharpPaintApp.tsx", "document.ts" })
                File.Copy(Path.Combine(root, "samples", "SharpPaint", file), Path.Combine(guestDirectory, file == entryPoint ? "main.tsx" : file), true);
            File.Copy(Path.Combine(conformanceRoot, "SharpPaint.Headless.Guest.dll"), Path.Combine(stage, "SharpTS.Gui.Guest.dll"), true);

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
            if (smokeClose)
                start.Environment["SHARPTS_GUI_SMOKE_CLOSE"] = "1";

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the SharpPaint Headless host.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"SharpPaint {mode} Headless run exceeded 45 seconds.");
            }

            string output = await stdout;
            string errors = await stderr;
            Assert.True(process.ExitCode == 0,
                $"SharpPaint {mode} Headless run failed with {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{errors}");
            Assert.False(File.Exists(Path.Combine(stage, "SharpPaint.Headless.Open.sharpaint")));
            Assert.False(File.Exists(Path.Combine(stage, "SharpPaint.Headless.Save.sharpaint")));

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
