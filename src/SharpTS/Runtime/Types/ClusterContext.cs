namespace SharpTS.Runtime.Types;

/// <summary>
/// Thread-local state to distinguish primary vs worker threads in the cluster module.
/// Each cluster worker thread sets these fields so that code running on that thread
/// knows it's in a worker context and can access IPC queues.
/// </summary>
public static class ClusterContext
{
    /// <summary>
    /// Whether the current thread is a cluster worker.
    /// </summary>
    [ThreadStatic]
    public static bool IsWorker;

    /// <summary>
    /// The worker ID assigned to the current thread (only valid when IsWorker is true).
    /// </summary>
    [ThreadStatic]
    public static double WorkerId;

    /// <summary>
    /// Queue for messages from primary to this worker (only valid when IsWorker is true).
    /// </summary>
    [ThreadStatic]
    public static System.Collections.Concurrent.BlockingCollection<object?>? PrimaryToWorkerQueue;

    /// <summary>
    /// Queue for messages from this worker to primary (only valid when IsWorker is true).
    /// </summary>
    [ThreadStatic]
    public static System.Collections.Concurrent.BlockingCollection<object?>? WorkerToPrimaryQueue;

    /// <summary>
    /// Cancellation token for the current worker thread.
    /// </summary>
    [ThreadStatic]
    public static CancellationToken CancellationToken;

    /// <summary>
    /// Reference to the worker object for this thread (only valid when IsWorker is true).
    /// Used by process.send() to route messages back to the parent.
    /// </summary>
    [ThreadStatic]
    public static SharpTSClusterWorker? CurrentWorker;

    /// <summary>
    /// Whether the current thread is the primary (not a cluster worker).
    /// </summary>
    public static bool IsPrimary => !IsWorker;
}
