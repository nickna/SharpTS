using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpTS.Gui;

internal sealed class DesktopRuntimeContext
{
    private const int MaximumImagePayloadBytes = 25 * 1024 * 1024;
    private readonly Action<DesktopRoot, Window> _showWindow;
    private readonly Action<Action> _postGuestCallback;
    private readonly Action<Action> _invokeGuestCallback;
    private readonly Action<Action> _scheduleGuestMicrotask;
    private readonly Action<int> _requestShutdown;
    private readonly IDesktopInteractionServices _interactionServices;
    private readonly string[] _launchArguments;
    private readonly bool _headless;
    private readonly List<DesktopRoot> _roots = [];
    private readonly List<DesktopTrayIcon> _trayIcons = [];
    private readonly List<Action> _desktopServiceIdleCallbacks = [];
    private DesktopApplicationSession? _application;
    private int _pendingDesktopServices;
    private bool _cancelNextWindowClose;

    public DesktopRuntimeContext(
        TraceRecorder recorder,
        Action<DesktopRoot, Window> showWindow,
        bool headless,
        Action<Action> postGuestCallback,
        Action<Action> invokeGuestCallback,
        Action<Action> scheduleGuestMicrotask,
        Action<int> requestShutdown,
        string[] launchArguments,
        IDesktopInteractionServices interactionServices)
    {
        Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
        _postGuestCallback = postGuestCallback
            ?? throw new ArgumentNullException(nameof(postGuestCallback));
        _invokeGuestCallback = invokeGuestCallback
            ?? throw new ArgumentNullException(nameof(invokeGuestCallback));
        _scheduleGuestMicrotask = scheduleGuestMicrotask
            ?? throw new ArgumentNullException(nameof(scheduleGuestMicrotask));
        _requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
        _launchArguments = launchArguments?.ToArray() ?? throw new ArgumentNullException(nameof(launchArguments));
        _interactionServices = interactionServices
            ?? throw new ArgumentNullException(nameof(interactionServices));
        _headless = headless;
        EnsureOwnerThread();
    }

    public TraceRecorder Recorder { get; }
    public bool IsHeadless => _headless;
    public IDesktopInteractionServices InteractionServices => _interactionServices;
    public DesktopRoot? CurrentRoot =>
        _roots.FirstOrDefault(root => root.IsMainWindow) ?? _roots.LastOrDefault();

    public IReadOnlyList<DesktopRoot> Roots => _roots;
    public string[] GetLaunchArguments() => _launchArguments.ToArray();

