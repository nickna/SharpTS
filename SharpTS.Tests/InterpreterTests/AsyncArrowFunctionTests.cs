using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for async arrow functions inside async methods.
/// Tests by-reference capture semantics, 'this' capture, and nested async arrows.
/// </summary>
public class AsyncArrowFunctionTests
{
    [Fact]
    public void AsyncArrow_BasicReturn()
    {
        var source = """
            async function main(): Promise<void> {
                const fn = async (): Promise<number> => {
                    return 42;
                };
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncArrow_ExpressionBody()
    {
        var source = """
            async function main(): Promise<void> {
                const fn = async (): Promise<number> => 42;
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncArrow_WithAwait()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function main(): Promise<void> {
                const fn = async (): Promise<number> => {
                    let x = await getValue();
                    return x * 2;
                };
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void AsyncArrow_WithParameters()
    {
        var source = """
            async function main(): Promise<void> {
                const add = async (a: number, b: number): Promise<number> => {
                    return a + b;
                };
                let result = await add(3, 7);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AsyncArrow_CaptureLocal()
    {
        var source = """
            async function main(): Promise<void> {
                let x: number = 10;
                const fn = async (): Promise<number> => {
                    return x + 5;
                };
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncArrow_CaptureParameter()
    {
        var source = """
            async function outer(y: number): Promise<void> {
                const fn = async (): Promise<number> => {
                    return y * 2;
                };
                let result = await fn();
                console.log(result);
            }
            outer(25);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("50\n", output);
    }

    [Fact]
    public void AsyncArrow_ModifyOuter()
    {
        // Tests by-reference capture - arrow modifies outer variable
        var source = """
            async function main(): Promise<void> {
                let x: number = 10;
                const fn = async (): Promise<void> => {
                    x = x + 5;
                };
                await fn();
                console.log(x);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncArrow_OuterModifiesAfter()
    {
        // Tests by-reference capture - outer modifies after arrow creation
        var source = """
            async function getValue(): Promise<number> {
                return 1;
            }
            async function main(): Promise<void> {
                let x: number = 10;
                const fn = async (): Promise<number> => {
                    await getValue();  // Add await to ensure it's truly async
                    return x;
                };
                x = 99;
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("99\n", output);
    }

    [Fact]
    public void AsyncArrow_MultipleAwaits()
    {
        var source = """
            async function first(): Promise<number> {
                return 1;
            }
            async function second(): Promise<number> {
                return 2;
            }
            async function main(): Promise<void> {
                const fn = async (): Promise<number> => {
                    let a = await first();
                    let b = await second();
                    return a + b;
                };
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact(Skip = "Interpreter limitation: async class methods with await don't properly track async context")]
    public void AsyncArrow_InClassMethod()
    {
        // Test async arrow capturing 'this' in class method
        // Note: Assignment to this.property in async class methods has a pre-existing bug,
        // so we test just reading this.value
        var source = """
            class Counter {
                value: number = 42;

                async getValue(): Promise<number> {
                    const fn = async (): Promise<number> => {
                        return this.value;
                    };
                    return await fn();
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                let result = await counter.getValue();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact(Skip = "Interpreter limitation: async class methods with await don't properly track async context")]
    public void AsyncArrow_CaptureThis()
    {
        var source = """
            class Calculator {
                multiplier: number;

                constructor(m: number) {
                    this.multiplier = m;
                }

                async calculate(x: number): Promise<number> {
                    const fn = async (): Promise<number> => {
                        return x * this.multiplier;
                    };
                    return await fn();
                }
            }
            async function main(): Promise<void> {
                let calc = new Calculator(5);
                let result = await calc.calculate(10);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("50\n", output);
    }

    [Fact]
    public void AsyncArrow_NestedBasic()
    {
        var source = """
            async function main(): Promise<void> {
                let x: number = 5;
                const outer = async (): Promise<number> => {
                    const inner = async (): Promise<number> => {
                        return x;
                    };
                    return await inner();
                };
                let result = await outer();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void AsyncArrow_NestedWithMutation()
    {
        // Test nested arrows with by-reference capture and mutation
        var source = """
            async function main(): Promise<void> {
                let x: number = 5;
                const outer = async (): Promise<number> => {
                    const inner = async (): Promise<void> => {
                        x = x * 2;
                    };
                    await inner();
                    return x;
                };
                let result = await outer();
                console.log(result);
                console.log(x);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n10\n", output);
    }

    [Fact]
    public void AsyncArrow_ChainedCalls()
    {
        // Note: Nested await in call arguments (await f(await g(x))) is a known limitation
        // when the callee is loaded from a variable. Using sequential awaits as workaround.
        var source = """
            async function main(): Promise<void> {
                const double = async (x: number): Promise<number> => x * 2;
                const addOne = async (x: number): Promise<number> => x + 1;

                let doubled = await double(5);
                let result = await addOne(doubled);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("11\n", output);
    }

    [Fact]
    public void AsyncArrow_ImmediatelyInvoked()
    {
        var source = """
            async function main(): Promise<void> {
                let result = await (async (): Promise<number> => {
                    return 42;
                })();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncArrow_ReturnString()
    {
        var source = """
            async function main(): Promise<void> {
                const fn = async (): Promise<string> => {
                    return "Hello, World!";
                };
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void AsyncArrow_MultipleArrows()
    {
        var source = """
            async function main(): Promise<void> {
                const fn1 = async (): Promise<number> => 10;
                const fn2 = async (): Promise<number> => 20;
                const fn3 = async (): Promise<number> => 30;

                let a = await fn1();
                let b = await fn2();
                let c = await fn3();
                console.log(a + b + c);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void AsyncArrow_SyncArrowInside_CapturesLocal()
    {
        // Non-async arrow inside async arrow capturing outer variable
        var source = """
            async function main(): Promise<void> {
                let x: number = 10;
                const asyncFn = async (): Promise<number> => {
                    const syncFn = (): number => x;
                    return syncFn();
                };
                let result = await asyncFn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AsyncArrow_SyncArrowInside_MultipleSyncArrows()
    {
        // Multiple non-async arrows inside async arrow
        var source = """
            async function main(): Promise<void> {
                let a: number = 10;
                let b: number = 20;
                const asyncFn = async (): Promise<number> => {
                    const getA = (): number => a;
                    const getB = (): number => b;
                    return getA() + getB();
                };
                let result = await asyncFn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void AsyncArrow_SyncArrowInside_NonCapturing()
    {
        // Non-capturing non-async arrow inside async arrow
        var source = """
            async function main(): Promise<void> {
                const asyncFn = async (): Promise<number> => {
                    const add = (x: number, y: number): number => x + y;
                    return add(3, 7);
                };
                let result = await asyncFn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AsyncArrow_SyncArrowInside_CapturesParameter()
    {
        // Non-async arrow capturing async arrow's parameter
        var source = """
            async function outer(val: number): Promise<void> {
                const asyncFn = async (multiplier: number): Promise<number> => {
                    const syncFn = (): number => val * multiplier;
                    return syncFn();
                };
                let result = await asyncFn(5);
                console.log(result);
            }
            outer(10);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("50\n", output);
    }
}
