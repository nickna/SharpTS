namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a JavaScript/TypeScript Timeout handle.
/// Returned by setTimeout() and used by clearTimeout() for cancellation.
/// </summary>
/// <remarks>
/// Follows Node.js-style Timeout object behavior with:
/// - Unique ID for each timeout (thread-safe generation)
/// - CancellationTokenSource for cancellation support
/// - Task reference for completion tracking
/// - ref()/unref() methods for controlling program exit behavior
/// </remarks>
public class SharpTSTimeout
{
    private static int _nextId = 0;
    private readonly int _id;
    private readonly CancellationTokenSource _cts;
    private Task? _task;
    private bool _hasRef = true;

    /// <summary>
    /// Creates a new timeout handle with a unique ID and cancellation support.
    /// </summary>
    /// <param name="cts">The cancellation token source for this timeout.</param>
    public SharpTSTimeout(CancellationTokenSource cts)
    {
        _id = Interlocked.Increment(ref _nextId);
        _cts = cts;
    }

    /// <summary>
    /// Creates a new timeout handle with a unique ID, cancellation support, and task reference.
    /// </summary>
    /// <param name="cts">The cancellation token source for this timeout.</param>
    /// <param name="task">The task representing the delayed execution.</param>
    public SharpTSTimeout(CancellationTokenSource cts, Task task)
    {
        _id = Interlocked.Increment(ref _nextId);
        _cts = cts;
        _task = task;
    }

    /// <summary>
    /// Gets the unique identifier for this timeout.
    /// </summary>
    public int Id => _id;

    /// <summary>
    /// Gets the cancellation token source for this timeout.
    /// </summary>
    public CancellationTokenSource CancellationTokenSource => _cts;

    /// <summary>
    /// Gets or sets the task representing the delayed execution.
    /// </summary>
    public Task? Task
    {
        get => _task;
        set => _task = value;
    }

    /// <summary>
    /// Gets whether this timeout has been cancelled.
    /// </summary>
    public bool IsCancelled => _cts.IsCancellationRequested;

    /// <summary>
    /// Gets whether this timeout is keeping the program alive.
    /// When true, the program will wait for this timeout before exiting.
    /// </summary>
    public bool HasRef => _hasRef;

    /// <summary>
    /// Cancels the timeout, preventing the callback from being executed.
    /// Safe to call multiple times.
    /// </summary>
    public void Cancel()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    /// <summary>
    /// Marks this timeout as keeping the program alive.
    /// Returns this for chaining: setTimeout(fn, 1000).ref()
    /// </summary>
    /// <returns>This timeout object for method chaining.</returns>
    public SharpTSTimeout Ref()
    {
        _hasRef = true;
        return this;
    }

    /// <summary>
    /// Marks this timeout as NOT keeping the program alive.
    /// The program may exit before this timeout fires if no other work is pending.
    /// Returns this for chaining: setTimeout(fn, 1000).unref()
    /// </summary>
    /// <returns>This timeout object for method chaining.</returns>
    public SharpTSTimeout Unref()
    {
        _hasRef = false;
        return this;
    }

    public override bool Equals(object? obj) =>
        obj is SharpTSTimeout other && _id == other._id;

    public override int GetHashCode() => _id;

    public override string ToString() => $"Timeout {{ _id: {_id} }}";
}
