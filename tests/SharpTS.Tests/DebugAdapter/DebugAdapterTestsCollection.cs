using Xunit;

namespace SharpTS.Tests.DebugAdapter;

/// <summary>
/// Raw DAP sessions own child processes and controller tests deliberately park interpreter
/// execution while awaiting debugger commands. Running them in the non-parallel phase keeps
/// xUnit's aggressive CPU-bound collections from delaying protocol I/O and cancellation timers.
/// </summary>
[CollectionDefinition("DebugAdapterTests", DisableParallelization = true)]
public sealed class DebugAdapterTestsCollection
{
}
