using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Defines the "ClusterTests" collection. Disables parallelization so cluster tests
/// (which fork worker threads and bind real OS ports via ClusterSingleton) never run
/// concurrently with the rest of the suite — prevents the CPU/port contention that times
/// out fork+port-bind tests under full-suite parallel load (issue #747).
/// </summary>
/// <remarks>
/// Without a matching CollectionDefinition, the <c>[Collection("ClusterTests")]</c> on
/// <c>ClusterModuleTests</c> binds to an implicit, parallelizable collection: its tests run
/// sequentially relative to each other (they share the <c>ClusterSingleton</c> global) but the
/// whole collection still runs concurrently with every other collection. This definition makes
/// xUnit schedule it in the non-parallel phase instead.
/// </remarks>
[CollectionDefinition("ClusterTests", DisableParallelization = true)]
public class ClusterTestsCollection
{
}
