using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async/await functionality. Runs against both interpreter and compiler.
/// </summary>
public class AsyncAwaitTests
{
    #region Basic Async Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_ReturnsPromise(ExecutionMode mode)
    {
        var source = """
            async function getData(): Promise<number> {
                return 42;
            }
            let result = getData();
            console.log(typeof result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_AwaitReturnsValue(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 100;
            }
            async function main(): Promise<void> {
                let x = await getValue();
                console.log(x);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncArrowFunction_Works(ExecutionMode mode)
    {
        var source = """
            const add = async (a: number, b: number): Promise<number> => {
                return a + b;
            };
            async function test(): Promise<void> {
                let sum = await add(3, 7);
                console.log(sum);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncArrowFunction_ExpressionBody(ExecutionMode mode)
    {
        var source = """
            const double = async (x: number): Promise<number> => x * 2;
            async function test(): Promise<void> {
                let result = await double(21);
                console.log(result);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_VoidReturn(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello\nDone\n", output);
    }

    #endregion

    #region Await in Control Flow

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitInLoop(ExecutionMode mode)
    {
        var source = """
            async function getNumber(n: number): Promise<number> {
                return n;
            }
            async function sumValues(): Promise<void> {
                let sum = 0;
                for (let i = 1; i <= 3; i++) {
                    sum += await getNumber(i);
                }
                console.log(sum);
            }
            sumValues();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitInConditional(ExecutionMode mode)
    {
        var source = """
            async function check(value: number): Promise<boolean> {
                return value > 5;
            }
            async function test(): Promise<void> {
                if (await check(10)) {
                    console.log("greater");
                } else {
                    console.log("lesser");
                }
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("greater\n", output);
    }

    #endregion

    #region Chained and Nested Calls

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ChainedAwaits(ExecutionMode mode)
    {
        var source = """
            async function first(): Promise<number> {
                return 10;
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Result: 20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedAsyncCalls(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MultipleAwaitsSameFunction(ExecutionMode mode)
    {
        var source = """
            async function getNumber(): Promise<number> {
                return 10;
            }
            async function test(): Promise<void> {
                let a = await getNumber();
                let b = await getNumber();
                console.log(a + b);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    #endregion

    #region Await with Non-Promise

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitOnNonPromise_ReturnsValue(ExecutionMode mode)
    {
        var source = """
            async function test(): Promise<void> {
                let x = await 42;
                console.log(x);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Async Class Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncClassMethod(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                async add(a: number, b: number): Promise<number> {
                    return a + b;
                }
            }
            async function test(): Promise<void> {
                let calc = new Calculator();
                let result = await calc.add(5, 3);
                console.log(result);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region Parameters and Return Types

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncWithParameters(ExecutionMode mode)
    {
        var source = """
            async function greet(name: string): Promise<string> {
                return "Hello, " + name;
            }
            async function test(): Promise<void> {
                let message = await greet("World");
                console.log(message);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncWithDefaultParameters(ExecutionMode mode)
    {
        var source = """
            async function greet(name: string = "Guest"): Promise<string> {
                return "Hello, " + name;
            }
            async function test(): Promise<void> {
                let msg1 = await greet();
                let msg2 = await greet("Alice");
                console.log(msg1);
                console.log(msg2);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, Guest\nHello, Alice\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncWithObjectReturn(ExecutionMode mode)
    {
        var source = """
            async function getData(): Promise<{ x: number; y: number }> {
                return { x: 10, y: 20 };
            }
            async function test(): Promise<void> {
                let obj = await getData();
                console.log(obj.x + obj.y);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncWithArrayReturn(ExecutionMode mode)
    {
        var source = """
            async function getNumbers(): Promise<number[]> {
                return [1, 2, 3, 4, 5];
            }
            async function test(): Promise<void> {
                let arr = await getNumbers();
                let sum = 0;
                for (let n of arr) {
                    sum += n;
                }
                console.log(sum);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_BooleanReturn(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_WithLocalVariables(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("21\n", output);
    }

    #endregion

    #region Await in Expressions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitInTemplateLiteral(ExecutionMode mode)
    {
        var source = """
            async function getName(): Promise<string> {
                return "World";
            }
            async function test(): Promise<void> {
                console.log(`Hello, ${await getName()}!`);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitInTernary(ExecutionMode mode)
    {
        var source = """
            async function check(): Promise<boolean> {
                return true;
            }
            async function test(): Promise<void> {
                let result = await check() ? "yes" : "no";
                console.log(result);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("yes\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitWithLogicalOperator(ExecutionMode mode)
    {
        var source = """
            async function getTrue(): Promise<boolean> {
                return true;
            }
            async function getFalse(): Promise<boolean> {
                return false;
            }
            async function test(): Promise<void> {
                let a = await getTrue() && await getFalse();
                let b = await getTrue() || await getFalse();
                console.log(a);
                console.log(b);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AwaitWithNullishCoalescing(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number | null> {
                return null;
            }
            async function test(): Promise<void> {
                let x = await getValue() ?? 100;
                console.log(x);
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region Try/Catch

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncWithTryCatch(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function test(): Promise<void> {
                try {
                    let x = await getValue();
                    console.log(x);
                } catch (e) {
                    console.log("error");
                }
            }
            test();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Type Checking

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeChecker_AwaitOutsideAsync_ThrowsError(ExecutionMode mode)
    {
        var source = """
            function test(): number {
                return await 42;
            }
            """;

        var exception = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("await", exception.Message.ToLower());
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TypeChecker_AsyncReturnsPromise(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            let x: Promise<number> = getValue();
            console.log("ok");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    #endregion

    #region Super in Async Methods (Compiler)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_SuperMethodCall(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_SuperMethodCallBeforeAwait(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_SuperMethodCallAfterAwait(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_SuperWithParameters(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    #endregion

    #region Capturing Arrow Functions in Async

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_CapturingArrowLocal(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_CapturingArrowParameter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_CapturingArrowMultipleVariables(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_CapturingArrowThis(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_CapturingArrowBeforeAwait(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncFunction_CapturingArrowPassedToMethod(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,4,6\n", output);
    }

    #endregion
}
