namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Control-flow exception representing a <c>worker.terminate()</c> abort injected into a
/// running worker thread (Node <c>worker_threads</c> semantics).
/// </summary>
/// <remarks>
/// Raised when a worker's <see cref="System.Threading.CancellationToken"/> is cancelled by
/// <c>Worker.Terminate()</c> while the worker is blocked in a synchronous runtime operation
/// that observes cancellation (notably <c>Atomics.wait</c>, which otherwise parks the worker
/// thread in <c>Monitor.Wait(..., Timeout.Infinite)</c> with no way to wake it). The exception
/// unwinds the worker thread so its host loop reaches its <c>finally</c> — emitting the
/// <c>exit</c> event and releasing the OS thread — instead of leaking until process exit.
///
/// Unlike <see cref="ThrowException"/>, this is NOT a guest <c>throw</c>: it must bypass guest
/// <c>catch</c> clauses (a worker cannot catch its own termination, matching V8). The
/// interpreter's block and <c>try</c>/<c>catch</c> executors recognize it and re-throw it ahead
/// of their generic <c>catch (Exception)</c> handlers — mirroring how
/// <see cref="GeneratorReturnException"/> bypasses the same frames — so it propagates silently
/// and uncatchably up to <c>SharpTSWorker.WorkerThreadMain</c>.
/// </remarks>
public sealed class WorkerTerminatedException : Exception
{
}
