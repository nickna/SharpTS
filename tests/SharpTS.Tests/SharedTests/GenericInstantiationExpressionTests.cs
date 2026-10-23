using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericInstantiationExpressionTests
{
    public static readonly string Source = """
        let calls = 0;
        function identity<T>(value: T): T { calls++; return value; }
        const numberIdentity = identity<number>;
        console.log(numberIdentity === identity, calls);
        console.log(numberIdentity(7), calls);
        """.ReplaceLineEndings("\n") + "\n";

    public const string Expected = "true 0\n7 1\n";

    [Theory, ModeData]
    public void InstantiationRetainsIdentityUntilExplicitCall(ExecutionMode mode)
    {
        Assert.Empty(GenericInstantiationDiagnosticTests.CollectStrictModules(Source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(Source);
            Assert.Empty(errors);
            Assert.Equal(Expected, output);
        }
        else
        {
            Assert.Equal(Expected, TestHarness.RunInterpreted(Source));
        }
    }
}
