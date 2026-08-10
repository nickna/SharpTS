using System.Runtime.CompilerServices;
using SharpTS.Gui;

namespace SharpTS.Gui.ConformanceSupport;

/// <summary>Privileged hooks used only by the repository conformance suite.</summary>
public static class DesktopConformanceSupportBridge
{
    public static void CancelNextWindowClose() => Context.CancelNextWindowClose();

    public static void Trace(string stage)
    {
        Context.EnsureOwnerThread();
        Context.Recorder.Record(stage);
    }

    public static void QueueMicrotask(Action callback) => Context.ScheduleGuestMicrotask(callback);

    public static void AfterTrace(string stage, Action callback)
    {
        Context.EnsureOwnerThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentNullException.ThrowIfNull(callback);
        void Recorded(GuiTraceEvent item)
        {
            if (!string.Equals(item.Stage, stage, StringComparison.Ordinal))
                return;
            Context.Recorder.Recorded -= Recorded;
            Context.ScheduleGuestMicrotask(callback);
        }
        Context.Recorder.Recorded += Recorded;
    }

    public static void TraceControlIdentities(DesktopRoot root, string stage)
    {
        RequireRoot(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Context.Recorder.Record(stage, detail: root.GetKeyedControlIdentities());
    }

    public static double GetIdentity(DesktopRoot root, string key) =>
        RuntimeHelpers.GetHashCode(RequireControl(root, key));

    public static double GetActiveSubscriptionCount(DesktopRoot root)
    {
        RequireRoot(root);
        return root.ActiveSubscriptions;
    }

    public static void FailNextNativeSetter(DesktopRoot root, string key)
    {
        RequireRoot(root);
        root.FailNextSetter(key);
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

    private static object RequireControl(DesktopRoot root, string key)
    {
        RequireRoot(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return root.FindControl(key)
            ?? throw new InvalidOperationException($"No control with key '{key}' exists in the supplied desktop window.");
    }

    private static void RequireRoot(DesktopRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Context.EnsureOwnerThread();
        if (!Context.Roots.Contains(root) || root.IsDisposed)
            throw new ArgumentException("The desktop window is not active in the current GUI runtime.", nameof(root));
    }

    private static DesktopRuntimeContext Context => DesktopBridge.RequireContext();
}
