using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Math built-in constants and methods. Runs against both interpreter and compiler.
/// </summary>
public class MathBuiltInTests
{
    #region Math Constants

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_PI_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.PI > 3.14);
            console.log(Math.PI < 3.15);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_E_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.E > 2.71);
            console.log(Math.E < 2.72);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Math Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Abs_ReturnsAbsoluteValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.abs(-5));
            console.log(Math.abs(5));
            console.log(Math.abs(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n5\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Floor_RoundsDown(ExecutionMode mode)
    {
        var source = """
            console.log(Math.floor(4.7));
            console.log(Math.floor(4.2));
            console.log(Math.floor(-4.2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n4\n-5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Ceil_RoundsUp(ExecutionMode mode)
    {
        var source = """
            console.log(Math.ceil(4.3));
            console.log(Math.ceil(4.7));
            console.log(Math.ceil(-4.7));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n5\n-4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Round_RoundsToNearest(ExecutionMode mode)
    {
        var source = """
            console.log(Math.round(4.4));
            console.log(Math.round(4.5));
            console.log(Math.round(4.6));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n5\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Round_JSParity_NegativeHalfValues(ExecutionMode mode)
    {
        // JavaScript rounds half-values toward +infinity
        // -2.5 rounds to -2 (not -3)
        // -1.5 rounds to -1 (not -2)
        var source = """
            console.log(Math.round(2.5));
            console.log(Math.round(-2.5));
            console.log(Math.round(1.5));
            console.log(Math.round(-1.5));
            console.log(Math.round(0.5));
            console.log(Math.round(-0.5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n-2\n2\n-1\n1\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Sqrt_ReturnsSquareRoot(ExecutionMode mode)
    {
        var source = """
            console.log(Math.sqrt(16));
            console.log(Math.sqrt(9));
            console.log(Math.sqrt(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n3\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Pow_ReturnsPower(ExecutionMode mode)
    {
        var source = """
            console.log(Math.pow(2, 3));
            console.log(Math.pow(3, 2));
            console.log(Math.pow(2, 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n9\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Min_ReturnsMinimum(ExecutionMode mode)
    {
        var source = """
            console.log(Math.min(1, 2, 3));
            console.log(Math.min(5, 2, 8));
            console.log(Math.min(-1, -5, 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n-5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Max_ReturnsMaximum(ExecutionMode mode)
    {
        var source = """
            console.log(Math.max(1, 2, 3));
            console.log(Math.max(5, 2, 8));
            console.log(Math.max(-1, -5, 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n8\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Sign_ReturnsSign(ExecutionMode mode)
    {
        var source = """
            console.log(Math.sign(-5));
            console.log(Math.sign(0));
            console.log(Math.sign(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n0\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Trunc_RemovesDecimal(ExecutionMode mode)
    {
        var source = """
            console.log(Math.trunc(4.7));
            console.log(Math.trunc(-4.7));
            console.log(Math.trunc(4.2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n-4\n4\n", output);
    }

    #endregion

    #region Trigonometric Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Sin_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.sin(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Cos_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.cos(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Tan_ReturnsCorrectValue(ExecutionMode mode)
    {
        var source = """
            console.log(Math.tan(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Exponential and Logarithmic Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Log_ReturnsNaturalLog(ExecutionMode mode)
    {
        var source = """
            console.log(Math.log(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Exp_ReturnsExponential(ExecutionMode mode)
    {
        var source = """
            console.log(Math.exp(0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    #endregion

    #region Random

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Math_Random_ReturnsValueInRange(ExecutionMode mode)
    {
        var source = """
            let r: number = Math.random();
            console.log(r >= 0);
            console.log(r < 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion
}
