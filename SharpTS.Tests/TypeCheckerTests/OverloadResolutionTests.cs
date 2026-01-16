using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for overload resolution in TypeChecker.Calls.cs.
/// Tests signature matching, most-specific selection, and error cases.
/// </summary>
public class OverloadResolutionTests
{
    #region Basic Overload Selection

    [Fact]
    public void OverloadByParameterCount_SelectsCorrectOne()
    {
        var source = """
            function format(value: string): string;
            function format(value: string, prefix: string): string;
            function format(value: string, prefix?: string): string {
                return (prefix ?? "") + value;
            }

            console.log(format("test"));
            console.log(format("test", ">>> "));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("test\n>>> test\n", result);
    }

    [Fact]
    public void OverloadByParameterType_SelectsCorrectOne()
    {
        var source = """
            function stringify(value: number): string;
            function stringify(value: boolean): string;
            function stringify(value: number | boolean): string {
                return "" + value;
            }

            console.log(stringify(42));
            console.log(stringify(true));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\ntrue\n", result);
    }

    [Fact(Skip = "parseInt() global function not available in SharpTS")]
    public void OverloadReturnType_DependsOnOverload()
    {
        var source = """
            function parse(input: string): number;
            function parse(input: number): string;
            function parse(input: string | number): number | string {
                if (typeof input === "string") {
                    return parseInt(input);
                }
                return "" + input;
            }

            let n: number = parse("42");
            let s: string = parse(42);
            console.log(n);
            console.log(s);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n42\n", result);
    }

    #endregion

    #region Most Specific Overload Selection

    [Fact]
    public void LiteralType_MoreSpecificThanPrimitive()
    {
        var source = """
            function handle(action: "start"): string;
            function handle(action: string): number;
            function handle(action: string): string | number {
                if (action === "start") {
                    return "started";
                }
                return 0;
            }

            let result1: string = handle("start");
            let result2: number = handle("stop");
            console.log(result1);
            console.log(result2);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("started\n0\n", result);
    }

    [Fact]
    public void SpecificType_MoreSpecificThanUnion()
    {
        var source = """
            function process(value: number): string;
            function process(value: number | string): number;
            function process(value: number | string): string | number {
                if (typeof value === "number") {
                    return "number: " + value;
                }
                return value.length;
            }

            console.log(process(42));
            console.log(process("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("number: 42\n5\n", result);
    }

    #endregion

    #region Array Method Overloads

    [Fact]
    public void ArrayPush_AcceptsElementType()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.push(4);
            arr.push(5, 6);
            console.log(arr.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3,4,5,6\n", result);
    }

    [Fact]
    public void ArrayMap_InfersReturnType()
    {
        var source = """
            let nums = [1, 2, 3];
            let strs: string[] = nums.map((n) => "" + n);
            console.log(strs.join(","));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3\n", result);
    }

    [Fact]
    public void ArrayReduce_WithInitialValue()
    {
        var source = """
            let nums = [1, 2, 3, 4];
            let sum: number = nums.reduce((acc, n) => acc + n, 0);
            console.log(sum);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", result);
    }

    #endregion

    #region Constructor Overloads

    [Fact]
    public void ClassConstructor_MultipleOverloads()
    {
        var source = """
            class Point {
                x: number;
                y: number;

                constructor();
                constructor(x: number);
                constructor(x: number, y: number);
                constructor(x?: number, y?: number) {
                    this.x = x ?? 0;
                    this.y = y ?? 0;
                }
            }

            let p1 = new Point();
            let p2 = new Point(5);
            let p3 = new Point(3, 4);

            console.log(p1.x + "," + p1.y);
            console.log(p2.x + "," + p2.y);
            console.log(p3.x + "," + p3.y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("0,0\n5,0\n3,4\n", result);
    }

    #endregion

    #region Rest Parameters in Overloads

    [Fact]
    public void OverloadWithRestParam_SelectsSpecificFirst()
    {
        var source = """
            function log(message: string): void;
            function log(message: string, ...args: any[]): void;
            function log(message: string, ...args: any[]): void {
                console.log(message);
            }

            log("simple");
            log("with", "extra", "args");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("simple\nwith\n", result);
    }

    #endregion

    #region Generic Overloads

    [Fact(Skip = "Generic function overload type inference not yet fully implemented")]
    public void GenericOverload_InfersTypeArg()
    {
        var source = """
            function first<T>(arr: T[]): T | undefined;
            function first<T>(arr: T[], defaultValue: T): T;
            function first<T>(arr: T[], defaultValue?: T): T | undefined {
                return arr.length > 0 ? arr[0] : defaultValue;
            }

            console.log(first([1, 2, 3]));
            console.log(first([], 0));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n0\n", result);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void NoMatchingOverload_Fails()
    {
        var source = """
            function process(value: number): string;
            function process(value: string): number;
            function process(value: number | string): string | number {
                return "" + value;
            }

            process(true);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void TooFewArguments_Fails()
    {
        var source = """
            function add(a: number, b: number): number;
            function add(a: number, b: number): number {
                return a + b;
            }

            add(1);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void WrongArgumentType_Fails()
    {
        var source = """
            function double(x: number): number;
            function double(x: number): number {
                return x * 2;
            }

            double("hello");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    #endregion

    #region Interface Method Overloads

    [Fact(Skip = "Interface with overloaded methods cannot be satisfied by object literal with union parameter")]
    public void InterfaceMethodOverload_Works()
    {
        var source = """
            interface Formatter {
                format(value: number): string;
                format(value: string): string;
            }

            let formatter: Formatter = {
                format: (value: number | string): string => "" + value
            };

            console.log(formatter.format(42));
            console.log(formatter.format("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\nhello\n", result);
    }

    #endregion

    #region Declaration Order

    [Fact]
    public void DeclarationOrder_FirstMatchWins()
    {
        // When multiple overloads match equally, first declared should win
        var source = """
            function test(x: string): string;
            function test(x: any): number;
            function test(x: any): string | number {
                return typeof x === "string" ? x : 0;
            }

            let result: string = test("hello");
            console.log(result);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region String Method Overloads

    [Fact]
    public void StringSlice_Overloads()
    {
        var source = """
            let str = "hello world";
            console.log(str.slice(0, 5));
            console.log(str.slice(6));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\nworld\n", result);
    }

    [Fact]
    public void StringSplit_WithLimit()
    {
        var source = """
            let str = "a,b,c,d,e";
            let parts = str.split(",", 3);
            console.log(parts.join("|"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("a|b|c\n", result);
    }

    #endregion

    #region Optional Parameter vs Overload

    [Fact]
    public void OptionalParam_CompatibleWithOverloads()
    {
        var source = """
            function greet(name: string): string;
            function greet(name: string, formal: boolean): string;
            function greet(name: string, formal?: boolean): string {
                return formal ? "Good day, " + name : "Hi, " + name;
            }

            console.log(greet("Alice"));
            console.log(greet("Bob", true));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("Hi, Alice\nGood day, Bob\n", result);
    }

    #endregion
}
