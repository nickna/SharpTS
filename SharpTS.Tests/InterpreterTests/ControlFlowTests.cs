using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ControlFlowTests
{
    // Switch Statements
    [Fact]
    public void Switch_BasicCase_MatchesCorrectly()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("two\n", output);
    }

    [Fact]
    public void Switch_DefaultCase_ExecutesWhenNoMatch()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("other\n", output);
    }

    [Fact]
    public void Switch_FallThrough_ExecutesMultipleCases()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("two\nthree\n", output);
    }

    [Fact]
    public void Switch_WithString_MatchesCorrectly()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    // For-of Loops
    [Fact]
    public void ForOf_Basic_IteratesArray()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            for (let n of nums) {
                console.log(n);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void ForOf_WithBreak_ExitsLoop()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void ForOf_WithContinue_SkipsIteration()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n4\n5\n", output);
    }

    [Fact]
    public void ForOf_WithStrings_IteratesElements()
    {
        var source = """
            let words: string[] = ["a", "b", "c"];
            for (let w of words) {
                console.log(w);
            }
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("a\nb\nc\n", output);
    }

    // Typeof Operator
    [Fact]
    public void Typeof_Number_ReturnsNumber()
    {
        var source = """
            console.log(typeof 42);
            console.log(typeof 3.14);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("number\nnumber\n", output);
    }

    [Fact]
    public void Typeof_String_ReturnsString()
    {
        var source = """
            console.log(typeof "hello");
            console.log(typeof "");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("string\nstring\n", output);
    }

    [Fact]
    public void Typeof_Boolean_ReturnsBoolean()
    {
        var source = """
            console.log(typeof true);
            console.log(typeof false);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("boolean\nboolean\n", output);
    }

    [Fact]
    public void Typeof_Array_ReturnsObject()
    {
        var source = """
            console.log(typeof [1, 2, 3]);
            console.log(typeof []);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\nobject\n", output);
    }

    [Fact]
    public void Typeof_Object_ReturnsObject()
    {
        var source = """
            console.log(typeof { x: 1 });
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void Typeof_Function_ReturnsFunction()
    {
        var source = """
            function foo(): void {}
            console.log(typeof foo);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("function\n", output);
    }

    [Fact]
    public void Typeof_Null_ReturnsObject()
    {
        var source = """
            let x: string | null = null;
            console.log(typeof x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\n", output);
    }

    // Instanceof Operator
    [Fact]
    public void Instanceof_DirectInstance_ReturnsTrue()
    {
        var source = """
            class Dog {}
            let d: Dog = new Dog();
            console.log(d instanceof Dog);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Instanceof_InheritedInstance_ReturnsTrue()
    {
        var source = """
            class Animal {}
            class Dog extends Animal {}
            let d: Dog = new Dog();
            console.log(d instanceof Dog);
            console.log(d instanceof Animal);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Instanceof_DifferentClass_ReturnsFalse()
    {
        var source = """
            class Dog {}
            class Cat {}
            let d: Dog = new Dog();
            console.log(d instanceof Cat);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Instanceof_ParentNotChild_ReturnsFalse()
    {
        var source = """
            class Animal {}
            class Dog extends Animal {}
            let a: Animal = new Animal();
            console.log(a instanceof Dog);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    // Break and Continue in While Loops
    [Fact]
    public void While_WithBreak_ExitsLoop()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1\n2\n3\n4\n", output);
    }

    [Fact]
    public void While_WithContinue_SkipsIteration()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n4\n5\n", output);
    }
}
