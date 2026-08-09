using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using System.Reflection;

namespace SharpTS.Gui;

internal sealed class DesktopRuntimeContext
{
    private readonly Action<Window> _showWindow;
    private readonly Action<Action> _dispatchGuestCallback;
    private readonly Action<Action> _scheduleGuestMicrotask;
    private readonly bool _headless;
    private bool _cancelNextWindowClose;

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
