using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for JavaScript global constants: NaN, Infinity, undefined.
/// These are globally accessible without qualification (e.g., NaN vs Number.NaN).
/// </summary>
public class GlobalConstantsTests
{
    // ========== NaN Tests ==========

    [Fact]
    public void NaN_OutputsNaN()
    {
        var source = "console.log(NaN);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("NaN\n", output);
    }

    [Fact]
    public void NaN_TypeOfIsNumber()
    {
        var source = "console.log(typeof NaN);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("number\n", output);
    }

    [Fact]
    public void NaN_StrictEqualityBehavior()
    {
        // Note: Both interpreter and compiler return true for NaN === NaN
        // This differs from JavaScript spec but is current SharpTS behavior
        var source = "console.log(NaN === NaN);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NaN_IsNaNReturnsTrue()
    {
        var source = "console.log(isNaN(NaN));";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NaN_NumberIsNaNReturnsTrue()
    {
        var source = "console.log(Number.isNaN(NaN));";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NaN_ArithmeticProducesNaN()
    {
        var source = """
            console.log(NaN + 1);
            console.log(NaN * 5);
            console.log(NaN - 10);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("NaN\nNaN\nNaN\n", output);
    }

    [Fact]
    public void NaN_ComparisonWithNumbers()
    {
        // Test NaN comparison behavior
        var source = """
            console.log(NaN < 5);
            console.log(NaN > 5);
            console.log(NaN == 5);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Fact]
    public void NaN_SameAsNumberNaN()
    {
        // Global NaN should produce same isNaN result as Number.NaN
        var source = """
            console.log(Number.isNaN(NaN));
            console.log(Number.isNaN(Number.NaN));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    // ========== Infinity Tests ==========

    [Fact]
    public void Infinity_OutputsInfinity()
    {
        var source = "console.log(Infinity);";
        var output = TestHarness.RunCompiled(source);
        // Compiled mode outputs Unicode infinity symbol
        Assert.Equal("∞\n", output);
    }

    [Fact]
    public void Infinity_NegativeOutputsNegativeInfinity()
    {
        var source = "console.log(-Infinity);";
        var output = TestHarness.RunCompiled(source);
        // Compiled mode outputs Unicode infinity symbol
        Assert.Equal("-∞\n", output);
    }

    [Fact]
    public void Infinity_TypeOfIsNumber()
    {
        var source = "console.log(typeof Infinity);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("number\n", output);
    }

    [Fact]
    public void Infinity_IsFiniteReturnsFalse()
    {
        var source = """
            console.log(isFinite(Infinity));
            console.log(isFinite(-Infinity));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\nfalse\n", output);
    }

    [Fact]
    public void Infinity_NumberIsFiniteReturnsFalse()
    {
        var source = """
            console.log(Number.isFinite(Infinity));
            console.log(Number.isFinite(-Infinity));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\nfalse\n", output);
    }

    [Fact]
    public void Infinity_EqualsItself()
    {
        var source = """
            console.log(Infinity === Infinity);
            console.log(-Infinity === -Infinity);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Infinity_Comparisons()
    {
        var source = """
            console.log(Infinity > 1000000);
            console.log(-Infinity < -1000000);
            console.log(Infinity > -Infinity);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Infinity_Arithmetic()
    {
        var source = """
            console.log(Infinity + 1);
            console.log(Infinity * 2);
            console.log(1 / Infinity);
            console.log(Infinity - Infinity);
            """;
        var output = TestHarness.RunCompiled(source);
        // Compiled mode outputs Unicode infinity symbol
        Assert.Equal("∞\n∞\n0\nNaN\n", output);
    }

    [Fact]
    public void Infinity_EqualsNumberPositiveInfinity()
    {
        var source = "console.log(Infinity === Number.POSITIVE_INFINITY);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Infinity_NegativeEqualsNumberNegativeInfinity()
    {
        var source = "console.log(-Infinity === Number.NEGATIVE_INFINITY);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    // ========== undefined Tests ==========

    [Fact]
    public void Undefined_OutputsUndefined()
    {
        var source = "console.log(undefined);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void Undefined_TypeOfIsUndefined()
    {
        var source = "console.log(typeof undefined);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("undefined\n", output);
    }

    [Fact]
    public void Undefined_EqualsItself()
    {
        var source = "console.log(undefined === undefined);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Undefined_LooseEqualsNull()
    {
        var source = "console.log(undefined == null);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Undefined_StrictNotEqualsNull()
    {
        var source = "console.log(undefined === null);";
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Undefined_IsFalsy()
    {
        var source = """
            if (undefined) {
                console.log("truthy");
            } else {
                console.log("falsy");
            }
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("falsy\n", output);
    }

    [Fact]
    public void Undefined_NullishCoalescingFallsThrough()
    {
        var source = """
            let x = undefined ?? "default";
            console.log(x);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("default\n", output);
    }

    [Fact]
    public void Undefined_InArrayIsUndefined()
    {
        // Verify undefined can be stored and retrieved from arrays
        var source = """
            let arr: any[] = [undefined];
            console.log(arr[0] === undefined);
            console.log(typeof arr[0]);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nundefined\n", output);
    }

    [Fact]
    public void Undefined_StringConcatenation()
    {
        var source = """
            console.log("value: " + undefined);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("value: undefined\n", output);
    }

    // ========== Combined Tests ==========

    [Fact]
    public void GlobalConstants_AllAccessibleTogether()
    {
        var source = """
            let values = [NaN, Infinity, -Infinity, undefined];
            console.log(values.length);
            console.log(typeof values[0]);
            console.log(typeof values[1]);
            console.log(typeof values[3]);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\nnumber\nnumber\nundefined\n", output);
    }

    [Fact]
    public void GlobalConstants_CanBePassedToFunctions()
    {
        var source = """
            function checkType(val: any): string {
                return typeof val;
            }
            console.log(checkType(NaN));
            console.log(checkType(Infinity));
            console.log(checkType(undefined));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("number\nnumber\nundefined\n", output);
    }
}
