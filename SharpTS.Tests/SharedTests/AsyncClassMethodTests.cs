using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for async class methods, particularly this.property access and assignment.
/// Runs against both interpreter and compiler.
/// </summary>
public class AsyncClassMethodTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_ReadThisProperty(ExecutionMode mode)
    {
        var source = """
            class Counter {
                value: number = 42;

                async getValue(): Promise<number> {
                    return this.value;
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                let val = await counter.getValue();
                console.log(val);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_WriteThisProperty(ExecutionMode mode)
    {
        var source = """
            class Counter {
                value: number = 0;

                async setValue(v: number): Promise<void> {
                    this.value = v;
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                await counter.setValue(99);
                console.log(counter.value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_IncrementThisProperty(ExecutionMode mode)
    {
        var source = """
            class Counter {
                value: number = 0;

                async increment(): Promise<void> {
                    this.value = this.value + 1;
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                console.log(counter.value);
                await counter.increment();
                console.log(counter.value);
                await counter.increment();
                console.log(counter.value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_MultipleProperties(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number = 0;
                y: number = 0;

                async move(dx: number, dy: number): Promise<void> {
                    this.x = this.x + dx;
                    this.y = this.y + dy;
                }

                async getPosition(): Promise<string> {
                    return "(" + this.x + "," + this.y + ")";
                }
            }
            async function main(): Promise<void> {
                let p = new Point();
                console.log(await p.getPosition());
                await p.move(5, 10);
                console.log(await p.getPosition());
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("(0,0)\n(5,10)\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_PropertyWithAwait(ExecutionMode mode)
    {
        var source = """
            async function getIncrement(): Promise<number> {
                return 5;
            }
            class Counter {
                value: number = 10;

                async addIncrement(): Promise<void> {
                    let inc = await getIncrement();
                    this.value = this.value + inc;
                }
            }
            async function main(): Promise<void> {
                let counter = new Counter();
                await counter.addIncrement();
                console.log(counter.value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_AssignmentExpression(ExecutionMode mode)
    {
        // Tests that property assignment returns the assigned value
        var source = """
            class Box {
                value: number = 0;

                async setAndReturn(v: number): Promise<number> {
                    return this.value = v;
                }
            }
            async function main(): Promise<void> {
                let box = new Box();
                let result = await box.setAndReturn(42);
                console.log("returned: " + result);
                console.log("property: " + box.value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("returned: 42\nproperty: 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncMethod_ChainedPropertyAccess(ExecutionMode mode)
    {
        var source = """
            class Inner {
                value: number = 100;
            }
            class Outer {
                inner: Inner;
                constructor() {
                    this.inner = new Inner();
                }
                async updateInner(v: number): Promise<void> {
                    this.inner.value = v;
                }
            }
            async function main(): Promise<void> {
                let outer = new Outer();
                console.log(outer.inner.value);
                await outer.updateInner(200);
                console.log(outer.inner.value);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }
}
