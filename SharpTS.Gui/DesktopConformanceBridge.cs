using Avalonia.Controls;

namespace SharpTS.Gui;

public static class DesktopConformanceBridge
{
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
