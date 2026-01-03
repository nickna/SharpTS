using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class MathBuiltInTests
{
    // Math Constants
    [Fact]
    public void Math_PI_ReturnsCorrectValue()
    {
        var source = """
            console.log(Math.PI > 3.14);
            console.log(Math.PI < 3.15);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Math_E_ReturnsCorrectValue()
    {
        var source = """
            console.log(Math.E > 2.71);
            console.log(Math.E < 2.72);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    // Math Functions
    [Fact]
    public void Math_Abs_ReturnsAbsoluteValue()
    {
        var source = """
            console.log(Math.abs(-5));
            console.log(Math.abs(5));
            console.log(Math.abs(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n5\n0\n", output);
    }

    [Fact]
    public void Math_Floor_RoundsDown()
    {
        var source = """
            console.log(Math.floor(4.7));
            console.log(Math.floor(4.2));
            console.log(Math.floor(-4.2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n4\n-5\n", output);
    }

    [Fact]
    public void Math_Ceil_RoundsUp()
    {
        var source = """
            console.log(Math.ceil(4.3));
            console.log(Math.ceil(4.7));
            console.log(Math.ceil(-4.7));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n5\n-4\n", output);
    }

    [Fact]
    public void Math_Round_RoundsToNearest()
    {
        var source = """
            console.log(Math.round(4.4));
            console.log(Math.round(4.5));
            console.log(Math.round(4.6));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n5\n5\n", output);
    }

    [Fact]
    public void Math_Sqrt_ReturnsSquareRoot()
    {
        var source = """
            console.log(Math.sqrt(16));
            console.log(Math.sqrt(9));
            console.log(Math.sqrt(1));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n3\n1\n", output);
    }

    [Fact]
    public void Math_Pow_ReturnsPower()
    {
        var source = """
            console.log(Math.pow(2, 3));
            console.log(Math.pow(3, 2));
            console.log(Math.pow(2, 0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n9\n1\n", output);
    }

    [Fact]
    public void Math_Min_ReturnsMinimum()
    {
        var source = """
            console.log(Math.min(1, 2, 3));
            console.log(Math.min(5, 2, 8));
            console.log(Math.min(-1, -5, 0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n-5\n", output);
    }

    [Fact]
    public void Math_Max_ReturnsMaximum()
    {
        var source = """
            console.log(Math.max(1, 2, 3));
            console.log(Math.max(5, 2, 8));
            console.log(Math.max(-1, -5, 0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n8\n0\n", output);
    }

    [Fact]
    public void Math_Sign_ReturnsSign()
    {
        var source = """
            console.log(Math.sign(-5));
            console.log(Math.sign(0));
            console.log(Math.sign(5));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("-1\n0\n1\n", output);
    }

    [Fact]
    public void Math_Trunc_RemovesDecimal()
    {
        var source = """
            console.log(Math.trunc(4.7));
            console.log(Math.trunc(-4.7));
            console.log(Math.trunc(4.2));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n-4\n4\n", output);
    }

    // Trigonometric Functions
    [Fact]
    public void Math_Sin_ReturnsCorrectValue()
    {
        var source = """
            console.log(Math.sin(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Math_Cos_ReturnsCorrectValue()
    {
        var source = """
            console.log(Math.cos(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Math_Tan_ReturnsCorrectValue()
    {
        var source = """
            console.log(Math.tan(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    // Exponential and Logarithmic Functions
    [Fact]
    public void Math_Log_ReturnsNaturalLog()
    {
        var source = """
            console.log(Math.log(1));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Math_Exp_ReturnsExponential()
    {
        var source = """
            console.log(Math.exp(0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", output);
    }

    // Random
    [Fact]
    public void Math_Random_ReturnsValueInRange()
    {
        var source = """
            let r: number = Math.random();
            console.log(r >= 0);
            console.log(r < 1);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }
}
