using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class StaticMembersTests
{
    [Fact]
    public void StaticField_InitializedCorrectly()
    {
        var source = """
            class Config {
                static version: number = 42;
            }
            console.log(Config.version);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void StaticField_StringInitializer()
    {
        var source = """
            class Config {
                static name: string = "SharpTS";
            }
            console.log(Config.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("SharpTS\n", output);
    }

    [Fact]
    public void StaticMethod_CanBeCalled()
    {
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
            }
            console.log(Math2.square(5));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("25\n", output);
    }

    [Fact]
    public void StaticMethod_CallsOtherStatic()
    {
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
                static cube(x: number): number {
                    return x * Math2.square(x);
                }
            }
            console.log(Math2.cube(3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("27\n", output);
    }

    [Fact]
    public void StaticField_ModificationPersists()
    {
        var source = """
            class Counter {
                static count: number = 0;
            }
            console.log(Counter.count);
            Counter.count = 10;
            console.log(Counter.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n10\n", output);
    }

    [Fact]
    public void StaticMethod_ModifiesStaticField()
    {
        var source = """
            class Counter {
                static count: number = 0;
                static increment(): number {
                    Counter.count = Counter.count + 1;
                    return Counter.count;
                }
            }
            console.log(Counter.increment());
            console.log(Counter.increment());
            console.log(Counter.count);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n2\n", output);
    }

    [Fact]
    public void StaticAndInstance_AreSeparate()
    {
        var source = """
            class Box {
                static total: number = 0;
                value: number;
                constructor(v: number) {
                    this.value = v;
                    Box.total = Box.total + 1;
                }
            }
            let a: Box = new Box(10);
            let b: Box = new Box(20);
            console.log(a.value);
            console.log(b.value);
            console.log(Box.total);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n2\n", output);
    }

    [Fact]
    public void StaticMethod_WithParameters()
    {
        var source = """
            class Calculator {
                static add(a: number, b: number): number {
                    return a + b;
                }
                static multiply(a: number, b: number): number {
                    return a * b;
                }
            }
            console.log(Calculator.add(3, 5));
            console.log(Calculator.multiply(4, 6));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("8\n24\n", output);
    }
}
