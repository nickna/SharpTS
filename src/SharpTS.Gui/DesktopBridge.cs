using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SharpTS.Gui;

internal sealed class ControlRef
{
    internal ControlRef(Control control) => Control = control;
    internal Control Control { get; }
}

public sealed class DesktopRef
{
    internal DesktopRef() { }

    internal ControlRef? Current { get; private set; }
    internal void Attach(object? value) => Current = (ControlRef?)value;
    internal bool IsAttached => Current is not null;
    public bool isAttached => IsAttached;
    public bool focus() => Current?.Control.Focus() == true;
}

public static class DesktopBridge
{
    public const int GuiApiVersion = 1;
    public const int CustomControlProviderApiVersion = 1;
    public const int DescriptorSchemaVersion = GeneratedControlContract.SchemaVersion;
    public const string DescriptorSchemaHash = GeneratedControlContract.SchemaHash;
    private static DesktopRuntimeContext? _context;

    /// <summary>
    /// Registers statically referenced custom-control providers before the desktop host starts.
    /// The returned scope must outlive the hosted application.
    /// </summary>
    public static IDisposable RegisterControlProviders(params IGuiControlProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (_context is not null)
            throw new InvalidOperationException(
                "GUI control providers must be registered before the desktop runtime starts.");

        var registrations = new List<IDisposable>(providers.Length);
        try
        {
            foreach (IGuiControlProvider provider in providers)
                registrations.Add(DescriptorRegistry.RegisterProvider(provider));
            return new ControlProviderRegistrationScope(registrations);
        }
        catch
        {
            for (int index = registrations.Count - 1; index >= 0; index--)
                registrations[index].Dispose();
            throw;
        }
    }

    internal static DesktopRuntimeRegistration Configure(
        TraceRecorder recorder,
        Action<DesktopRoot, Window> showWindow,
        bool headless,
        Action<Action> dispatchGuestCallback,
        Action<Action> scheduleGuestMicrotask,
        Action<int>? requestShutdown = null,
        string[]? launchArguments = null)
    {
        if (_context is not null)
            throw new InvalidOperationException("A desktop runtime context is already registered.");

        var context = new DesktopRuntimeContext(
            recorder,
            showWindow,
            headless,
            dispatchGuestCallback,
            scheduleGuestMicrotask,
            requestShutdown ?? (_ => { }),
            launchArguments ?? []);
        _context = context;
        return new DesktopRuntimeRegistration(context, ReleaseContext);
    }

    public static DesktopRef CreateRef()
    {
        EnsureOwnerThread();
        return new DesktopRef();
    }

    public static GuiVNode WithCommon(
        GuiVNode node,
        double width,
        double height,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        string horizontalAlignment,
        string verticalAlignment,
        bool isVisible,
        bool isEnabled,
        double opacity,
        string? toolTip,
        string? automationName,
        string[] classes,
        double gridRow,
        double gridColumn,
        double gridRowSpan,
        double gridColumnSpan,
        string dock,
        double canvasLeft,
        double canvasTop,
        Func<string, bool, bool, bool, bool, bool, bool>? keyDown,
        Func<string, bool, bool, bool, bool, bool, bool>? keyUp,
        bool hasKeyDown,
        bool hasKeyUp,
        bool allowDrop,
        Func<string[], string?, string, bool, bool, bool, bool, string>? dragOver,
        Action<string[], string?, string, bool, bool, bool, bool>? drop,
        bool hasDragOver,
        bool hasDrop) =>
        node with
        {
            Width = width,
            Height = height,
            MinWidth = minWidth,
            MinHeight = minHeight,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            MarginLeft = marginLeft,
            MarginTop = marginTop,
            MarginRight = marginRight,
            MarginBottom = marginBottom,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            IsVisible = isVisible,
            IsEnabled = isEnabled,
            Opacity = opacity,
            ToolTip = EmptyToNull(toolTip),
            AutomationName = EmptyToNull(automationName),
            Classes = classes,
            GridRow = ToInteger(gridRow, nameof(gridRow)),
            GridColumn = ToInteger(gridColumn, nameof(gridColumn)),
            GridRowSpan = ToInteger(gridRowSpan, nameof(gridRowSpan)),
            GridColumnSpan = ToInteger(gridColumnSpan, nameof(gridColumnSpan)),
            Dock = dock,
            CanvasLeft = canvasLeft,
            CanvasTop = canvasTop,
            KeyDown = hasKeyDown ? keyDown : null,
            KeyUp = hasKeyUp ? keyUp : null,
            AllowDrop = allowDrop,
            DragOver = hasDragOver ? dragOver : null,
            Drop = hasDrop ? drop : null,
        };

