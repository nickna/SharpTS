using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for dead code elimination and control flow correctness.
/// Verifies that unreachable code is not executed based on:
/// - Level 1: Constant conditions (literal true/false)
/// - Level 2: Type-based conditions (typeof checks against known types)
/// - Level 3: Control flow (unreachable code after terminators, exhaustive switch)
/// Runs against both interpreter and compiler.
/// </summary>
public class DeadCodeEliminationTests
{
    #region Level 1: Constant Condition Tests

    [Theory, ModeData]
    public void IfTrue_OnlyThenBranchExecutes(ExecutionMode mode)
    {
        var source = """
            if (true) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void IfFalse_OnlyElseBranchExecutes(ExecutionMode mode)
    {
        var source = """
            if (false) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("else\n", output);
    }

    [Theory, ModeData]
    public void IfFalse_NoElse_NothingExecutes(ExecutionMode mode)
    {
        var source = """
            if (false) {
                console.log("then");
            }
            console.log("after");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("after\n", output);
    }

    [Theory, ModeData]
    public void LogicalAnd_FalseShortCircuits(ExecutionMode mode)
    {
        var source = """
            if (false && true) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("else\n", output);
    }

    [Theory, ModeData]
    public void LogicalOr_TrueShortCircuits(ExecutionMode mode)
    {
        var source = """
            if (true || false) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void Negation_NotFalse_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            if (!false) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void Negation_NotTrue_ExecutesElse(ExecutionMode mode)
    {
        var source = """
            if (!true) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("else\n", output);
    }

    [Theory, ModeData]
    public void ComplexLogical_TrueAndTrue_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            if (true && true) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void ComplexLogical_FalseOrFalse_ExecutesElse(ExecutionMode mode)
    {
        var source = """
            if (false || false) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("else\n", output);
    }

    #endregion

    #region Level 2: Type-Based Condition Tests

    [Theory, ModeData]
    public void TypeofString_AlwaysTrue_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            let x: string = "hello";
            if (typeof x === "string") {
                console.log("is string");
            } else {
                console.log("not string");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("is string\n", output);
    }

    [Theory, ModeData]
    public void TypeofString_AlwaysFalse_SkipsEntireIf(ExecutionMode mode)
    {
        var source = """
            let x: string = "hello";
            if (typeof x === "number") {
                console.log("is number");
            }
            console.log("done");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("done\n", output);
    }

    [Theory, ModeData]
    public void TypeofNumber_AlwaysTrue_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            let n: number = 42;
            if (typeof n === "number") {
                console.log("is number");
            } else {
                console.log("not number");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("is number\n", output);
    }

    [Theory, ModeData]
    public void TypeofBoolean_AlwaysFalse_ExecutesElse(ExecutionMode mode)
    {
        var source = """
            let s: string = "test";
            if (typeof s === "boolean") {
                console.log("is boolean");
            } else {
                console.log("not boolean");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("not boolean\n", output);
    }

    [Theory, ModeData]
    public void TypeofNotEqual_StringIsNotNumber_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            let s: string = "test";
            if (typeof s !== "number") {
                console.log("not number");
            } else {
                console.log("is number");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("not number\n", output);
    }

    [Theory, ModeData]
    public void TypeofStrictEqual_StringIsString_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            let s: string = "test";
            if (typeof s === "string") {
                console.log("is string");
            } else {
                console.log("not string");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("is string\n", output);
    }

    [Theory, ModeData]
    public void TypeofStrictNotEqual_NumberIsNotString_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            let n: number = 42;
            if (typeof n !== "string") {
                console.log("not string");
            } else {
                console.log("is string");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("not string\n", output);
    }

    [Theory, ModeData]
    public void UnionType_MixedTypeof_BothBranchesReachable(ExecutionMode mode)
    {
        var source = """
            function check(x: string | number): void {
                if (typeof x === "string") {
                    console.log("string");
                } else {
                    console.log("number");
                }
            }
            check("hello");
            check(42);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\nnumber\n", output);
    }

    #endregion

    #region Level 3: Control Flow Tests

