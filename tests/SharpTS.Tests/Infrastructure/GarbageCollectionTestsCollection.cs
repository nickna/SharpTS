using Xunit;

namespace SharpTS.Tests.Infrastructure;

// Full-heap collection pauses affect every thread in the test host. Keep these
// lifetime checks separate from tests with compilation/execution deadlines.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GarbageCollectionTestsCollection
{
    public const string Name = "GarbageCollectionTests";
}
