using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;

namespace SharpTS.Gui;

internal static class DesktopPlatformServices
{
    public static string PlatformInfoJson()
    {
        var info = new
        {
            operatingSystem = OperatingSystem.IsWindows() ? "windows" :
                OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            framework = RuntimeInformation.FrameworkDescription,
            applicationDirectory = AppContext.BaseDirectory,
            localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            roamingApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            temporaryDirectory = Path.GetTempPath(),
        };
        return JsonSerializer.Serialize(info);
    }

    public static Task OpenExternalAsync(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string value;
        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) &&
            uri.Scheme is "http" or "https" or "mailto")
        {
            value = uri.AbsoluteUri;
        }
        else
        {
            value = ResolveExistingPath(target);
        }
        Start(new ProcessStartInfo(value) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public static Task ShowItemInFolderAsync(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("showItemInFolder is currently supported on Windows only.");
        string resolved = ResolveExistingPath(path);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/select," + resolved);
        Start(start);
        return Task.CompletedTask;
    }

    public static Task PrintFileAsync(string path)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("printFile is currently supported on Windows only.");
        string resolved = ResolveExistingPath(path);
        Start(new ProcessStartInfo(resolved) { UseShellExecute = true, Verb = "print" });
        return Task.CompletedTask;
    }

    internal static string ResolveExistingPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, AppContext.BaseDirectory);
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
            throw new FileNotFoundException($"Desktop service target '{resolved}' does not exist.", resolved);
        return resolved;
    }

    private static void Start(ProcessStartInfo start)
    {
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException($"The operating system did not launch '{start.FileName}'.");
    }
}

public sealed class DesktopTrayIcon : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly DesktopRuntimeContext _context;
    private readonly TrayIcon? _native;
    private readonly Action<DesktopTrayIcon> _release;
    private Action? _clicked;
    private Action<string>? _menuClicked;
    private bool _disposed;

    internal DesktopTrayIcon(
        DesktopRuntimeContext context,
        DesktopApplicationSession application,
        bool headless,
        string icon,
        string toolTip,
        string menuJson,
        Action? clicked,
        Action<string>? menuClicked,
        Action<DesktopTrayIcon> release)
    {
        _context = context;
        Application = application;
        _release = release;
        if (!headless)
        {
            _native = new TrayIcon();
            _native.Clicked += OnClicked;
        }
        try
        {
            Update(icon, toolTip, menuJson, clicked, menuClicked);
        }
        catch
        {
            if (_native is not null)
            {
                _native.Clicked -= OnClicked;
                _native.Dispose();
            }
            throw;
        }
    }

    internal DesktopApplicationSession Application { get; }
    public bool IsDisposed => _disposed;

    public void Update(
        string icon,
        string toolTip,
        string menuJson,
        Action? clicked,
        Action<string>? menuClicked)
    {
        _context.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        TrayMenuItemModel[] menu = JsonSerializer.Deserialize<TrayMenuItemModel[]>(menuJson, JsonOptions) ?? [];
        ValidateMenu(menu);
        _clicked = clicked;
        _menuClicked = menuClicked;
        if (_native is null)
            return;

        WindowIcon nextIcon = _context.LoadWindowIcon(icon);
        NativeMenu? nextMenu = BuildMenu(menu);
        _native.IsVisible = false;
        _native.Icon = nextIcon;
        _native.ToolTipText = string.IsNullOrWhiteSpace(toolTip) ? null : toolTip;
        _native.Menu = nextMenu;
        _native.IsVisible = true;
    }

    internal void RaiseClickForTesting() => OnClicked(this, EventArgs.Empty);

    internal void RaiseMenuClickForTesting(string id)
    {
        _context.EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.DispatchGuestCallback(() => _menuClicked?.Invoke(id));
    }

    public void Dispose()
    {
        _context.EnsureOwnerThread();
        if (_disposed)
            return;
        _disposed = true;
        if (_native is not null)
        {
            _native.Clicked -= OnClicked;
            _native.IsVisible = false;
            _native.Dispose();
        }
        _clicked = null;
        _menuClicked = null;
        _release(this);
    }

    private NativeMenu? BuildMenu(TrayMenuItemModel[] items)
    {
        if (items.Length == 0)
            return null;
        var menu = new NativeMenu();
        foreach (TrayMenuItemModel model in items)
        {
            if (model.Separator)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
                continue;
            }
            var item = new NativeMenuItem(model.Label!)
            {
                IsEnabled = model.IsEnabled,
                IsChecked = model.IsChecked,
            };
            string id = model.Id!;
            item.Click += (_, _) => _context.DispatchGuestCallback(() => _menuClicked?.Invoke(id));
            menu.Items.Add(item);
        }
        return menu;
    }

    private void OnClicked(object? sender, EventArgs args)
    {
        _context.EnsureOwnerThread();
        if (!_disposed)
            _context.DispatchGuestCallback(() => _clicked?.Invoke());
    }

    private static void ValidateMenu(TrayMenuItemModel[] items)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (TrayMenuItemModel item in items)
        {
            if (item.Separator)
                continue;
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label))
                throw new ArgumentException("Tray menu items require non-empty id and label values.");
            if (!ids.Add(item.Id))
                throw new ArgumentException($"Duplicate tray menu item id '{item.Id}'.");
        }
    }

    private sealed record TrayMenuItemModel(
        string? Id,
        string? Label,
        bool Separator = false,
        bool IsEnabled = true,
        bool IsChecked = false);
}
