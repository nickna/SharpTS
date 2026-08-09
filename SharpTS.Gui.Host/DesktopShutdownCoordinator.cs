#pragma warning disable SHARPTS_HOSTING001

using Avalonia.Controls;
using SharpTS.Hosting;

namespace SharpTS.Gui.Host;

internal sealed class DesktopShutdownCoordinator
{
    private readonly Func<IGuestRuntime?> _getGuest;
    private readonly Action<Action> _post;
    private readonly Action<int> _shutdownLifetime;
    private readonly Action<Exception> _reportFailure;
    private readonly Action? _ensureCleanup;
    private readonly object _gate = new();
    private readonly Dictionary<Window, EventHandler<WindowClosingEventArgs>> _windowHandlers = [];
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ShutdownRequest? _request;

    public DesktopShutdownCoordinator(
        Func<IGuestRuntime?> getGuest,
        Action<Action> post,
        Action<int> shutdownLifetime,
        Action<Exception> reportFailure,
        Action? ensureCleanup = null)
    {
        _getGuest = getGuest ?? throw new ArgumentNullException(nameof(getGuest));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _shutdownLifetime = shutdownLifetime ?? throw new ArgumentNullException(nameof(shutdownLifetime));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        _ensureCleanup = ensureCleanup;
    }

    public bool IsShutdownStarted
    {
        get
        {
            lock (_gate)
                return _request is not null;
        }
    }

    public Task Completion => _completion.Task;

    public void AttachWindow(
        Window window,
        Func<bool>? cancelClose = null,
        Func<bool>? shouldRequestShutdown = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
        {
            if (_request is not null || _windowHandlers.ContainsKey(window))
                return;

            EventHandler<WindowClosingEventArgs> handler = (_, eventArgs) =>
                OnWindowClosing(eventArgs, cancelClose, shouldRequestShutdown);
            _windowHandlers.Add(window, handler);
            window.Closing += handler;
        }
    }

    public bool RequestShutdown(SharpTSHostedShutdownReason reason, int exitCode)
    {
        ShutdownRequest request;
        lock (_gate)
        {
            if (_request is not null)
                return false;

            request = new ShutdownRequest(reason, exitCode);
            _request = request;
        }

        try
        {
            _post(() => _ = CompleteShutdownAsync(request));
        }
        catch (Exception exception)
        {
            CompleteAfterSchedulingFailure(exception, exitCode);
        }
        return true;
    }

    private void OnWindowClosing(
        WindowClosingEventArgs eventArgs,
        Func<bool>? cancelClose,
        Func<bool>? shouldRequestShutdown)
    {
        if (eventArgs.Cancel)
            return;
        if (cancelClose?.Invoke() == true)
        {
            eventArgs.Cancel = true;
            return;
        }
        if (IsShutdownStarted)
        {
            eventArgs.Cancel = true;
            return;
        }

        if (shouldRequestShutdown?.Invoke() == false)
            return;

        if (RequestShutdown(SharpTSHostedShutdownReason.HostRequested, 0))
            eventArgs.Cancel = true;
    }

    private async Task CompleteShutdownAsync(ShutdownRequest request)
    {
        int exitCode = request.ExitCode;
        try
        {
            lock (_gate)
                DetachWindowHandlersUnderLock();
            IGuestRuntime? guest = _getGuest();
            if (guest is not null)
                await guest.ShutdownAsync(request.Reason, request.ExitCode);
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _reportFailure(exception);
        }
        finally
        {
            EnsureHostCleanup(ref exitCode);
            try
            {
                _shutdownLifetime(exitCode);
            }
            catch (Exception exception)
            {
                _reportFailure(exception);
            }
            _completion.TrySetResult();
        }
    }

    private void CompleteAfterSchedulingFailure(Exception exception, int exitCode)
    {
        _reportFailure(exception);
        int effectiveExitCode = exitCode == 0 ? 1 : exitCode;
        EnsureHostCleanup(ref effectiveExitCode);
        try
        {
            lock (_gate)
                DetachWindowHandlersUnderLock();
            _shutdownLifetime(effectiveExitCode);
        }
        catch (Exception lifetimeException)
        {
            _reportFailure(lifetimeException);
        }
        _completion.TrySetResult();
    }

    private void EnsureHostCleanup(ref int exitCode)
    {
        if (_ensureCleanup is null)
            return;
        try
        {
            _ensureCleanup();
        }
        catch (Exception exception)
        {
            exitCode = 1;
            _reportFailure(exception);
        }
    }

    private void DetachWindowHandlersUnderLock()
    {
        foreach ((Window window, EventHandler<WindowClosingEventArgs> handler) in _windowHandlers)
            window.Closing -= handler;
        _windowHandlers.Clear();
    }

    private sealed record ShutdownRequest(SharpTSHostedShutdownReason Reason, int ExitCode);
}
