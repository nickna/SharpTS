using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for control flow statements. Runs against both interpreter and compiler.
/// </summary>
public class ControlFlowTests
{
    #region Switch Statements

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Switch_BasicCase_MatchesCorrectly(ExecutionMode mode)
    {
        var source = """
            let x: number = 2;
            switch (x) {
                case 1:
                    console.log("one");
                    break;
                case 2:
                    console.log("two");
                    break;
                case 3:
                    console.log("three");
                    break;
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("two\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Switch_DefaultCase_ExecutesWhenNoMatch(ExecutionMode mode)
    {
        var source = """
            let x: number = 5;
            switch (x) {
                case 1:
                    console.log("one");
                    break;
                case 2:
                    console.log("two");
                    break;
                default:
                    console.log("other");
                    break;
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("other\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Switch_FallThrough_ExecutesMultipleCases(ExecutionMode mode)
    {
        var source = """
            let x: number = 2;
            switch (x) {
                case 1:
                    console.log("one");
                    break;
                case 2:
                    console.log("two");
                case 3:
                    console.log("three");
                    break;
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("two\nthree\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Switch_WithString_MatchesCorrectly(ExecutionMode mode)
    {
        var source = """
            let s: string = "hello";
            switch (s) {
                case "hi":
                    console.log("hi");
                    break;
                case "hello":
                    console.log("hello");
                    break;
                default:
                    console.log("unknown");
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region For-of Loops

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForOf_Basic_IteratesArray(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            for (let n of nums) {
                console.log(n);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForOf_WithBreak_ExitsLoop(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            for (let n of nums) {
                if (n == 3) {
                    break;
                }
                console.log(n);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForOf_WithContinue_SkipsIteration(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            for (let n of nums) {
                if (n == 3) {
                    continue;
                }
                console.log(n);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n4\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ForOf_WithStrings_IteratesElements(ExecutionMode mode)
    {
        var source = """
            let words: string[] = ["a", "b", "c"];
            for (let w of words) {
                console.log(w);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Typeof Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Number_ReturnsNumber(ExecutionMode mode)
    {
        var source = """
            console.log(typeof 42);
            console.log(typeof 3.14);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("number\nnumber\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_String_ReturnsString(ExecutionMode mode)
    {
        var source = """
            console.log(typeof "hello");
            console.log(typeof "");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\nstring\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Boolean_ReturnsBoolean(ExecutionMode mode)
    {
        var source = """
            console.log(typeof true);
            console.log(typeof false);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("boolean\nboolean\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Array_ReturnsObject(ExecutionMode mode)
    {
        var source = """
            console.log(typeof [1, 2, 3]);
            console.log(typeof []);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\nobject\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Object_ReturnsObject(ExecutionMode mode)
    {
        var source = """
            console.log(typeof { x: 1 });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Function_ReturnsFunction(ExecutionMode mode)
    {
        var source = """
            function foo(): void {}
            console.log(typeof foo);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Typeof_Null_ReturnsObject(ExecutionMode mode)
    {
        var source = """
            let x: string | null = null;
            console.log(typeof x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    #endregion

    #region Instanceof Operator

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Instanceof_DirectInstance_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            class Dog {}
            let d: Dog = new Dog();
            console.log(d instanceof Dog);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Instanceof_InheritedInstance_ReturnsTrue(ExecutionMode mode)
    {
        var source = """
            class Animal {}
            class Dog extends Animal {}
            let d: Dog = new Dog();
            console.log(d instanceof Dog);
            console.log(d instanceof Animal);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Instanceof_DifferentClass_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            class Dog {}
            class Cat {}
            let d: Dog = new Dog();
            console.log(d instanceof Cat);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Instanceof_ParentNotChild_ReturnsFalse(ExecutionMode mode)
    {
        var source = """
            class Animal {}
            class Dog extends Animal {}
            let a: Animal = new Animal();
            console.log(a instanceof Dog);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Break and Continue in While Loops

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void While_WithBreak_ExitsLoop(ExecutionMode mode)
    {
        var source = """
            let i: number = 0;
            while (i < 10) {
                if (i == 5) {
                    break;
                }
                console.log(i);
                i = i + 1;
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void While_WithContinue_SkipsIteration(ExecutionMode mode)
    {
        var source = """
            let i: number = 0;
            while (i < 5) {
                i = i + 1;
                if (i == 3) {
                    continue;
                }
                console.log(i);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n4\n5\n", output);
    }

    #endregion

    #region For Loops

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void For_Basic_IteratesCorrectly(ExecutionMode mode)
    {
        var source = """
            for (let i: number = 0; i < 5; i = i + 1) {
                console.log(i);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void For_WithBreak_ExitsLoop(ExecutionMode mode)
    {
        var source = """
            for (let i: number = 0; i < 10; i = i + 1) {
                if (i == 3) {
                    break;
                }
                console.log(i);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void For_WithContinue_ExecutesIncrement(ExecutionMode mode)
    {
        var source = """
            let sum: number = 0;
            for (let i: number = 0; i < 5; i = i + 1) {
                if (i == 2) {
                    continue;
                }
                sum = sum + i;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);  // 0+1+3+4 = 8
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void For_WithContinue_DoesNotInfiniteLoop(ExecutionMode mode)
    {
        var source = """
            let count: number = 0;
            for (let i: number = 0; i < 3; i = i + 1) {
                count = count + 1;
                continue;
            }
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void For_ContinueAtStart_StillIncrements(ExecutionMode mode)
    {
        var source = """
            let printed: number = 0;
            for (let i: number = 0; i < 5; i = i + 1) {
                if (i < 3) {
                    continue;
                }
                printed = printed + 1;
            }
            console.log(printed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);  // Only i=3 and i=4 reach the increment of printed
    }

    #endregion
}
