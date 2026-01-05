using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Async/await compilation tests.
/// Uses native IL state machine generation (no Roslyn dependency).
/// </summary>
public class AsyncAwaitTests
{

    [Fact]
    public void AsyncFunction_BasicReturn()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                let x = await getValue();
                console.log(x);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncFunction_MultipleAwaits()
    {
        var source = """
            async function first(): Promise<number> {
                return 10;
            }
            async function second(): Promise<number> {
                return 20;
            }
            async function main(): Promise<void> {
                let a = await first();
                let b = await second();
                console.log(a + b);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void AsyncFunction_ChainedCalls()
    {
        var source = """
            async function first(): Promise<number> {
                return 5;
            }
            async function second(x: number): Promise<number> {
                return x * 2;
            }
            async function third(x: number): Promise<string> {
                return "Result: " + x;
            }
            async function main(): Promise<void> {
                let a = await first();
                let b = await second(a);
                let c = await third(b);
                console.log(c);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Result: 10\n", output);
    }

    [Fact]
    public void AsyncFunction_WithParameters()
    {
        var source = """
            async function add(a: number, b: number): Promise<number> {
                return a + b;
            }
            async function main(): Promise<void> {
                let sum = await add(3, 7);
                console.log(sum);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AsyncFunction_StringReturn()
    {
        var source = """
            async function greet(name: string): Promise<string> {
                return "Hello, " + name + "!";
            }
            async function main(): Promise<void> {
                let msg = await greet("World");
                console.log(msg);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void AsyncFunction_InLoop()
    {
        var source = """
            async function getNumber(n: number): Promise<number> {
                return n;
            }
            async function main(): Promise<void> {
                let sum = 0;
                for (let i = 1; i <= 3; i++) {
                    sum += await getNumber(i);
                }
                console.log(sum);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void AsyncFunction_InConditional()
    {
        var source = """
            async function check(value: number): Promise<boolean> {
                return value > 5;
            }
            async function main(): Promise<void> {
                if (await check(10)) {
                    console.log("greater");
                } else {
                    console.log("lesser");
                }
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("greater\n", output);
    }

    [Fact]
    public void AsyncFunction_NestedCalls()
    {
        var source = """
            async function inner(): Promise<number> {
                return 5;
            }
            async function outer(): Promise<number> {
                let x = await inner();
                return x + 10;
            }
            async function main(): Promise<void> {
                let result = await outer();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncFunction_WithLocalVariables()
    {
        var source = """
            async function compute(x: number): Promise<number> {
                let doubled = x * 2;
                let incremented = doubled + 1;
                return incremented;
            }
            async function main(): Promise<void> {
                let result = await compute(10);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("21\n", output);
    }

    [Fact]
    public void AsyncFunction_BooleanReturn()
    {
        var source = """
            async function isEven(n: number): Promise<boolean> {
                return n % 2 === 0;
            }
            async function main(): Promise<void> {
                let result = await isEven(4);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void AsyncFunction_TernaryExpression()
    {
        var source = """
            async function check(): Promise<boolean> {
                return true;
            }
            async function main(): Promise<void> {
                let result = await check() ? "yes" : "no";
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void AsyncFunction_LogicalOperators()
    {
        var source = """
            async function getTrue(): Promise<boolean> {
                return true;
            }
            async function getFalse(): Promise<boolean> {
                return false;
            }
            async function main(): Promise<void> {
                let a = await getTrue() && await getFalse();
                let b = await getTrue() || await getFalse();
                console.log(a);
                console.log(b);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void AsyncFunction_CalledMultipleTimes()
    {
        var source = """
            async function getNumber(): Promise<number> {
                return 10;
            }
            async function main(): Promise<void> {
                let a = await getNumber();
                let b = await getNumber();
                console.log(a + b);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void AsyncFunction_VoidReturn()
    {
        var source = """
            async function printMessage(): Promise<void> {
                console.log("Hello");
            }
            async function main(): Promise<void> {
                await printMessage();
                console.log("Done");
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\nDone\n", output);
    }

    #region Phase 5: Super in async methods

    [Fact]
    public void AsyncMethod_SuperMethodCall()
    {
        var source = """
            class Parent {
                greet(): string {
                    return "Hello";
                }
            }
            class Child extends Parent {
                async greetAsync(): Promise<string> {
                    await Promise.resolve(null);
                    return super.greet() + " World";
                }
            }
            async function main(): Promise<void> {
                let child = new Child();
                let result = await child.greetAsync();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello World\n", output);
    }

    [Fact]
    public void AsyncMethod_SuperMethodCallBeforeAwait()
    {
        var source = """
            class Parent {
                getValue(): number {
                    return 10;
                }
            }
            class Child extends Parent {
                async calculate(): Promise<number> {
                    let parentVal = super.getValue();
                    await Promise.resolve(null);
                    return parentVal * 2;
                }
            }
            async function main(): Promise<void> {
                let child = new Child();
                let result = await child.calculate();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void AsyncMethod_SuperMethodCallAfterAwait()
    {
        var source = """
            class Parent {
                getValue(): number {
                    return 5;
                }
            }
            class Child extends Parent {
                async calculate(): Promise<number> {
                    await Promise.resolve(null);
                    return super.getValue() + 3;
                }
            }
            async function main(): Promise<void> {
                let child = new Child();
                let result = await child.calculate();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void AsyncMethod_SuperWithParameters()
    {
        var source = """
            class Parent {
                add(a: number, b: number): number {
                    return a + b;
                }
            }
            class Child extends Parent {
                async addAsync(x: number, y: number): Promise<number> {
                    await Promise.resolve(null);
                    return super.add(x, y);
                }
            }
            async function main(): Promise<void> {
                let child = new Child();
                let result = await child.addAsync(7, 8);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Phase 6: Capturing arrow functions in async

    [Fact]
    public void AsyncFunction_CapturingArrowLocal()
    {
        var source = """
            async function test(): Promise<number> {
                let x = 10;
                await Promise.resolve(null);
                let fn = () => x * 2;
                return fn();
            }
            async function main(): Promise<void> {
                let result = await test();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void AsyncFunction_CapturingArrowParameter()
    {
        var source = """
            async function test(y: number): Promise<number> {
                await Promise.resolve(null);
                let fn = () => y + 5;
                return fn();
            }
            async function main(): Promise<void> {
                let result = await test(10);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncFunction_CapturingArrowMultipleVariables()
    {
        var source = """
            async function test(a: number): Promise<number> {
                let b = 20;
                await Promise.resolve(null);
                let fn = () => a + b;
                return fn();
            }
            async function main(): Promise<void> {
                let result = await test(10);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void AsyncMethod_CapturingArrowThis()
    {
        var source = """
            class Counter {
                value: number = 0;
                async increment(): Promise<number> {
                    await Promise.resolve(null);
                    let fn = () => {
                        this.value = this.value + 1;
                        return this.value;
                    };
                    return fn();
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                let result = await counter.increment();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void AsyncFunction_CapturingArrowBeforeAwait()
    {
        var source = """
            async function test(): Promise<number> {
                let x = 5;
                let fn = () => x * 3;
                await Promise.resolve(null);
                return fn();
            }
            async function main(): Promise<void> {
                let result = await test();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncFunction_CapturingArrowPassedToMethod()
    {
        var source = """
            function apply(arr: number[], fn: (x: number) => number): number[] {
                return arr.map(fn);
            }
            async function test(): Promise<number[]> {
                let multiplier = 2;
                await Promise.resolve(null);
                return apply([1, 2, 3], (x) => x * multiplier);
            }
            async function main(): Promise<void> {
                let result = await test();
                console.log(result.join(","));
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2,4,6\n", output);
    }

    #endregion
}
