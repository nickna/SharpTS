using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Shared generator tests that run against both interpreter and compiler.
/// These tests verify core generator functionality that should work identically in both modes.
/// </summary>
public class GeneratorTests
{
    #region For...Of Integration

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_ForOfLoop_IteratesAllValues(ExecutionMode mode)
    {
        var source = """
            function* nums() {
                yield 10;
                yield 20;
                yield 30;
            }

            for (let n of nums()) {
                console.log(n);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_WithParameters_UsesParameters(ExecutionMode mode)
    {
        var source = """
            function* range(start: number, end: number) {
                for (let i = start; i <= end; i++) {
                    yield i;
                }
            }

            for (let x of range(5, 8)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    #endregion

    #region Yield* Delegation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_YieldStarArray_DelegatesCorrectly(ExecutionMode mode)
    {
        var source = """
            function* withArray() {
                yield* [1, 2, 3];
            }

            for (let x of withArray()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_YieldStarGenerator_DelegatesCorrectly(ExecutionMode mode)
    {
        var source = """
            function* inner() {
                yield 2;
                yield 3;
            }

            function* outer() {
                yield 1;
                yield* inner();
                yield 4;
            }

            for (let x of outer()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    #endregion

    #region Control Flow

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_WhileLoop_YieldsMultipleTimes(ExecutionMode mode)
    {
        var source = """
            function* countdown(n: number) {
                while (n > 0) {
                    yield n;
                    n--;
                }
            }

            for (let x of countdown(3)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_IfStatement_ConditionalYield(ExecutionMode mode)
    {
        var source = """
            function* conditionalYield(includeSecond: boolean) {
                yield 1;
                if (includeSecond) {
                    yield 2;
                }
                yield 3;
            }

            console.log("With second:");
            for (let x of conditionalYield(true)) {
                console.log(x);
            }

            console.log("Without second:");
            for (let x of conditionalYield(false)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("With second:\n1\n2\n3\nWithout second:\n1\n3\n", output);
    }

    #endregion

    #region Closures

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_Closure_CapturesVariables(ExecutionMode mode)
    {
        var source = """
            function* makeSequence(multiplier: number) {
                for (let i = 1; i <= 3; i++) {
                    yield i * multiplier;
                }
            }

            for (let x of makeSequence(10)) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    #endregion

    #region Yield* with Map and Set

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_YieldStarMap_DelegatesEntries(ExecutionMode mode)
    {
        var source = """
            function* mapIter() {
                let m = new Map<string, number>();
                m.set("a", 1);
                m.set("b", 2);
                yield* m;
            }

            for (let pair of mapIter()) {
                console.log(pair[0] + ":" + pair[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a:1\nb:2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_YieldStarSet_DelegatesValues(ExecutionMode mode)
    {
        var source = """
            function* setIter() {
                let s = new Set<number>();
                s.add(100);
                s.add(200);
                yield* s;
            }

            for (let v of setIter()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    #endregion
}
