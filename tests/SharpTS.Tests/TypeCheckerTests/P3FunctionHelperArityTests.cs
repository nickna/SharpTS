using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3FunctionHelperArityTests
{
    [Theory]
    [InlineData("call")]
    [InlineData("apply")]
    [InlineData("bind")]
    public void HelpersRequireTheReceiverArgument(string helper)
    {
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow($"declare function f(): number; f.{helper}();")).Diagnostics;
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2554");
    }

    [Fact]
    public void ExplicitReceiversRestArgumentsAndResultsRemainAvailable()
    {
        const string source = """
            declare function f(): number;
            const a: number = f.call(undefined, 1, 'two', true);
            const b: number = f.apply(null);
            const c: number = f.apply({}, [1, 'two']);
            const d: number = f.bind({}, 1, 'two')(false, 3);
            """;
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(
            TestHarness.ParseOrThrow(source)).Diagnostics);
    }
}
