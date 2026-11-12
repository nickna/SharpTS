using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_AsyncFromSyncMissingThrowClosesAndRejects()
        => AssertStandaloneAsyncGeneratorProtocol(AsyncFromSyncMissingThrowTests.Source,
            AsyncFromSyncMissingThrowTests.Expected);
}
