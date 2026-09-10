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
        void ConfirmGuestIdle()
        {
            if (validated.IsDisposed)
                return;
            if (validated.HasPendingEventWork)
            {
                CheckGuestWork();
                return;
            }
            // A hosted Promise continuation can be admitted to the guest queue as
            // the tracked operation settles. Require one complete idle checkpoint
            // with no newly tracked render before exposing the native tree.
            context.PostGuestIdleProbe(callback);
        }
        void CheckGuestWork()
        {
            if (validated.IsDisposed)
                return;
            if (validated.HasPendingEventWork)
            {
                // Wait for completion rather than continuously queueing probes that
                // would starve the hosted timers the operation itself awaits.
                validated.WhenEventWorkIdle(() => context.PostGuestIdleProbe(CheckGuestWork));
                return;
            }
            context.PostGuestIdleProbe(ConfirmGuestIdle);
        }
        context.DispatchGuestCallback(() =>
        {
            if (!validated.IsDisposed)
                context.AfterDesktopServices(CheckGuestWork);
        });
    }

    public static void Click(DesktopRoot root, string key)
    {
        Button button = RequireControl<Button>(root, key);
        if (!button.IsEffectivelyEnabled) return;
        if (button is ToggleButton toggle) toggle.IsChecked = button is RadioButton || toggle.IsChecked != true;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    public static DesktopRoot FindOwnedWindow(DesktopRoot root, string title)
    {
        RequireRoot(root);
        return DesktopBridge.RequireContext().Roots.Single(candidate => candidate.Owner == root && candidate.Window?.Title == title);
    }

    public static void Wheel(DesktopRoot root, string key, double x, double y, double deltaX, double deltaY, bool ctrl)
    {
        Window window = RequireWindow(RequireRoot(root));
        Control control = RequireControl<Control>(root, key);
        Point point = control.TranslatePoint(new Point(x, y), window) ?? throw new InvalidOperationException("Control is not arranged.");
        window.MouseWheel(point, new Vector(deltaX, deltaY), ctrl ? RawInputModifiers.Control : RawInputModifiers.None);
    }

    public static void SetNumericValue(DesktopRoot root, string key, double value) =>
        RequireControl<NumericUpDown>(root, key).Value = (decimal)value;

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
        Window window = RequireRoot(root).Window ?? throw new InvalidOperationException("Window is not mounted.");
        string gestureText = key switch
        {
            "+" => "Add",
            "-" => "Subtract",
            "*" => "Multiply",
            "/" => "Divide",
            "." => "Decimal",
            "=" => "Enter",
            "%" => "Shift+D5",
            _ when key.Length == 1 && char.IsAsciiDigit(key[0]) => "D" + key,
            _ => key,
        };
        KeyGesture gesture = KeyGesture.Parse(gestureText);
        var modifiers = (RawInputModifiers)gesture.KeyModifiers;
        window.KeyPress(gesture.Key, modifiers, PhysicalKey.None, null);
        // A default/cancel action can close the window during KeyDown.
        if (!root.IsDisposed) window.KeyRelease(gesture.Key, modifiers, PhysicalKey.None, null);
    }

    public static bool Focus(DesktopRoot root, string key)
    {
        var reference = new DesktopRef();
        reference.Attach(new ControlRef(RequireControl<Control>(root, key)));
        return reference.focus();
    }

    public static bool IsFocused(DesktopRoot root, string key) => RequireControl<Control>(root, key).IsKeyboardFocusWithin;

    public static void TypeText(DesktopRoot root, string text)
    {
        Window window = RequireRoot(root).Window ?? throw new InvalidOperationException("Window is not mounted.");
        window.KeyTextInput(text);
    }

    public static void SetRenderScaling(DesktopRoot root, double scaling)
    {
        Window window = RequireRoot(root).Window ?? throw new InvalidOperationException("Window is not mounted.");
        if (!double.IsFinite(scaling) || scaling <= 0) throw new ArgumentOutOfRangeException(nameof(scaling));
        window.SetRenderScaling(scaling);
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
        "isChecked" => (RequireControl<Control>(root, key) as ToggleButton)?.IsChecked.ToString() ?? "",
        "value" => (RequireControl<Control>(root, key) as NumericUpDown)?.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
        "width" => RequireControl<Control>(root, key).Bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "height" => RequireControl<Control>(root, key).Bounds.Height.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

    public static bool IsInViewport(DesktopRoot root, string key) =>
        DesktopGeometry.IsInViewport(RequireControl<Control>(root, key));

    public static string CaptureSnapshot(DesktopRoot root, string path) =>
        DesktopDevtoolsBridge.CaptureWindowSnapshot(RequireWindow(RequireRoot(root)), path);

    public static string AssertSnapshot(DesktopRoot root, string path, bool update, int maxDifferentPixels = 0) =>
        DesktopDevtoolsBridge.AssertWindowSnapshot(RequireWindow(RequireRoot(root)), path, update, maxDifferentPixels);

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

    public static void HoverPointer(DesktopRoot root, string key, double x, double y)
    {
        DesktopRoot validated = RequireRoot(root);
        if (ActivePointers.TryGetValue(validated, out _))
            throw new InvalidOperationException("Release the active test pointer before hovering.");
        Window window = RequireWindow(validated);
        window.MouseMove(TranslatePoint(RequireControl<Control>(validated, key), window, x, y),
            RawInputModifiers.None);
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
        return validated.FindTestControl(key) as T
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
