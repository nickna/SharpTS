using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SharpTS.Gui;

internal sealed class DesktopRuntimeContext
{
    private readonly Action<DesktopRoot, Window> _showWindow;
    private readonly Action<Action> _dispatchGuestCallback;
    private readonly Action<Action> _scheduleGuestMicrotask;
    private readonly Action<int> _requestShutdown;
    private readonly string[] _launchArguments;
    private readonly bool _headless;
    private readonly List<DesktopRoot> _roots = [];
    private readonly List<DesktopTrayIcon> _trayIcons = [];
    private DesktopApplicationSession? _application;
    private bool _cancelNextWindowClose;

    public DesktopRuntimeContext(
        TraceRecorder recorder,
        Action<DesktopRoot, Window> showWindow,
        bool headless,
        Action<Action> dispatchGuestCallback,
        Action<Action> scheduleGuestMicrotask,
        Action<int> requestShutdown,
        string[] launchArguments)
    {
        Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
        _dispatchGuestCallback = dispatchGuestCallback
            ?? throw new ArgumentNullException(nameof(dispatchGuestCallback));
        _scheduleGuestMicrotask = scheduleGuestMicrotask
            ?? throw new ArgumentNullException(nameof(scheduleGuestMicrotask));
        _requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
        _launchArguments = launchArguments?.ToArray() ?? throw new ArgumentNullException(nameof(launchArguments));
        _headless = headless;
        EnsureOwnerThread();
    }

    public TraceRecorder Recorder { get; }
    public bool IsHeadless => _headless;
    public DesktopRoot? CurrentRoot =>
        _roots.FirstOrDefault(root => root.IsMainWindow) ?? _roots.LastOrDefault();

    public IReadOnlyList<DesktopRoot> Roots => _roots;
    public string[] GetLaunchArguments() => _launchArguments.ToArray();

    public string GetDisplaysJson()
    {
        EnsureOwnerThread();
        Window window = CurrentRoot?.Window
            ?? throw new InvalidOperationException("No desktop Window is mounted.");
        return JsonSerializer.Serialize(window.Screens.All.Select(screen => new
        {
            name = screen.DisplayName ?? string.Empty,
            isPrimary = screen.IsPrimary,
            scaling = screen.Scaling,
            orientation = screen.CurrentOrientation.ToString().ToLowerInvariant(),
            bounds = new
            {
                x = screen.Bounds.X,
                y = screen.Bounds.Y,
                width = screen.Bounds.Width,
                height = screen.Bounds.Height,
            },
            workingArea = new
            {
                x = screen.WorkingArea.X,
                y = screen.WorkingArea.Y,
                width = screen.WorkingArea.Width,
                height = screen.WorkingArea.Height,
            },
        }));
    }

    public DesktopRoot CreateRoot(Action reactiveCleanup)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(reactiveCleanup);
        if (_roots.Count != 0 || _application is not null)
            throw new InvalidOperationException("Only one active desktop Window root is permitted per application.");

        var root = new DesktopRoot(
            Recorder,
            _showWindow,
            _dispatchGuestCallback,
            _headless,
            reactiveCleanup,
            ReleaseRoot,
            application: null,
            owner: null,
            isModal: false,
            isMainWindow: true);
        _roots.Add(root);
        return root;
    }

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
            _dispatchGuestCallback,
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

    public void DisposeCurrentRoot()
    {
        EnsureOwnerThread();
        CurrentRoot?.Dispose();
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

    public void RaiseFirstButtonClick()
    {
        EnsureOwnerThread();
        var button = CurrentRoot?.FindFirstButton()
            ?? throw new InvalidOperationException("No mounted Button exists.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void RaiseButtonClick(string key)
    {
        EnsureOwnerThread();
        RequireControl<Button>(key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    public void RaiseKeyPress(string key)
    {
        EnsureOwnerThread();
        var window = CurrentRoot?.Window
            ?? throw new InvalidOperationException("No mounted Window exists.");
        Key nativeKey = key switch
        {
            "0" => Key.D0, "1" => Key.D1, "2" => Key.D2, "3" => Key.D3, "4" => Key.D4,
            "5" => Key.D5, "6" => Key.D6, "7" => Key.D7, "8" => Key.D8, "9" => Key.D9,
            "+" => Key.Add, "-" => Key.Subtract, "*" => Key.Multiply, "/" => Key.Divide,
            "." => Key.Decimal, "Enter" or "=" => Key.Return, "%" => Key.D5,
            "Backspace" => Key.Back, "Delete" => Key.Delete, "Escape" => Key.Escape,
            "c" or "C" => Key.C, "x" or "X" => Key.X,
            _ => throw new ArgumentException($"Unsupported Headless key '{key}'.", nameof(key)),
        };
        KeyModifiers modifiers = key == "%" ? KeyModifiers.Shift : KeyModifiers.None;
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = nativeKey, KeyModifiers = modifiers };
        window.RaiseEvent(down);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = nativeKey, KeyModifiers = modifiers });
    }

    public void CloseWindow()
    {
        EnsureOwnerThread();
        var window = CurrentRoot?.Window
            ?? throw new InvalidOperationException("No mounted Window exists.");
        window.Close();
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

    public T RequireControl<T>(string key) where T : Control
    {
        EnsureOwnerThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Control? control = CurrentRoot?.FindControl(key);
        return control as T
            ?? throw new InvalidOperationException($"Mounted control '{key}' is not a {typeof(T).Name}.");
    }

    public void DispatchGuestCallback(Action callback) => _dispatchGuestCallback(callback);

    public Window RequireWindowForServices()
    {
        EnsureOwnerThread();
        if (_headless)
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
        if (source.StartsWith("asset:///", StringComparison.OrdinalIgnoreCase))
        {
            string logicalName = source[9..].Replace('\\', '/');
            Stream stream = Assembly.GetEntryAssembly()?.GetManifestResourceStream($"SharpTS.Gui.Asset/{logicalName}")
                ?? throw new FileNotFoundException($"Packaged GUI asset '{logicalName}' was not found.");
            using (stream)
                return new Bitmap(stream);
        }

        string path = Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.IsFile
            ? uri.LocalPath
            : source;
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(path, AppContext.BaseDirectory);
        return new Bitmap(path);
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

    public DesktopRoot CreateWindowRoot(Action reactiveCleanup, DesktopRoot? owner, bool modal, bool mainWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _context.CreateApplicationRoot(this, reactiveCleanup, owner, modal, mainWindow);
    }

    public void ConfigureStyleResources(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (WindowCount != 0)
            throw new InvalidOperationException("Desktop resources and styles must be configured before creating a window.");
        _styleResources = DesktopStyleResources.Parse(json);
    }

    internal void ApplyStyleResources(Window window) => _styleResources?.Apply(window);

    public object? FindResource(DesktopRoot root, string key)
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
