using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Tests in this collection create and terminate real operating-system process trees. Running
/// this small collection in xUnit's non-parallel phase prevents CPU-heavy compiler collections
/// from turning process readiness deadlines into suite-load assertions while preserving
/// aggressive parallelism for the rest of the test assembly.
/// </summary>
[CollectionDefinition("ExternalProcessTests", DisableParallelization = true)]
public sealed class ExternalProcessTestsCollection
{
}