    public static GuiVNode WithStyle(
        GuiVNode node, string? background, string? foreground,
        double paddingLeft, double paddingTop, double paddingRight, double paddingBottom,
        double cornerRadius, double fontSize, string fontWeight, string fontStyle,
        string? fontFamily, string textAlignment) =>
        node with
        {
            Background = EmptyToNull(background),
            Foreground = EmptyToNull(foreground),
            PaddingLeft = paddingLeft,
            PaddingTop = paddingTop,
            PaddingRight = paddingRight,
            PaddingBottom = paddingBottom,
            CornerRadius = cornerRadius,
            FontSize = fontSize,
            FontWeight = fontWeight,
            FontStyle = fontStyle,
            FontFamily = fontFamily ?? string.Empty,
            TextAlignment = textAlignment,
        };

    public static GuiVNode CreateWindow(
        string title,
        double width,
        double height,
        bool canResize,
        bool topmost,
        string theme,
        GuiVNode[] content,
        object? key,
        DesktopRef? reference) =>
        new(
            "Window",
            NormalizeKey(key),
            Title: title,
            Width: width,
            Height: height,
            CanResize: canResize,
            Topmost: topmost,
            Theme: theme,
            Children: content,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateStackPanel(
        string kind,
        double spacing,
        string orientation,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new(
            kind,
            NormalizeKey(key),
            Spacing: spacing,
            Orientation: orientation,
            Children: children,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateWrapPanel(double spacing, string orientation, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new("WrapPanel", NormalizeKey(key), Spacing: spacing, Orientation: orientation, Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateDockPanel(bool lastChildFill, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new("DockPanel", NormalizeKey(key), LastChildFill: lastChildFill, Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateGrid(
        string rows,
        string columns,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new(
            "Grid",
            NormalizeKey(key),
            Rows: rows,
            Columns: columns,
            Children: children,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateBorder(
        string kind,
        double paddingLeft,
        double paddingTop,
        double paddingRight,
        double paddingBottom,
        string? background,
        string? borderBrush,
        double borderLeft,
        double borderTop,
        double borderRight,
        double borderBottom,
        double cornerRadius,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new(
            kind,
            NormalizeKey(key),
            PaddingLeft: paddingLeft,
            PaddingTop: paddingTop,
            PaddingRight: paddingRight,
            PaddingBottom: paddingBottom,
            Background: EmptyToNull(background),
            BorderBrush: EmptyToNull(borderBrush),
            BorderLeft: borderLeft,
            BorderTop: borderTop,
            BorderRight: borderRight,
            BorderBottom: borderBottom,
            CornerRadius: cornerRadius,
            Children: children,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateScrollViewer(
        string horizontalScrollBarVisibility,
        string verticalScrollBarVisibility,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new(
            "ScrollViewer",
            NormalizeKey(key),
            HorizontalScrollBarVisibility: horizontalScrollBarVisibility,
            VerticalScrollBarVisibility: verticalScrollBarVisibility,
            Children: children,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateTextBlock(
        string text,
        double fontSize,
        string fontWeight,
        string fontStyle,
        string textWrapping,
        string textAlignment,
        string? foreground,
        object? key,
        DesktopRef? reference) =>
        new(
            "TextBlock",
            NormalizeKey(key),
            Text: text,
            FontSize: fontSize,
            FontWeight: fontWeight,
            FontStyle: fontStyle,
            TextWrapping: textWrapping,
            TextAlignment: textAlignment,
            Foreground: EmptyToNull(foreground),
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateSeparator(object? key, DesktopRef? reference) =>
        new("Separator", NormalizeKey(key), AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateContentControl(
        string kind, string text, bool isChecked, string? groupName, Action? click,
        Action<bool>? checkedChanged, string? background, string? foreground,
        double paddingLeft, double paddingTop, double paddingRight, double paddingBottom,
        double fontSize, string fontWeight, string horizontalContentAlignment,
        string verticalContentAlignment, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new(kind, NormalizeKey(key), Text: text, IsChecked: isChecked, GroupName: groupName,
            Click: click, CheckedChanged: checkedChanged, Background: EmptyToNull(background),
            Foreground: EmptyToNull(foreground), PaddingLeft: paddingLeft, PaddingTop: paddingTop,
            PaddingRight: paddingRight, PaddingBottom: paddingBottom, FontSize: fontSize,
            FontWeight: fontWeight, HorizontalContentAlignment: horizontalContentAlignment,
            VerticalContentAlignment: verticalContentAlignment, Children: children,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateTextBox(
        string kind,
        string text,
        string? placeholder,
        bool isReadOnly,
        bool acceptsReturn,
        double maxLength,
        bool isPassword,
        Action<string>? textChanged,
        object? key,
        DesktopRef? reference) =>
        new(
            kind,
            NormalizeKey(key),
            Text: text,
            Placeholder: EmptyToNull(placeholder),
            IsReadOnly: isReadOnly,
            AcceptsReturn: acceptsReturn,
            MaxLength: ToInteger(maxLength, nameof(maxLength)),
            IsPassword: isPassword,
            TextChanged: textChanged,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateComboBox(
        string[] items,
        double selectedIndex,
        Action<double>? selectionChanged,
        object? key,
        DesktopRef? reference) =>
        new(
            "ComboBox",
            NormalizeKey(key),
            Items: items,
            SelectedIndex: ToInteger(selectedIndex, nameof(selectedIndex)),
            SelectionChanged: selectionChanged,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateSlider(
        double minimum,
        double maximum,
        double value,
        Action<double>? valueChanged,
        object? key,
        DesktopRef? reference) =>
        new(
            "Slider",
            NormalizeKey(key),
            Minimum: minimum,
            Maximum: maximum,
            Value: value,
            ValueChanged: valueChanged,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateProgressBar(
        double minimum,
        double maximum,
        double value,
        object? key,
        DesktopRef? reference) =>
        new(
            "ProgressBar",
            NormalizeKey(key),
            Minimum: minimum,
            Maximum: maximum,
            Value: value,
            AttachRef: GetAttach(reference),
            RefIdentity: reference);

    public static GuiVNode CreateListBox(string[] items, int[] selectedIndices, string selectionMode, Action<int[]>? changed, object? key, DesktopRef? reference) =>
        new("ListBox", NormalizeKey(key), Items: items, SelectedIndices: selectedIndices,
            SelectionMode: selectionMode, IndicesChanged: changed, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateNumericUpDown(double minimum, double maximum, double increment, double? value, Action<double?>? changed, object? key, DesktopRef? reference) =>
        new("NumericUpDown", NormalizeKey(key), Minimum: minimum, Maximum: maximum,
            Increment: increment, NullableValue: value, NullableValueChanged: changed,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateDateTimePicker(string kind, string? value, Action<string?>? changed, object? key, DesktopRef? reference) =>
        new(kind, NormalizeKey(key), StringValue: EmptyToNull(value), NullableStringChanged: changed,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateImage(string source, string stretch, Action? loaded, Action<string>? error, object? key, DesktopRef? reference) =>
        new("Image", NormalizeKey(key), Source: source, Stretch: stretch, Loaded: loaded,
            LoadError: error, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateTabControl(double selectedIndex, Action<double>? changed, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new("TabControl", NormalizeKey(key), SelectedIndex: ToInteger(selectedIndex, nameof(selectedIndex)),
            SelectionChanged: changed, Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateTabItem(string header, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new("TabItem", NormalizeKey(key), Header: header, Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateMenu(GuiVNode[] children, object? key, DesktopRef? reference) =>
        new("Menu", NormalizeKey(key), Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateItemsControl(string kind, GuiVNode[] children, object? key, DesktopRef? reference) =>
        new(kind, NormalizeKey(key), Children: children, AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateVirtualizingList(
        int[] selectedIndices,
        string selectionMode,
        Action<int[]>? changed,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new("VirtualizingList", NormalizeKey(key), SelectedIndices: selectedIndices,
            SelectionMode: selectionMode, IndicesChanged: changed, Children: children,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateTreeViewItem(
        string header,
        bool isExpanded,
        Action<bool>? expandedChanged,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference) =>
        new("TreeViewItem", NormalizeKey(key), Header: header, IsExpanded: isExpanded,
            ExpandedChanged: expandedChanged, Children: children,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateRichTextBlock(string runsJson, object? key, DesktopRef? reference) =>
        new("RichTextBlock", NormalizeKey(key), RichTextJson: runsJson,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateDrawingCanvas(string commandsJson, object? key, DesktopRef? reference) =>
        new("DrawingCanvas", NormalizeKey(key), DrawingJson: commandsJson,
            AttachRef: GetAttach(reference), RefIdentity: reference);

    public static GuiVNode CreateCustomControl(
        string kind,
        string propertiesJson,
        GuiVNode[] children,
        object? key,
        DesktopRef? reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(propertiesJson);
        ArgumentNullException.ThrowIfNull(children);
        return new(kind, NormalizeKey(key), CustomPropertiesJson: propertiesJson, Children: children,
            AttachRef: GetAttach(reference), RefIdentity: reference);
    }

    public static GuiVNode WithSource(GuiVNode node, string file, double line, double column) =>
        node with { SourceFile = file, SourceLine = (int)line, SourceColumn = (int)column };

    public static GuiVNode WithSpecifiedProperties(GuiVNode node, string[] properties)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(properties);
        return node with { SpecifiedProperties = properties };
    }

    public static GuiVNode WithBoundary(GuiVNode node, string boundaryPath)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundaryPath);
        return node with { BoundaryPath = boundaryPath };
    }

    public static DesktopApplicationSession CreateDesktopApplication(string shutdownMode)
    {
        return RequireContext().CreateApplication(shutdownMode);
    }

    public static DesktopRoot CreateDesktopApplicationRoot(
        DesktopApplicationSession application,
        Action reactiveCleanup,
        DesktopRoot? owner,
        bool modal,
        bool mainWindow)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.CreateWindowRoot(reactiveCleanup, owner, modal, mainWindow);
    }

    public static void ConfigureDesktopStyleResources(
        DesktopApplicationSession application,
        string json)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ConfigureStyleResources(json);
    }

    public static object? FindDesktopResource(
        DesktopApplicationSession application,
        DesktopRoot root,
        string key)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(root);
        return application.FindResource(root, key);
    }

    internal static string? GetBoundaryPath(GuiVNode node) =>
        node.BoundaryPath;

    internal static void DisposeAllRoots() => RequireContext().DisposeAllRoots();

    public static void QueueMicrotask(Action callback)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(callback);
        RequireContext().ScheduleGuestMicrotask(callback);
    }

    public static Task<string> ShowMessageDialogAsync(string title, string message, string buttons) =>
        DesktopServices.ShowMessageAsync(RequireContext().RequireWindowForServices(), title, message, buttons);

    public static Task<string[]> ShowOpenFileDialogAsync(string title, bool allowMultiple, string filtersJson) =>
        DesktopServices.OpenFilesAsync(RequireContext().RequireWindowForServices(), title, allowMultiple, filtersJson);

    public static Task<string?> ShowSaveFileDialogAsync(string title, string suggestedFileName, string defaultExtension, string filtersJson) =>
        DesktopServices.SaveFileAsync(RequireContext().RequireWindowForServices(), title, suggestedFileName, defaultExtension, filtersJson);

    public static Task<string?> ShowFolderDialogAsync(string title) =>
        DesktopServices.OpenFolderAsync(RequireContext().RequireWindowForServices(), title);

    public static Task<string> ReadClipboardTextAsync() =>
        DesktopServices.ReadClipboardAsync(RequireContext().RequireWindowForServices());

    public static Task WriteClipboardTextAsync(string value) =>
        DesktopServices.WriteClipboardAsync(RequireContext().RequireWindowForServices(), value);

    public static string[] GetDesktopLaunchArguments() =>
        RequireContext().GetLaunchArguments();

    public static string GetDesktopPlatformInfoJson() =>
        DesktopPlatformServices.PlatformInfoJson();

    public static string GetDesktopDisplaysJson() =>
        RequireContext().GetDisplaysJson();

    public static Task OpenDesktopExternalAsync(string target) =>
        DesktopPlatformServices.OpenExternalAsync(target);

    public static Task ShowDesktopItemInFolderAsync(string path) =>
        DesktopPlatformServices.ShowItemInFolderAsync(path);

    public static Task PrintDesktopFileAsync(string path) =>
        DesktopPlatformServices.PrintFileAsync(path);

    public static Task ShowDesktopNotificationAsync(string title, string message, bool silent)
    {
        DesktopRuntimeContext context = RequireContext();
        context.EnsureOwnerThread();
        return DesktopNotifications.ShowAsync(context.IsHeadless, title, message, silent);
    }

    public static DesktopTrayIcon CreateDesktopTrayIcon(
        DesktopApplicationSession application,
        string icon,
        string toolTip,
        string menuJson,
        Action? clicked,
        Action<string>? menuClicked)
    {
        ArgumentNullException.ThrowIfNull(application);
        return application.CreateTrayIcon(icon, toolTip, menuJson, clicked, menuClicked);
    }

    internal static void EnsureOwnerThread() => RequireContext().EnsureOwnerThread();

    internal static DesktopRuntimeContext RequireContext() =>
        _context ?? throw new InvalidOperationException(
            "DesktopBridge.Configure must run before guest code.");

    private static int ToInteger(double value, string name)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "The value must be a finite 32-bit integer.");
        return (int)value;
    }

    private static string? NormalizeKey(object? key)
    {
        if (key is null)
            return null;
        if (key is string text)
            return text;
        if (key is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        return key.ToString() ?? string.Empty;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static Action<object?>? GetAttach(DesktopRef? reference) =>
        reference is null ? null : reference.Attach;

    private static void ReleaseContext(DesktopRuntimeContext context)
    {
        if (ReferenceEquals(_context, context))
            _context = null;
    }

    private sealed class ControlProviderRegistrationScope(List<IDisposable> registrations) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int index = registrations.Count - 1; index >= 0; index--)
                registrations[index].Dispose();
        }
    }
}
