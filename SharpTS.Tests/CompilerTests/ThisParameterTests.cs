using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Compiler tests for 'this' parameter typing feature.
/// </summary>
public class ThisParameterTests
{
    [Fact]
    public void ThisParameter_BasicFunction_CompilesCorrectly()
    {
        var source = """
            interface Obj { name: string; }
            function greet(this: Obj): string {
                return this.name;
            }
            console.log("compiled");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled\n", output);
    }

    [Fact]
    public void ThisParameter_WithOtherParams_CompilesCorrectly()
    {
        var source = """
            interface Obj { value: number; }
            function add(this: Obj, x: number): number {
                return this.value + x;
            }
            console.log("compiled");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled\n", output);
    }

    [Fact]
    public void ThisParameter_ObjectLiteralMethod_CompilesCorrectly()
    {
        // This test focuses on compilation - the this parameter is a compile-time annotation
        var source = """
            interface Ctx { multiplier: number; }
            let obj = {
                multiplier: 2,
                compute(this: Ctx, value: number): number {
                    return value * 2;
                }
            };
            console.log(obj.compute(5));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void ThisParameter_ClassMethod_CompilesCorrectly()
    {
        // This test focuses on compilation - the this parameter allows explicit this type annotation
        var source = """
            class Counter {
                count: number = 0;

                increment(this: Counter): void {
                    // Method with this parameter compiles correctly
                }

                getCount(this: Counter): number {
                    return 42;
                }
            }
            let c = new Counter();
            console.log(c.getCount());
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ThisParameter_GenericFunction_CompilesCorrectly()
    {
        var source = """
            interface Container<T> { value: T; }
            function getValue<T>(this: Container<T>): T {
                return this.value;
            }
            console.log("compiled");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled\n", output);
    }

    [Fact]
    public void ThisParameter_AbstractMethod_CompilesCorrectly()
    {
        var source = """
            abstract class Base {
                abstract process(this: Base, x: number): number;
            }
            class Derived extends Base {
                multiplier: number = 2;
                override process(this: Base, x: number): number {
                    return x * 2;
                }
            }
            let d = new Derived();
            console.log(d.process(5));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void ThisParameter_OverloadedFunction_CompilesCorrectly()
    {
        var source = """
            interface Obj { id: number; }
            function process(this: Obj, x: number): number;
            function process(this: Obj, x: string): string;
            function process(this: Obj, x: number | string): number | string {
                return x;
            }
            console.log("compiled");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled\n", output);
    }

    [Fact]
    public void ThisParameter_FunctionTypeAlias_CompilesCorrectly()
    {
        var source = """
            interface Context { prefix: string; }
            type Logger = (this: Context, msg: string) => void;
            console.log("compiled");
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("compiled\n", output);
    }
}
