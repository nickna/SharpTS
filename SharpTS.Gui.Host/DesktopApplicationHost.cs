#pragma warning disable SHARPTS_HOSTING001

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using SharpTS.Gui;
using SharpTS.Hosting;
using System.Reflection;
using System.Security.Cryptography;

namespace SharpTS.Gui.Host;

internal static class DesktopApplicationHost
{
    public static int Run(HostOptions options, Assembly? embeddedPayloadAssembly)
    {
        int ownerThreadId = Environment.CurrentManagedThreadId;
        var trace = new TraceRecorder(ownerThreadId, enabled: options.TracePath is not null);
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        AppBuilder builder = AppBuilder.Configure<GuiApplication>();
        builder = options.Headless
            ? builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true,
            })
            : builder.UsePlatformDetect();
        builder.SetupWithLifetime(lifetime);
        AvaloniaSynchronizationContext.InstallIfNeeded();

        var dispatcher = Dispatcher.UIThread;
        var hostDispatcher = new AvaloniaHostDispatcher(dispatcher);
        var setupContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Avalonia did not install a SynchronizationContext.");
        SynchronizationContext? hostedContext = null;
        trace.Record("avalonia-setup", detail: setupContext.GetType().FullName);

        IGuestRuntime? guest = null;
        Exception? failure = null;
        ISharpTSScheduledWork? watchdog = null;
        ISharpTSScheduledWork? hotReloadDelay = null;
        Timer? hotReloadPoll = null;
        FileSystemWatcher? hotReloadWatcher = null;
        string? hotReloadEntryPath = null;
        string? hotReloadConfigPath = null;
        bool hotReloadAttempt = false;
        bool hotReloadRunning = false;
        bool guestInitializationComplete = false;
        bool hotReloadChangePending = false;
        string hotReloadFingerprint = string.Empty;
        var hotReloadGate = new object();

        void RecordFailure(Exception exception)
        {
            failure ??= exception;
            try
            {
                Console.Error.WriteLine(exception);
            }
            catch
            {
                // A Windows-subsystem application may not have inherited stderr.
            }
        }

        var shutdown = new DesktopShutdownCoordinator(
            () => guest,
            callback => dispatcher.Post(callback, DispatcherPriority.Send),
            exitCode =>
            {
                trace.Record("host-exit-request", detail: exitCode.ToString());
                lifetime.Shutdown(exitCode);
            },
            RecordFailure,
            DesktopBridge.DisposeAllRoots);
        var hostLifetime = new AvaloniaHostLifetime(exitCode =>
        {
            SharpTSHostedShutdownReason reason = guest?.ShutdownReason
                ?? (exitCode == 0
                    ? SharpTSHostedShutdownReason.ProgramCompleted
                    : SharpTSHostedShutdownReason.UncaughtError);
            shutdown.RequestShutdown(reason, exitCode);
        });

        DesktopRuntimeContext? bridgeContext = null;
        using DesktopRuntimeRegistration bridgeRegistration = DesktopBridge.Configure(trace, (root, window) =>
        {
            if (root.IsMainWindow || lifetime.MainWindow is null)
                lifetime.MainWindow = window;
            if (!options.AutoClose)
            {
                shutdown.AttachWindow(
                    window,
                    () => bridgeContext?.ConsumeCloseCancellation() == true,
                    () => bridgeContext?.ShouldRequestShutdown(root) != false);
            }
            if (root.Owner?.Window is Window owner)
            {
                if (root.IsModal)
                    _ = window.ShowDialog(owner);
                else
                    window.Show(owner);
            }
            else
            {
                window.Show();
            }
        }, options.Headless, callback =>
        {
            if (shutdown.IsShutdownStarted)
                return;
            var currentGuest = guest
                ?? throw new InvalidOperationException("Guest callback arrived before runtime creation.");
            currentGuest.Notify(callback);
        }, callback =>
        {
            if (shutdown.IsShutdownStarted)
                return;
            var currentGuest = guest
                ?? throw new InvalidOperationException("Guest microtask arrived before runtime creation.");
            currentGuest.QueueMicrotask(callback);
        }, exitCode => shutdown.RequestShutdown(
            exitCode == 0 ? SharpTSHostedShutdownReason.HostRequested : SharpTSHostedShutdownReason.UncaughtError,
            exitCode), options.GuestArguments);
        bridgeContext = bridgeRegistration.Context;

        bool clickRaised = false;
        int closing = 0;

        void Fail(
            Exception exception,
            SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.UncaughtError)
        {
            RecordFailure(exception);
            watchdog?.Cancel();
            shutdown.RequestShutdown(reason, 1);
        }

        void ReportHostedError(SharpTSHostedError error)
        {
            if (hotReloadAttempt)
                Console.Error.WriteLine($"SharpTS GUI hot reload rejected: {error.Exception.Message}");
            else
                RecordFailure(error.Exception);
            // HostedInterpreterRuntime owns error shutdown ordering and requests
            // the host exit only after guest cleanup and lifecycle delivery.
        }

        void ReportReloadFailure(Exception exception)
        {
            try { Console.Error.WriteLine($"SharpTS GUI hot reload rejected: {exception.Message}"); }
            catch { }
            trace.Record("hot-reload-rejected", detail: exception.Message);
        }

        async Task ReloadInterpretedGuestAsync()
        {
            if (hotReloadRunning || shutdown.IsShutdownStarted ||
                hotReloadEntryPath is null || hotReloadConfigPath is null)
                return;
            SynchronizationContext reloadContext = SynchronizationContext.Current
                ?? throw new InvalidOperationException("Hot reload did not start on the Avalonia dispatcher.");
            hotReloadRunning = true;
            try
            {
                try
                {
                    InterpretedGuestRuntime.ValidateProgram(hotReloadEntryPath, hotReloadConfigPath);
                }
                catch (Exception exception)
                {
                    ReportReloadFailure(exception);
                    return;
                }

                trace.Record("hot-reload-begin");
                DesktopBridge.DisposeAllRoots();
                IGuestRuntime? previous = guest;
                guest = null;
                previous?.Dispose();

                var reloadLifetime = new AvaloniaHostLifetime(exitCode =>
                {
                    if (!hotReloadAttempt)
                        hostLifetime.RequestExit(exitCode);
                });
                var next = new InterpretedGuestRuntime(
                    hotReloadEntryPath,
                    hotReloadConfigPath,
                    hostDispatcher,
                    reloadLifetime,
                    new DelegateHostedErrorSink(ReportHostedError));
                guest = next;
                hotReloadAttempt = true;
                try
                {
                    await next.InitializeAsync();
                }
                finally
                {
                    hotReloadAttempt = false;
                }
                AssertSynchronizationContext(reloadContext);
                if (bridgeRegistration.Context.CurrentRoot?.Window is null)
                    throw new InvalidOperationException("Reloaded guest did not mount a Window.");
                trace.Record("hot-reload-end");
                if (options.AutoClose)
                {
                    watchdog?.Cancel();
                    shutdown.RequestShutdown(SharpTSHostedShutdownReason.HostRequested, 0);
                }
            }
            catch (Exception exception)
            {
                hotReloadAttempt = false;
                try { DesktopBridge.DisposeAllRoots(); } catch { }
                try { guest?.Dispose(); } catch { }
                guest = null;
                ReportReloadFailure(exception);
            }
            finally
            {
                hotReloadRunning = false;
                if (hotReloadChangePending && !shutdown.IsShutdownStarted)
                    QueueHotReload();
            }
        }

        void QueueHotReload()
        {
            if (shutdown.IsShutdownStarted)
                return;
            hotReloadChangePending = false;
            hotReloadDelay?.Cancel();
            hotReloadDelay?.Dispose();
            hotReloadDelay = hostDispatcher.Schedule(
                TimeSpan.FromMilliseconds(175),
                () => _ = ReloadInterpretedGuestAsync());
        }

        void NotifyHotReloadChange(string detail, bool ownerThread)
        {
            trace.Record("hot-reload-change", detail: detail, requireOwnerThread: ownerThread);
            if (ownerThread)
            {
                hotReloadChangePending = true;
                if (guestInitializationComplete)
                    QueueHotReload();
            }
            else
            {
                dispatcher.Post(() =>
                {
                    hotReloadChangePending = true;
                    if (guestInitializationComplete)
                        QueueHotReload();
                }, DispatcherPriority.Background);
            }
        }

        void PollHotReload(string root)
        {
            if (shutdown.IsShutdownStarted)
                return;
            try
            {
                string next = WatchFingerprint(root);
                bool changed;
                lock (hotReloadGate)
                {
                    changed = !string.Equals(next, hotReloadFingerprint, StringComparison.Ordinal);
                    if (changed)
                        hotReloadFingerprint = next;
                }
                if (changed)
                    NotifyHotReloadChange("poll", ownerThread: false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"SharpTS GUI hot-reload polling warning: {exception.Message}");
            }
        }

        void StartHotReloadWatcher(string root)
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            void Changed(object? sender, FileSystemEventArgs args)
            {
                string extension = Path.GetExtension(args.FullPath);
                if (extension is not (".ts" or ".tsx") || IsIgnoredWatchPath(root, args.FullPath))
                    return;
                NotifyHotReloadChange(args.FullPath, ownerThread: false);
            }
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += Changed;
            watcher.EnableRaisingEvents = true;
            hotReloadWatcher = watcher;
            lock (hotReloadGate)
                hotReloadFingerprint = WatchFingerprint(root);
            hotReloadPoll = new Timer(
                _ => PollHotReload(root), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
            trace.Record("hot-reload-watch", detail: root);
        }

        void CompleteAutoClose()
        {
            watchdog?.Cancel();
            shutdown.RequestShutdown(SharpTSHostedShutdownReason.HostRequested, 0);
        }

        void CheckAutoClose()
        {
            if (!options.AutoClose || Volatile.Read(ref closing) != 0)
                return;
            if (trace.Contains("guest-click") &&
                trace.Contains("reactive-update-complete") &&
                trace.Contains("guest-timer") &&
                trace.Contains("guest-async-resume") &&
                trace.Contains("dispatcher-sentinel") &&
                Interlocked.Exchange(ref closing, 1) == 0)
            {
                dispatcher.Post(CompleteAutoClose, DispatcherPriority.Background);
            }
        }

        void RaiseClickAfterReactiveCommit()
        {
            if (!options.AutoClose ||
                !trace.Contains("reactive-update-complete") ||
                !trace.Contains("guest-init-end"))
                return;
            if (Interlocked.Exchange(ref clickRaised, true) == false)
            {
                dispatcher.Post(() =>
                {
                    if (bridgeRegistration.Context.CurrentRoot is not null)
                        bridgeRegistration.Context.RaiseFirstButtonClick();
                }, DispatcherPriority.Background);
            }
        }

        trace.Recorded += _ =>
        {
            RaiseClickAfterReactiveCommit();
            CheckAutoClose();
        };

        dispatcher.Post(async () =>
        {
            try
            {
                hostedContext = SynchronizationContext.Current
                    ?? throw new InvalidOperationException("Avalonia dispatcher has no SynchronizationContext.");
                trace.Record("dispatcher-context-captured", detail: hostedContext.GetType().FullName);
                AssertSynchronizationContext(hostedContext);
                trace.Record("guest-init-begin", detail: options.Mode.ToString());
                string baseDirectory = AppContext.BaseDirectory;
                GuiAppManifest manifest = embeddedPayloadAssembly is null
                    ? GuiPayloadLoader.LoadFile(baseDirectory)
                    : GuiPayloadLoader.LoadEmbedded(embeddedPayloadAssembly);
                string interpretedEntryPath = GuiPayloadLoader.ResolvePath(baseDirectory, manifest.EntryPath);
                string interpretedConfigPath = Path.Combine(baseDirectory, ".sharpts", "tsconfig.json");
                if (options.Watch)
                {
                    interpretedEntryPath = ResolveDevelopmentEntry(Environment.CurrentDirectory, manifest.EntryPath);
                    hotReloadEntryPath = interpretedEntryPath;
                    hotReloadConfigPath = interpretedConfigPath;
                    StartHotReloadWatcher(Environment.CurrentDirectory);
                }
                guest = options.Mode switch
                {
                    GuestMode.Interpreted => new InterpretedGuestRuntime(
                        interpretedEntryPath,
                        interpretedConfigPath,
                        hostDispatcher,
                        hostLifetime,
                        new DelegateHostedErrorSink(ReportHostedError)),
                    GuestMode.Compiled => embeddedPayloadAssembly is null
                        ? new CompiledGuestRuntime(
                            GuiPayloadLoader.ResolvePath(baseDirectory, manifest.CompiledAssembly),
                            hostDispatcher,
                            hostLifetime,
                            new DelegateHostedErrorSink(ReportHostedError))
                        : new CompiledGuestRuntime(
                            GuiPayloadLoader.ReadEmbeddedResource(embeddedPayloadAssembly, manifest.CompiledAssembly),
                            hostDispatcher,
                            hostLifetime,
                            new DelegateHostedErrorSink(ReportHostedError)),
                    _ => throw new ArgumentOutOfRangeException()
                };
                await guest.InitializeAsync();
                guestInitializationComplete = true;
                AssertSynchronizationContext(hostedContext);
                if (bridgeRegistration.Context.CurrentRoot?.Window is null)
                    throw new InvalidOperationException("Guest initialization returned without mounting a Window.");
                trace.Record("guest-init-end");
                if (hotReloadChangePending)
                    QueueHotReload();
                dispatcher.Post(() => trace.Record("dispatcher-sentinel"), DispatcherPriority.Background);
                if (options.AutoClose)
                {
                    watchdog = hostDispatcher.Schedule(TimeSpan.FromSeconds(15), () =>
                        Fail(new TimeoutException("SharpTS GUI auto-close scenario timed out after 15 seconds.")));
                }
            }
            catch (Exception exception)
            {
                if (!shutdown.IsShutdownStarted)
                    Fail(exception, SharpTSHostedShutdownReason.StartupFailure);
            }
        }, DispatcherPriority.Send);

        try
        {
            lifetime.Start(args: []);
        }
        catch (Exception exception)
        {
            failure ??= exception;
            Console.Error.WriteLine(exception);
        }
        finally
        {
            watchdog?.Cancel();
            hotReloadDelay?.Cancel();
            hotReloadDelay?.Dispose();
            hotReloadPoll?.Dispose();
            hotReloadWatcher?.Dispose();
            try
            {
                DesktopBridge.DisposeAllRoots();
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }
            try
            {
                guest?.Dispose();
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
            }
            finally
            {
                trace.Record("runtime-dispose");
            }
        }

        string? writtenTracePath = TryWriteTrace(trace, options);

        if (failure == null && options.AutoClose && !options.Watch)
        {
            var validationFailures = trace.ValidateRequiredStages(options.Headless);
            if (validationFailures.Count > 0)
            {
                foreach (string validationFailure in validationFailures)
                    Console.Error.WriteLine(validationFailure);
                failure = new InvalidOperationException(
                    "SharpTS GUI conformance trace validation failed.");
            }
        }

        if (writtenTracePath is not null)
            Console.WriteLine($"SharpTS GUI {options.Mode} trace: {writtenTracePath}");
        if (failure is null)
            return 0;

        FatalDiagnostics.Report(failure, !options.Headless && !options.AutoClose);
        return 1;
    }

    private static string? TryWriteTrace(TraceRecorder trace, HostOptions options)
    {
        if (options.TracePath is null)
            return null;

        try
        {
            string path = Path.GetFullPath(options.TracePath);
            trace.WriteJson(path);
            if (options.IsTracePathHostManaged)
            {
                HostDiagnosticPaths.Prune(
                    Path.GetDirectoryName(path)!,
                    "sharpts-gui-host-*.json",
                    HostDiagnosticPaths.RetainedDefaultTraceCount);
            }
            return path;
        }
        catch (Exception exception)
        {
            try
            {
                Console.Error.WriteLine($"SharpTS GUI trace could not be written: {exception.Message}");
            }
            catch
            {
                // Trace diagnostics must not destabilize a Windows-subsystem application.
            }
            return null;
        }
    }

    private static void AssertSynchronizationContext(SynchronizationContext expected)
    {
        if (!ReferenceEquals(expected, SynchronizationContext.Current))
        {
            throw new InvalidOperationException(
                $"Avalonia SynchronizationContext changed from '{expected.GetType().FullName}' to " +
                $"'{SynchronizationContext.Current?.GetType().FullName ?? "<null>"}'.");
        }
    }

    private static string ResolveDevelopmentEntry(string root, string manifestEntry)
    {
        string relative = manifestEntry.Replace('/', Path.DirectorySeparatorChar);
        string prefix = "Guest" + Path.DirectorySeparatorChar;
        string withoutGuest = relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? relative[prefix.Length..]
            : relative;
        string direct = Path.GetFullPath(withoutGuest, root);
        if (File.Exists(direct))
            return direct;
        string packagedLayout = Path.GetFullPath(relative, root);
        if (File.Exists(packagedLayout))
            return packagedLayout;
        throw new FileNotFoundException(
            $"SharpTS GUI watch entry '{withoutGuest}' was not found under '{root}'.", direct);
    }

    private static bool IsIgnoredWatchPath(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.Split('/').Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".sharpts", StringComparison.OrdinalIgnoreCase));
    }

    private static string WatchFingerprint(string root) =>
        string.Join("|", Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".ts" or ".tsx")
            .Where(path => !IsIgnoredWatchPath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                string contentHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return $"{Path.GetRelativePath(root, path)}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{contentHash}";
            }));

}
