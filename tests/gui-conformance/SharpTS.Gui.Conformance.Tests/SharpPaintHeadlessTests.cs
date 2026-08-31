using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class SharpPaintHeadlessTests
{
    private static readonly TimeSpan ModelTestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProcessCleanupTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ModelTestsPassThroughSharpTSInterpreter()
    {
        string output = await RunModelTestsAsync(ModelTestTimeout);
        Assert.Contains("SharpPaint model tests passed.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelTestTimeoutTerminatesProcessAndObservesOutput()
    {
        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            RunModelTestsAsync(TimeSpan.Zero));

        Assert.Contains("exited with code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cleanup completed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stdout:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stderr:", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<string> RunModelTestsAsync(TimeSpan executionTimeout)
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
        using var timeout = new CancellationTokenSource(executionTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            string diagnostics = await TerminateAndObserveProcessAsync(
                process,
                stdout,
                stderr,
                ProcessCleanupTimeout);
            throw new TimeoutException(
                $"SharpPaint model tests exceeded {executionTimeout.TotalSeconds:F0} seconds. {diagnostics}",
                exception);
        }

        string output = await stdout;
        string errors = await stderr;
        Assert.True(process.ExitCode == 0,
            $"SharpPaint model tests failed.{Environment.NewLine}{output}{Environment.NewLine}{errors}");
        return output;
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

    private static async Task<string> TerminateAndObserveProcessAsync(
        Process process,
        Task<string> stdout,
        Task<string> stderr,
        TimeSpan timeout)
    {
        int processId = process.Id;
        var cleanupNotes = new List<string>();

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            cleanupNotes.Add($"termination failed with {exception.GetType().Name}: {exception.Message}");
        }

        Task exit;
        try
        {
            exit = process.WaitForExitAsync();
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            cleanupNotes.Add($"exit observation failed with {exception.GetType().Name}: {exception.Message}");
            exit = Task.CompletedTask;
        }

        Task observation = Task.WhenAll(exit, stdout, stderr);
        try
        {
            await observation.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            cleanupNotes.Add($"cleanup did not complete within {timeout.TotalSeconds:F0} seconds");
        }
        catch (Exception exception)
        {
            cleanupNotes.Add($"cleanup observation failed with {exception.GetType().Name}: {exception.Message}");
        }

        if (!observation.IsCompleted)
        {
            _ = observation.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        string notes = cleanupNotes.Count == 0 ? "cleanup completed" : string.Join("; ", cleanupNotes);
        return $"PID {processId}; {DescribeExit(process)}; {notes}; " +
            $"{DescribeOutput("stdout", stdout)}; {DescribeOutput("stderr", stderr)}";
    }

    private static string DescribeExit(Process process)
    {
        try
        {
            return process.HasExited
                ? $"exited with code {process.ExitCode}"
                : "still running";
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return $"exit state unavailable ({exception.GetType().Name}: {exception.Message})";
        }
    }

    private static string DescribeOutput(string name, Task<string> output)
    {
        if (output.IsCompletedSuccessfully)
        {
            string text = output.Result;
            const int limit = 2_000;
            if (text.Length > limit)
                text = text[..limit] + "... <truncated>";
            return $"{name}: {(string.IsNullOrWhiteSpace(text) ? "<empty>" : text.Trim())}";
        }

        if (output.IsCanceled)
            return $"{name}: read canceled";
        if (output.IsFaulted)
        {
            Exception exception = output.Exception!.GetBaseException();
            return $"{name}: read failed with {exception.GetType().Name}: {exception.Message}";
        }

        return $"{name}: read did not complete";
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
