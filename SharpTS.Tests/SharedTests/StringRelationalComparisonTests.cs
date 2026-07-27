using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for string relational operators (&lt;, &gt;, &lt;=, &gt;=) per JS
/// AbstractRelationalComparison: when both operands are strings, compare
/// lexicographically. Runs in both interpreter and compiled modes.
/// Regression tests for the fix that removed the url.ts localeCompare workaround.
/// </summary>
public class StringRelationalComparisonTests
{
    [Theory, ModeData]
    public void LessThan_Strings(ExecutionMode mode)
    {
        var source = """
            const a = "apple";
            const b = "banana";
            console.log(a < b);
            console.log(b < a);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void GreaterThan_Strings(ExecutionMode mode)
    {
        var source = """
            const a = "apple";
            const b = "banana";
            console.log(b > a);
            console.log(a > b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void LessEqual_Strings_Equal(ExecutionMode mode)
    {
        var source = """
            const a = "apple";
            const b = "apple";
            console.log(a <= b);
            console.log(a >= b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void GreaterEqual_Strings(ExecutionMode mode)
    {
        var source = """
            const a = "banana";
            const b = "apple";
            console.log(a >= b);
            console.log(b >= a);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void EmptyString_LessThan_NonEmpty(ExecutionMode mode)
    {
        var source = """
            console.log("" < "a");
            console.log("a" < "");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void StringLiteral_Compare(ExecutionMode mode)
    {
        var source = """
            console.log("apple" < "banana");
            console.log("zebra" > "apple");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void InsertionSort_UsingStringCompare(ExecutionMode mode)
    {
        // Mirrors the pattern used by stdlib/node/url.ts URLSearchParams.sort.
        var source = """
            const keys = ["banana", "apple", "cherry", "date"];
            for (let i = 1; i < keys.length; i++) {
                const k = keys[i];
                let j = i - 1;
                while (j >= 0 && keys[j] > k) {
                    keys[j + 1] = keys[j];
                    j--;
                }
                keys[j + 1] = k;
            }
            console.log(keys.join(","));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("apple,banana,cherry,date\n", output);
    }
}
