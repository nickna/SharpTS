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

    [Theory, ModeData]
    public void BitwiseAnd_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 & 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void BitwiseOr_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 | 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void BitwiseXor_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(5 ^ 3);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void BitwiseNot_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(~5);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-6\n", output);
    }

    [Theory, ModeData]
    public void LeftShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(2 << 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void RightShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(8 >> 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void UnsignedRightShift_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            console.log(-1 >>> 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4294967295\n", output);
    }

    // ECMA-262 ToInt32/ToUint32 wraps operands modulo 2^32 before bitwise ops.
    // Regression for uuid.parse() which uses `v >>> 24 & 0xff` on a 48-bit number.
    [Theory, ModeData]
    public void UnsignedRightShift_OperandAbove2Pow32_UsesLow32Bits(ExecutionMode mode)
    {
        var source = """
            const v = 0x123456789abc;
            console.log((v >>> 24) & 0xff);
            console.log((v >>> 16) & 0xff);
            console.log((v >>> 8) & 0xff);
            console.log(v & 0xff);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("86\n120\n154\n188\n", output);
    }

    [Theory, ModeData]
    public void BitwiseOr_OperandAbove2Pow32_WrapsModulo(ExecutionMode mode)
    {
        var source = """
            console.log((2 ** 32 + 5) | 0);
            console.log((2 ** 32) | 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n0\n", output);
    }

    [Theory, ModeData]
    public void BitwiseOr_NonFiniteOperand_ReturnsZero(ExecutionMode mode)
    {
        var source = """
            console.log(NaN | 0);
            console.log(Infinity | 0);
            console.log(-Infinity | 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n0\n0\n", output);
    }

    [Theory, ModeData]
    public void UnsignedRightShiftCompound_OperandAbove2Pow32_UsesLow32Bits(ExecutionMode mode)
    {
        var source = """
            let v = 0x123456789abc;
            v >>>= 24;
            console.log(v & 0xff);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("86\n", output);
    }

    [Theory, ModeData]
    public void NumericBitwise_FastPathPreservesToInt32Boundaries(ExecutionMode mode)
    {
        var source = """
            function asInt32(value: number): number {
                return value | 0;
            }
            console.log(asInt32(NaN));
            console.log(asInt32(Infinity));
            console.log(asInt32(-Infinity));
            console.log(asInt32(-0));
            console.log(asInt32(1.9));
            console.log(asInt32(-1.9));
            console.log(asInt32(4294967297));
            console.log(asInt32(-4294967297));
            console.log(asInt32(2147483648));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n0\n0\n0\n1\n-1\n1\n-1\n-2147483648\n", output);
    }

    [Theory, ModeData]
    public void NumericBitwise_FastPathCoversAllBinaryOperatorsAndShiftMask(ExecutionMode mode)
    {
        var source = """
            function report(a: number, b: number): void {
                console.log(a & b);
                console.log(a | b);
                console.log(a ^ b);
                console.log(a << b);
                console.log(a >> b);
                console.log(a >>> b);
            }
            report(0x12345678, 255);
            console.log(8 >>> 33);
            console.log(1 << 33);
            console.log(-8 >> 33);
            console.log(-1 >>> 0);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("120\n305420031\n305419911\n0\n0\n0\n4\n2\n-4\n4294967295\n", output);
    }

    [Theory, ModeData]
    public void DynamicBitwise_RetainsGetValueAndToNumericSemantics(ExecutionMode mode)
    {
        var source = """
            const events: string[] = [];
            const left: any = {
                valueOf: () => { events.push("left-valueOf"); return 7; }
            };
            function right(): any {
                events.push("right-expression");
                return { valueOf: () => { events.push("right-valueOf"); return 3; } };
            }
            console.log(left & right());
            console.log(events.join(","));

            try { console.log((Symbol("s") as any) & 1); }
            catch { console.log("symbol-error"); }
            try { console.log((1n as any) & 1); }
            catch { console.log("mixed-bigint-error"); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "3\nright-expression,left-valueOf,right-valueOf\nsymbol-error\nmixed-bigint-error\n",
            output);
    }

    [Theory, ModeData]
    public void NumericBitwise_ResultFlowsThroughTypedArrayStore(ExecutionMode mode)
    {
        var source = """
            function update(tape: Uint8Array, index: number, delta: number): number {
                tape[index] = (tape[index] + delta) & 255;
                return tape[index];
            }
            const tape = new Uint8Array(1);
            console.log(update(tape, 0, -1));
            console.log(update(tape, 0, 2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("255\n1\n", output);
    }

    #endregion

    #region Nullish Coalescing

    [Theory, ModeData]
    public void NullishCoalescing_WithNull_ReturnsDefault(ExecutionMode mode)
    {
        var source = """
            let x: string | null = null;
            console.log(x ?? "default");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    [Theory, ModeData]
    public void NullishCoalescing_WithValue_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            let x: string | null = "value";
            console.log(x ?? "default");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\n", output);
    }

    [Theory, ModeData]
    public void NullishCoalescing_WithZero_ReturnsZero(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            console.log(x ?? 100);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void OptionalChaining_WithValue_ReturnsProperty(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } = { name: "test" };
            console.log(obj?.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory, ModeData]
    public void OptionalChaining_WithNull_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } | null = null;
            console.log(obj?.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void OptionalChaining_Nested_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            let obj: { nested: { value: number } } = { nested: { value: 42 } };
            console.log(obj?.nested?.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Ternary_TrueCondition_ReturnsTrueValue(ExecutionMode mode)
    {
        var source = """
            console.log(true ? "yes" : "no");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory, ModeData]
    public void Ternary_FalseCondition_ReturnsFalseValue(ExecutionMode mode)
    {
        var source = """
            console.log(false ? "yes" : "no");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("no\n", output);
    }

    [Theory, ModeData]
    public void Ternary_WithComparison_ReturnsCorrectResult(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            console.log(x > 5 ? "big" : "small");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("big\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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
