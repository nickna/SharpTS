using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ArithmeticTests
{
    [Fact]
    public void Addition_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 10 + 5;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void Subtraction_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 20 - 8;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("12\n", output);
    }

    [Fact]
    public void Multiplication_ReturnsCorrectResult()
    {
        var source = """
            console.log(6 * 7);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Division_ReturnsCorrectResult()
    {
        var source = """
            console.log(100 / 4);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("25\n", output);
    }

    [Fact]
    public void Modulo_ReturnsCorrectResult()
    {
        var source = """
            console.log(17 % 5);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void ComplexExpression_ReturnsCorrectResult()
    {
        var source = """
            let result: number = (10 + 5) * 2 - 3;
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("27\n", output);
    }

    [Fact]
    public void Comparison_LessThan_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 < 10);
            console.log(10 < 5);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Comparison_GreaterThan_ReturnsCorrectResult()
    {
        var source = """
            console.log(10 > 5);
            console.log(5 > 10);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Comparison_Equality_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 == 5);
            console.log(5 == 6);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Comparison_NotEqual_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 != 6);
            console.log(5 != 5);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Comparison_LessThanOrEqual_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 <= 5);
            console.log(5 <= 10);
            console.log(10 <= 5);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Comparison_GreaterThanOrEqual_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 >= 5);
            console.log(10 >= 5);
            console.log(5 >= 10);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Fact]
    public void UnaryMinus_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 5;
            console.log(-x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-5\n", output);
    }

    [Fact]
    public void LogicalNot_ReturnsCorrectResult()
    {
        var source = """
            console.log(!true);
            console.log(!false);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void LogicalAnd_ReturnsCorrectResult()
    {
        var source = """
            console.log(true && true);
            console.log(true && false);
            console.log(false && true);
            console.log(false && false);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    [Fact]
    public void LogicalOr_ReturnsCorrectResult()
    {
        var source = """
            console.log(true || true);
            console.log(true || false);
            console.log(false || true);
            console.log(false || false);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\nfalse\n", output);
    }
}
