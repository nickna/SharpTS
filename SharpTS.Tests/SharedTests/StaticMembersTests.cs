using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for static class members (fields and methods). Runs against both interpreter and compiler.
/// </summary>
public class StaticMembersTests
{
    #region Static Fields

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticField_InitializedCorrectly(ExecutionMode mode)
    {
        var source = """
            class Config {
                static version: number = 42;
            }
            console.log(Config.version);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticField_StringInitializer(ExecutionMode mode)
    {
        var source = """
            class Config {
                static name: string = "SharpTS";
            }
            console.log(Config.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("SharpTS\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticField_ModificationPersists(ExecutionMode mode)
    {
        var source = """
            class Counter {
                static count: number = 0;
            }
            console.log(Counter.count);
            Counter.count = 10;
            console.log(Counter.count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n10\n", output);
    }

    #endregion

    #region Static Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticMethod_CanBeCalled(ExecutionMode mode)
    {
        var source = """
            class Math2 {
                static square(x: number): number {
                    return x * x;
                }
            }
            console.log(Math2.square(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("25\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticMethod_CallsOtherStatic(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("27\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticMethod_ModifiesStaticField(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticMethod_WithParameters(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n24\n", output);
    }

    #endregion

    #region Static and Instance Separation

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticAndInstance_AreSeparate(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n2\n", output);
    }

    #endregion
}
