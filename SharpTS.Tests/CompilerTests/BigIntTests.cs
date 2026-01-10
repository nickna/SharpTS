using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class BigIntTests
{
    [Fact]
    public void BigInt_LiteralSyntax_Works()
    {
        var source = """
            let x = 123n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("123n\n", output);
    }

    [Fact]
    public void BigInt_TypeofReturnsBigint()
    {
        var source = """
            let x = 42n;
            console.log(typeof x);
            console.log(typeof x === "bigint");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("bigint\ntrue\n", output);
    }

    [Fact]
    public void BigInt_ConstructorFromNumber_Works()
    {
        var source = """
            let x = BigInt(42);
            console.log(x);
            console.log(typeof x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42n\nbigint\n", output);
    }

    [Fact]
    public void BigInt_ConstructorFromString_Works()
    {
        var source = """
            let x = BigInt("12345");
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("12345n\n", output);
    }

    [Fact]
    public void BigInt_ConstructorFromHexString_Works()
    {
        var source = """
            let x = BigInt("0xFF");
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("255n\n", output);
    }

    [Fact]
    public void BigInt_TypeAnnotation_Works()
    {
        var source = """
            let x: bigint = 100n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100n\n", output);
    }

    [Fact]
    public void BigInt_Addition_Works()
    {
        var source = """
            let a = 10n;
            let b = 20n;
            console.log(a + b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30n\n", output);
    }

    [Fact]
    public void BigInt_Subtraction_Works()
    {
        var source = """
            let a = 50n;
            let b = 30n;
            console.log(a - b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20n\n", output);
    }

    [Fact]
    public void BigInt_Multiplication_Works()
    {
        var source = """
            let a = 7n;
            let b = 6n;
            console.log(a * b);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42n\n", output);
    }

    [Fact]
    public void BigInt_Division_TruncatesTowardZero()
    {
        var source = """
            console.log(7n / 3n);
            console.log(-7n / 3n);
            console.log(10n / 2n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2n\n-2n\n5n\n", output);
    }

    [Fact]
    public void BigInt_Remainder_Works()
    {
        var source = """
            console.log(7n % 3n);
            console.log(10n % 4n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1n\n2n\n", output);
    }

    [Fact]
    public void BigInt_Exponentiation_Works()
    {
        var source = """
            console.log(2n ** 10n);
            console.log(3n ** 4n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1024n\n81n\n", output);
    }

    [Fact]
    public void BigInt_UnaryNegation_Works()
    {
        var source = """
            let x = 42n;
            console.log(-x);
            console.log(-(-x));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-42n\n42n\n", output);
    }

    [Fact]
    public void BigInt_BitwiseAnd_Works()
    {
        var source = """
            console.log(12n & 10n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8n\n", output);
    }

    [Fact]
    public void BigInt_BitwiseOr_Works()
    {
        var source = """
            console.log(12n | 10n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("14n\n", output);
    }

    [Fact]
    public void BigInt_BitwiseXor_Works()
    {
        var source = """
            console.log(12n ^ 10n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6n\n", output);
    }

    [Fact]
    public void BigInt_BitwiseNot_Works()
    {
        var source = """
            console.log(~5n);
            console.log(~(-6n));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-6n\n5n\n", output);
    }

    [Fact]
    public void BigInt_LeftShift_Works()
    {
        var source = """
            console.log(1n << 10n);
            console.log(5n << 3n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1024n\n40n\n", output);
    }

    [Fact]
    public void BigInt_RightShift_Works()
    {
        var source = """
            console.log(1024n >> 5n);
            console.log(100n >> 2n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("32n\n25n\n", output);
    }

    [Fact]
    public void BigInt_Equality_Works()
    {
        var source = """
            console.log(5n === 5n);
            console.log(5n === 6n);
            console.log(5n !== 6n);
            console.log(5n !== 5n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\ntrue\nfalse\n", output);
    }

    [Fact]
    public void BigInt_Comparisons_Work()
    {
        var source = """
            console.log(5n < 10n);
            console.log(10n < 5n);
            console.log(5n <= 5n);
            console.log(5n > 3n);
            console.log(3n > 5n);
            console.log(5n >= 5n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\ntrue\ntrue\nfalse\ntrue\n", output);
    }

    [Fact]
    public void BigInt_LargeNumbers_Work()
    {
        var source = """
            let large = 9007199254740993n;
            console.log(large);
            console.log(large + 1n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("9007199254740993n\n9007199254740994n\n", output);
    }

    [Fact]
    public void BigInt_VeryLargeNumbers_Work()
    {
        var source = """
            let huge = 123456789012345678901234567890n;
            console.log(huge);
            console.log(huge * 2n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("123456789012345678901234567890n\n246913578024691357802469135780n\n", output);
    }

    [Fact]
    public void BigInt_InFunction_Works()
    {
        var source = """
            function addBigInts(a: bigint, b: bigint): bigint {
                return a + b;
            }
            console.log(addBigInts(100n, 200n));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("300n\n", output);
    }

    [Fact]
    public void BigInt_AsVariable_Works()
    {
        var source = """
            let x: bigint = 10n;
            x = x + 5n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15n\n", output);
    }

    [Fact]
    public void BigInt_ZeroAndNegative_Work()
    {
        var source = """
            console.log(0n);
            console.log(-1n);
            console.log(-100n);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0n\n-1n\n-100n\n", output);
    }

    [Fact]
    public void BigInt_ChainedOperations_Work()
    {
        var source = """
            let result = (10n + 5n) * 2n - 3n;
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("27n\n", output);
    }

    [Fact]
    public void BigInt_MixedWithNumber_ThrowsTypeError()
    {
        var source = """
            let x = 10n + 5;
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void BigInt_UnsignedRightShift_ThrowsTypeError()
    {
        var source = """
            let x = 10n >>> 2n;
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunCompiled(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void BigInt_InTernary_Works()
    {
        var source = """
            let x = true ? 10n : 20n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10n\n", output);
    }

    [Fact]
    public void BigInt_Reassignment_Works()
    {
        var source = """
            let x = 5n;
            x = 10n;
            console.log(x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10n\n", output);
    }
}
