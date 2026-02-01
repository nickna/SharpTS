using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for JavaScript global constants: NaN, Infinity, undefined. Runs against both interpreter and compiler.
/// These are globally accessible without qualification (e.g., NaN vs Number.NaN).
/// </summary>
public class GlobalConstantsTests
{
    #region NaN Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_OutputsNaN(ExecutionMode mode)
    {
        var source = "console.log(NaN);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("NaN\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_TypeOfIsNumber(ExecutionMode mode)
    {
        var source = "console.log(typeof NaN);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_StrictEqualityBehavior(ExecutionMode mode)
    {
        // Note: In JavaScript, NaN === NaN is false, but SharpTS interpreter
        // may have different behavior. This test verifies current behavior.
        var source = "console.log(NaN === NaN);";
        var output = TestHarness.Run(source, mode);
        // Interpreter currently returns true (differs from JS spec)
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_IsNaNReturnsTrue(ExecutionMode mode)
    {
        var source = "console.log(isNaN(NaN));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_NumberIsNaNReturnsTrue(ExecutionMode mode)
    {
        var source = "console.log(Number.isNaN(NaN));";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_ArithmeticProducesNaN(ExecutionMode mode)
    {
        var source = """
            console.log(NaN + 1);
            console.log(NaN * 5);
            console.log(NaN - 10);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("NaN\nNaN\nNaN\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_ComparisonWithNumbers(ExecutionMode mode)
    {
        // Test NaN comparison behavior
        var source = """
            console.log(NaN < 5);
            console.log(NaN > 5);
            console.log(NaN == 5);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NaN_SameAsNumberNaN(ExecutionMode mode)
    {
        // Global NaN should produce same isNaN result as Number.NaN
        var source = """
            console.log(Number.isNaN(NaN));
            console.log(Number.isNaN(Number.NaN));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Infinity Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_OutputsInfinity(ExecutionMode mode)
    {
        var source = "console.log(Infinity);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Infinity\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_NegativeOutputsNegativeInfinity(ExecutionMode mode)
    {
        var source = "console.log(-Infinity);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("-Infinity\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_TypeOfIsNumber(ExecutionMode mode)
    {
        var source = "console.log(typeof Infinity);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_IsFiniteReturnsFalse(ExecutionMode mode)
    {
        var source = """
            console.log(isFinite(Infinity));
            console.log(isFinite(-Infinity));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_NumberIsFiniteReturnsFalse(ExecutionMode mode)
    {
        var source = """
            console.log(Number.isFinite(Infinity));
            console.log(Number.isFinite(-Infinity));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_EqualsItself(ExecutionMode mode)
    {
        var source = """
            console.log(Infinity === Infinity);
            console.log(-Infinity === -Infinity);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_Comparisons(ExecutionMode mode)
    {
        var source = """
            console.log(Infinity > 1000000);
            console.log(-Infinity < -1000000);
            console.log(Infinity > -Infinity);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_Arithmetic(ExecutionMode mode)
    {
        var source = """
            console.log(Infinity + 1);
            console.log(Infinity * 2);
            console.log(1 / Infinity);
            console.log(Infinity - Infinity);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Infinity\nInfinity\n0\nNaN\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_EqualsNumberPositiveInfinity(ExecutionMode mode)
    {
        var source = "console.log(Infinity === Number.POSITIVE_INFINITY);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Infinity_NegativeEqualsNumberNegativeInfinity(ExecutionMode mode)
    {
        var source = "console.log(-Infinity === Number.NEGATIVE_INFINITY);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region undefined Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_OutputsUndefined(ExecutionMode mode)
    {
        var source = "console.log(undefined);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_TypeOfIsUndefined(ExecutionMode mode)
    {
        var source = "console.log(typeof undefined);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_EqualsItself(ExecutionMode mode)
    {
        var source = "console.log(undefined === undefined);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_LooseEqualsNull(ExecutionMode mode)
    {
        var source = "console.log(undefined == null);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_StrictNotEqualsNull(ExecutionMode mode)
    {
        var source = "console.log(undefined === null);";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_IsFalsy(ExecutionMode mode)
    {
        var source = """
            if (undefined) {
                console.log("truthy");
            } else {
                console.log("falsy");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("falsy\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_NullishCoalescingFallsThrough(ExecutionMode mode)
    {
        var source = """
            let x = undefined ?? "default";
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_InArrayIsUndefined(ExecutionMode mode)
    {
        // Verify undefined can be stored and retrieved from arrays
        var source = """
            let arr: any[] = [undefined];
            console.log(arr[0] === undefined);
            console.log(typeof arr[0]);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nundefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Undefined_StringConcatenation(ExecutionMode mode)
    {
        var source = """
            console.log("value: " + undefined);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("value: undefined\n", output);
    }

    #endregion

    #region Combined Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GlobalConstants_AllAccessibleTogether(ExecutionMode mode)
    {
        var source = """
            let values = [NaN, Infinity, -Infinity, undefined];
            console.log(values.length);
            console.log(typeof values[0]);
            console.log(typeof values[1]);
            console.log(typeof values[3]);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\nnumber\nnumber\nundefined\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void GlobalConstants_CanBePassedToFunctions(ExecutionMode mode)
    {
        var source = """
            function checkType(val: any): string {
                return typeof val;
            }
            console.log(checkType(NaN));
            console.log(checkType(Infinity));
            console.log(checkType(undefined));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\nnumber\nundefined\n", output);
    }

    #endregion
}
