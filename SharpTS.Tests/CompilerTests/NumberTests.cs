using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class NumberTests
{
    // ========== Static Properties ==========

    [Fact]
    public void Number_MAX_VALUE_ReturnsLargeNumber()
    {
        // Use a comparison that doesn't require scientific notation parsing
        var source = "console.log(Number.MAX_VALUE > 1000000000000);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_MIN_VALUE_ReturnsSmallPositive()
    {
        // MIN_VALUE is the smallest positive number (like double.Epsilon)
        var source = "console.log(Number.MIN_VALUE > 0 && Number.MIN_VALUE < 1);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_NaN_IsNaN()
    {
        var source = "console.log(Number.isNaN(Number.NaN));";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_POSITIVE_INFINITY_IsInfinity()
    {
        var source = "console.log(Number.POSITIVE_INFINITY > Number.MAX_VALUE);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_NEGATIVE_INFINITY_IsNegativeInfinity()
    {
        // Verify NEGATIVE_INFINITY is less than any large negative number
        var source = "console.log(Number.NEGATIVE_INFINITY < -1000000000000);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_MAX_SAFE_INTEGER_HasCorrectValue()
    {
        var source = "console.log(Number.MAX_SAFE_INTEGER === 9007199254740991);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_MIN_SAFE_INTEGER_HasCorrectValue()
    {
        var source = "console.log(Number.MIN_SAFE_INTEGER === -9007199254740991);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Number_EPSILON_IsSmallPositive()
    {
        // EPSILON is 2^-52, a very small positive number
        var source = "console.log(Number.EPSILON > 0 && Number.EPSILON < 0.001);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    // ========== Static Methods ==========

    [Fact]
    public void Number_parseInt_ParsesIntegers()
    {
        var source = """
            console.log(Number.parseInt("42"));
            console.log(Number.parseInt("42.9"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n42\n", output);
    }

    [Fact]
    public void Number_parseInt_WithRadix()
    {
        var source = """
            console.log(Number.parseInt("ff", 16));
            console.log(Number.parseInt("101", 2));
            console.log(Number.parseInt("77", 8));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("255\n5\n63\n", output);
    }

    [Fact]
    public void Number_parseFloat_ParsesFloats()
    {
        var source = """
            console.log(Number.parseFloat("3.14"));
            console.log(Number.parseFloat("42"));
            console.log(Number.parseFloat("3.14abc"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3.14\n42\n3.14\n", output);
    }

    [Fact]
    public void Number_isNaN_StrictBehavior()
    {
        var source = """
            console.log(Number.isNaN(Number.NaN));
            console.log(Number.isNaN(42));
            console.log(Number.isNaN("hello"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Number_isFinite_StrictBehavior()
    {
        var source = """
            console.log(Number.isFinite(42));
            console.log(Number.isFinite(Number.POSITIVE_INFINITY));
            console.log(Number.isFinite(Number.NaN));
            console.log(Number.isFinite("42"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Number_isInteger_DetectsIntegers()
    {
        var source = """
            console.log(Number.isInteger(42));
            console.log(Number.isInteger(42.0));
            console.log(Number.isInteger(42.5));
            console.log(Number.isInteger(Number.POSITIVE_INFINITY));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    [Fact]
    public void Number_isSafeInteger_DetectsSafeIntegers()
    {
        var source = """
            console.log(Number.isSafeInteger(42));
            console.log(Number.isSafeInteger(Number.MAX_SAFE_INTEGER));
            console.log(Number.isSafeInteger(Number.MAX_SAFE_INTEGER + 1));
            console.log(Number.isSafeInteger(42.5));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    // ========== Global Functions ==========

    [Fact]
    public void Global_parseInt_Works()
    {
        var source = """
            console.log(parseInt("42"));
            console.log(parseInt("ff", 16));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n255\n", output);
    }

    [Fact]
    public void Global_parseFloat_Works()
    {
        var source = """
            console.log(parseFloat("3.14"));
            console.log(parseFloat("42.5abc"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3.14\n42.5\n", output);
    }

    [Fact]
    public void Global_isNaN_CoercesBehavior()
    {
        var source = """
            console.log(isNaN(Number.NaN));
            console.log(isNaN(42));
            console.log(isNaN("hello"));
            console.log(isNaN("42"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\ntrue\nfalse\n", output);
    }

    [Fact]
    public void Global_isFinite_CoercesBehavior()
    {
        var source = """
            console.log(isFinite(42));
            console.log(isFinite(Number.POSITIVE_INFINITY));
            console.log(isFinite("42"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\ntrue\n", output);
    }
}
