using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class IndirectEvalRuntimeFeatureTests
{
    [Theory]
    [InlineData("(0, eval)")]
    [InlineData("(eval)")]
    [InlineData("alias")]
    public void AsyncLiteralBodiesSelectPromiseRuntimeBeforeCallableDiscovery(string callee)
    {
        string source = "const alias = eval; " + callee +
            "('(async function(){ await 0; console.log(7); })()');";
        var statements = TestHarness.ParseOrThrow(source);
        Assert.True(new RuntimeFeatureDetector().Detect(statements).UsesPromise);
        // #3711 owns compilation/runtime selection; saved execution is covered
        // separately by #3712's callable AST identity regression.
        Assert.NotEmpty(TestHarness.CompileUncheckedAndVerify(source));
    }
}