    public string GetDisplaysJson()
    {
        EnsureOwnerThread();
        Window window = CurrentRoot?.Window
            ?? throw new InvalidOperationException("No desktop Window is mounted.");
        DisplayInfo[] displays = window.Screens.All.Select(screen =>
        {
            double scaling = NormalizeScaling(screen.Scaling);
            return new DisplayInfo(
                screen.DisplayName ?? string.Empty,
                screen.IsPrimary,
                scaling,
                screen.CurrentOrientation.ToString().ToLowerInvariant(),
                new DisplayBounds(
                    screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                new DisplayBounds(
                    screen.WorkingArea.X, screen.WorkingArea.Y,
                    screen.WorkingArea.Width, screen.WorkingArea.Height),
                new DisplaySize(screen.Bounds.Width / scaling, screen.Bounds.Height / scaling),
                new DisplaySize(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling));
        }).ToArray();
        return JsonSerializer.Serialize(displays, DisplayJsonContext.Default.DisplayInfoArray);
    }

    internal static string GetWindowMetricsJson(Window window)
    {
        Avalonia.Platform.Screen? screen = window.Screens.ScreenFromWindow(window)
            ?? window.Screens.Primary
            ?? window.Screens.All.FirstOrDefault();
        double scaling = NormalizeScaling(screen?.Scaling ?? window.RenderScaling);
        PixelRect workingArea = screen?.WorkingArea ?? new PixelRect(
            0, 0,
            Math.Max(1, (int)Math.Round(window.ClientSize.Width * scaling)),
            Math.Max(1, (int)Math.Round(window.ClientSize.Height * scaling)));
        var metrics = new WindowMetricsInfo(
            window.ClientSize.Width,
            window.ClientSize.Height,
            scaling,
            window.WindowState switch
            {
                Avalonia.Controls.WindowState.Minimized => "minimized",
                Avalonia.Controls.WindowState.Maximized => "maximized",
                Avalonia.Controls.WindowState.FullScreen => "fullScreen",
                _ => "normal",
            },
            screen?.DisplayName ?? string.Empty,
            screen?.IsPrimary ?? true,
            workingArea.Width / scaling,
            workingArea.Height / scaling,
            new DisplayBounds(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height));
        return JsonSerializer.Serialize(metrics, DisplayJsonContext.Default.WindowMetricsInfo);
    }

    private static double NormalizeScaling(double scaling) =>
        double.IsFinite(scaling) && scaling > 0 ? scaling : 1;

    public DesktopApplicationSession CreateApplication(string shutdownMode)
    {
        EnsureOwnerThread();
        if (_application is not null || _roots.Count != 0)
            throw new InvalidOperationException("Only one active desktop application is permitted per guest runtime.");

        _application = new DesktopApplicationSession(this, shutdownMode);
        return _application;
    }

    internal DesktopRoot CreateApplicationRoot(
        DesktopApplicationSession application,
        Action reactiveCleanup,
        DesktopRoot? owner,
        bool modal,
        bool mainWindow)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(reactiveCleanup);
        if (!ReferenceEquals(application, _application) || application.IsDisposed)
            throw new ObjectDisposedException(nameof(DesktopApplicationSession));
        if (owner is not null && (!ReferenceEquals(owner.Application, application) || owner.IsDisposed))
            throw new ArgumentException("The owner must be an active window from the same desktop application.", nameof(owner));
        if (modal && owner is null)
            throw new ArgumentException("A modal window requires an owner.", nameof(owner));
        if (mainWindow && _roots.Any(root => ReferenceEquals(root.Application, application) && root.IsMainWindow))
            throw new InvalidOperationException("The desktop application already has a main window.");

        bool effectiveMainWindow = mainWindow || !_roots.Any(root => ReferenceEquals(root.Application, application));
        var root = new DesktopRoot(
            Recorder,
            _showWindow,
            _postGuestCallback,
            _invokeGuestCallback,
            _scheduleGuestMicrotask,
            _headless,
            reactiveCleanup,
            ReleaseRoot,
            application,
            owner,
            modal,
            effectiveMainWindow);
        _roots.Add(root);
        return root;
    }

    public void DisposeAllRoots()
    {
        EnsureOwnerThread();
        _application?.Dispose();
        foreach (DesktopRoot root in _roots.ToArray().Reverse())
            root.Dispose();
    }

    internal void DisposeApplication(DesktopApplicationSession application)
    {
        EnsureOwnerThread();
        foreach (DesktopTrayIcon trayIcon in _trayIcons.Where(icon => ReferenceEquals(icon.Application, application)).ToArray().Reverse())
            trayIcon.Dispose();
        foreach (DesktopRoot root in _roots.Where(root => ReferenceEquals(root.Application, application)).ToArray().Reverse())
            root.Dispose();
        if (ReferenceEquals(_application, application))
            _application = null;
    }

