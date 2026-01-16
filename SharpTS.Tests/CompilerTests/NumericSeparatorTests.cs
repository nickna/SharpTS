using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class NumericSeparatorTests
{
    [Fact]
    public void NumericSeparator_IntegerLiteral_ParsesCorrectly()
    {
        var source = """
            let x = 1_000_000;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1000000\n", output);
    }

    [Fact]
    public void NumericSeparator_DecimalLiteral_ParsesCorrectly()
    {
        var source = """
            let x = 1_234.567_89;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1234.56789\n", output);
    }

    [Fact]
    public void NumericSeparator_MultipleSeparators_ParsesCorrectly()
    {
        var source = """
            let x = 1_2_3_4_5;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("12345\n", output);
    }

    [Fact]
    public void NumericSeparator_BigInt_ParsesCorrectly()
    {
        var source = """
            let x = 1_000_000n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1000000n\n", output);
    }

    [Fact]
    public void NumericSeparator_Arithmetic_WorksCorrectly()
    {
        var source = """
            let a = 1_000;
            let b = 2_000;
            console.log(a + b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3000\n", output);
    }

    [Fact]
    public void NumericSeparator_InExpression_WorksCorrectly()
    {
        var source = """
            let result = 1_000 * 2 + 500;
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2500\n", output);
    }

    [Fact]
    public void NumericSeparator_LeadingUnderscore_ThrowsError()
    {
        var source = """
            let x = _123;
            console.log(x);
            """;

        // _123 is an identifier, not a number - this should fail type checking
        // because _123 is not defined
        Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
    }

    [Fact]
    public void NumericSeparator_TrailingUnderscore_ThrowsError()
    {
        var source = """
            let x = 123_;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
    }

    [Fact]
    public void NumericSeparator_AdjacentUnderscores_ThrowsError()
    {
        var source = """
            let x = 1__000;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
    }

    [Fact]
    public void NumericSeparator_BeforeDecimalPoint_ThrowsError()
    {
        var source = """
            let x = 123_.456;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
    }

    [Fact]
    public void NumericSeparator_AfterDecimalPoint_ThrowsError()
    {
        var source = """
            let x = 123._456;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunCompiled(source));
    }
}
