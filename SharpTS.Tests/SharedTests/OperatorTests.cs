using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for operators (bitwise, nullish, optional chaining, ternary, increment/decrement, compound assignment).
/// Runs against both interpreter and compiler.
/// </summary>
public class OperatorTests
{
    #region Bitwise Operators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BitwiseAnd_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 & 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BitwiseOr_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 | 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BitwiseXor_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 ^ 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BitwiseNot_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(~5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LeftShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(2 << 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RightShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(8 >> 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UnsignedRightShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(-1 >>> 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4294967295\n", output);
    }

    #endregion

    #region Nullish Coalescing

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithNull_ReturnsDefault(ExecutionMode mode)
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithValue_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            let x: string | null = "value";
            console.log(x ?? "default");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithZero_ReturnsZero(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            console.log(x ?? 100);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_WithEmptyString_ReturnsEmptyString(ExecutionMode mode)
    {
        var source = """
            let x: string = "";
            console.log(x ?? "fallback");
            console.log("done");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("\ndone\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NullishCoalescing_Chained_ReturnsFirstNonNull(ExecutionMode mode)
    {
        var source = """
            let a: string | null = null;
            let b: string | null = null;
            let c: string = "third";
            console.log(a ?? b ?? c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("third\n", output);
    }

    #endregion

    #region Optional Chaining

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_WithValue_ReturnsProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } = { name: "test" };
            console.log(obj?.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_WithNull_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } | null = null;
            console.log(obj?.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_Nested_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            let obj: { nested: { value: number } } = { nested: { value: 42 } };
            console.log(obj?.nested?.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void OptionalChaining_CombinedWithNullish_ReturnsDefault(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } | null = null;
            console.log(obj?.name ?? "not found");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("not found\n", output);
    }

    #endregion

    #region Ternary Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Ternary_TrueCondition_ReturnsTrueValue(ExecutionMode mode)
    {
        var source = """
            console.log(true ? "yes" : "no");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Ternary_FalseCondition_ReturnsFalseValue(ExecutionMode mode)
    {
        var source = """
            console.log(false ? "yes" : "no");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("no\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Ternary_WithComparison_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            console.log(x > 5 ? "big" : "small");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("big\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Ternary_Nested_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let grade: number = 85;
            let result: string = grade >= 90 ? "A" : grade >= 80 ? "B" : grade >= 70 ? "C" : "F";
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("B\n", output);
    }

    #endregion

    #region Increment/Decrement Operators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PrefixIncrement_ReturnsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(++x);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PostfixIncrement_ReturnsOldValue(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(x++);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PrefixDecrement_ReturnsNewValue(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(--x);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PostfixDecrement_ReturnsOldValue(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(x--);
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void IncrementInExpression_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            console.log(++x + 10);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("16\n", output);
    }

    #endregion

    #region Compound Assignment Operators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_Add_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            x += 5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_Subtract_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            x -= 3;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_Multiply_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            x *= 2;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_Divide_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 20;
            x /= 4;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_Modulo_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 17;
            x %= 5;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_StringConcat_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let s: string = "Hello";
            s += " World";
            console.log(s);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_OnArrayElement_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr[1] += 10;
            console.log(arr[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_OnObjectProperty_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let obj: { count: number } = { count: 5 };
            obj.count += 3;
            console.log(obj.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region Bitwise Compound Assignment

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_BitwiseAnd_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 7;
            x &= 3;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_BitwiseOr_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            x |= 2;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_LeftShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 2;
            x <<= 2;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CompoundAssignment_RightShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 8;
            x >>= 2;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    #endregion
}
