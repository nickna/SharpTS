using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class LabeledStatementTests
{
    // Basic Labeled Loop Tests
    [Fact]
    public void LabeledWhile_Break_ExitsLoop()
    {
        var source = """
            let count: number = 0;
            outer: while (count < 10) {
                count = count + 1;
                if (count == 3) {
                    break outer;
                }
            }
            console.log(count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LabeledWhile_Continue_RestartsLoop()
    {
        var source = """
            let i: number = 0;
            let sum: number = 0;
            outer: while (i < 5) {
                i = i + 1;
                if (i == 3) {
                    continue outer;
                }
                sum = sum + i;
            }
            console.log(sum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("12\n", output);  // 1 + 2 + 4 + 5 = 12 (skips 3)
    }

    [Fact]
    public void LabeledForOf_Break_ExitsLoop()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            loop: for (let n of nums) {
                if (n == 3) {
                    break loop;
                }
                console.log(n);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void LabeledDoWhile_Break_ExitsLoop()
    {
        var source = """
            let i: number = 0;
            loop: do {
                i = i + 1;
                if (i == 2) {
                    break loop;
                }
                console.log(i);
            } while (i < 10);
            console.log("done");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\ndone\n", output);
    }

    // Nested Loop Tests
    [Fact]
    public void NestedLoops_BreakOuter_ExitsBoth()
    {
        var source = """
            outer: while (true) {
                while (true) {
                    console.log("inner");
                    break outer;
                }
                console.log("should not print");
            }
            console.log("done");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inner\ndone\n", output);
    }

    [Fact]
    public void NestedLoops_ContinueOuter_SkipsInner()
    {
        var source = """
            let outerCount: number = 0;
            outer: while (outerCount < 3) {
                outerCount = outerCount + 1;
                let innerCount: number = 0;
                while (innerCount < 3) {
                    innerCount = innerCount + 1;
                    if (innerCount == 2) {
                        continue outer;
                    }
                    console.log(innerCount);
                }
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n1\n1\n", output);  // Only prints 1 from inner loop each time
    }

    [Fact]
    public void NestedLoops_BreakInner_OnlyExitsInner()
    {
        var source = """
            let count: number = 0;
            outer: while (count < 2) {
                count = count + 1;
                let inner: number = 0;
                while (inner < 3) {
                    inner = inner + 1;
                    console.log(inner);
                    if (inner == 2) {
                        break;  // Unlabeled break - exits inner only
                    }
                }
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n1\n2\n", output);
    }

    [Fact]
    public void ThreeLevelNesting_BreakMiddle()
    {
        var source = """
            let result: string = "";
            outer: while (true) {
                middle: while (true) {
                    while (true) {
                        result = result + "inner,";
                        break middle;
                    }
                    result = result + "middle,";
                }
                result = result + "outer";
                break outer;
            }
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("inner,outer\n", output);
    }

    // Labeled Block Tests
    [Fact]
    public void LabeledBlock_Break_ExitsBlock()
    {
        var source = """
            console.log("before");
            block: {
                console.log("in block");
                break block;
                console.log("should not print");
            }
            console.log("after");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("before\nin block\nafter\n", output);
    }

    [Fact]
    public void LabeledBlock_Nested_BreakOuter()
    {
        var source = """
            outer: {
                console.log("outer start");
                inner: {
                    console.log("inner start");
                    break outer;
                    console.log("inner end");
                }
                console.log("outer end");
            }
            console.log("done");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("outer start\ninner start\ndone\n", output);
    }

    [Fact]
    public void LabeledBlock_InLoop_BreakBlockNotLoop()
    {
        var source = """
            let i: number = 0;
            while (i < 3) {
                i = i + 1;
                block: {
                    if (i == 2) {
                        break block;  // Only breaks the block, loop continues
                    }
                    console.log(i);
                }
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n", output);  // Prints 1 and 3, skips 2
    }

    // Switch Nesting Tests
    [Fact]
    public void SwitchInLoop_BreakLabel_ExitsLoop()
    {
        var source = """
            let x: number = 1;
            outer: while (true) {
                switch (x) {
                    case 1:
                        console.log("case 1");
                        break outer;  // Exits the while, not just switch
                }
                console.log("should not print");
            }
            console.log("done");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("case 1\ndone\n", output);
    }

    [Fact]
    public void SwitchInLoop_UnlabeledBreak_ExitsSwitch()
    {
        var source = """
            let count: number = 0;
            while (count < 2) {
                count = count + 1;
                switch (count) {
                    case 1:
                        console.log("case 1");
                        break;  // Exits switch only, loop continues
                    case 2:
                        console.log("case 2");
                        break;
                }
                console.log("after switch");
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("case 1\nafter switch\ncase 2\nafter switch\n", output);
    }

    [Fact]
    public void SwitchInLoop_ContinueLabel_ContinuesLoop()
    {
        var source = """
            let i: number = 0;
            outer: while (i < 3) {
                i = i + 1;
                switch (i) {
                    case 2:
                        continue outer;  // Skip to next loop iteration
                }
                console.log(i);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n", output);  // Skips 2
    }

    // Edge Cases
    [Fact]
    public void LabeledEmptyStatement_Allowed()
    {
        var source = """
            let x: number = 0;
            label: x = 5;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void MultipleLabels_DifferentStatements()
    {
        var source = """
            let result: string = "";
            first: while (true) {
                result = result + "a";
                break first;
            }
            second: while (true) {
                result = result + "b";
                break second;
            }
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ab\n", output);
    }

    // Error Cases
    [Fact]
    public void Break_NonexistentLabel_ThrowsError()
    {
        var source = """
            while (true) {
                break missing;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Label 'missing' not found", ex.Message);
    }

    [Fact]
    public void Continue_NonexistentLabel_ThrowsError()
    {
        var source = """
            while (true) {
                continue missing;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Label 'missing' not found", ex.Message);
    }

    [Fact]
    public void Continue_ToNonLoopLabel_ThrowsError()
    {
        var source = """
            block: {
                continue block;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Cannot continue to non-loop label", ex.Message);
    }

    [Fact]
    public void LabelShadowing_ThrowsError()
    {
        var source = """
            outer: while (true) {
                outer: while (true) {
                    break;
                }
                break;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Label 'outer' already declared", ex.Message);
    }
}
