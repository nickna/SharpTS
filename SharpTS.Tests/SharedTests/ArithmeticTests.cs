using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for arithmetic operations. Runs against both interpreter and compiler.
/// </summary>
public class ArithmeticTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Addition_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10 + 5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Subtraction_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 20 - 8;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Multiplication_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(6 * 7);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Division_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(100 / 4);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Modulo_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(17 % 5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ComplexExpression_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let result: number = (10 + 5) * 2 - 3;
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("27\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_LessThan_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 < 10);
            console.log(10 < 5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_GreaterThan_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(10 > 5);
            console.log(5 > 10);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_Equality_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 == 5);
            console.log(5 == 6);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_NotEqual_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 != 6);
            console.log(5 != 5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_LessThanOrEqual_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 <= 5);
            console.log(5 <= 10);
            console.log(10 <= 5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Comparison_GreaterThanOrEqual_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 >= 5);
            console.log(10 >= 5);
            console.log(5 >= 10);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UnaryMinus_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(-x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LogicalNot_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(!true);
            console.log(!false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LogicalAnd_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(true && true);
            console.log(true && false);
            console.log(false && true);
            console.log(false && false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LogicalOr_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(true || true);
            console.log(true || false);
            console.log(false || true);
            console.log(false || false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\nfalse\n", output);
    }
}
