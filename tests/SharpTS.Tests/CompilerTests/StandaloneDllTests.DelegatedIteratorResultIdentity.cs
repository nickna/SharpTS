using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_DelegatedIteratorResultIdentity()
        => AssertStandaloneAsyncGeneratorProtocol(DelegatedIteratorResultIdentityTests.Source,
            DelegatedIteratorResultIdentityTests.Expected);

    [Fact]
    public void Isolated_DelegatedIteratorResultConsumers()
        => AssertStandaloneAsyncGeneratorProtocol(DelegatedIteratorResultIdentityTests.ConsumerSource,
            DelegatedIteratorResultIdentityTests.ConsumerExpected);
}
