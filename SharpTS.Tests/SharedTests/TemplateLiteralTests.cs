using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for template literals (backtick strings). Runs against both interpreter and compiler.
/// </summary>
public class TemplateLiteralTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_SimpleString_Works(ExecutionMode mode)
    {
        var source = """
            console.log(`hello world`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_SingleInterpolation_Works(ExecutionMode mode)
    {
        var source = """
            let name: string = "World";
            console.log(`Hello, ${name}!`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_MultipleInterpolations_Works(ExecutionMode mode)
    {
        var source = """
            let a: number = 5;
            let b: number = 3;
            console.log(`${a} + ${b} = ${a + b}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 + 3 = 8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_ExpressionEvaluation_Works(ExecutionMode mode)
    {
        var source = """
            console.log(`Double of 5 is ${5 * 2}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Double of 5 is 10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_PropertyAccess_Works(ExecutionMode mode)
    {
        var source = """
            let obj: { name: string } = { name: "Alice" };
            console.log(`Name: ${obj.name}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Name: Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_FunctionCall_Works(ExecutionMode mode)
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            console.log(`Result: ${greet("Bob")}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Result: Hello, Bob\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_NumberCoercion_Works(ExecutionMode mode)
    {
        var source = """
            let x: number = 42;
            console.log(`The answer is ${x}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("The answer is 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_BooleanCoercion_Works(ExecutionMode mode)
    {
        var source = """
            let flag: boolean = true;
            console.log(`Value is ${flag}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Value is true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_NestedInterpolation_Works(ExecutionMode mode)
    {
        var source = """
            let x: number = 2;
            let y: number = 3;
            console.log(`Result: ${x * y}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Result: 6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_EmptyInterpolation_Works(ExecutionMode mode)
    {
        var source = """
            let s: string = "";
            console.log(`before${s}after`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("beforeafter\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Template_ConditionalExpression_Works(ExecutionMode mode)
    {
        var source = """
            let x: number = 10;
            console.log(`x is ${x > 5 ? "big" : "small"}`);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x is big\n", output);
    }
}
