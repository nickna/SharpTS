namespace SharpTS.Runtime.EventLoop;

/// <summary>
/// Interface for async resources that keep the event loop alive.
/// Any resource that should prevent the process from exiting while active
/// should implement this interface (HTTP servers, file watchers, timers, etc.).
/// </summary>
public interface IAsyncHandle
{
    /// <summary>
    /// Whether this handle is currently active and should keep the event loop running.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Event raised when the handle's active state changes.
    /// The event loop uses this to wake up and re-evaluate whether to continue running.
    /// </summary>
    event Action? OnStateChanged;
}
