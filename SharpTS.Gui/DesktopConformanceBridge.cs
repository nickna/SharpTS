using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using System.Runtime.CompilerServices;

namespace SharpTS.Gui;

public static class DesktopConformanceBridge
{
    public static void Click(string key) => Context.RaiseButtonClick(key);

    public static void PressKey(string key) => Context.RaiseKeyPress(key);

    public static string GetText(string key) => Context.RequireControl<Control>(key) switch
    {
        TextBlock text => text.Text ?? string.Empty,
        TextBox textBox => textBox.Text ?? string.Empty,
        ContentControl content => content.Content?.ToString() ?? string.Empty,
        _ => string.Empty,
    };

    public static string GetProperty(string key, string property) =>
        property switch
        {
            "automationName" => AutomationProperties.GetName(Context.RequireControl<Control>(key)) ?? string.Empty,
            "background" => (Context.RequireControl<Control>(key) as TemplatedControl)?.Background?.ToString() ?? string.Empty,
            "foreground" => (Context.RequireControl<Control>(key) as TemplatedControl)?.Foreground?.ToString() ?? string.Empty,
            "toolTip" => ToolTip.GetTip(Context.RequireControl<Control>(key))?.ToString() ?? string.Empty,
            "isEnabled" => Context.RequireControl<Control>(key).IsEnabled.ToString(),
            "isVisible" => Context.RequireControl<Control>(key).IsVisible.ToString(),
            _ => throw new ArgumentException($"Unsupported Headless property '{property}'.", nameof(property)),
        };

    public static double GetIdentity(string key) =>
        RuntimeHelpers.GetHashCode(Context.RequireControl<Control>(key));

    public static double GetActiveSubscriptionCount() =>
        Context.CurrentRoot?.ActiveSubscriptions
        ?? throw new InvalidOperationException("No desktop root is active.");

    public static void SetTextBoxValue(string key, string value) =>
        Context.RequireControl<TextBox>(key).Text = value;

    public static void SetCheckBoxValue(string key, bool value) =>
        Context.RequireControl<CheckBox>(key).IsChecked = value;

    public static void SetComboBoxIndex(string key, double value) =>
        Context.RequireControl<ComboBox>(key).SelectedIndex = ToInteger(value, nameof(value));

    public static void SetSliderValue(string key, double value) =>
        Context.RequireControl<Slider>(key).Value = value;

    public static void TraceControlIdentities(string stage)
    {
        Context.EnsureOwnerThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        var root = Context.CurrentRoot
            ?? throw new InvalidOperationException("No desktop root is active.");
        Context.Recorder.Record(stage, detail: root.GetKeyedControlIdentities());
    }

    public static bool IsRefAttached(DesktopRef reference)
    {
        Context.EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(reference);
        return reference.IsAttached;
    }

    public static void QueueMicrotask(Action callback) =>
        Context.ScheduleGuestMicrotask(callback);

    public static void Trace(string stage)
    {
        Context.EnsureOwnerThread();
        Context.Recorder.Record(stage);
    }

    public static Task<object?> CompleteOffThreadAsync()
    {
        Context.EnsureOwnerThread();
        Context.Recorder.Record("task-requested");
        return Task.Run(() =>
        {
            Thread.Sleep(25);
            Context.Recorder.Record("task-complete-off-thread", requireOwnerThread: false);
            return (object?)1d;
        });
    }

    public static void BeginOffThreadTask(Action continuation)
    {
        Context.EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(continuation);
        Context.Recorder.Record("task-requested");
        _ = Task.Run(() =>
        {
            Thread.Sleep(25);
            Context.Recorder.Record("task-complete-off-thread", requireOwnerThread: false);
            Context.DispatchGuestCallback(continuation);
        });
    }

    private static DesktopRuntimeContext Context => DesktopBridge.RequireContext();

    private static int ToInteger(double value, string name)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new ArgumentOutOfRangeException(name, "The value must be a finite 32-bit integer.");
        return (int)value;
    }
}
