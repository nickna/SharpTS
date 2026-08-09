#pragma warning disable SHARPTS_HOSTING001

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using SharpTS.Gui;
using SharpTS.Hosting;
using System.Reflection;

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
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions())
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
            DesktopBridge.DisposeCurrentRoot);
        var hostLifetime = new AvaloniaHostLifetime(exitCode =>
        {
            SharpTSHostedShutdownReason reason = guest?.ShutdownReason
                ?? (exitCode == 0
                    ? SharpTSHostedShutdownReason.ProgramCompleted
                    : SharpTSHostedShutdownReason.UncaughtError);
            shutdown.RequestShutdown(reason, exitCode);
        });

        DesktopRuntimeContext? bridgeContext = null;
        using DesktopRuntimeRegistration bridgeRegistration = DesktopBridge.Configure(trace, window =>
        {
            lifetime.MainWindow = window;
            if (!options.AutoClose)
            {
                shutdown.AttachWindow(
                    window,
                    () => bridgeContext?.ConsumeCloseCancellation() == true);
            }
            window.Show();
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
        });
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
            RecordFailure(error.Exception);
            // HostedInterpreterRuntime owns error shutdown ordering and requests
            // the host exit only after guest cleanup and lifecycle delivery.
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
                guest = options.Mode switch
                {
                    GuestMode.Interpreted => new InterpretedGuestRuntime(
                        GuiPayloadLoader.ResolvePath(baseDirectory, manifest.EntryPath),
                        Path.Combine(baseDirectory, ".sharpts", "tsconfig.json"),
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
                AssertSynchronizationContext(hostedContext);
                if (bridgeRegistration.Context.CurrentRoot?.Window is null)
                    throw new InvalidOperationException("Guest initialization returned without mounting a Window.");
                trace.Record("guest-init-end");
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
            try
            {
                DesktopBridge.DisposeCurrentRoot();
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

        if (failure == null && options.AutoClose)
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

}
