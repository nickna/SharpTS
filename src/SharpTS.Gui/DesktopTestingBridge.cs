using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SharpTS.Gui;

/// <summary>
/// Implements the supported Headless testing surface exposed by
/// <c>@sharpts/gui/testing</c>. Applications should use the TypeScript facade.
/// </summary>
public static class DesktopTestingBridge
{
    public static void AfterRender(DesktopRoot root, Action callback)
    {
        DesktopRoot validated = RequireRoot(root);
        ArgumentNullException.ThrowIfNull(callback);
        DesktopBridge.RequireContext().ScheduleGuestMicrotask(() =>
        {
            if (!validated.IsDisposed)
                callback();
        });
    }

    public static void Click(DesktopRoot root, string key) =>
        RequireControl<Button>(root, key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
        "topmost" => RequireControl<Window>(root, key).Topmost.ToString(),
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

    private static int ToInteger(double value, string name)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "The value must be a finite 32-bit integer.");
        return (int)value;
    }
}
