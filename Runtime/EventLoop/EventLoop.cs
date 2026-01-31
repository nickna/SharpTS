namespace SharpTS.Runtime.EventLoop;

/// <summary>
/// Manages the event loop for async operations.
/// Keeps the process alive while there are active handles (servers, timers, etc.).
/// Uses efficient wait primitives instead of polling.
/// </summary>
public sealed class EventLoop : IDisposable
{
    private readonly List<IAsyncHandle> _handles = [];
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _wakeSignal = new(false);
    private volatile bool _isDisposed;

    /// <summary>
    /// Registers an async handle with the event loop.
    /// The event loop will keep running while this handle is active.
    /// </summary>
    public void Register(IAsyncHandle handle)
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (!_handles.Contains(handle))
            {
                _handles.Add(handle);
                handle.OnStateChanged += OnHandleStateChanged;
            }
        }
    }

    /// <summary>
    /// Unregisters an async handle from the event loop.
    /// </summary>
    public void Unregister(IAsyncHandle handle)
    {
        lock (_lock)
        {
            handle.OnStateChanged -= OnHandleStateChanged;
            _handles.Remove(handle);
        }

        // Wake the event loop to re-evaluate
        _wakeSignal.Set();
    }

    /// <summary>
    /// Called when any handle's state changes.
    /// </summary>
    private void OnHandleStateChanged()
    {
        // Wake the event loop to re-evaluate
        _wakeSignal.Set();
    }

    /// <summary>
    /// Checks if there are any active handles.
    /// </summary>
    public bool HasActiveHandles()
    {
        lock (_lock)
        {
            foreach (var handle in _handles)
            {
                if (handle.IsActive) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Runs the event loop until all handles are inactive or disposed.
    /// Uses efficient waiting instead of polling.
    /// </summary>
    /// <param name="onTick">Optional callback invoked on each iteration (for processing timers, etc.)</param>
    public void Run(Action? onTick = null)
    {
        while (!_isDisposed && HasActiveHandles())
        {
            // Process any pending work
            onTick?.Invoke();

            // Wait for a state change or timeout (100ms max to allow periodic timer processing)
            // The signal is set when handles change state
            _wakeSignal.Wait(100);
            _wakeSignal.Reset();
        }
    }

    /// <summary>
    /// Disposes the event loop and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        lock (_lock)
        {
            foreach (var handle in _handles)
            {
                handle.OnStateChanged -= OnHandleStateChanged;
            }
            _handles.Clear();
        }

        _wakeSignal.Set(); // Wake any waiting thread
        _wakeSignal.Dispose();
    }
}
