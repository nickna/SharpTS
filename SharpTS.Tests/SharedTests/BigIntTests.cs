using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for BigInt type. Runs against both interpreter and compiler.
/// </summary>
public class BigIntTests
{
    #region Literal and Typeof Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LiteralSyntax_Works(ExecutionMode mode)
    {
        var source = """
            let x = 123n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("123n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_TypeofReturnsBigint(ExecutionMode mode)
    {
        var source = """
            let x = 42n;
            console.log(typeof x);
            console.log(typeof x === "bigint");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bigint\ntrue\n", output);
    }

    #endregion

    #region Constructor Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ConstructorFromNumber_Works(ExecutionMode mode)
    {
        var source = """
            let x = BigInt(42);
            console.log(x);
            console.log(typeof x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42n\nbigint\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ConstructorFromString_Works(ExecutionMode mode)
    {
        var source = """
            let x = BigInt("12345");
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12345n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ConstructorFromHexString_Works(ExecutionMode mode)
    {
        var source = """
            let x = BigInt("0xFF");
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("255n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_TypeAnnotation_Works(ExecutionMode mode)
    {
        var source = """
            let x: bigint = 100n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100n\n", output);
    }

    #endregion

    #region Arithmetic Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Addition_Works(ExecutionMode mode)
    {
        var source = """
            let a = 10n;
            let b = 20n;
            console.log(a + b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Subtraction_Works(ExecutionMode mode)
    {
        var source = """
            let a = 50n;
            let b = 30n;
            console.log(a - b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Multiplication_Works(ExecutionMode mode)
    {
        var source = """
            let a = 7n;
            let b = 6n;
            console.log(a * b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Division_TruncatesTowardZero(ExecutionMode mode)
    {
        var source = """
            console.log(7n / 3n);
            console.log(-7n / 3n);
            console.log(10n / 2n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2n\n-2n\n5n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Remainder_Works(ExecutionMode mode)
    {
        var source = """
            console.log(7n % 3n);
            console.log(10n % 4n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1n\n2n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Exponentiation_Works(ExecutionMode mode)
    {
        var source = """
            console.log(2n ** 10n);
            console.log(3n ** 4n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1024n\n81n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_UnaryNegation_Works(ExecutionMode mode)
    {
        var source = """
            let x = 42n;
            console.log(-x);
            console.log(-(-x));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-42n\n42n\n", output);
    }

    #endregion

    #region Bitwise Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_BitwiseAnd_Works(ExecutionMode mode)
    {
        var source = """
            console.log(12n & 10n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_BitwiseOr_Works(ExecutionMode mode)
    {
        var source = """
            console.log(12n | 10n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("14n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_BitwiseXor_Works(ExecutionMode mode)
    {
        var source = """
            console.log(12n ^ 10n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_BitwiseNot_Works(ExecutionMode mode)
    {
        var source = """
            console.log(~5n);
            console.log(~(-6n));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-6n\n5n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LeftShift_Works(ExecutionMode mode)
    {
        var source = """
            console.log(1n << 10n);
            console.log(5n << 3n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1024n\n40n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_RightShift_Works(ExecutionMode mode)
    {
        var source = """
            console.log(1024n >> 5n);
            console.log(100n >> 2n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("32n\n25n\n", output);
    }

    #endregion

    #region Comparison Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Equality_Works(ExecutionMode mode)
    {
        var source = """
            console.log(5n === 5n);
            console.log(5n === 6n);
            console.log(5n !== 6n);
            console.log(5n !== 5n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Comparisons_Work(ExecutionMode mode)
    {
        var source = """
            console.log(5n < 10n);
            console.log(10n < 5n);
            console.log(5n <= 5n);
            console.log(5n > 3n);
            console.log(3n > 5n);
            console.log(5n >= 5n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\ntrue\ntrue\nfalse\ntrue\n", output);
    }

    #endregion

    #region Large Numbers

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LargeNumbers_Work(ExecutionMode mode)
    {
        var source = """
            let large = 9007199254740993n;
            console.log(large);
            console.log(large + 1n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9007199254740993n\n9007199254740994n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_VeryLargeNumbers_Work(ExecutionMode mode)
    {
        var source = """
            let huge = 123456789012345678901234567890n;
            console.log(huge);
            console.log(huge * 2n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("123456789012345678901234567890n\n246913578024691357802469135780n\n", output);
    }

    #endregion

    #region Usage in Functions and Variables

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_InFunction_Works(ExecutionMode mode)
    {
        var source = """
            function addBigInts(a: bigint, b: bigint): bigint {
                return a + b;
            }
            console.log(addBigInts(100n, 200n));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("300n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_AsVariable_Works(ExecutionMode mode)
    {
        var source = """
            let x: bigint = 10n;
            x = x + 5n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ZeroAndNegative_Work(ExecutionMode mode)
    {
        var source = """
            console.log(0n);
            console.log(-1n);
            console.log(-100n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0n\n-1n\n-100n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ChainedOperations_Work(ExecutionMode mode)
    {
        var source = """
            let result = (10n + 5n) * 2n - 3n;
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("27n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_InTernary_Works(ExecutionMode mode)
    {
        var source = """
            let x = true ? 10n : 20n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10n\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_Reassignment_Works(ExecutionMode mode)
    {
        var source = """
            let x = 5n;
            x = 10n;
            console.log(x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10n\n", output);
    }

    #endregion

    #region Type Errors

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_MixedWithNumber_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            let x = 10n + 5;
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_UnsignedRightShift_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            let x = 10n >>> 2n;
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Coercion and Methods (#912)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ToString_NoRadix(ExecutionMode mode)
    {
        var source = """
            console.log((123n).toString());
            console.log((0n).toString());
            console.log((-42n).toString());
            """;
        Assert.Equal("123\n0\n-42\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ToString_Radix(ExecutionMode mode)
    {
        var source = """
            console.log((255n).toString(16));
            console.log((255n).toString(2));
            console.log((255n).toString(8));
            console.log((255n).toString(36));
            console.log((-255n).toString(16));
            console.log((0n).toString(16));
            """;
        Assert.Equal("ff\n11111111\n377\n73\n-ff\n0\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ToString_Radix_ArbitraryPrecision(ExecutionMode mode)
    {
        var source = """
            console.log((123456789012345678901234567890n).toString(16));
            """;
        Assert.Equal("18ee90ff6c373e0ee4e3f0ad2\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_StringCoercion_IsBareNumericForm(ExecutionMode mode)
    {
        // ECMA-262 ToString(bigint) has no "n" suffix (that form is console.log-only).
        var source = """
            console.log(String(42n));
            console.log("" + 42n);
            console.log(`${42n}`);
            console.log(`${-7n}`);
            """;
        Assert.Equal("42\n42\n42\n-7\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_ConsoleLog_KeepsDebugForm(ExecutionMode mode)
    {
        // console.log / inspection keeps the "42n" debug form even though String() drops it.
        var source = """
            console.log(42n);
            console.log(0n + 5n);
            """;
        Assert.Equal("42n\n5n\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_NumberCoercion(ExecutionMode mode)
    {
        var source = """
            console.log(Number(42n));
            console.log(Number(-7n));
            console.log(Number(0n));
            """;
        Assert.Equal("42\n-7\n0\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_BooleanCoercion(ExecutionMode mode)
    {
        // ToBoolean(bigint): 0n is falsy, every other bigint is truthy.
        var source = """
            console.log(Boolean(0n));
            console.log(Boolean(5n));
            console.log(Boolean(-1n));
            console.log(!0n);
            console.log(0n ? "t" : "f");
            console.log(7n ? "t" : "f");
            """;
        Assert.Equal("false\ntrue\ntrue\ntrue\nf\nt\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LooseEquality_WithNumber(ExecutionMode mode)
    {
        // ECMA-262 7.2.15: bigint == number compares mathematical values.
        var source = """
            console.log(10n == 10);
            console.log(10n == 11);
            console.log(10n == 10.5);
            console.log(10n != 10);
            console.log(10n != 11);
            """;
        Assert.Equal("true\nfalse\nfalse\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LooseEquality_WithStringAndBoolean(ExecutionMode mode)
    {
        var source = """
            console.log(10n == "10");
            console.log(10n == "abc");
            console.log(0n == "");
            console.log(1n == true);
            console.log(0n == false);
            console.log(2n == true);
            """;
        Assert.Equal("true\nfalse\ntrue\ntrue\ntrue\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_StrictEquality_AcrossTypes_IsFalse(ExecutionMode mode)
    {
        // A bigint is never the same Type as a number, so === is always false.
        var source = """
            console.log(10n === 10n);
            console.log(10n !== 5n);
            console.log(10n === 10);
            console.log(10n !== 10);
            """;
        Assert.Equal("true\ntrue\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Instanceof Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_InstanceOf_Object_IsFalse(ExecutionMode mode)
    {
        // bigint is a primitive; instanceof must return false, not throw a
        // compile error. Compiled mode previously rejected INSTANCEOF in the
        // bigint-arithmetic path (#361).
        var source = """
            console.log((10n) instanceof Object);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_InstanceOf_UserClass_IsFalse(ExecutionMode mode)
    {
        var source = """
            class Foo {}
            console.log((42n) instanceof Foo);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Bigint Literal Types (#1207)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LiteralTypedOperands_UseBigintArithmetic(ExecutionMode mode)
    {
        // const-bound operands carry TypeInfo.BigIntLiteral in the type map; the
        // compiled bigint-arithmetic detection must treat them as bigint, not fall
        // through to the number path.
        var source = """
            const a = 2n;
            const b = 3n;
            console.log(a + b);
            console.log((a * b).toString(2));
            console.log(a < b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5n\n110\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_TypeofGuard_BranchSurvivesDeadCodeAnalysis(ExecutionMode mode)
    {
        // The compiled dead-code analyzer had no "bigint" arm in its typeof matcher,
        // so `typeof x === "bigint"` on a bigint-typed variable read as never-true and
        // the guarded branch was folded out of the emitted IL.
        var source = """
            let x = 5n;
            if (typeof x === "bigint") { console.log("big"); } else { console.log("not"); }
            const c = 7n;
            if (typeof c === "bigint") { console.log("lit"); } else { console.log("not"); }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("big\nlit\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LiteralTypedReceiver_MethodCallWorks(ExecutionMode mode)
    {
        // toString(radix) on a BigIntLiteral-typed receiver must route through the
        // bigint helper (which honors the radix), same as a bigint-typed one.
        var source = """
            const c = 255n;
            console.log(c.toString(16));
            console.log(c.toString());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ff\n255\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BigInt_LiteralUnionNarrowing_RunsCorrectBranch(ExecutionMode mode)
    {
        var source = """
            function g(x: 1n | 2n | "z"): string {
                if (x === 1n) {
                    return "one";
                }
                return typeof x === "bigint" ? "two" : x;
            }
            console.log(g(1n), g(2n), g("z"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("one two z\n", output);
    }

    #endregion
}
