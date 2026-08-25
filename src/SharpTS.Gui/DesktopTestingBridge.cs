using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace SharpTS.Gui;

/// <summary>
/// Implements the supported Headless testing surface exposed by
/// <c>@sharpts/gui/testing</c>. Applications should use the TypeScript facade.
/// </summary>
public static class DesktopTestingBridge
{
    private static readonly ConditionalWeakTable<DesktopRoot, TestPointerState> ActivePointers = new();
    public static void AfterRender(DesktopRoot root, Action callback)
    {
        DesktopRoot validated = RequireRoot(root);
        ArgumentNullException.ThrowIfNull(callback);
        DesktopRuntimeContext context = DesktopBridge.RequireContext();
        void CheckEventWork()
        {
            if (validated.IsDisposed)
                return;
            if (validated.HasPendingEventWork)
            {
                context.PostGuestIdleProbe(CheckEventWork);
                return;
            }
            callback();
        }
        context.DispatchGuestCallback(() =>
        {
            if (!validated.IsDisposed)
                context.AfterDesktopServices(CheckEventWork);
        });
    }

    public static void Click(DesktopRoot root, string key) =>
        RequireControl<Button>(root, key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public static void ClickMenuItem(DesktopRoot root, string key)
    {
        MenuItem item = RequireControl<MenuItem>(root, key);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    public static void QueueMessageDialogResult(DesktopRoot root, string result) =>
        RequireScriptedInteractions(root).EnqueueMessageResult(result);

    public static void QueueOpenFileDialogResult(DesktopRoot root, string[] paths) =>
        RequireScriptedInteractions(root).EnqueueOpenResult(
            paths ?? throw new ArgumentNullException(nameof(paths)));

    public static void QueueSaveFileDialogResult(DesktopRoot root, string? path) =>
        RequireScriptedInteractions(root).EnqueueSaveResult(path);

    public static void QueueFolderDialogResult(DesktopRoot root, string? path) =>
        RequireScriptedInteractions(root).EnqueueFolderResult(path);

    public static void PressKey(DesktopRoot root, string key)
    {
        Window window = RequireRoot(root).Window
            ?? throw new InvalidOperationException("The desktop test window is not mounted.");
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
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = nativeKey,
            KeyModifiers = modifiers,
        });
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = nativeKey,
            KeyModifiers = modifiers,
        });
    }

    public static string GetText(DesktopRoot root, string key) => RequireControl<Control>(root, key) switch
    {
        TextBlock text => text.Text ?? string.Empty,
        TextBox textBox => textBox.Text ?? string.Empty,
        ContentControl content => content.Content?.ToString() ?? string.Empty,
        _ => string.Empty,
    };

    public static string GetProperty(DesktopRoot root, string key, string property) => property switch
    {
        "automationName" => AutomationProperties.GetName(RequireControl<Control>(root, key)) ?? string.Empty,
        "background" => (RequireControl<Control>(root, key) as TemplatedControl)?.Background?.ToString() ?? string.Empty,
        "foreground" => (RequireControl<Control>(root, key) as TemplatedControl)?.Foreground?.ToString() ?? string.Empty,
        "toolTip" => ToolTip.GetTip(RequireControl<Control>(root, key))?.ToString() ?? string.Empty,
        "isEnabled" => RequireControl<Control>(root, key).IsEnabled.ToString(),
        "isVisible" => RequireControl<Control>(root, key).IsVisible.ToString(),
        _ => throw new ArgumentException($"Unsupported Headless property '{property}'.", nameof(property)),
    };

    public static void SetTextBoxValue(DesktopRoot root, string key, string value) =>
        RequireControl<TextBox>(root, key).Text = value;

    public static void SetCheckBoxValue(DesktopRoot root, string key, bool value) =>
        RequireControl<CheckBox>(root, key).IsChecked = value;

    public static void SetComboBoxIndex(DesktopRoot root, string key, double value) =>
        RequireControl<ComboBox>(root, key).SelectedIndex = ToInteger(value, nameof(value));

    public static void SetSliderValue(DesktopRoot root, string key, double value) =>
        RequireControl<Slider>(root, key).Value = value;

    public static void SetWindowClientSize(DesktopRoot root, double width, double height)
    {
        DesktopRoot validated = RequireRoot(root);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1)
            throw new ArgumentOutOfRangeException("width/height", "Window client dimensions must be positive finite values.");
        Window window = RequireWindow(validated);
        window.Width = width;
        window.Height = height;
        window.InvalidateMeasure();
        Dispatcher.UIThread.RunJobs();
    }

    public static void PressPointer(DesktopRoot root, string key, double x, double y)
    {
        DesktopRoot validated = RequireRoot(root);
        if (ActivePointers.TryGetValue(validated, out _))
            throw new InvalidOperationException("The desktop test window already has an active pointer gesture.");
        Control target = RequireControl<Control>(validated, key);
        IPointer? pointer = null;
        EventHandler<PointerPressedEventArgs> observer = (_, args) => pointer = args.Pointer;
        target.AddHandler(
            InputElement.PointerPressedEvent,
            observer,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        try
        {
            RequireWindow(validated).MouseDown(
                TranslatePoint(target, RequireWindow(validated), x, y),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
        }
        finally
        {
            target.RemoveHandler(InputElement.PointerPressedEvent, observer);
        }
        ActivePointers.Add(validated, new TestPointerState(
            pointer ?? throw new InvalidOperationException("The Headless host did not create a pointer for the press."),
            key));
    }

    public static void MovePointer(DesktopRoot root, string key, double x, double y)
    {
        DesktopRoot validated = RequireRoot(root);
        TestPointerState state = RequireActivePointer(validated, key);
        Control target = RequireControl<Control>(validated, state.Key);
        RequireWindow(validated).MouseMove(
            TranslatePoint(target, RequireWindow(validated), x, y),
            RawInputModifiers.LeftMouseButton);
    }

    public static void ReleasePointer(DesktopRoot root, string key, double x, double y)
    {
        DesktopRoot validated = RequireRoot(root);
        TestPointerState state = RequireActivePointer(validated, key);
        Control target = RequireControl<Control>(validated, state.Key);
        try
        {
            RequireWindow(validated).MouseUp(
                TranslatePoint(target, RequireWindow(validated), x, y),
                MouseButton.Left,
                RawInputModifiers.None);
        }
        finally
        {
            ActivePointers.Remove(validated);
        }
    }

    public static void CancelPointer(DesktopRoot root, string key)
    {
        DesktopRoot validated = RequireRoot(root);
        TestPointerState state = RequireActivePointer(validated, key);
        try
        {
            if (state.Pointer.Captured is null)
                throw new InvalidOperationException("The active test pointer is not captured and cannot be cancelled deterministically.");
            state.Pointer.Capture(null);
        }
        finally
        {
            ActivePointers.Remove(validated);
        }
    }

    public static void DragPointer(DesktopRoot root, string key, double[] coordinates)
    {
        if (coordinates is null || coordinates.Length < 4 || coordinates.Length % 2 != 0)
            throw new ArgumentException("Pointer drags require at least two x/y coordinate pairs.", nameof(coordinates));
        PressPointer(root, key, coordinates[0], coordinates[1]);
        try
        {
            for (int index = 2; index < coordinates.Length - 2; index += 2)
                MovePointer(root, key, coordinates[index], coordinates[index + 1]);
            ReleasePointer(root, key, coordinates[^2], coordinates[^1]);
        }
        catch
        {
            if (ActivePointers.TryGetValue(root, out TestPointerState? state))
            {
                try { state.Pointer.Capture(null); }
                finally { ActivePointers.Remove(root); }
            }
            throw;
        }
    }

    public static string DropText(DesktopRoot root, string key, string value)
    {
        Control target = RequireControl<Control>(root, key);
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(value));
        var over = new DragEventArgs(
            DragDrop.DragOverEvent, transfer, target, default, KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy,
        };
        target.RaiseEvent(over);
        var drop = new DragEventArgs(
            DragDrop.DropEvent, transfer, target, default, KeyModifiers.None)
        {
            DragEffects = over.DragEffects,
        };
        target.RaiseEvent(drop);
        return over.DragEffects.ToString().ToLowerInvariant();
    }

    private static T RequireControl<T>(DesktopRoot root, string key) where T : Control
    {
        DesktopRoot validated = RequireRoot(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return validated.FindControl(key) as T
            ?? throw new InvalidOperationException(
                $"No {typeof(T).Name} with key '{key}' exists in the supplied desktop test window.");
    }

    private static DesktopRoot RequireRoot(DesktopRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        DesktopRuntimeContext context = DesktopBridge.RequireContext();
        context.EnsureOwnerThread();
        if (!context.IsHeadless)
            throw new InvalidOperationException("@sharpts/gui/testing is available only when the GUI host runs in Headless mode.");
        ObjectDisposedException.ThrowIf(root.IsDisposed, root);
        if (!context.Roots.Contains(root))
            throw new ArgumentException("The desktop test window does not belong to the active GUI runtime.", nameof(root));
        return root;
    }

    private static ScriptedDesktopInteractionServices RequireScriptedInteractions(DesktopRoot root)
    {
        RequireRoot(root);
        return DesktopBridge.RequireContext().InteractionServices as ScriptedDesktopInteractionServices
            ?? throw new InvalidOperationException(
                "Scripted desktop-service results are available only in the Headless GUI host.");
    }

    private static Window RequireWindow(DesktopRoot root) => root.Window
        ?? throw new InvalidOperationException("The desktop test window is not mounted.");

    private static Point TranslatePoint(Control target, Window window, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException("x/y", "Pointer coordinates must be finite.");
        return target.TranslatePoint(new Point(x, y), window)
            ?? throw new InvalidOperationException("The pointer target is not connected to its desktop window.");
    }

    private static TestPointerState RequireActivePointer(DesktopRoot root, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        TestPointerState state = ActivePointers.TryGetValue(root, out TestPointerState? active)
            ? active
            : throw new InvalidOperationException("The desktop test window does not have an active pointer gesture.");
        if (!string.Equals(state.Key, key, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The active pointer belongs to '{state.Key}', not '{key}'.");
        return state;
    }

    private static int ToInteger(double value, string name)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "The value must be a finite 32-bit integer.");
        return (int)value;
    }

    private sealed record TestPointerState(IPointer Pointer, string Key);
}
