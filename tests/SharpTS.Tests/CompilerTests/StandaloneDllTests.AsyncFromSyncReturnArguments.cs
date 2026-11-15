using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_AsyncFromSyncReturnArguments()
        => AssertStandaloneAsyncGeneratorProtocol(AsyncFromSyncReturnArgumentTests.Source,
            AsyncFromSyncReturnArgumentTests.Expected);
}