    public bool ShouldRequestShutdown(DesktopRoot closingRoot)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(closingRoot);
        return closingRoot.Application?.ShouldRequestShutdown(closingRoot) ?? true;
    }

    public void RequestShutdown(int exitCode)
    {
        EnsureOwnerThread();
        _requestShutdown(exitCode);
    }

    public void ScheduleGuestMicrotask(Action callback)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(callback);
        _scheduleGuestMicrotask(callback);
    }

    public void CancelNextWindowClose()
    {
        EnsureOwnerThread();
        _cancelNextWindowClose = true;
    }

    public bool ConsumeCloseCancellation()
    {
        EnsureOwnerThread();
        bool cancel = _cancelNextWindowClose;
        _cancelNextWindowClose = false;
        return cancel;
    }

    public void DispatchGuestCallback(Action callback) => _postGuestCallback(callback);

    public void InvokeGuestCallback(Action callback) => _invokeGuestCallback(callback);

    public Task<T> ScheduleDesktopService<T>(Func<Task<T>> operation)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(operation);
        // Completion occurs in a deferred dispatcher turn, so inline Task continuations are safe
        // here and preserve ordering before Headless idle checkpoints are released.
        var completion = new TaskCompletionSource<T>();
        _pendingDesktopServices++;
        try
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    completion.TrySetResult(await operation());
                }
                catch (OperationCanceledException cancellation)
                {
                    completion.TrySetCanceled(cancellation.CancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    CompleteDesktopService();
                }
            }, DispatcherPriority.Normal);
        }
        catch
        {
            _pendingDesktopServices--;
            throw;
        }
        return completion.Task;
    }

    public Task ScheduleDesktopService(Func<Task> operation) =>
        ScheduleDesktopService(async () =>
        {
            await operation();
            return true;
        });

    public void AfterDesktopServices(Action callback)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(callback);
        if (_pendingDesktopServices == 0)
            DispatchGuestCallback(callback);
        else
            _desktopServiceIdleCallbacks.Add(callback);
    }

    public void PostGuestIdleProbe(Action callback)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(callback);
        Dispatcher.UIThread.Post(
            () => DispatchGuestCallback(callback),
            DispatcherPriority.Background);
    }

    private void CompleteDesktopService()
    {
        EnsureOwnerThread();
        if (_pendingDesktopServices <= 0)
            throw new InvalidOperationException("Desktop service accounting underflowed.");
        _pendingDesktopServices--;
        if (_pendingDesktopServices != 0 || _desktopServiceIdleCallbacks.Count == 0)
            return;
        Action[] callbacks = _desktopServiceIdleCallbacks.ToArray();
        _desktopServiceIdleCallbacks.Clear();
        // Let Task/Promise continuations produced by the completed service reach the guest
        // scheduler before releasing Headless assertions that observe the resulting render.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (Action callback in callbacks)
                DispatchGuestCallback(callback);
        }, DispatcherPriority.Background);
    }

    public Window RequireWindowForServices()
    {
        EnsureOwnerThread();
        if (_headless && !_interactionServices.SupportsHeadless)
            throw new InvalidOperationException("Desktop dialogs and clipboard are unavailable in Headless mode.");
        return CurrentRoot?.Window
            ?? throw new InvalidOperationException("A mounted desktop root is required for this service.");
    }

    internal DesktopTrayIcon CreateTrayIcon(
        DesktopApplicationSession application,
        string icon,
        string toolTip,
        string menuJson,
        Action? clicked,
        Action<string>? menuClicked)
    {
        EnsureOwnerThread();
        if (!ReferenceEquals(application, _application) || application.IsDisposed)
            throw new ObjectDisposedException(nameof(DesktopApplicationSession));
        var handle = new DesktopTrayIcon(
            this, application, _headless, icon, toolTip, menuJson, clicked, menuClicked, ReleaseTrayIcon);
        _trayIcons.Add(handle);
        return handle;
    }

    public Bitmap LoadImage(string source)
    {
        EnsureOwnerThread();
        using Stream stream = OpenImageStream(source);
        return new Bitmap(stream);
    }

    internal Stream OpenImageStream(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.StartsWith("asset:///", StringComparison.OrdinalIgnoreCase))
        {
            string logicalName = source[9..].Replace('\\', '/');
            Stream stream = Assembly.GetEntryAssembly()?.GetManifestResourceStream($"SharpTS.Gui.Asset/{logicalName}")
                ?? throw new FileNotFoundException($"Packaged GUI asset '{logicalName}' was not found.");
            return RequireBoundedImageStream(stream);
        }
        const string dataPrefix = "data:image/png;base64,";
        if (source.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string encoded = source[dataPrefix.Length..];
            if (encoded.Length > 35_000_000)
                throw new InvalidDataException("Embedded PNG data exceeds the 25 MiB limit.");
            byte[] bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > MaximumImagePayloadBytes)
                throw new InvalidDataException("Embedded PNG data exceeds the 25 MiB limit.");
            return new MemoryStream(bytes, writable: false);
        }

        string path = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile
            ? uri.LocalPath
            : source;
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path, AppContext.BaseDirectory);
        return RequireBoundedImageStream(File.OpenRead(path));
    }

    private static Stream RequireBoundedImageStream(Stream stream)
    {
        try
        {
            if (!stream.CanSeek || stream.Length > MaximumImagePayloadBytes)
                throw new InvalidDataException("Image payloads are limited to 25 MiB.");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public WindowIcon LoadWindowIcon(string source)
    {
        EnsureOwnerThread();
        if (source.StartsWith("asset:///", StringComparison.OrdinalIgnoreCase))
        {
            string logicalName = source[9..].Replace('\\', '/');
            Stream stream = Assembly.GetEntryAssembly()?.GetManifestResourceStream($"SharpTS.Gui.Asset/{logicalName}")
                ?? throw new FileNotFoundException($"Packaged GUI asset '{logicalName}' was not found.");
            using (stream)
                return new WindowIcon(stream);
        }

        string path = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile
            ? uri.LocalPath
            : source;
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path, AppContext.BaseDirectory);
        return new WindowIcon(path);
    }

    public void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != Recorder.OwnerThreadId)
        {
            throw new InvalidOperationException(
                $"Avalonia bridge ran on managed thread {Environment.CurrentManagedThreadId}; " +
                $"owner is {Recorder.OwnerThreadId}.");
        }
    }

    private void ReleaseRoot(DesktopRoot root)
    {
        EnsureOwnerThread();
        _roots.Remove(root);
    }

    private void ReleaseTrayIcon(DesktopTrayIcon trayIcon)
    {
        EnsureOwnerThread();
        _trayIcons.Remove(trayIcon);
    }

    internal int CountApplicationRoots(DesktopApplicationSession application) =>
        _roots.Count(root => ReferenceEquals(root.Application, application) && !root.IsDisposed);
}

