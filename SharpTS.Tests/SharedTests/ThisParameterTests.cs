using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for 'this' parameter typing feature. Runs against both interpreter and compiler.
/// The 'this' parameter allows explicit declaration of the 'this' type in functions.
/// </summary>
public class ThisParameterTests
{
    #region Basic Function Parsing

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_BasicFunction_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Obj { name: string; }
            function greet(this: Obj): string {
                return this.name;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_WithOtherParams_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Obj { value: number; }
            function add(this: Obj, x: number): number {
                return this.value + x;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_OnlyThisParam_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Self { name: string; }
            function getName(this: Self): string {
                return this.name;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    #endregion

    #region Interface and Type Alias

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_InInterface_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Handler {
                handle(this: Handler, value: number): void;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_InTypeAlias_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Ctx { id: number; }
            type Handler = (this: Ctx, value: string) => void;
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_FunctionTypeAnnotation_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Window { title: string; }
            type ClickHandler = (this: Window, x: number, y: number) => void;
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    #endregion

    #region Class Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_InAbstractMethod_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            abstract class Base {
                abstract handle(this: Base, x: number): number;
            }
            class Derived extends Base {
                value: number = 10;
                override handle(this: Base, x: number): number {
                    return x;
                }
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_ClassMethod_ParsesCorrectly(ExecutionMode mode)
    {
        // This test focuses on parsing - the this parameter allows explicit this type annotation
        var source = """
            class Counter {
                count: number = 0;

                increment(this: Counter): void {
                    // Method with this parameter parses correctly
                }

                getCount(this: Counter): number {
                    return 42;
                }
            }
            let c = new Counter();
            console.log(c.getCount());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Object Literal Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_ObjectLiteralMethod_ParsesCorrectly(ExecutionMode mode)
    {
        // This test focuses on parsing - the this parameter is a compile-time annotation
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
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region With Other Parameter Types

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_WithRestParams_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Logger { prefix: string; }
            function log(this: Logger, ...args: string[]): void {
                console.log(this.prefix);
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_WithDefaultParams_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Config { base: number; }
            function compute(this: Config, multiplier: number = 1): number {
                return this.base * multiplier;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    #endregion

    #region Generic Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_GenericFunction_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Container<T> { value: T; }
            function getValue<T>(this: Container<T>): T {
                return this.value;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    #endregion

    #region Overloaded Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_OverloadedFunction_ParsesCorrectly(ExecutionMode mode)
    {
        var source = """
            interface Obj { id: number; }
            function process(this: Obj, x: number): number;
            function process(this: Obj, x: string): string;
            function process(this: Obj, x: number | string): number | string {
                return x;
            }
            console.log("parsed");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("parsed\n", output);
    }

    #endregion

    #region Type Checking

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_TypeChecksThisAccess(ExecutionMode mode)
    {
        // This test verifies that accessing properties on 'this' is type-checked
        // against the declared this type
        var source = """
            interface Obj {
                name: string;
                value: number;
            }
            function test(this: Obj): string {
                return this.name;
            }
            console.log("type checked");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("type checked\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ThisParameter_ValidThisPropertyAccess_TypeChecks(ExecutionMode mode)
    {
        // Verify that accessing valid properties on the declared this type works
        var source = """
            interface Obj { name: string; value: number; }
            function test(this: Obj): string {
                return this.name;
            }
            console.log("type checked");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("type checked\n", output);
    }

    #endregion
}
