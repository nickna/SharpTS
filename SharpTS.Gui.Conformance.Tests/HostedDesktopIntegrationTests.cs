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
        Assert.Equal(8, events.Count(item => item.Stage.StartsWith("view-render-", StringComparison.Ordinal)));
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

    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    public async Task Headless_InitializesExpandedTopLevelAwaitModuleJobs(string mode)
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(
            repositoryRoot, "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string stageDirectory = Path.Combine(
            Path.GetTempPath(), $"sharpts-gui-tla-host-{Guid.NewGuid():N}");
        string hostAssembly = Path.Combine(stageDirectory, "SharpTS.Gui.Host.dll");
        Directory.CreateDirectory(stageDirectory);
        string guestDirectory = Path.Combine(stageDirectory, "Guest");
        string fixtureDirectory = Path.Combine(
            repositoryRoot,
            "SharpTS.Gui.Conformance.Tests",
            "Fixtures",
            "HostedTopLevelAwait");
        string tracePath = Path.Combine(stageDirectory, "trace.json");
        string[] fixtureNames = ["main.tsx", "lazy.ts", "lazy-dependency.ts", "rejected.ts"];

        try
        {
            CopyDirectory(hostSource, stageDirectory);
            foreach (string fixtureName in fixtureNames)
            {
                File.Copy(
                    Path.Combine(fixtureDirectory, fixtureName),
                    Path.Combine(guestDirectory, fixtureName),
                    overwrite: true);
            }

            string conformanceRoot = Path.Combine(
                repositoryRoot,
                "SharpTS.Gui.Conformance.Tests",
                "obj",
                configuration,
                "net10.0",
                ".sharpts-gui-conformance");
            string stageConfigDirectory = Path.Combine(stageDirectory, ".sharpts");
            Directory.CreateDirectory(stageConfigDirectory);
            File.Copy(
                Path.Combine(conformanceRoot, "tsconfig.json"),
                Path.Combine(stageConfigDirectory, "tsconfig.json"),
                overwrite: true);
            CopyDirectory(
                Path.Combine(conformanceRoot, "node_modules"),
                Path.Combine(stageConfigDirectory, "node_modules"));
            File.Copy(
                Path.Combine(repositoryRoot, "SharpTS.Gui.Conformance.Tests", "GuiConformanceApp.json"),
                Path.Combine(stageConfigDirectory, "app.json"),
                overwrite: true);

            if (mode == "compiled")
            {
                string compilerAssembly = Path.Combine(
                    repositoryRoot, "bin", configuration, "net10.0", "SharpTS.dll");
                string bridgeAssembly = Path.Combine(
                    repositoryRoot, "SharpTS.Gui", "bin", configuration, "net10.0", "SharpTS.Gui.dll");
                var compile = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = stageDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                compile.ArgumentList.Add(compilerAssembly);
                compile.ArgumentList.Add("-p");
                compile.ArgumentList.Add(Path.Combine(stageConfigDirectory, "tsconfig.json"));
                compile.ArgumentList.Add("-r");
                compile.ArgumentList.Add(bridgeAssembly);
                compile.ArgumentList.Add("-c");
                compile.ArgumentList.Add(Path.Combine(guestDirectory, "main.tsx"));
                compile.ArgumentList.Add("--target");
                compile.ArgumentList.Add("dll");
                compile.ArgumentList.Add("--hosted");
                compile.ArgumentList.Add("--verify");
                compile.ArgumentList.Add("--quiet");
                compile.ArgumentList.Add("-o");
                compile.ArgumentList.Add(Path.Combine(stageDirectory, "SharpTS.Gui.Guest.dll"));
                using var compileProcess = Process.Start(compile)
                    ?? throw new InvalidOperationException("Could not start the GUI guest compiler.");
                Task<string> compileStdoutTask = compileProcess.StandardOutput.ReadToEndAsync();
                Task<string> compileStderrTask = compileProcess.StandardError.ReadToEndAsync();
                await compileProcess.WaitForExitAsync();
                string compileStdout = await compileStdoutTask;
                string compileStderr = await compileStderrTask;
                Assert.True(compileProcess.ExitCode == 0,
                    $"Hosted top-level-await compilation failed with {compileProcess.ExitCode}." +
                    $"{Environment.NewLine}stdout:{Environment.NewLine}{compileStdout}" +
                    $"{Environment.NewLine}stderr:{Environment.NewLine}{compileStderr}");
            }

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = stageDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(hostAssembly);
            startInfo.ArgumentList.Add("--mode");
            startInfo.ArgumentList.Add(mode);
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
                throw new TimeoutException("Hosted top-level-await scenario exceeded 30 seconds.");
            }
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            Assert.True(process.ExitCode == 0,
                $"Hosted top-level await failed with {process.ExitCode}.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{stderr}");
            using var trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
            string[] stages = trace.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("Stage").GetString()!)
                .ToArray();
            string[] expected =
            [
                "tla-main-start",
                "tla-compound-rejected",
                "tla-conditional-rejected",
                "tla-loop-rejected",
                "tla-dynamic-import-rejected",
                "tla-dependency-start",
                "tla-dependency-microtask",
                "tla-lazy-start",
                "tla-lazy-end",
                "tla-lazy-microtask",
                "tla-main-resume-5-7-6-42",
                "tla-window-mounted",
                "tla-before-exit",
                "tla-before-exit-microtask",
                "unmount",
                "tla-exit",
                "host-exit-request",
                "runtime-dispose"
            ];
            int previous = -1;
            foreach (string stage in expected)
            {
                int current = Array.IndexOf(stages, stage);
                Assert.True(
                    current > previous,
                    $"Trace stage '{stage}' was missing or out of order. Actual: " +
                    string.Join(", ", stages));
                previous = current;
            }
        }
        finally
        {
            try { Directory.Delete(stageDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string directory in Directory.EnumerateDirectories(
            source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(
            source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
