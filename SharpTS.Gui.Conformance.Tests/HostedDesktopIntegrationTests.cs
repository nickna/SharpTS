using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class HostedDesktopIntegrationTests
{
    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    public async Task HeadlessScenario_UsesHostedAbiOwnerThreadAndDispatcherFairness(string mode)
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostDirectory = Path.Combine(
            repositoryRoot,
            "SharpTS.Gui.Host",
            "bin",
            configuration,
            "net10.0");
        string hostAssembly = Path.Combine(hostDirectory, "SharpTS.Gui.Host.dll");
        string tracePath = Path.Combine(Path.GetTempPath(), $"sharpts-gui-hosted-{mode}-{Guid.NewGuid():N}.json");
        Assert.True(File.Exists(hostAssembly), $"GUI host was not built: {hostAssembly}");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = hostDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add(mode);
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--auto-close");
        startInfo.ArgumentList.Add("--trace");
        startInfo.ArgumentList.Add(tracePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the GUI host.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"GUI {mode} headless host exceeded 30 seconds.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        Assert.True(process.ExitCode == 0,
            $"GUI {mode} host failed with {process.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        Assert.True(File.Exists(tracePath), $"GUI {mode} trace was not produced.");

        using var trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
        var events = trace.RootElement.EnumerateArray().Select(item => new
        {
            Sequence = item.GetProperty("Sequence").GetInt64(),
            Stage = item.GetProperty("Stage").GetString()!,
            Thread = item.GetProperty("ManagedThreadId").GetInt32(),
            Context = item.GetProperty("SynchronizationContext").ValueKind == JsonValueKind.Null
                ? null
                : item.GetProperty("SynchronizationContext").GetString(),
            Detail = item.GetProperty("Detail").ValueKind == JsonValueKind.Null
                ? null
                : item.GetProperty("Detail").GetString()
        }).ToArray();

        int owner = events.Single(item => item.Stage == "avalonia-setup").Thread;
        string? ownerContext = events.Single(item => item.Stage == "avalonia-setup").Context;
        string[] ownerStages =
        [
            "guest-init-begin", "guest-init-end", "mount", "dispatcher-sentinel",
            "button-click-event", "guest-click", "coalesced-update-complete",
            "dependency-switch-complete", "reactive-update-complete",
            "text-changed-event", "checked-changed-event", "selection-changed-event",
            "value-changed-event", "forms-events-complete",
            "late-reactive-work-ignored", "guest-timer", "guest-async-resume",
            "before-exit", "before-exit-microtask", "effect-cleanup",
            "unmount", "unsubscribe", "exit", "host-exit-request", "runtime-dispose"
        ];
        foreach (string stage in ownerStages)
        {
            Assert.All(events.Where(item => item.Stage == stage), item =>
            {
                Assert.Equal(owner, item.Thread);
                Assert.Equal(ownerContext, item.Context);
            });
        }

        Assert.NotEqual(owner, events.Single(item => item.Stage == "task-complete-off-thread").Thread);
        Assert.Equal(4, events.Count(item => item.Stage.StartsWith("view-render-", StringComparison.Ordinal)));
        Assert.Equal(7, events.Count(item => item.Stage == "unsubscribe"));
        Assert.DoesNotContain(events, item => item.Stage == "stale-guest-click");
        Assert.True(
            events.Single(item => item.Stage == "guest-init-end").Sequence <
            events.Single(item => item.Stage == "dispatcher-sentinel").Sequence,
            "Dispatcher sentinel did not execute after guest initialization.");
        Assert.True(
            events.Single(item => item.Stage == "dispatcher-sentinel").Sequence <
            events.Single(item => item.Stage == "guest-timer").Sequence,
            "Dispatcher sentinel did not run between initialization and guest timer work.");

        Assert.DoesNotContain(events, item => item.Stage == "pump");
        string[] stages = events.Select(item => item.Stage).ToArray();
        AssertLifecycle(stages, requireCleanupAfterBeforeExit: false);
        Assert.Equal(1, stages.Count(stage => stage == "effect-cleanup"));
        Assert.Equal(1, stages.Count(stage => stage == "unmount"));
        Assert.True(Array.IndexOf(stages, "effect-cleanup") < Array.IndexOf(stages, "unmount"));
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("cancelled")]
    [InlineData("repeated")]
    [InlineData("initialization")]
    [InlineData("queued")]
    public async Task InterpretedWindowClose_UsesOrderedIdempotentShutdown(string scenario)
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostDirectory = Path.Combine(
            repositoryRoot, "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string hostAssembly = Path.Combine(hostDirectory, "SharpTS.Gui.Host.dll");
        string tracePath = Path.Combine(
            Path.GetTempPath(), $"sharpts-gui-window-close-{scenario}-{Guid.NewGuid():N}.json");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = hostDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["SHARPTS_GUI_LIFECYCLE_SCENARIO"] = scenario;
        startInfo.ArgumentList.Add(hostAssembly);
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("interpreted");
        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--trace");
        startInfo.ArgumentList.Add(tracePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the GUI host.");
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"GUI {scenario} close scenario exceeded 30 seconds.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        Assert.True(process.ExitCode == 0,
            $"GUI {scenario} close failed with {process.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        Assert.True(File.Exists(tracePath));

        using var trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
        string[] stages = trace.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("Stage").GetString()!)
            .ToArray();
        AssertLifecycle(stages, requireCleanupAfterBeforeExit: true);
        Assert.Equal(1, stages.Count(stage => stage == "effect-cleanup"));
        Assert.Equal(1, stages.Count(stage => stage == "unmount"));
        Assert.Equal(1, stages.Count(stage => stage == "host-exit-request"));
        Assert.DoesNotContain("late-window-timer", stages);
        if (scenario == "cancelled")
            Assert.Contains("window-close-cancelled", stages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterpretedLaunch_LeavesTracingDisabledOrTreatsWriteFailureAsNonfatal(
        bool useUnwritableTracePath)
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostDirectory = Path.Combine(
            repositoryRoot, "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string hostAssembly = Path.Combine(hostDirectory, "SharpTS.Gui.Host.dll");
        string stage = Path.Combine(
            Path.GetTempPath(), "sharpts-gui-trace-failure-" + Guid.NewGuid().ToString("N"));
        string blocker = Path.Combine(stage, "not-a-directory");
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(blocker, "blocked");

        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = hostDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.Environment["SHARPTS_GUI_LIFECYCLE_SCENARIO"] = "normal";
            startInfo.ArgumentList.Add(hostAssembly);
            startInfo.ArgumentList.Add("--mode");
            startInfo.ArgumentList.Add("interpreted");
            startInfo.ArgumentList.Add("--headless");
            if (useUnwritableTracePath)
            {
                startInfo.ArgumentList.Add("--trace");
                startInfo.ArgumentList.Add(Path.Combine(blocker, "trace.json"));
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the GUI host.");
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            Assert.Equal(0, process.ExitCode);
            Assert.DoesNotContain("SharpTS GUI Interpreted trace:", stdout, StringComparison.Ordinal);
            if (useUnwritableTracePath)
                Assert.Contains("trace could not be written", stderr, StringComparison.OrdinalIgnoreCase);
            else
                Assert.DoesNotContain("trace could not be written", stderr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(stage, recursive: true);
        }
    }

    private static void AssertLifecycle(
        IReadOnlyList<string> stages,
        bool requireCleanupAfterBeforeExit)
    {
        string[] lifecycle = requireCleanupAfterBeforeExit
            ?
            [
                "before-exit", "before-exit-microtask", "effect-cleanup", "unmount",
                "exit", "host-exit-request", "runtime-dispose"
            ]
            :
            [
                "before-exit", "before-exit-microtask", "exit",
                "host-exit-request", "runtime-dispose"
            ];
        var stageList = stages.ToList();
        int previous = -1;
        foreach (string stage in lifecycle)
        {
            int current = stageList.IndexOf(stage);
            Assert.True(current > previous, $"Lifecycle stage '{stage}' was missing or out of order.");
            previous = current;
        }
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "SharpTS.sln")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate the SharpTS repository root.");
    }
}
