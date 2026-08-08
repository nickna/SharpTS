using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System.Reflection;

namespace SharpTS.Gui;

internal sealed class DesktopRuntimeContext
{
    private readonly Action<Window> _showWindow;
    private readonly Action<Action> _dispatchGuestCallback;
    private readonly Action<Action> _scheduleGuestMicrotask;
    private readonly bool _headless;

    public DesktopRuntimeContext(
        TraceRecorder recorder,
        Action<Window> showWindow,
        bool headless,
        Action<Action> dispatchGuestCallback,
        Action<Action> scheduleGuestMicrotask)
    {
        Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
        _dispatchGuestCallback = dispatchGuestCallback
            ?? throw new ArgumentNullException(nameof(dispatchGuestCallback));
        _scheduleGuestMicrotask = scheduleGuestMicrotask
            ?? throw new ArgumentNullException(nameof(scheduleGuestMicrotask));
        _headless = headless;
        EnsureOwnerThread();
    }

    public TraceRecorder Recorder { get; }
    public bool IsHeadless => _headless;
    public DesktopRoot? CurrentRoot { get; private set; }

    public DesktopRoot CreateRoot(Action reactiveCleanup)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(reactiveCleanup);
        if (CurrentRoot is not null)
            throw new InvalidOperationException("Only one active desktop Window root is permitted per application.");

        var root = new DesktopRoot(
            Recorder,
            _showWindow,
            _dispatchGuestCallback,
            _headless,
            reactiveCleanup,
            ReleaseRoot);
        CurrentRoot = root;
        return root;
    }

    public void DisposeCurrentRoot()
    {
        EnsureOwnerThread();
        CurrentRoot?.Dispose();
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
        if (ReferenceEquals(CurrentRoot, root))
            CurrentRoot = null;
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
        Context.DisposeCurrentRoot();
        release(Context);
    }
}
