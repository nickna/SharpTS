using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_AsyncGeneratorInitialDelegationArgument()
        => AssertStandaloneAsyncGeneratorProtocol(AsyncGeneratorInitialDelegationArgumentTests.Source,
            AsyncGeneratorInitialDelegationArgumentTests.OneProtocolExpected +
            AsyncGeneratorInitialDelegationArgumentTests.OneProtocolExpected);
}
