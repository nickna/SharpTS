namespace SharpTS.Runtime.Types;

/// <summary>
/// Test-only latency injection for event-loop liveness regression tests.
/// </summary>
/// <remarks>
/// When <c>SHARPTS_TEST_STREAM_PARKED_RESUME_DELAY_MS</c> is set, the resume of a
/// parked stream read (async-iterator <c>next()</c>, pipeTo pump read) stalls that
/// long immediately after the parked task settles. Deterministically reproduces the
/// loaded-CI thread-pool scheduling delay that exposed the settled-but-not-yet-
/// enqueued continuation window (#1211/#1212): with the resume hopping through the
/// thread pool the injected stall sits inside the window invisible to
/// <c>RunEventLoopCore</c>'s exit check and the program exits silently; with the
/// resume riding the interpreter's SynchronizationContext the stall runs on the
/// event-loop thread inside a queued callback, merely delaying output. The sleep is
/// deliberately synchronous — an awaited <c>Task.Delay</c> would itself be invisible
/// in-flight work and re-create the very window under test. Read per call so a test
/// can scope it with try/finally. Same env-var seam pattern as
/// SHARPTS_TEST_FS_ASYNC_DELAY_MS (#319).
/// </remarks>
internal static class EventLoopTestHooks
{
    internal static void ParkedResumeDelay()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("SHARPTS_TEST_STREAM_PARKED_RESUME_DELAY_MS"), out var ms) && ms > 0)
            Thread.Sleep(ms);
    }
}
