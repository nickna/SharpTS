using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class AsyncAwaitTests
{
    [Fact]
    public void AsyncFunction_ReturnsPromise()
    {
        var source = """
            async function getData(): Promise<number> {
                return 42;
            }
            let result = getData();
            console.log(typeof result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void AsyncFunction_AwaitReturnsValue()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void AsyncArrowFunction_Works()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void AsyncArrowFunction_ExpressionBody()
    {
        var source = """
            const double = async (x: number): Promise<number> => x * 2;
            async function test(): Promise<void> {
                let result = await double(21);
                console.log(result);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AwaitInLoop()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void AwaitInConditional()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("greater\n", output);
    }

    [Fact]
    public void ChainedAwaits()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Result: 20\n", output);
    }

    [Fact]
    public void AwaitOnNonPromise_ReturnsValue()
    {
        var source = """
            async function test(): Promise<void> {
                let x = await 42;
                console.log(x);
            }
            test();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncClassMethod()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void NestedAsyncCalls()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AsyncWithParameters()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void AsyncWithDefaultParameters()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, Guest\nHello, Alice\n", output);
    }

    [Fact]
    public void AsyncWithTryCatch()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void MultipleAwaitsSameFunction()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void AsyncWithObjectReturn()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("30\n", output);
    }

    [Fact]
    public void AsyncWithArrayReturn()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void AwaitInTemplateLiteral()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void AwaitInTernary()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("yes\n", output);
    }

    [Fact]
    public void TypeChecker_AwaitOutsideAsync_ThrowsError()
    {
        var source = """
            function test(): number {
                return await 42;
            }
            """;

        var exception = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("await", exception.Message.ToLower());
    }

    [Fact]
    public void TypeChecker_AsyncReturnsPromise()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            let x: Promise<number> = getValue();
            console.log("ok");
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", output);
    }

    [Fact]
    public void AwaitWithLogicalOperator()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void AwaitWithNullishCoalescing()
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("100\n", output);
    }
}
