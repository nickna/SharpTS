using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Number static properties, methods, and instance methods. Runs against both interpreter and compiler.
/// </summary>
public class NumberTests
{
    #region Static Properties

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_MAX_VALUE_ReturnsLargeNumber(ExecutionMode mode)
    {
        // Use a comparison that doesn't require scientific notation parsing
        var source = "console.log(Number.MAX_VALUE > 1000000000000);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_MIN_VALUE_ReturnsSmallPositive(ExecutionMode mode)
    {
        // MIN_VALUE is the smallest positive number (like double.Epsilon)
        var source = "console.log(Number.MIN_VALUE > 0 && Number.MIN_VALUE < 1);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_NaN_IsNaN(ExecutionMode mode)
    {
        var source = "console.log(Number.isNaN(Number.NaN));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_POSITIVE_INFINITY_IsInfinity(ExecutionMode mode)
    {
        var source = "console.log(Number.POSITIVE_INFINITY > Number.MAX_VALUE);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_NEGATIVE_INFINITY_IsNegativeInfinity(ExecutionMode mode)
    {
        // Verify NEGATIVE_INFINITY is less than any large negative number
        var source = "console.log(Number.NEGATIVE_INFINITY < -1000000000000);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_MAX_SAFE_INTEGER_HasCorrectValue(ExecutionMode mode)
    {
        var source = "console.log(Number.MAX_SAFE_INTEGER === 9007199254740991);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_MIN_SAFE_INTEGER_HasCorrectValue(ExecutionMode mode)
    {
        var source = "console.log(Number.MIN_SAFE_INTEGER === -9007199254740991);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_EPSILON_IsSmallPositive(ExecutionMode mode)
    {
        // EPSILON is 2^-52, a very small positive number
        var source = "console.log(Number.EPSILON > 0 && Number.EPSILON < 0.001);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Static Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_parseInt_ParsesIntegers(ExecutionMode mode)
    {
        var source = """
            console.log(Number.parseInt("42"));
            console.log(Number.parseInt("42.9"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_parseInt_WithRadix(ExecutionMode mode)
    {
        var source = """
            console.log(Number.parseInt("ff", 16));
            console.log(Number.parseInt("101", 2));
            console.log(Number.parseInt("77", 8));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n5\n63\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_parseFloat_ParsesFloats(ExecutionMode mode)
    {
        var source = """
            console.log(Number.parseFloat("3.14"));
            console.log(Number.parseFloat("42"));
            console.log(Number.parseFloat("3.14abc"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3.14\n42\n3.14\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_isNaN_StrictBehavior(ExecutionMode mode)
    {
        var source = """
            console.log(Number.isNaN(Number.NaN));
            console.log(Number.isNaN(42));
            console.log(Number.isNaN("hello"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_isFinite_StrictBehavior(ExecutionMode mode)
    {
        var source = """
            console.log(Number.isFinite(42));
            console.log(Number.isFinite(Number.POSITIVE_INFINITY));
            console.log(Number.isFinite(Number.NaN));
            console.log(Number.isFinite("42"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_isInteger_DetectsIntegers(ExecutionMode mode)
    {
        var source = """
            console.log(Number.isInteger(42));
            console.log(Number.isInteger(42.0));
            console.log(Number.isInteger(42.5));
            console.log(Number.isInteger(Number.POSITIVE_INFINITY));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_isSafeInteger_DetectsSafeIntegers(ExecutionMode mode)
    {
        var source = """
            console.log(Number.isSafeInteger(42));
            console.log(Number.isSafeInteger(Number.MAX_SAFE_INTEGER));
            console.log(Number.isSafeInteger(Number.MAX_SAFE_INTEGER + 1));
            console.log(Number.isSafeInteger(42.5));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    #endregion

    #region Instance Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_toFixed_FormatsDecimals(ExecutionMode mode)
    {
        var source = """
            let n: number = 3.14159;
            console.log(n.toFixed(2));
            console.log((42).toFixed(2));
            console.log((3.1).toFixed(0));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3.14\n42.00\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_toPrecision_FormatsPrecision(ExecutionMode mode)
    {
        var source = """
            let n: number = 123.456;
            console.log(n.toPrecision(4));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Contains("123.5", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_toExponential_FormatsExponential(ExecutionMode mode)
    {
        var source = """
            let n: number = 12345;
            console.log(n.toExponential(2));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Contains("e+", output.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Number_toString_WithRadix(ExecutionMode mode)
    {
        var source = """
            console.log((255).toString(16));
            console.log((7).toString(2));
            console.log((100).toString(10));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ff\n111\n100\n", output);
    }

    #endregion

    #region Global Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Global_parseInt_Works(ExecutionMode mode)
    {
        var source = """
            console.log(parseInt("42"));
            console.log(parseInt("ff", 16));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n255\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Global_parseFloat_Works(ExecutionMode mode)
    {
        var source = """
            console.log(parseFloat("3.14"));
            console.log(parseFloat("42.5abc"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3.14\n42.5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Global_isNaN_CoercesBehavior(ExecutionMode mode)
    {
        var source = """
            console.log(isNaN(Number.NaN));
            console.log(isNaN(42));
            console.log(isNaN("hello"));
            console.log(isNaN("42"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Global_isFinite_CoercesBehavior(ExecutionMode mode)
    {
        var source = """
            console.log(isFinite(42));
            console.log(isFinite(Number.POSITIVE_INFINITY));
            console.log(isFinite("42"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    #endregion
}
