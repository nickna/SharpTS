using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class LiftedGeneratorClassOrderTests
{
    [Theory]
    [InlineData("function make() { const read = function*() { yield Later; }; class Later {} return read; }")]
    [InlineData("function make() { class Later {} (function*() { yield Later; })(); }")]
    public void DeferredAndAfterDeclarationGeneratorsRemainValid(string source)
        => new TypeChecker().Check(TestHarness.ParseOrThrow(source));

    [Fact]
    public void RecoveryReportsTheOriginalGeneratorCallOnce()
    {
        const string source = "function make() { (function*() { yield Later; })(); class Later {} }";
        var result = new TypeChecker().CheckWithRecovery(TestHarness.ParseOrThrow(source));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2449");
    }
}
