using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async/await functionality. Runs against both interpreter and compiler.
/// </summary>
public class AsyncAwaitTests
{
    private const string SuspendingForLoopSource = """
        async function next(value: number): Promise<number> {
            return await Promise.resolve(value).then(
                (current: number): number => current);
        }
        async function main(): Promise<void> {
            const values: number[] = [10, 20, 30];
            for (let i: number = 0; i < values.length; i++) {
                console.log(await next(values[i]));
            }
            console.log("done");
        }
        main();
        """;

    #region Basic Async Functions

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void AwaitInForLoop_PreservesBackedgeLocalsAcrossRealSuspension(
        ExecutionMode mode)
    {
        Assert.Equal(
            "10\n20\n30\ndone\n",
            TestHarness.Run(SuspendingForLoopSource, mode));
    }

    [Fact]
    public void AwaitInForLoop_StandaloneCompletesEveryIteration()
    {
        Assert.Equal(
            "10\n20\n30\ndone\n",
            TestHarness.RunCompiledStandalone(SuspendingForLoopSource));
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    #region Top-level then-continuations stay on the event loop (#238)

    /// <summary>
    /// #238: a then-chain started at module top level must have its continuation
    /// run on the interpreter's event loop. Before the fix, the continuation
    /// captured a null SynchronizationContext (it was installed only in
    /// RunEventLoop, after top-level statements), resumed on a thread-pool
    /// thread, and raced the main thread's ambient environment — surfacing as
    /// "Undefined variable 'v'" for the callback's own parameter, or as
    /// silently missing output.
    /// </summary>
    /// <remarks>
    /// Interpreter-only: in compiled mode a top-level <c>p.then(...)</c>
    /// expression statement blocks on the promise at the entry point, so a
    /// resolve that happens in a later statement would deadlock — a separate
    /// known compiled-mode behavior, not what this test pins.
    /// </remarks>
    [Theory, InterpretedOnlyData]
    public void TopLevelThen_ResolvedLater_RunsCallbackWithItsParameter(ExecutionMode mode)
    {
        var source = """
            let resolveFn: any;
            const p = new Promise((resolve) => { resolveFn = resolve; });
            p.then((v: any) => console.log("then:", v.tag));
            resolveFn({ tag: "ok" });
            console.log("end");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Contains("then: ok", output);
        Assert.Contains("end", output);
    }

    #endregion

    #region Null vs Undefined Across Await (#600)

    // #600: in compiled mode, awaiting a promise that resolves with JS null produced a value that
    // compared `=== undefined` as true. Two root causes inside state-machine bodies: the `undefined`
    // literal was emitted as CLR null (no SharpTSUndefined arm in the base EmitLiteral), and `===`
    // used the null≡undefined-collapsing loose equality helper instead of strict. The awaited null
    // must stay null-ish (typeof "object", === null) but must NOT be === undefined.
    [Theory, ModeData]
    public void Await_NullResolvedPromise_IsNotStrictUndefined(ExecutionMode mode)
    {
        var source = """
            async function f() { return null; }
            async function main() {
                const v = await f();
                console.log(typeof v);
                console.log(v === null);
                console.log(v === undefined);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\nfalse\n", output);
    }

    // #600 (companion): the `undefined` literal must be the undefined sentinel inside an async body,
    // and `===` must keep null and undefined distinct while loose `==` still collapses them.
    [Theory, ModeData]
    public void Await_UndefinedResolvedPromise_AndStrictEquality(ExecutionMode mode)
    {
        var source = """
            async function g() { return undefined; }
            async function main() {
                const u = await g();
                console.log(typeof u);
                console.log(u === undefined);
                console.log(u === null);
                console.log(null === undefined);
                console.log(null == undefined);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\ntrue\nfalse\nfalse\ntrue\n", output);
    }

    #endregion
}
