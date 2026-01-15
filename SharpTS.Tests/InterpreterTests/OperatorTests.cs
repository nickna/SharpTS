using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class OperatorTests
{
    // Bitwise Operators
    [Fact]
    public void BitwiseAnd_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 & 3);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void BitwiseOr_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 | 3);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void BitwiseXor_ReturnsCorrectResult()
    {
        var source = """
            console.log(5 ^ 3);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void BitwiseNot_ReturnsCorrectResult()
    {
        var source = """
            console.log(~5);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("-6\n", output);
    }

    [Fact]
    public void LeftShift_ReturnsCorrectResult()
    {
        var source = """
            console.log(2 << 2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void RightShift_ReturnsCorrectResult()
    {
        var source = """
            console.log(8 >> 2);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void UnsignedRightShift_ReturnsCorrectResult()
    {
        var source = """
            console.log(-1 >>> 0);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4294967295\n", output);
    }

    // Nullish Coalescing
    [Fact]
    public void NullishCoalescing_WithNull_ReturnsDefault()
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithValue_ReturnsValue()
    {
        var source = """
            let x: string | null = "value";
            console.log(x ?? "default");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("value\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithZero_ReturnsZero()
    {
        var source = """
            let x: number = 0;
            console.log(x ?? 100);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void NullishCoalescing_WithEmptyString_ReturnsEmptyString()
    {
        var source = """
            let x: string = "";
            console.log(x ?? "fallback");
            console.log("done");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("\ndone\n", output);
    }

    [Fact]
    public void NullishCoalescing_Chained_ReturnsFirstNonNull()
    {
        var source = """
            let a: string | null = null;
            let b: string | null = null;
            let c: string = "third";
            console.log(a ?? b ?? c);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("third\n", output);
    }

    // Optional Chaining
    [Fact]
    public void OptionalChaining_WithValue_ReturnsProperty()
    {
        var source = """
            let obj: { name: string } = { name: "test" };
            console.log(obj?.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void OptionalChaining_WithNull_ReturnsUndefined()
    {
        // In JavaScript, optional chaining on null or undefined returns undefined
        var source = """
            let obj: { name: string } | null = null;
            console.log(obj?.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void OptionalChaining_Nested_ReturnsValue()
    {
        var source = """
            let obj: { nested: { value: number } } = { nested: { value: 42 } };
            console.log(obj?.nested?.value);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void OptionalChaining_CombinedWithNullish_ReturnsDefault()
    {
        var source = """
            let obj: { name: string } | null = null;
            console.log(obj?.name ?? "not found");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("not found\n", output);
    }

    // Ternary Operator
    [Fact]
    public void Ternary_TrueCondition_ReturnsTrueValue()
    {
        var source = """
            console.log(true ? "yes" : "no");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void Ternary_FalseCondition_ReturnsFalseValue()
    {
        var source = """
            console.log(false ? "yes" : "no");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("no\n", output);
    }

    [Fact]
    public void Ternary_WithComparison_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 10;
            console.log(x > 5 ? "big" : "small");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("big\n", output);
    }

    [Fact]
    public void Ternary_Nested_ReturnsCorrectResult()
    {
        var source = """
            let grade: number = 85;
            let result: string = grade >= 90 ? "A" : grade >= 80 ? "B" : grade >= 70 ? "C" : "F";
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("B\n", output);
    }

    // Increment/Decrement Operators
    [Fact]
    public void PrefixIncrement_ReturnsNewValue()
    {
        var source = """
            let x: number = 5;
            console.log(++x);
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n6\n", output);
    }

    [Fact]
    public void PostfixIncrement_ReturnsOldValue()
    {
        var source = """
            let x: number = 5;
            console.log(x++);
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n6\n", output);
    }

    [Fact]
    public void PrefixDecrement_ReturnsNewValue()
    {
        var source = """
            let x: number = 5;
            console.log(--x);
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n4\n", output);
    }

    [Fact]
    public void PostfixDecrement_ReturnsOldValue()
    {
        var source = """
            let x: number = 5;
            console.log(x--);
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n4\n", output);
    }

    [Fact]
    public void IncrementInExpression_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 5;
            console.log(++x + 10);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("16\n", output);
    }

    // Compound Assignment Operators
    [Fact]
    public void CompoundAssignment_Add_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 10;
            x += 5;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void CompoundAssignment_Subtract_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 10;
            x -= 3;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void CompoundAssignment_Multiply_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 10;
            x *= 2;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void CompoundAssignment_Divide_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 20;
            x /= 4;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void CompoundAssignment_Modulo_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 17;
            x %= 5;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void CompoundAssignment_StringConcat_ReturnsCorrectResult()
    {
        var source = """
            let s: string = "Hello";
            s += " World";
            console.log(s);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello World\n", output);
    }

    [Fact]
    public void CompoundAssignment_OnArrayElement_ReturnsCorrectResult()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr[1] += 10;
            console.log(arr[1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("12\n", output);
    }

    [Fact]
    public void CompoundAssignment_OnObjectProperty_ReturnsCorrectResult()
    {
        var source = """
            let obj: { count: number } = { count: 5 };
            obj.count += 3;
            console.log(obj.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n", output);
    }

    // Bitwise Compound Assignment
    [Fact]
    public void CompoundAssignment_BitwiseAnd_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 7;
            x &= 3;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void CompoundAssignment_BitwiseOr_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 5;
            x |= 2;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void CompoundAssignment_LeftShift_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 2;
            x <<= 2;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void CompoundAssignment_RightShift_ReturnsCorrectResult()
    {
        var source = """
            let x: number = 8;
            x >>= 2;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }
}
