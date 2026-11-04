using SharpTS.Testing;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class SharpPaintHeadlessTests(ITestOutputHelper testOutput)
{
    private static readonly TimeSpan ModelTestTimeout = TimeSpan.FromSeconds(30);
    // Each editing/theme scenario has its own guard and retained phase timings.
    private static readonly TimeSpan WorkflowTestTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SmokeTestTimeout = TimeSpan.FromSeconds(90);

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
string compiler = GuiConformanceBuildPaths.Compiler;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(compiler);
        start.ArgumentList.Add(Path.Combine(root, "samples", "SharpPaint", "document.tests.ts"));
        var process = await TestProcess.RunAsync(start, executionTimeout, "SharpPaint model tests");
        string output = process.StandardOutput;
        string errors = process.StandardError;
        Assert.True(process.ExitCode == 0,
            $"SharpPaint model tests failed.{Environment.NewLine}{output}{Environment.NewLine}{errors}");
        return output;
    }

    [Theory]
    [InlineData("interpreted", "editing", 2)]
    [InlineData("compiled", "editing", 2)]
    [InlineData("interpreted", "light", 3)]
    [InlineData("compiled", "light", 3)]
    [InlineData("interpreted", "dark", 3)]
    [InlineData("compiled", "dark", 3)]
    public async Task WorkflowsPass(string mode, string scenario, int expectedWindows)
    {
        TraceEvent[] events = await RunAsync(mode, scenario: scenario);
        Assert.Single(events, item => item.Stage == "guest-init-end");
        Assert.Equal(expectedWindows, events.Count(item => item.Stage == "headless-window-shown"));
        Assert.True(events.Count(item => item.Stage == "render-commit") >= 8);
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

    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    public async Task HeadlessTimeoutTerminatesProcessAndObservesOutput(string mode)
    {
        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            RunAsync(mode, executionTimeout: TimeSpan.Zero));

        Assert.Contains($"SharpPaint {mode} Headless run exceeded 0 seconds.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exited with code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cleanup completed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stdout:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stderr:", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedFailureRetainsDiagnosticsAndSuccessfulRunOnlyCleansUp(bool fail)
    {
        string? stage = null;
        byte[]? traceBytes = null;
        string? retained = null;
        const string sentinel = "injected failure after successful headless verification";
        try
        {
            Exception? failure = await Record.ExceptionAsync(async () => await RunAsync(
                "interpreted", entryPoint: "main.tsx", smokeClose: true,
                verifyBeforeCleanup: currentStage =>
                {
                    stage = currentStage;
                    retained = Path.Combine(FindRepositoryRoot(), "artifacts", "gui-conformance-failures", Path.GetFileName(stage));
                    traceBytes = File.ReadAllBytes(Path.Combine(stage, "interpreted.json"));
                    if (fail) throw new InvalidOperationException(sentinel);
                }));
            Assert.NotNull(stage);
            Assert.False(Directory.Exists(stage));
            Assert.NotNull(retained);
            if (!fail)
            {
                Assert.Null(failure);
                Assert.False(Directory.Exists(retained));
                return;
            }
            Assert.Equal(sentinel, Assert.IsType<InvalidOperationException>(failure).Message);
            Assert.Equal(traceBytes, File.ReadAllBytes(Path.Combine(retained, "trace.json")));
            using JsonDocument evidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(retained, "failure.json")));
            JsonElement root = evidence.RootElement;
            Assert.Equal("interpreted", root.GetProperty("Mode").GetString());
            Assert.Equal("editing", root.GetProperty("Scenario").GetString());
            Assert.Equal("main.tsx", root.GetProperty("EntryPoint").GetString());
            Assert.True(root.GetProperty("TracePresent").GetBoolean());
            Assert.Contains(sentinel, root.GetProperty("Failure").GetString());
            Assert.Equal(0, root.GetProperty("ExitCode").GetInt32());
            Assert.Contains("SharpTS GUI Interpreted trace:", root.GetProperty("StandardOutput").GetString());
            Assert.Equal("", root.GetProperty("StandardError").GetString());
            Assert.NotEmpty(root.GetProperty("Timeline").GetString()!);
            Assert.Equal(2, Directory.GetFiles(retained).Length);
        }
        finally
        {
            if (retained is not null) await TestDirectory.TryDeleteAsync(retained, testOutput.WriteLine);
        }
    }

    private async Task<TraceEvent[]> RunAsync(
        string mode,
        string entryPoint = "headless.tests.tsx",
        bool smokeClose = false,
        TimeSpan? executionTimeout = null,
        string scenario = "editing",
        Action<string>? verifyBeforeCleanup = null)
    {
        string root = FindRepositoryRoot();
string hostSource = GuiConformanceBuildPaths.DesktopHostDirectory;
        string conformanceRoot = GuiConformanceBuildPaths.Assets;
        string stage = Path.Combine(Path.GetTempPath(), $"sharpts-sharpaint-{mode}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        TestProcessResult? process = null;
        TestProcess? observedProcess = null;
        try
        {
            CopyDirectory(hostSource, stage);
            GuiInterpretedTestAssets.Stage(root, stage);
            string guestDirectory = Path.Combine(stage, "Guest");
            Directory.CreateDirectory(guestDirectory);
            foreach (string file in Directory.GetFiles(Path.Combine(root, "samples", "SharpPaint"), "*.ts*").Select(Path.GetFileName).OfType<string>().Where(file => file != "main.tsx" || entryPoint == "main.tsx"))
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
            start.Environment["SHARPAINT_TEST_SCENARIO"] = scenario;
            start.Environment["SHARPAINT_STORAGE_DIRECTORY"] = Path.Combine(stage, "settings");
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--trace");
            start.ArgumentList.Add(tracePath);
            if (smokeClose)
                start.Environment["SHARPTS_GUI_SMOKE_CLOSE"] = "1";

            TimeSpan limit = executionTimeout ?? (smokeClose ? SmokeTestTimeout : WorkflowTestTimeout);
            await using (observedProcess = new TestProcess(start, limit, $"SharpPaint {mode} Headless run"))
                process = await observedProcess.WaitForExitAsync();
            testOutput.WriteLine($"{mode} / {scenario}:\n{process.Timeline}");
            string output = process.StandardOutput;
            string errors = process.StandardError;
            Assert.True(process.ExitCode == 0,
                $"SharpPaint {mode} Headless run failed with {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{errors}");
            Assert.False(File.Exists(Path.Combine(stage, "SharpPaint.Headless.Open.sharpaint")));
            Assert.False(File.Exists(Path.Combine(stage, "SharpPaint.Headless.Save.sharpaint")));

            if (!smokeClose) Assert.True(output.Contains("SharpPaint headless workflows passed.", StringComparison.Ordinal), $"stdout: {output}\nstderr: {errors}");
            using JsonDocument trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
            TraceEvent[] events = trace.RootElement.EnumerateArray().Select(item => new TraceEvent(
                item.GetProperty("Stage").GetString()!,
                item.GetProperty("Detail").ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetProperty("Detail").GetString())).ToArray();
            verifyBeforeCleanup?.Invoke(stage);
            return events;
        }
        catch (Exception failure)
        {
            // The zero-timeout cases deliberately test process cleanup. Preserve
            // unexpected failures before the existing temporary-stage cleanup.
            if (executionTimeout != TimeSpan.Zero)
            {
                try
                {
                    string retained = SharpPaintFailureArtifacts.Retain(root, stage, mode, scenario, entryPoint, failure, process, observedProcess);
                    testOutput.WriteLine($"Retained SharpPaint failure diagnostics: {retained}");
                }
                catch (Exception diagnosticsFailure) when (diagnosticsFailure is IOException or UnauthorizedAccessException)
                {
                    testOutput.WriteLine($"Could not retain SharpPaint diagnostics: {diagnosticsFailure.Message}");
                }
            }
            throw;
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
