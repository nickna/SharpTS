#pragma warning disable SHARPTS_HOSTING001

using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SharpTS.Hosting;

namespace SharpTS.Gui.Host;

internal sealed class AvaloniaHostDispatcher : ISharpTSHostDispatcher
{
    private readonly Dispatcher _dispatcher;

    public AvaloniaHostDispatcher(Dispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action hostTurn) =>
        _dispatcher.Post(hostTurn, DispatcherPriority.Background);

    public ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn)
    {
        ArgumentNullException.ThrowIfNull(hostTurn);
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = delay <= TimeSpan.Zero ? TimeSpan.FromTicks(1) : delay,
        };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            hostTurn();
        };
        timer.Tick += handler;
        timer.Start();
        return new AvaloniaScheduledWork(timer, handler);
    }

    private sealed class AvaloniaScheduledWork(
        DispatcherTimer timer,
        EventHandler handler) : ISharpTSScheduledWork
    {
        private int _cancelled;

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _cancelled, 1) != 0)
                return;
            timer.Stop();
            timer.Tick -= handler;
        }

        public void Dispose() => Cancel();
    }
}

internal sealed class AvaloniaHostLifetime(
    ClassicDesktopStyleApplicationLifetime lifetime) : ISharpTSHostLifetime
{
    public void RequestExit(int exitCode) => lifetime.Shutdown(exitCode);
}

internal sealed class DelegateHostedErrorSink(Action<SharpTSHostedError> report)
    : ISharpTSHostedErrorSink
{
    public void Report(SharpTSHostedError error) => report(error);
}
