using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the non-null assertion operator (postfix !).
/// Runs against both interpreter and compiler.
/// </summary>
public class NonNullAssertionTests
{
    #region Basic Syntax

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnVariable(ExecutionMode mode)
    {
        var source = """
            let x: string | null = "hello";
            let y = x!;
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnNullableString(ExecutionMode mode)
    {
        var source = """
            let name: string | null = "Alice";
            let len = name!.length;
            console.log(len);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnPropertyAccess(ExecutionMode mode)
    {
        var source = """
            interface Person { name: string | null; }
            let p: Person = { name: "Bob" };
            let n = p.name!;
            console.log(n);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Bob\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnMethodResult(ExecutionMode mode)
    {
        var source = """
            function maybeGet(): string | null {
                return "test";
            }
            let result = maybeGet()!;
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Chained Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_ChainedPropertyAccess(ExecutionMode mode)
    {
        var source = """
            interface User { name: string; }
            interface Container { user: User | null; }
            let c: Container = { user: { name: "Charlie" } };
            let name = c.user!.name;
            console.log(name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Charlie\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_ChainedMethodCall(ExecutionMode mode)
    {
        var source = """
            let str: string | null = "hello";
            let upper = str!.toUpperCase();
            console.log(upper);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_MultipleInChain(ExecutionMode mode)
    {
        var source = """
            interface A { b: B | null; }
            interface B { c: C | null; }
            interface C { value: number; }
            let a: A = { b: { c: { value: 42 } } };
            let val = a.b!.c!.value;
            console.log(val);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Array Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_ArrayElement(ExecutionMode mode)
    {
        var source = """
            let arr: (string | null)[] = ["a", "b", "c"];
            let elem = arr[1]!;
            console.log(elem);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("b\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_ArrayElement_WithMethodCall(ExecutionMode mode)
    {
        var source = """
            let arr: (string | null)[] = ["hello", "world"];
            let upper = arr[0]!.toUpperCase();
            console.log(upper);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\n", output);
    }

    #endregion

    #region Object Literal

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InObjectProperty(ExecutionMode mode)
    {
        var source = """
            let x: string | null = "value";
            let obj = { prop: x! };
            console.log(obj.prop);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\n", output);
    }

    #endregion

    #region Function Arguments

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InFunctionArgument(ExecutionMode mode)
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            let maybeName: string | null = "World";
            console.log(greet(maybeName!));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_MultipleArguments(ExecutionMode mode)
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let x: number | null = 10;
            let y: number | null = 20;
            console.log(add(x!, y!));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("30\n", output);
    }

    #endregion

    #region Class Methods and Properties

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnClassProperty(ExecutionMode mode)
    {
        var source = """
            class Container {
                value: string | null;
                constructor(v: string | null) {
                    this.value = v;
                }
            }
            let c = new Container("test");
            console.log(c.value!);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnClassMethodResult(ExecutionMode mode)
    {
        var source = """
            class Provider {
                getValue(): string | null {
                    return "provided";
                }
            }
            let p = new Provider();
            console.log(p.getValue()!);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("provided\n", output);
    }

    #endregion

    #region Arithmetic Operations

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InArithmeticExpression(ExecutionMode mode)
    {
        var source = """
            let x: number | null = 5;
            let y: number | null = 3;
            let result = x! + y!;
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InComparison(ExecutionMode mode)
    {
        var source = """
            let x: number | null = 10;
            let y: number | null = 5;
            console.log(x! > y!);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Combined with Other Operators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_BeforeTernary(ExecutionMode mode)
    {
        var source = """
            let x: number | null = 10;
            let result = x! > 5 ? "big" : "small";
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("big\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_AfterOptionalChaining(ExecutionMode mode)
    {
        var source = """
            interface Data { value: number | null; }
            let obj: Data | null = { value: 42 };
            let result = obj?.value!;
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InStringConcatenation(ExecutionMode mode)
    {
        var source = """
            let first: string | null = "Hello";
            let last: string | null = "World";
            console.log(first! + " " + last!);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello World\n", output);
    }

    #endregion

    #region Type Narrowing

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_NarrowsUnionType(ExecutionMode mode)
    {
        var source = """
            let x: string | null = "hello";
            let y: string = x!;  // Should work - ! narrows to string
            console.log(y.length);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnMultiTypeUnion(ExecutionMode mode)
    {
        var source = """
            let x: string | number | null = "test";
            let y = x!;  // Type is string | number (null removed)
            console.log(typeof y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnAlreadyNonNull(ExecutionMode mode)
    {
        var source = """
            let x: string = "hello";
            let y = x!;  // Redundant but valid
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Nested Expressions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InNestedExpression(ExecutionMode mode)
    {
        var source = """
            let a: number | null = 10;
            let b: number | null = 20;
            let result = (a! + b!) * 2;
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnGroupedExpression(ExecutionMode mode)
    {
        var source = """
            function getValue(): number | null { return 5; }
            let result = (getValue())!;
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    #endregion

    #region Template Literals

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InTemplateLiteral(ExecutionMode mode)
    {
        var source = """
            let name: string | null = "Alice";
            let greeting = `Hello, ${name!}!`;
            console.log(greeting);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Hello, Alice!\n", output);
    }

    #endregion

    #region Arrow Functions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InArrowFunction(ExecutionMode mode)
    {
        var source = """
            let transform = (x: string | null) => x!.toUpperCase();
            console.log(transform("test"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("TEST\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_InArrowFunctionReturn(ExecutionMode mode)
    {
        var source = """
            let transform = (x: string | null): string => {
                let result = x!;
                return result.toUpperCase();
            };
            console.log(transform("hello"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("HELLO\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_SingleParamArrowWithoutParens(ExecutionMode mode)
    {
        // Single-parameter arrow function without parentheses
        var source = """
            let fn = x => x!;
            let result: string | null = "hello";
            console.log(fn(result));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_SingleParamArrowWithBinaryOp(ExecutionMode mode)
    {
        // Single-parameter arrow with non-null assertion followed by comparison
        var source = """
            let arr: (number | null)[] = [1, 2, 3, 4, 5];
            let result = arr.filter(x => x! > 2);
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[3, 4, 5]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_SingleParamArrowWithArithmetic(ExecutionMode mode)
    {
        // Single-parameter arrow with non-null assertion followed by arithmetic
        var source = """
            let arr: (number | null)[] = [1, 2, 3];
            let result = arr.map(x => x! * 2);
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[2, 4, 6]\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_ChainedSingleParamArrows(ExecutionMode mode)
    {
        // Chained array methods with single-parameter arrows
        var source = """
            let arr: (number | null)[] = [1, 2, 3, 4, 5];
            let result = arr.filter(x => x! > 2).map(x => x! * 10);
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[30, 40, 50]\n", output);
    }

    #endregion

    #region With Type Assertions

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_CombinedWithTypeAssertion(ExecutionMode mode)
    {
        var source = """
            let x: unknown = "hello";
            let y = (x as string | null)!;
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnNumberZero(ExecutionMode mode)
    {
        // Zero is falsy but not null - assertion should pass
        var source = """
            let x: number | null = 0;
            console.log(x! + 1);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnEmptyString(ExecutionMode mode)
    {
        // Empty string is falsy but not null - assertion should pass
        var source = """
            let x: string | null = "";
            console.log(x!.length);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_OnBoolean(ExecutionMode mode)
    {
        var source = """
            let x: boolean | null = false;
            console.log(x!);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NonNullAssertion_Consecutive(ExecutionMode mode)
    {
        // Multiple ! in a row (redundant but valid)
        var source = """
            let x: string | null = "test";
            let y = x!!;
            console.log(y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    #endregion
}
