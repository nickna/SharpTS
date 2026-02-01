using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES2022 private class elements (#field, #method) in async contexts.
/// These verify that private field access works correctly in async methods,
/// async arrow functions, generators, and async generators.
/// </summary>
public class PrivateFieldAsyncTests
{
    #region Async Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_CanReadPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 42;

                async getCount(): Promise<number> {
                    await Promise.resolve();
                    return this.#count;
                }
            }

            const c = new Counter();
            c.getCount().then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_CanWritePrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;

                async increment(): Promise<number> {
                    await Promise.resolve();
                    this.#count = this.#count + 1;
                    return this.#count;
                }

                getCount(): number {
                    return this.#count;
                }
            }

            const c = new Counter();
            c.increment().then(v => {
                console.log(v);
                console.log(c.getCount());
            });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_CanCallPrivateMethod(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                #add(a: number, b: number): number {
                    return a + b;
                }

                async sum(x: number, y: number): Promise<number> {
                    await Promise.resolve();
                    return this.#add(x, y);
                }
            }

            const calc = new Calculator();
            calc.sum(3, 4).then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_MultipleAwaitsWithPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;

                async incrementTwice(): Promise<number> {
                    await Promise.resolve();
                    this.#count = this.#count + 1;
                    await Promise.resolve();
                    this.#count = this.#count + 1;
                    return this.#count;
                }
            }

            const c = new Counter();
            c.incrementTwice().then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    #endregion

    #region Generators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_CanReadPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #values: number[] = [1, 2, 3];

                *getValues() {
                    for (const v of this.#values) {
                        yield v;
                    }
                }
            }

            const c = new Counter();
            for (const v of c.getValues()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_CanWritePrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;

                *countUp(limit: number) {
                    while (this.#count < limit) {
                        this.#count = this.#count + 1;
                        yield this.#count;
                    }
                }

                getCount(): number {
                    return this.#count;
                }
            }

            const c = new Counter();
            for (const v of c.countUp(3)) {
                console.log(v);
            }
            console.log(c.getCount());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Generator_CanCallPrivateMethod(ExecutionMode mode)
    {
        var source = """
            class Processor {
                #double(n: number): number {
                    return n * 2;
                }

                *processValues() {
                    yield this.#double(1);
                    yield this.#double(2);
                    yield this.#double(3);
                }
            }

            const p = new Processor();
            for (const v of p.processValues()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n6\n", output);
    }

    #endregion

    #region Async Generators

    // NOTE: Async generator methods in classes require interpreter support for recognizing
    // async generator class methods as async iterables, and compiler support for generator
    // state machine building in class methods.

    [Theory(Skip = "Interpreter does not recognize async generator class methods as async iterables yet")]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncGenerator_CanReadPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #values: number[] = [1, 2, 3];

                async *getValues() {
                    for (const v of this.#values) {
                        await Promise.resolve();
                        yield v;
                    }
                }
            }

            async function main() {
                const c = new Counter();
                for await (const v of c.getValues()) {
                    console.log(v);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory(Skip = "Interpreter does not recognize async generator class methods as async iterables yet")]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncGenerator_CanWritePrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;

                async *countUp(limit: number) {
                    while (this.#count < limit) {
                        await Promise.resolve();
                        this.#count = this.#count + 1;
                        yield this.#count;
                    }
                }

                getCount(): number {
                    return this.#count;
                }
            }

            async function main() {
                const c = new Counter();
                for await (const v of c.countUp(3)) {
                    console.log(v);
                }
                console.log(c.getCount());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n3\n", output);
    }

    #endregion

    #region Async Arrow Functions (in class methods)

    // NOTE: Async arrow functions inside regular (non-async) class methods have a pre-existing
    // compilation issue where the promise callback is not invoked. These tests are marked as
    // InterpretedOnly until that issue is resolved.

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncArrow_CanReadPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 42;

                process(): Promise<number> {
                    const getAsync = async () => {
                        await Promise.resolve();
                        return this.#count;
                    };
                    return getAsync();
                }
            }

            const c = new Counter();
            c.process().then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncArrow_CanWritePrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number = 0;

                process(): Promise<number> {
                    const incrementAsync = async () => {
                        await Promise.resolve();
                        this.#count = this.#count + 1;
                        return this.#count;
                    };
                    return incrementAsync();
                }

                getCount(): number {
                    return this.#count;
                }
            }

            const c = new Counter();
            c.process().then(v => {
                console.log(v);
                console.log(c.getCount());
            });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncArrow_CanCallPrivateMethod(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                #multiply(a: number, b: number): number {
                    return a * b;
                }

                process(): Promise<number> {
                    const calculate = async () => {
                        await Promise.resolve();
                        return this.#multiply(6, 7);
                    };
                    return calculate();
                }
            }

            const calc = new Calculator();
            calc.process().then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Static Private Fields in Async Contexts

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_StaticPrivateField(ExecutionMode mode)
    {
        var source = """
            class Counter {
                static #instanceCount: number = 0;

                static async getCountAsync(): Promise<number> {
                    await Promise.resolve();
                    return Counter.#instanceCount;
                }

                static async incrementAsync(): Promise<void> {
                    await Promise.resolve();
                    Counter.#instanceCount = Counter.#instanceCount + 1;
                }
            }

            async function main() {
                console.log(await Counter.getCountAsync());
                await Counter.incrementAsync();
                console.log(await Counter.getCountAsync());
                await Counter.incrementAsync();
                console.log(await Counter.getCountAsync());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_PrivateFieldWithMultipleInstances(ExecutionMode mode)
    {
        var source = """
            class Counter {
                #count: number;

                constructor(initial: number) {
                    this.#count = initial;
                }

                async increment(): Promise<number> {
                    await Promise.resolve();
                    this.#count = this.#count + 1;
                    return this.#count;
                }
            }

            async function main() {
                const a = new Counter(0);
                const b = new Counter(100);

                console.log(await a.increment());
                console.log(await b.increment());
                console.log(await a.increment());
                console.log(await b.increment());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n101\n2\n102\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_MixedPublicAndPrivateFields(ExecutionMode mode)
    {
        var source = """
            class Box {
                name: string;
                #secret: number = 42;

                constructor(name: string) {
                    this.name = name;
                }

                async reveal(): Promise<string> {
                    await Promise.resolve();
                    return this.name + ": " + this.#secret;
                }
            }

            const box = new Box("MyBox");
            box.reveal().then(v => console.log(v));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("MyBox: 42\n", output);
    }

    #endregion
}