    [Theory, ModeData]
    public void AfterReturn_CodeNotExecuted(ExecutionMode mode)
    {
        var source = """
            function test(): number {
                console.log("before");
                return 1;
                console.log("after");
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("before\n", output);
    }

    [Theory, ModeData]
    public void AfterThrow_CodeNotExecuted(ExecutionMode mode)
    {
        var source = """
            function test(): void {
                console.log("before");
                throw "error";
                console.log("after");
            }
            try {
                test();
            } catch (e) {
                console.log("caught");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("before\ncaught\n", output);
    }

    [Theory, ModeData]
    public void AfterBreak_CodeNotExecuted(ExecutionMode mode)
    {
        var source = """
            let i: number = 0;
            while (i < 5) {
                console.log(i);
                if (i === 2) {
                    break;
                    console.log("unreachable");
                }
                i = i + 1;
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void AfterContinue_CodeNotExecuted(ExecutionMode mode)
    {
        var source = """
            let i: number = 0;
            while (i < 3) {
                i = i + 1;
                if (i === 2) {
                    continue;
                    console.log("unreachable");
                }
                console.log(i);
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n", output);
    }

    [Theory, ModeData]
    public void ReturnFromFunctionCall_AfterCodeNotExecuted(ExecutionMode mode)
    {
        // Test that code after return with function call result is not executed
        var source = """
            function getValue(): number {
                console.log("getValue");
                return 42;
            }
            function test(): number {
                console.log("before");
                return getValue();
                console.log("after");
            }
            console.log(test());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("before\ngetValue\n42\n", output);
    }

    [Theory, ModeData]
    public void IfBothBranchesReturn_AfterIfNotExecuted(ExecutionMode mode)
    {
        var source = """
            function test(x: boolean): number {
                if (x) {
                    return 1;
                } else {
                    return 2;
                }
                console.log("unreachable");
            }
            console.log(test(true));
            console.log(test(false));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void MultipleReturns_OnlyFirstExecuted(ExecutionMode mode)
    {
        var source = """
            function test(): number {
                console.log("first");
                return 1;
                console.log("second");
                return 2;
                console.log("third");
                return 3;
            }
            console.log(test());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\n1\n", output);
    }

    #endregion

    #region Exhaustive Switch Tests

    [Theory, ModeData]
    public void ExhaustiveSwitch_DefaultNotExecuted(ExecutionMode mode)
    {
        var source = """
            type Status = "a" | "b";
            function check(s: Status): number {
                switch (s) {
                    case "a": return 1;
                    case "b": return 2;
                    default: console.log("unreachable"); return 0;
                }
            }
            console.log(check("a"));
            console.log(check("b"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void NonExhaustiveSwitch_DefaultExecuted(ExecutionMode mode)
    {
        var source = """
            function check(s: string): number {
                switch (s) {
                    case "a": return 1;
                    case "b": return 2;
                    default: console.log("default"); return 0;
                }
            }
            console.log(check("c"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n0\n", output);
    }

    [Theory, ModeData]
    public void SwitchWithThreeOptions_AllCasesCovered(ExecutionMode mode)
    {
        var source = """
            type Color = "red" | "green" | "blue";
            function getCode(c: Color): number {
                switch (c) {
                    case "red": return 1;
                    case "green": return 2;
                    case "blue": return 3;
                    default: console.log("never"); return 0;
                }
            }
            console.log(getCode("red"));
            console.log(getCode("green"));
            console.log(getCode("blue"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void NestedIfTrue_BothLevelsOptimized(ExecutionMode mode)
    {
        var source = """
            if (true) {
                if (true) {
                    console.log("inner then");
                } else {
                    console.log("inner else");
                }
            } else {
                console.log("outer else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner then\n", output);
    }

    [Theory, ModeData]
    public void NestedIfFalse_BothLevelsOptimized(ExecutionMode mode)
    {
        var source = """
            if (false) {
                console.log("outer then");
            } else {
                if (false) {
                    console.log("inner then");
                } else {
                    console.log("inner else");
                }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner else\n", output);
    }

    [Theory, ModeData]
    public void WhileFalse_BodyNeverExecutes(ExecutionMode mode)
    {
        var source = """
            while (false) {
                console.log("loop");
            }
            console.log("after");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("after\n", output);
    }

    [Theory, ModeData]
    public void TernaryWithTrue_ReturnsFirstValue(ExecutionMode mode)
    {
        var source = """
            let x: number = true ? 1 : 2;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void TernaryWithFalse_ReturnsSecondValue(ExecutionMode mode)
    {
        var source = """
            let x: number = false ? 1 : 2;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void GroupedCondition_TrueInParens_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            if ((true)) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void DoubleNegation_NotNotTrue_ExecutesThen(ExecutionMode mode)
    {
        var source = """
            if (!!true) {
                console.log("then");
            } else {
                console.log("else");
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("then\n", output);
    }

    [Theory, ModeData]
    public void FunctionWithMultipleExitPoints_CorrectPathExecutes(ExecutionMode mode)
    {
        var source = """
            function route(x: number): string {
                if (x === 1) {
                    console.log("path 1");
                    return "one";
                }
                if (x === 2) {
                    console.log("path 2");
                    return "two";
                }
                console.log("path default");
                return "other";
            }
            console.log(route(1));
            console.log(route(2));
            console.log(route(3));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("path 1\none\npath 2\ntwo\npath default\nother\n", output);
    }

    #endregion
}
