using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for nested await expressions in call arguments.
/// </summary>
public class AsyncNestedAwaitTests
{
    [Fact]
    public void NestedAwait_GlobalFunction_SingleArg()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function double(x: number): Promise<number> {
                return x * 2;
            }
            async function main(): Promise<void> {
                let result = await double(await getValue());
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void NestedAwait_GlobalFunction_MultipleArgs()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 5;
            }
            async function add(a: number, b: number): Promise<number> {
                return a + b;
            }
            async function main(): Promise<void> {
                let result = await add(await getValue(), await getValue());
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void NestedAwait_ArrowFunction_SingleArg()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function main(): Promise<void> {
                const double = async (x: number): Promise<number> => x * 2;
                let result = await double(await getValue());
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void NestedAwait_ArrowFunction_MultipleArgs()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 3;
            }
            async function main(): Promise<void> {
                const multiply = async (a: number, b: number): Promise<number> => a * b;
                let result = await multiply(await getValue(), await getValue());
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("9\n", output);
    }

    [Fact]
    public void NestedAwait_ChainedCalls()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 5;
            }
            async function main(): Promise<void> {
                const double = async (x: number): Promise<number> => x * 2;
                const addOne = async (x: number): Promise<number> => x + 1;
                let result = await addOne(await double(await getValue()));
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("11\n", output);
    }

    [Fact]
    public void NestedAwait_MixedArgsWithLiteral()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function add(a: number, b: number): Promise<number> {
                return a + b;
            }
            async function main(): Promise<void> {
                let result = await add(await getValue(), 5);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void NestedAwait_InMethodCall()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 7;
            }
            class Calculator {
                async add(a: number, b: number): Promise<number> {
                    return a + b;
                }
            }
            async function main(): Promise<void> {
                let calc = new Calculator();
                let result = await calc.add(await getValue(), await getValue());
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("14\n", output);
    }

    [Fact]
    public void NestedAwait_DeeplyNested()
    {
        var source = """
            async function inc(x: number): Promise<number> {
                return x + 1;
            }
            async function main(): Promise<void> {
                let result = await inc(await inc(await inc(await inc(0))));
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void NestedAwait_WithAwaitInCallee()
    {
        // Callee itself is loaded via await
        var source = """
            async function getFunction(): Promise<(x: number) => number> {
                return (x: number): number => x * 3;
            }
            async function main(): Promise<void> {
                let fn = await getFunction();
                let result = fn(10);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", output);
    }
}
