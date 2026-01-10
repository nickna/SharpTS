using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for the @lock decorator which provides thread-safe method execution.
/// </summary>
public class LockDecoratorTests
{
    [Fact]
    public void LockDecorator_BasicUsage_Works()
    {
        // Basic test that @lock decorator doesn't break compilation
        var source = """
            class Counter {
                value: number = 0;

                @lock
                increment(): void {
                    this.value = this.value + 1;
                }
            }

            let c: Counter = new Counter();
            c.increment();
            c.increment();
            c.increment();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LockDecorator_WithReturnValue_Works()
    {
        // Test that @lock method can return values correctly
        var source = """
            class Calculator {
                result: number = 0;

                @lock
                addAndGet(n: number): number {
                    this.result = this.result + n;
                    return this.result;
                }
            }

            let calc: Calculator = new Calculator();
            console.log(calc.addAndGet(5));
            console.log(calc.addAndGet(10));
            console.log(calc.addAndGet(3));
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("5\n15\n18\n", output);
    }

    [Fact]
    public void LockDecorator_Reentrancy_AllowsNestedCalls()
    {
        // Test that a @lock method calling another @lock method doesn't deadlock
        var source = """
            class Counter {
                value: number = 0;

                @lock
                innerIncrement(): void {
                    this.value = this.value + 1;
                }

                @lock
                outerIncrement(): void {
                    this.innerIncrement();
                    this.innerIncrement();
                }
            }

            let c: Counter = new Counter();
            c.outerIncrement();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void LockDecorator_StaticMethod_Works()
    {
        // Test that @lock works on static methods
        var source = """
            class Counter {
                static count: number = 0;

                @lock
                static increment(): void {
                    Counter.count = Counter.count + 1;
                }
            }

            Counter.increment();
            Counter.increment();
            Counter.increment();
            console.log(Counter.count);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LockDecorator_StaticReentrancy_AllowsNestedCalls()
    {
        // Test that a static @lock method calling another static @lock method doesn't deadlock
        var source = """
            class Counter {
                static value: number = 0;

                @lock
                static innerIncrement(): void {
                    Counter.value = Counter.value + 1;
                }

                @lock
                static outerIncrement(): void {
                    Counter.innerIncrement();
                    Counter.innerIncrement();
                }
            }

            Counter.outerIncrement();
            console.log(Counter.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void LockDecorator_MultipleInstances_IndependentLocks()
    {
        // Test that different instances have independent locks
        var source = """
            class Counter {
                name: string;
                value: number = 0;

                constructor(name: string) {
                    this.name = name;
                }

                @lock
                increment(): void {
                    this.value = this.value + 1;
                }
            }

            let a: Counter = new Counter("A");
            let b: Counter = new Counter("B");

            a.increment();
            a.increment();
            b.increment();

            console.log(a.value);
            console.log(b.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n1\n", output);
    }

    [Fact]
    public void LockDecorator_MixedLockAndNonLock_Works()
    {
        // Test that non-@lock methods still work alongside @lock methods
        var source = """
            class Counter {
                value: number = 0;

                @lock
                lockedIncrement(): void {
                    this.value = this.value + 1;
                }

                unlockedIncrement(): void {
                    this.value = this.value + 10;
                }
            }

            let c: Counter = new Counter();
            c.lockedIncrement();
            c.unlockedIncrement();
            c.lockedIncrement();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("12\n", output);
    }

    [Fact]
    public void LockDecorator_WithInheritance_Works()
    {
        // Test @lock with inheritance - child class has its own lock
        var source = """
            class Parent {
                value: number = 0;

                @lock
                parentIncrement(): void {
                    this.value = this.value + 1;
                }
            }

            class Child extends Parent {
                @lock
                childIncrement(): void {
                    this.value = this.value + 10;
                }
            }

            let c: Child = new Child();
            c.parentIncrement();
            c.childIncrement();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("11\n", output);
    }

    [Fact]
    public void LockDecorator_NoDecorator_NoOverhead()
    {
        // Test that classes without @lock don't get lock fields
        var source = """
            class Plain {
                value: number = 0;

                increment(): void {
                    this.value = this.value + 1;
                }
            }

            let p: Plain = new Plain();
            p.increment();
            p.increment();
            console.log(p.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void LockDecorator_ExceptionInMethod_ReleasesLock()
    {
        // Test that exceptions inside @lock methods still release the lock
        // (The method should be callable again after exception)
        var source = """
            class Counter {
                value: number = 0;
                shouldThrow: boolean = false;

                @lock
                increment(): void {
                    if (this.shouldThrow) {
                        throw "Error!";
                    }
                    this.value = this.value + 1;
                }
            }

            let c: Counter = new Counter();
            c.increment();
            c.shouldThrow = true;

            try {
                c.increment();
            } catch (e) {
                console.log("caught");
            }

            c.shouldThrow = false;
            c.increment();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("caught\n2\n", output);
    }

    [Fact]
    public void LockDecorator_DeeplyNestedReentrancy_Works()
    {
        // Test deeply nested reentrant calls
        var source = """
            class Counter {
                value: number = 0;

                @lock
                level1(): void {
                    this.value = this.value + 1;
                    this.level2();
                }

                @lock
                level2(): void {
                    this.value = this.value + 1;
                    this.level3();
                }

                @lock
                level3(): void {
                    this.value = this.value + 1;
                }
            }

            let c: Counter = new Counter();
            c.level1();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LockDecorator_LegacyMode_Works()
    {
        // Test that @lock works in Legacy decorator mode too
        var source = """
            class Counter {
                value: number = 0;

                @lock
                increment(): void {
                    this.value = this.value + 1;
                }
            }

            let c: Counter = new Counter();
            c.increment();
            c.increment();
            console.log(c.value);
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Legacy);
        Assert.Equal("2\n", output);
    }

    // ==================== Async @lock Tests ====================

    [Fact]
    public void LockDecorator_AsyncMethod_BasicUsage_Works()
    {
        // Basic test that @lock decorator works on async methods
        var source = """
            async function asyncValue(): Promise<number> {
                return 1;
            }

            class Counter {
                value: number = 0;

                @lock
                async increment(): Promise<void> {
                    let v = await asyncValue();
                    this.value = this.value + v;
                }
            }

            async function main(): Promise<void> {
                let c: Counter = new Counter();
                await c.increment();
                await c.increment();
                await c.increment();
                console.log(c.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void LockDecorator_AsyncMethod_WithReturnValue_Works()
    {
        // Test that async @lock method can return values correctly
        var source = """
            async function asyncValue(n: number): Promise<number> {
                return n;
            }

            class Calculator {
                result: number = 0;

                @lock
                async addAndGet(n: number): Promise<number> {
                    let v = await asyncValue(n);
                    this.result = this.result + v;
                    return this.result;
                }
            }

            async function main(): Promise<void> {
                let calc: Calculator = new Calculator();
                console.log(await calc.addAndGet(5));
                console.log(await calc.addAndGet(10));
                console.log(await calc.addAndGet(3));
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("5\n15\n18\n", output);
    }

    [Fact]
    public void LockDecorator_AsyncMethod_Reentrancy_AllowsNestedCalls()
    {
        // Test that an async @lock method calling another async @lock method doesn't deadlock
        var source = """
            async function asyncValue(): Promise<number> {
                return 1;
            }

            class Counter {
                value: number = 0;

                @lock
                async innerIncrement(): Promise<void> {
                    let v = await asyncValue();
                    this.value = this.value + v;
                }

                @lock
                async outerIncrement(): Promise<void> {
                    await this.innerIncrement();
                    await this.innerIncrement();
                }
            }

            async function main(): Promise<void> {
                let c: Counter = new Counter();
                await c.outerIncrement();
                console.log(c.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void LockDecorator_AsyncMethod_ExceptionInMethod_ReleasesLock()
    {
        // Test that exceptions inside async @lock methods still release the lock
        var source = """
            async function asyncValue(): Promise<number> {
                return 1;
            }

            class Counter {
                value: number = 0;
                shouldThrow: boolean = false;

                @lock
                async increment(): Promise<void> {
                    let v = await asyncValue();
                    if (this.shouldThrow) {
                        throw "Error!";
                    }
                    this.value = this.value + v;
                }
            }

            async function main(): Promise<void> {
                let c: Counter = new Counter();
                await c.increment();
                c.shouldThrow = true;

                try {
                    await c.increment();
                } catch (e) {
                    console.log("caught");
                }

                c.shouldThrow = false;
                await c.increment();
                console.log(c.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("caught\n2\n", output);
    }

    [Fact]
    public void LockDecorator_AsyncMethod_NoAwait_Works()
    {
        // Test that async @lock method without actual await still works
        var source = """
            class Counter {
                value: number = 0;

                @lock
                async increment(): Promise<void> {
                    this.value = this.value + 1;
                }
            }

            async function main(): Promise<void> {
                let c: Counter = new Counter();
                await c.increment();
                await c.increment();
                console.log(c.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void LockDecorator_AsyncMethod_MultipleInstances_IndependentLocks()
    {
        // Test that different instances have independent async locks
        var source = """
            async function asyncValue(): Promise<number> {
                return 1;
            }

            class Counter {
                name: string;
                value: number = 0;

                constructor(name: string) {
                    this.name = name;
                }

                @lock
                async increment(): Promise<void> {
                    let v = await asyncValue();
                    this.value = this.value + v;
                }
            }

            async function main(): Promise<void> {
                let a: Counter = new Counter("A");
                let b: Counter = new Counter("B");

                await a.increment();
                await a.increment();
                await b.increment();

                console.log(a.value);
                console.log(b.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("2\n1\n", output);
    }

    [Fact]
    public void LockDecorator_MixedSyncAndAsync_Works()
    {
        // Test that sync @lock and async @lock methods work together
        var source = """
            async function asyncValue(): Promise<number> {
                return 10;
            }

            class Counter {
                value: number = 0;

                @lock
                syncIncrement(): void {
                    this.value = this.value + 1;
                }

                @lock
                async asyncIncrement(): Promise<void> {
                    let v = await asyncValue();
                    this.value = this.value + v;
                }
            }

            async function main(): Promise<void> {
                let c: Counter = new Counter();
                c.syncIncrement();
                await c.asyncIncrement();
                c.syncIncrement();
                console.log(c.value);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source, DecoratorMode.Stage3);
        Assert.Equal("12\n", output);
    }
}
