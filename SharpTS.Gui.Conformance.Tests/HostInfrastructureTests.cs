#pragma warning disable SHARPTS_HOSTING001

using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using SharpTS.Gui.Host;
using SharpTS.Hosting;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class HostInfrastructureTests
{
    [Fact]
    public void OptionParser_ParsesSupportedModesAndRejectsIncompleteOptions()
    {
        HostOptions options = HostOptionsParser.Parse(
            ["--mode", "compiled", "--headless", "--trace", "trace.json"],
            GuestMode.Interpreted);

        Assert.Equal(GuestMode.Compiled, options.Mode);
        Assert.True(options.Headless);
        Assert.Equal("trace.json", options.TracePath);
        Assert.False(options.IsTracePathHostManaged);
        Assert.False(options.ValidateCompiledOnly);
        Assert.Empty(options.GuestArguments);
        Assert.False(options.Watch);
        Assert.True(HostOptionsParser.Parse(["--watch"], GuestMode.Interpreted).Watch);
        HostOptions fileLaunch = HostOptionsParser.Parse(
            ["document.sharpts", "--", "--literal", "second.txt"], GuestMode.Compiled);
        Assert.Equal(["document.sharpts", "--literal", "second.txt"], fileLaunch.GuestArguments);
        HostOptions compiledOnlyValidation = HostOptionsParser.Parse(
            ["--validate-deps-compiled-only", "publish"], GuestMode.Interpreted);
        Assert.Equal("publish", compiledOnlyValidation.ValidateDepsDirectory);
        Assert.True(compiledOnlyValidation.ValidateCompiledOnly);
        Assert.Throws<ArgumentException>(() =>
            HostOptionsParser.Parse(["--mode"], GuestMode.Interpreted));
        Assert.Throws<ArgumentException>(() =>
            HostOptionsParser.Parse(["--unknown"], GuestMode.Interpreted));
    }

    [Fact]
    public void OptionParser_DisablesOrdinaryTracingAndSupportsBareAndExplicitTracing()
    {
        HostOptions ordinary = HostOptionsParser.Parse([], GuestMode.Interpreted);
        Assert.Null(ordinary.TracePath);
        Assert.False(ordinary.IsTracePathHostManaged);

        HostOptions bare = HostOptionsParser.Parse(["--trace"], GuestMode.Interpreted);
        Assert.StartsWith(HostDiagnosticPaths.TraceDirectory, bare.TracePath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(bare.IsTracePathHostManaged);

        HostOptions explicitPath = HostOptionsParser.Parse(
            ["--trace", "custom.json"], GuestMode.Interpreted);
        Assert.Equal("custom.json", explicitPath.TracePath);
        Assert.False(explicitPath.IsTracePathHostManaged);
        Assert.Throws<ArgumentException>(() =>
            HostOptionsParser.Parse(["--auto-close"], GuestMode.Compiled));
    }

    [Fact]
    public async Task ShutdownCoordinator_DefersAcceptedCloseAndHonorsEarlierCancellation()
    {
        EnsureAvalonia();
        var guest = new RecordingGuestRuntime();
        var posted = new Queue<Action>();
        var exits = new List<int>();
        var failures = new List<Exception>();
        var coordinator = new DesktopShutdownCoordinator(
            () => guest, posted.Enqueue, exits.Add, failures.Add);
        var window = new Window();
        EventHandler<WindowClosingEventArgs> cancelFirst = (_, eventArgs) => eventArgs.Cancel = true;
        window.Closing += cancelFirst;
        coordinator.AttachWindow(window);

        window.Close();

        Assert.False(coordinator.IsShutdownStarted);
        Assert.Empty(posted);
        window.Closing -= cancelFirst;

        window.Close();

        Assert.True(coordinator.IsShutdownStarted);
        Assert.Equal(SharpTSHostedShutdownReason.HostRequested, guest.Reason);
        Assert.Equal(0, guest.ExitCode);
        Assert.Single(posted);
        Assert.Empty(exits);
        posted.Dequeue()();
        await coordinator.Completion;

        Assert.Equal(SharpTSHostedShutdownReason.HostRequested, guest.Reason);
        Assert.Equal(0, guest.ExitCode);
        Assert.Equal([0], exits);
        Assert.Empty(failures);
    }

    [Fact]
    public void ShutdownCoordinator_AllowsWindowOnlyCloseWhenPolicyDeclinesShutdown()
    {
        EnsureAvalonia();
        var posted = new Queue<Action>();
        var coordinator = new DesktopShutdownCoordinator(
            () => null, posted.Enqueue, _ => { }, _ => { });
        var window = new Window();
        coordinator.AttachWindow(window, shouldRequestShutdown: () => false);

        window.Close();

        Assert.False(coordinator.IsShutdownStarted);
        Assert.Empty(posted);
    }

    [Theory]
    [InlineData(SharpTSHostedShutdownReason.StartupFailure, 1)]
    [InlineData(SharpTSHostedShutdownReason.UncaughtError, 1)]
    [InlineData(SharpTSHostedShutdownReason.ProgramCompleted, 7)]
    public async Task ShutdownCoordinator_UsesFirstReasonAndIsIdempotent(
        SharpTSHostedShutdownReason reason,
        int exitCode)
    {
        var guest = new RecordingGuestRuntime();
        var posted = new Queue<Action>();
        var exits = new List<int>();
        var coordinator = new DesktopShutdownCoordinator(
            () => guest, posted.Enqueue, exits.Add, _ => { });

        Assert.True(coordinator.RequestShutdown(reason, exitCode));
        Assert.Equal(1, guest.ShutdownCount);
        Assert.False(coordinator.RequestShutdown(SharpTSHostedShutdownReason.HostRequested, 0));
        Assert.False(coordinator.RequestShutdown(reason, exitCode));
        posted.Dequeue()();
        await coordinator.Completion;

        Assert.Equal(1, guest.ShutdownCount);
        Assert.Equal(reason, guest.Reason);
        Assert.Equal(exitCode, guest.ExitCode);
        Assert.Equal([exitCode], exits);
    }

    [Fact]
    public async Task ShutdownCoordinator_CloseDuringInitializationWaitsForRuntimeShutdown()
    {
        var guest = new RecordingGuestRuntime { DeferShutdown = true };
        var posted = new Queue<Action>();
        var exits = new List<int>();
        var coordinator = new DesktopShutdownCoordinator(
            () => guest, posted.Enqueue, exits.Add, _ => { });

        coordinator.RequestShutdown(SharpTSHostedShutdownReason.HostRequested, 0);
        posted.Dequeue()();
        Assert.Empty(exits);

        guest.CompleteShutdown();
        await coordinator.Completion;

        Assert.Equal([0], exits);
    }

    [Fact]
    public async Task ShutdownCoordinator_EnsuresHostCleanupBeforeStartupFailureExit()
    {
        var order = new List<string>();
        var guest = new RecordingGuestRuntime { OnShutdown = () => order.Add("shutdown") };
        var posted = new Queue<Action>();
        var coordinator = new DesktopShutdownCoordinator(
            () => guest,
            posted.Enqueue,
            _ => order.Add("exit"),
            _ => { },
            () => order.Add("cleanup"));

        coordinator.RequestShutdown(SharpTSHostedShutdownReason.StartupFailure, 1);
        posted.Dequeue()();
        await coordinator.Completion;

        Assert.Equal(["shutdown", "cleanup", "exit"], order);
    }

    [Fact]
    public void PayloadLoader_RejectsEscapingPathsAndAbiMismatches()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-payload-" + Guid.NewGuid().ToString("N"));
        string metadata = Path.Combine(root, ".sharpts");
        Directory.CreateDirectory(metadata);
        try
        {
            string contained = GuiPayloadLoader.ResolvePath(root, "Guest/main.tsx");
            Assert.StartsWith(Path.GetFullPath(root), contained, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<InvalidOperationException>(() =>
                GuiPayloadLoader.ResolvePath(root, "../outside.tsx"));

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = int.MaxValue,
                    GuiApiVersion = 2
                }));
            Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = 1,
                    GuiApiVersion = 2
                }));
            InvalidOperationException rebuild = Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));
            Assert.Contains("rebuild", rebuild.Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = 1,
                    GuiApiVersion = 2,
                    DescriptorSchemaVersion = 99,
                    DescriptorSchemaHash = new string('0', 64)
                }));
            InvalidOperationException schema = Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));
            Assert.Contains("host version 1", schema.Message, StringComparison.Ordinal);
            Assert.Contains("application version 99", schema.Message, StringComparison.Ordinal);
            Assert.Contains(DesktopBridge.DescriptorSchemaHash, schema.Message, StringComparison.Ordinal);

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = 1,
                    GuiApiVersion = 1
                }));
            InvalidOperationException oldApi = Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));
            Assert.Contains("supports GUI API 2", oldApi.Message, StringComparison.Ordinal);
            Assert.Contains("requires GUI API 1", oldApi.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("migrate", oldApi.Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = 1,
                    GuiApiVersion = int.MaxValue
                }));
            Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsDiagnostics_UsesConfiguredLogWithoutOwningHostPolicy()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-diagnostics-" + Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(root, "error.log");
        string? previous = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
        Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", logPath);
        try
        {
            var diagnostics = new WindowsFatalDiagnostics();
            Assert.Equal(logPath, diagnostics.TryWriteLog(new InvalidOperationException("expected failure")));
            Assert.Contains("expected failure", File.ReadAllText(logPath), StringComparison.Ordinal);
            diagnostics.TryWriteLog(new InvalidOperationException("replacement failure"));
            string replacement = File.ReadAllText(logPath);
            Assert.DoesNotContain("expected failure", replacement, StringComparison.Ordinal);
            Assert.Contains("replacement failure", replacement, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", previous);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsDiagnostics_SeparatesAndPrunesDefaultErrorLogs()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-errors-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
        Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", null);
        try
        {
            var diagnostics = new WindowsFatalDiagnostics(root);
            for (int index = 0; index < 14; index++)
            {
                Assert.IsType<string>(
                    diagnostics.TryWriteLog(new InvalidOperationException($"failure-{index}")));
            }

            Assert.Equal(HostDiagnosticPaths.RetainedDefaultErrorCount,
                Directory.EnumerateFiles(root, "sharpts-gui-error-*.log").Count());
            Assert.DoesNotContain(Directory.EnumerateFiles(root), path =>
                path.Contains(Path.Combine("SharpTS.Gui", "Traces"), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", previous);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MacOsDiagnostics_WritesAndPrunesWithoutInvokingPlatformUiOffMac()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-macos-errors-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
        Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", null);
        try
        {
            var diagnostics = new MacOsFatalDiagnostics(root);
            for (int index = 0; index < 12; index++)
            {
                string path = Assert.IsType<string>(
                    diagnostics.TryWriteLog(new InvalidOperationException($"mac-failure-{index}")));
                Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase);
            }

            var dialog = MacOsFatalDiagnostics.CreateDialogStartInfo(
                new InvalidOperationException("quoted \"message\""),
                "/tmp/path with spaces/error.log");
            Assert.Equal("/usr/bin/osascript", dialog.FileName);
            Assert.Equal("--", dialog.ArgumentList[2]);
            Assert.Contains("quoted \"message\"", dialog.ArgumentList[3], StringComparison.Ordinal);
            Assert.Contains("/tmp/path with spaces/error.log", dialog.ArgumentList[3], StringComparison.Ordinal);
            if (!OperatingSystem.IsMacOS())
                diagnostics.TryShowDialog(new InvalidOperationException("not-on-mac"), null);
            Assert.Equal(HostDiagnosticPaths.RetainedDefaultErrorCount,
                Directory.EnumerateFiles(root, "sharpts-gui-error-*.log").Count());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", previous);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TraceRetention_PrunesOnlyManagedTraceNames()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-traces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int index = 0; index < 25; index++)
            {
                string path = Path.Combine(root, $"sharpts-gui-host-test-{index:D2}.json");
                File.WriteAllText(path, "[]");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(index));
            }
            string explicitPath = Path.Combine(root, "explicit.json");
            File.WriteAllText(explicitPath, "[]");

            HostDiagnosticPaths.Prune(
                root,
                "sharpts-gui-host-*.json",
                HostDiagnosticPaths.RetainedDefaultTraceCount);

            Assert.Equal(HostDiagnosticPaths.RetainedDefaultTraceCount,
                Directory.EnumerateFiles(root, "sharpts-gui-host-*.json").Count());
            Assert.True(File.Exists(explicitPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<TestApplication>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }

    private sealed class RecordingGuestRuntime : IGuestRuntime
    {
        private readonly TaskCompletionSource _shutdown = new();

        public bool DeferShutdown { get; init; }
        public Action? OnShutdown { get; init; }
        public int ShutdownCount { get; private set; }
        public SharpTSHostedShutdownReason? Reason { get; private set; }
        public int? ExitCode { get; private set; }
        public SharpTSHostedShutdownReason? ShutdownReason => Reason;

        public Task InitializeAsync() => Task.CompletedTask;
        public void Notify(Action callback) => callback();
        public void QueueMicrotask(Action callback) => callback();

        public Task ShutdownAsync(SharpTSHostedShutdownReason reason, int exitCode)
        {
            ShutdownCount++;
            Reason = reason;
            ExitCode = exitCode;
            OnShutdown?.Invoke();
            return DeferShutdown ? _shutdown.Task : Task.CompletedTask;
        }

        public void CompleteShutdown() => _shutdown.TrySetResult();
        public void Dispose() { }
    }

    private sealed class TestApplication : Application;
}
