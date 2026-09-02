using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async arrow functions inside async methods.
/// Tests by-reference capture semantics, 'this' capture, and nested async arrows.
/// </summary>
public class AsyncArrowFunctionTests
{
    [Theory, InterpretedOnlyData]
    public void AsyncArrow_CallRestoresCallerScopeBeforeThenCallback(ExecutionMode mode)
    {
        var source = """
            const suspend = async (): Promise<void> => {
                await new Promise((resolve: any) => setTimeout(resolve, 10));
            };

            function run(): Promise<void> {
                const marker = "caller scope";
                return suspend().then(() => console.log(marker));
            }

            run();
            """;

        Assert.Equal("caller scope\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_SyncArrowCapturesCanonicalTopLevelFunctionObject(ExecutionMode mode)
    {
        var source = """
            function helper() {}
            helper.extra = 42;

            async function run() {
                const callback = () => helper.extra;
                return callback();
            }

            run().then(value => console.log(value));
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_BasicReturn(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_ExpressionBody(ExecutionMode mode)
    {
        var source = """
            async function main(): Promise<void> {
                const fn = async (): Promise<number> => 42;
                let result = await fn();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_WithAwait(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_WithParameters(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_CaptureLocal(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_CaptureParameter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("50\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_ModifyOuter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_OuterModifiesAfter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_MultipleAwaits(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_InClassMethod(ExecutionMode mode)
    {
        // Test async arrow capturing 'this' in class method
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_CaptureThis(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("50\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_NestedBasic(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_NestedWithMutation(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n10\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_ChainedCalls(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_ImmediatelyInvoked(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_ReturnString(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World!\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_MultipleArrows(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_SyncArrowInside_CapturesLocal(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_SyncArrowInside_MultipleSyncArrows(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_SyncArrowInside_NonCapturing(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void AsyncArrow_SyncArrowInside_CapturesParameter(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("50\n", output);
    }

    // #615: a top-level (standalone) async arrow whose body nests another async-arrow expression
    // (e.g. an immediately-invoked async arrow) failed to compile with "Async arrow with nested
    // arrows does not have SelfBoxedField set." — the standalone arrow never provisioned the
    // <>__selfBoxed field the nested-arrow emit requires. The interpreter was always correct.
    [Theory, ModeData]
    public void AsyncArrow_NestsAsyncArrowIife_Compiles(ExecutionMode mode)
    {
        var source = """
            const f = async () => { const x = await (async () => 9)(); console.log(x); };
            f();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);
    }

    // #615: deeper and repeated nesting of self-contained async arrows inside a standalone async
    // arrow.
    [Theory, ModeData]
    public void AsyncArrow_NestsAsyncArrows_DeepAndRepeated(ExecutionMode mode)
    {
        var source = """
            const f = async () => {
                const a = await (async () => 1)();
                const b = await (async () => await (async () => 2)())();
                console.log(a + b);
            };
            f();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    // #615: a *parameterized* nested async arrow inside a standalone async arrow. The nested arrow
    // is emitted as an independent TSFunction over its own stub (null target); passing the
    // enclosing arrow's boxed state machine as the target would clobber the first parameter
    // (InvalidCastException at the call site).
    [Theory, ModeData]
    public void AsyncArrow_NestsParameterizedAsyncArrow(ExecutionMode mode)
    {
        var source = """
            const f = async () => {
                const x = await (async (n: number) => n + 1)(5);
                const y = await (async (a: number, b: number) => a * b)(6, 7);
                console.log(x + " " + y);
            };
            f();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6 42\n", output);
    }

    // #430/#645: a `for await...of` over an async generator inside an async ARROW must drive the
    // async-iterator protocol. Previously the arrow emitter had no EmitForOf override, so the loop
    // fell through to the synchronous for-of path and threw InvalidCastException casting the
    // async-generator state machine to IEnumerable in compiled mode.
    [Theory, ModeData]
    public void AsyncArrow_ForAwaitOfAsyncGenerator(ExecutionMode mode)
    {
        var source = """
            async function* g() { yield 1; yield 2; }
            const run = async () => {
                let s = "";
                for await (const v of g()) s += v + ",";
                console.log("arrow=" + s);
            };
            run();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("arrow=1,2,\n", output);
    }

    // #430/#645: breaking out of a `for await` inside an async arrow must run the loop's cleanup
    // (iterator.return()) path without corrupting the loop variable binding.
    [Theory, ModeData]
    public void AsyncArrow_ForAwaitBreakRunsCleanup(ExecutionMode mode)
    {
        var source = """
            async function* g() { yield 10; yield 20; yield 30; }
            const run = async () => {
                for await (const v of g()) {
                    console.log("v=" + v);
                    if (v === 20) break;
                }
                console.log("done");
            };
            run();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("v=10\nv=20\ndone\n", output);
    }

    // #430/#645: the async-arrow `for await` must also honor the custom Symbol.asyncIterator
    // protocol (not only the $IAsyncGenerator fast path).
    [Theory, ModeData]
    public void AsyncArrow_ForAwaitCustomAsyncIterator(ExecutionMode mode)
    {
        var source = """
            const obj = {
                [Symbol.asyncIterator]() {
                    let i = 0;
                    return {
                        next() {
                            return Promise.resolve(i < 3 ? { value: i++, done: false } : { value: undefined, done: true });
                        }
                    };
                }
            };
            const run = async () => {
                let s = "";
                for await (const v of obj) s += v;
                console.log("got=" + s);
            };
            run();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got=012\n", output);
    }

    // #430/#645: the loop variable bound inside an async-arrow `for await` must resolve to the
    // arrow's own local store. The original fix landed `null` values here because the binding was
    // registered in Ctx.Locals while the arrow's resolver reads its private local map.
    [Theory, ModeData]
    public void AsyncArrow_ForAwaitAccumulatesCapturedVariable(ExecutionMode mode)
    {
        var source = """
            async function* g() { yield 1; yield 2; yield 3; }
            const run = async () => {
                let sum = 0;
                for await (const v of g()) sum += v;
                console.log("sum=" + sum);
            };
            run();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("sum=6\n", output);
    }
}
