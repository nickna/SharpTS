using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class VoidOperatorTests
{
    [Fact]
    public void Void_ReturnsUndefined()
    {
        var source = """
            console.log(void 0 === undefined);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Void_EvaluatesExpressionForSideEffects()
    {
        var source = """
            let x: number = 0;
            void (x = 5);
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void Void_WithFunctionCall()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Void_WithNumber()
    {
        var source = """
            console.log(void 42 === undefined);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Void_WithExpression()
    {
        var source = """
            console.log(void (1 + 2) === undefined);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Void_InConditional()
    {
        var source = """
            let x: any = void 0;
            if (x === undefined) {
                console.log("is undefined");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("is undefined\n", output);
    }
}
