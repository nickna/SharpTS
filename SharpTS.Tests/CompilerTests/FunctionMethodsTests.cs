using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for function methods (bind, call, apply) in compiled mode.
/// </summary>
public class FunctionMethodsTests
{
    // bind tests
    [Fact]
    public void Bind_PartialApplication_PrependArgs()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let add5 = add.bind(null, 5);
            console.log(add5(3));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void Bind_ArrowFunction_IgnoresThisArg()
    {
        var source = """
            let outer = { name: "outer" };
            let fn = (): string => "arrow";
            let bound = fn.bind(outer);
            console.log(bound());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("arrow\n", output);
    }

    // call tests
    [Fact]
    public void Call_WithMultipleArgs()
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            console.log(sum.call(null, 1, 2, 3));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void Call_ArrowFunction_IgnoresThisArg()
    {
        var source = """
            let fn = (x: number): number => x * 2;
            let obj = { value: 100 };
            console.log(fn.call(obj, 5));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    // apply tests
    [Fact]
    public void Apply_SpreadArrayArgs()
    {
        var source = """
            function sum(a: number, b: number, c: number): number {
                return a + b + c;
            }
            let args: number[] = [1, 2, 3];
            console.log(sum.apply(null, args));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void Apply_NullArgs_CallsWithNoArgs()
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, null));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hi\n", output);
    }

    [Fact]
    public void Apply_EmptyArgs_CallsWithNoArgs()
    {
        var source = """
            function sayHi(): string {
                return "Hi";
            }
            console.log(sayHi.apply(null, []));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hi\n", output);
    }
}
