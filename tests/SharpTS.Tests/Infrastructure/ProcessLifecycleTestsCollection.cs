using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Serialized collection for tests that register process-level listeners
/// (exit/beforeExit/warning/signal events). The interpreter's process object
/// is a process-wide singleton (SharpTSProcess.Instance) and the harness now
/// fires lifecycle events at event-loop drain, so listener registrations must
/// not overlap across concurrently running tests. Tests in this collection
/// reset the shared state via ProcessBuiltIns.ResetProcessState() before each
/// run (see ProcessLifecycleTests).
/// </summary>
[CollectionDefinition("ProcessLifecycleTests", DisableParallelization = true)]
public class ProcessLifecycleTestsCollection
{
}
