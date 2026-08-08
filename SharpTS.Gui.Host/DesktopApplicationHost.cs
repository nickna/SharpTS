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
        var trace = new TraceRecorder(ownerThreadId);
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
        var hostLifetime = new AvaloniaHostLifetime(lifetime);
        var setupContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Avalonia did not install a SynchronizationContext.");
        SynchronizationContext? hostedContext = null;
        trace.Record("avalonia-setup", detail: setupContext.GetType().FullName);

        IGuestRuntime? guest = null;
        using DesktopRuntimeRegistration bridgeRegistration = DesktopBridge.Configure(trace, window =>
        {
            lifetime.MainWindow = window;
            if (!options.AutoClose)
                window.Closed += (_, _) => lifetime.Shutdown();
            window.Show();
        }, options.Headless, callback =>
        {
            var currentGuest = guest
                ?? throw new InvalidOperationException("Guest callback arrived before runtime creation.");
            currentGuest.Notify(callback);
        }, callback =>
        {
            var currentGuest = guest
                ?? throw new InvalidOperationException("Guest microtask arrived before runtime creation.");
            currentGuest.QueueMicrotask(callback);
        });

        Exception? failure = null;
        bool clickRaised = false;
        int closing = 0;
        ISharpTSScheduledWork? watchdog = null;

        void Fail(Exception exception)
        {
            failure ??= exception;
            Console.Error.WriteLine(exception);
            watchdog?.Cancel();
            lifetime.Shutdown(1);
        }

        void ReportHostedError(SharpTSHostedError error)
        {
            failure ??= error.Exception;
            Console.Error.WriteLine(error.Exception);
            // HostedInterpreterRuntime owns error shutdown ordering and requests
            // the host exit only after guest cleanup and lifecycle delivery.
        }

        async void CompleteAutoClose()
        {
            try
            {
                watchdog?.Cancel();
                if (guest != null)
                    await guest.ShutdownAsync();
                lifetime.Shutdown(0);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
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
                Fail(exception);
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
            guest?.Dispose();
            trace.WriteJson(options.TracePath);
        }

        if (failure == null && options.AutoClose)
        {
            var validationFailures = trace.ValidateRequiredStages(options.Headless);
            if (validationFailures.Count > 0)
            {
                foreach (string validationFailure in validationFailures)
                    Console.Error.WriteLine(validationFailure);
                return 1;
            }
        }

        Console.WriteLine($"SharpTS GUI {options.Mode} trace: {Path.GetFullPath(options.TracePath)}");
        if (failure is null)
            return 0;

        FatalDiagnostics.Report(failure, !options.Headless && !options.AutoClose);
        return 1;
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
