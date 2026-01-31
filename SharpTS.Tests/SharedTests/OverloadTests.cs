using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for TypeScript-style function and method overloading. Runs against both interpreter and compiler.
/// </summary>
public class OverloadTests
{
    #region Function Overloads

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Function_Overload_ArityBased(ExecutionMode mode)
    {
        var source = """
            function greet(name: string): string;
            function greet(name: string, age: number): string;
            function greet(name: string, age?: number): string {
                if (age !== null) {
                    return name + " is " + age;
                }
                return "Hello " + name;
            }
            console.log(greet("Alice"));
            console.log(greet("Bob", 30));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello Alice\nBob is 30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Function_Overload_TypeBased(ExecutionMode mode)
    {
        var source = """
            function process(x: number): number;
            function process(x: string): string;
            function process(x: number | string): number | string {
                if (typeof x === "number") {
                    return x * 2;
                }
                return x.toUpperCase();
            }
            console.log(process(5));
            console.log(process("hello"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\nHELLO\n", output);
    }

    #endregion

    #region Method Overloads

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Method_Overload_Basic(ExecutionMode mode)
    {
        var source = """
            class Calculator {
                add(a: number): number;
                add(a: number, b: number): number;
                add(a: number, b?: number): number {
                    if (b !== null) {
                        return a + b;
                    }
                    return a + 1;
                }
            }
            let calc: Calculator = new Calculator();
            console.log(calc.add(5));
            console.log(calc.add(5, 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n15\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Method_Overload_ThreeSignatures(ExecutionMode mode)
    {
        var source = """
            class Math2 {
                max(a: number): number;
                max(a: number, b: number): number;
                max(a: number, b: number, c: number): number;
                max(a: number, b?: number, c?: number): number {
                    let result: number = a;
                    if (b !== null && b > result) {
                        result = b;
                    }
                    if (c !== null && c > result) {
                        result = c;
                    }
                    return result;
                }
            }
            let m: Math2 = new Math2();
            console.log(m.max(5));
            console.log(m.max(3, 7));
            console.log(m.max(1, 9, 4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n7\n9\n", output);
    }

    #endregion

    #region Constructor Overloads

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Constructor_Overload(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number);
                constructor(value: number);
                constructor(xOrValue: number, y?: number) {
                    if (y !== null) {
                        this.x = xOrValue;
                        this.y = y;
                    } else {
                        this.x = xOrValue;
                        this.y = xOrValue;
                    }
                }
            }
            let p1: Point = new Point(3, 4);
            let p2: Point = new Point(5);
            console.log(p1.x + "," + p1.y);
            console.log(p2.x + "," + p2.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,4\n5,5\n", output);
    }

    #endregion

    #region Static Method Overloads

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StaticMethod_Overload(ExecutionMode mode)
    {
        var source = """
            class StringUtils {
                static format(value: number): string;
                static format(value: string): string;
                static format(value: number | string): string {
                    if (typeof value === "number") {
                        return "Number: " + value;
                    }
                    return "String: " + value;
                }
            }
            console.log(StringUtils.format(42));
            console.log(StringUtils.format("test"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Number: 42\nString: test\n", output);
    }

    #endregion

    #region Overload with Inheritance

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Overload_WithInheritance(ExecutionMode mode)
    {
        var source = """
            class Animal {
                speak(): string;
                speak(loud: boolean): string;
                speak(loud?: boolean): string {
                    if (loud === true) {
                        return "LOUD!";
                    }
                    return "quiet";
                }
            }
            class Dog extends Animal {
                bark(): string {
                    return "woof";
                }
            }
            let d: Dog = new Dog();
            console.log(d.speak());
            console.log(d.speak(true));
            console.log(d.bark());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("quiet\nLOUD!\nwoof\n", output);
    }

    #endregion

    #region Method Chaining with Overloads

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Overload_MethodChaining(ExecutionMode mode)
    {
        var source = """
            class Builder {
                value: string;
                constructor() {
                    this.value = "";
                }
                append(s: string): Builder;
                append(n: number): Builder;
                append(x: string | number): Builder {
                    this.value = this.value + x;
                    return this;
                }
                build(): string {
                    return this.value;
                }
            }
            let b: Builder = new Builder();
            let result: string = b.append("Hello").append(123).append("World").build();
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello123World\n", output);
    }

    #endregion
}
