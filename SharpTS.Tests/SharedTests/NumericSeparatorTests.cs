using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for numeric separator feature (e.g., 1_000_000). Runs against both interpreter and compiler.
/// </summary>
public class NumericSeparatorTests
{
    #region Valid Numeric Separators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_IntegerLiteral_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            let x = 1_000_000;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1000000\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_DecimalLiteral_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            let x = 1_234.567_89;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1234.56789\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_MultipleSeparators_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            let x = 1_2_3_4_5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12345\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_BigInt_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            let x = 1_000_000n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1000000n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_Arithmetic_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let a = 1_000;
            let b = 2_000;
            console.log(a + b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3000\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_InExpression_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            let result = 1_000 * 2 + 500;
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2500\n", output);
    }

    #endregion

    #region Invalid Numeric Separators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_LeadingUnderscore_ThrowsError(ExecutionMode mode)
    {
        var source = """
            let x = _123;
            console.log(x);
            """;

        // _123 is an identifier, not a number - this should fail type checking
        // because _123 is not defined
        Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_TrailingUnderscore_ThrowsError(ExecutionMode mode)
    {
        var source = """
            let x = 123_;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_AdjacentUnderscores_ThrowsError(ExecutionMode mode)
    {
        var source = """
            let x = 1__000;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_BeforeDecimalPoint_ThrowsError(ExecutionMode mode)
    {
        var source = """
            let x = 123_.456;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NumericSeparator_AfterDecimalPoint_ThrowsError(ExecutionMode mode)
    {
        var source = """
            let x = 123._456;
            console.log(x);
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
    }

    #endregion
}