internal sealed record DisplayInfo(
    string Name,
    bool IsPrimary,
    double Scaling,
    string Orientation,
    DisplayBounds Bounds,
    DisplayBounds WorkingArea,
    DisplaySize BoundsSize,
    DisplaySize WorkingAreaSize);
internal sealed record DisplayBounds(double X, double Y, double Width, double Height);
internal sealed record DisplaySize(double Width, double Height);
internal sealed record WindowMetricsInfo(
    double ClientWidth,
    double ClientHeight,
    double Scaling,
    string WindowState,
    string DisplayName,
    bool IsPrimary,
    double WorkingAreaWidth,
    double WorkingAreaHeight,
    DisplayBounds PixelWorkingArea);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DisplayInfo[]))]
[JsonSerializable(typeof(WindowMetricsInfo))]
internal sealed partial class DisplayJsonContext : JsonSerializerContext;

public sealed class DesktopApplicationSession : IDisposable
{
    private readonly DesktopRuntimeContext _context;
    private readonly string _shutdownMode;
    private bool _disposed;
    private DesktopStyleResources? _styleResources;

    internal DesktopApplicationSession(DesktopRuntimeContext context, string shutdownMode)
    {
        _context = context;
        _shutdownMode = shutdownMode switch
        {
            "onLastWindowClose" or "onMainWindowClose" or "explicit" => shutdownMode,
            _ => throw new ArgumentException($"Unsupported desktop shutdown mode '{shutdownMode}'.", nameof(shutdownMode)),
        };
    }

    public bool IsDisposed => _disposed;
    public int WindowCount => _disposed ? 0 : _context.CountApplicationRoots(this);

    internal DesktopTrayIcon CreateTrayIcon(
        string icon,
        string toolTip,
        string menuJson,
        Action? clicked,
        Action<string>? menuClicked)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _context.CreateTrayIcon(this, icon, toolTip, menuJson, clicked, menuClicked);
    }

    internal DesktopRoot CreateWindowRoot(Action reactiveCleanup, DesktopRoot? owner, bool modal, bool mainWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _context.CreateApplicationRoot(this, reactiveCleanup, owner, modal, mainWindow);
    }

    internal void ConfigureStyleResources(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (WindowCount != 0)
            throw new InvalidOperationException("Desktop resources and styles must be configured before creating a window.");
        _styleResources = DesktopStyleResources.Parse(json);
    }

    internal void ApplyStyleResources(Window window) => _styleResources?.Apply(window);

    internal object? FindResource(DesktopRoot root, string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(root.Application, this) || root.IsDisposed)
            throw new ArgumentException("The window must be active and belong to this desktop application.", nameof(root));
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return root.Window?.TryFindResource(key, out object? value) == true ? value : null;
    }

    public void Shutdown(int exitCode = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.RequestShutdown(exitCode);
    }

    internal bool ShouldRequestShutdown(DesktopRoot closingRoot) => _shutdownMode switch
    {
        "onLastWindowClose" => _context.CountApplicationRoots(this) <= 1,
        "onMainWindowClose" => closingRoot.IsMainWindow,
        "explicit" => false,
        _ => throw new UnreachableException(),
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _context.DisposeApplication(this);
    }
}

internal sealed class DesktopRuntimeRegistration(
    DesktopRuntimeContext context,
    Action<DesktopRuntimeContext> release) : IDisposable
{
    private int _disposed;

    public DesktopRuntimeContext Context { get; } = context;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Context.DisposeAllRoots();
        release(Context);
    }
}
