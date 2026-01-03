using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class TemplateLiteralTests
{
    [Fact]
    public void Template_SimpleString_Works()
    {
        var source = """
            console.log(`hello world`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Template_SingleInterpolation_Works()
    {
        var source = """
            let name: string = "World";
            console.log(`Hello, ${name}!`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void Template_MultipleInterpolations_Works()
    {
        var source = """
            let a: number = 5;
            let b: number = 3;
            console.log(`${a} + ${b} = ${a + b}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5 + 3 = 8\n", output);
    }

    [Fact]
    public void Template_ExpressionEvaluation_Works()
    {
        var source = """
            console.log(`Double of 5 is ${5 * 2}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Double of 5 is 10\n", output);
    }

    [Fact]
    public void Template_PropertyAccess_Works()
    {
        var source = """
            let obj: { name: string } = { name: "Alice" };
            console.log(`Name: ${obj.name}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Name: Alice\n", output);
    }

    [Fact]
    public void Template_FunctionCall_Works()
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            console.log(`Result: ${greet("Bob")}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Result: Hello, Bob\n", output);
    }

    [Fact]
    public void Template_NumberCoercion_Works()
    {
        var source = """
            let x: number = 42;
            console.log(`The answer is ${x}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("The answer is 42\n", output);
    }

    [Fact]
    public void Template_BooleanCoercion_Works()
    {
        var source = """
            let flag: boolean = true;
            console.log(`Value is ${flag}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Value is true\n", output);
    }

    [Fact]
    public void Template_NestedInterpolation_Works()
    {
        var source = """
            let x: number = 2;
            let y: number = 3;
            console.log(`Result: ${x * y}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Result: 6\n", output);
    }

    [Fact]
    public void Template_EmptyInterpolation_Works()
    {
        var source = """
            let s: string = "";
            console.log(`before${s}after`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("beforeafter\n", output);
    }

    [Fact]
    public void Template_ConditionalExpression_Works()
    {
        var source = """
            let x: number = 10;
            console.log(`x is ${x > 5 ? "big" : "small"}`);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("x is big\n", output);
    }
}
