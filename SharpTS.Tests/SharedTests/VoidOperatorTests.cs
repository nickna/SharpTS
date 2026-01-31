using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the void operator. Runs against both interpreter and compiler.
/// </summary>
public class VoidOperatorTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            console.log(void 0 === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_EvaluatesExpressionForSideEffects(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            void (x = 5);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_WithFunctionCall(ExecutionMode mode)
    {
        var source = """
            let called: boolean = false;
            function setFlag(): void {
                called = true;
            }
            let result = void setFlag();
            console.log(called);
            console.log(result === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_WithNumber(ExecutionMode mode)
    {
        var source = """
            console.log(void 42 === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_WithExpression(ExecutionMode mode)
    {
        var source = """
            console.log(void (1 + 2) === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Void_InConditional(ExecutionMode mode)
    {
        var source = """
            let x: any = void 0;
            if (x === undefined) {
                console.log("is undefined");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("is undefined\n", output);
    }
}
