using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for return type inference when functions lack explicit return type annotations.
/// </summary>
public class ReturnTypeInferenceTests
{
    [Fact]
    public void Function_InfersNumberReturn()
    {
        var source = """
            function add(a: number, b: number) {
                return a + b;
            }
            let x: number = add(1, 2);
            console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", result);
    }

    [Fact]
    public void Function_InfersStringReturn()
    {
        var source = """
            function greet(name: string) {
                return "Hello, " + name;
            }
            let s: string = greet("world");
            console.log(s);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, world\n", result);
    }

    [Fact]
    public void Function_InfersVoidWhenNoReturn()
    {
        var source = """
            function log(msg: string) {
                console.log(msg);
            }
            log("hello");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void Function_InferredReturnUsedByCallers()
    {
        // The inferred return type should be used for downstream type checking
        var source = """
            function getNum() { return 42; }
            let s: string = getNum();
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ClassMethod_InfersReturn()
    {
        var source = """
            class Calculator {
                double(x: number) {
                    return x * 2;
                }
            }
            let c = new Calculator();
            let result: number = c.double(21);
            console.log(result);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void ArrowBlockBody_InfersReturn()
    {
        var source = """
            const multiply = (a: number, b: number) => {
                return a * b;
            };
            let result: number = multiply(6, 7);
            console.log(result);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void Function_IssueReproduction()
    {
        // The original issue reproduction case
        var source = """
            function test() {
                return 42;
            }
            console.log(test());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void Function_InfersBoolean()
    {
        var source = """
            function isPositive(n: number) {
                return n > 0;
            }
            let b: boolean = isPositive(5);
            console.log(b);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", result);
    }

    [Fact]
    public void Function_WithExplicitAnnotation_StillWorks()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            console.log(add(3, 4));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n", result);
    }

    [Fact]
    public void Function_ExplicitAnnotation_StillRejectsWrongReturn()
    {
        var source = """
            function getNum(): number {
                return "hello";
            }
            """;

        Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
    }
}
