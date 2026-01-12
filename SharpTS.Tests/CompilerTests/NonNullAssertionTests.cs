using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for the non-null assertion operator (postfix !).
/// Compiler parity tests - same as InterpreterTests but using RunCompiled.
/// </summary>
public class NonNullAssertionTests
{
    #region Basic Syntax

    [Fact]
    public void NonNullAssertion_OnVariable()
    {
        var source = """
            let x: string | null = "hello";
            let y = x!;
            console.log(y);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnNullableString()
    {
        var source = """
            let name: string | null = "Alice";
            let len = name!.length;
            console.log(len);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnPropertyAccess()
    {
        var source = """
            interface Person { name: string | null; }
            let p: Person = { name: "Bob" };
            let n = p.name!;
            console.log(n);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Bob\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnMethodResult()
    {
        var source = """
            function maybeGet(): string | null {
                return "test";
            }
            let result = maybeGet()!;
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Chained Operations

    [Fact]
    public void NonNullAssertion_ChainedPropertyAccess()
    {
        var source = """
            interface User { name: string; }
            interface Container { user: User | null; }
            let c: Container = { user: { name: "Charlie" } };
            let name = c.user!.name;
            console.log(name);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Charlie\n", output);
    }

    [Fact]
    public void NonNullAssertion_ChainedMethodCall()
    {
        var source = """
            let str: string | null = "hello";
            let upper = str!.toUpperCase();
            console.log(upper);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("HELLO\n", output);
    }

    [Fact]
    public void NonNullAssertion_MultipleInChain()
    {
        var source = """
            interface A { b: B | null; }
            interface B { c: C | null; }
            interface C { value: number; }
            let a: A = { b: { c: { value: 42 } } };
            let val = a.b!.c!.value;
            console.log(val);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Array Operations

    [Fact]
    public void NonNullAssertion_ArrayElement()
    {
        var source = """
            let arr: (string | null)[] = ["a", "b", "c"];
            let elem = arr[1]!;
            console.log(elem);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("b\n", output);
    }

    [Fact]
    public void NonNullAssertion_ArrayElement_WithMethodCall()
    {
        var source = """
            let arr: (string | null)[] = ["hello", "world"];
            let upper = arr[0]!.toUpperCase();
            console.log(upper);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("HELLO\n", output);
    }

    #endregion

    #region Object Literal

    [Fact]
    public void NonNullAssertion_InObjectProperty()
    {
        var source = """
            let x: string | null = "value";
            let obj = { prop: x! };
            console.log(obj.prop);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("value\n", output);
    }

    #endregion

    #region Function Arguments

    [Fact]
    public void NonNullAssertion_InFunctionArgument()
    {
        var source = """
            function greet(name: string): string {
                return "Hello, " + name;
            }
            let maybeName: string | null = "World";
            console.log(greet(maybeName!));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World\n", output);
    }

    [Fact]
    public void NonNullAssertion_MultipleArguments()
    {
        var source = """
            function add(a: number, b: number): number {
                return a + b;
            }
            let x: number | null = 10;
            let y: number | null = 20;
            console.log(add(x!, y!));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("30\n", output);
    }

    #endregion

    #region Class Methods and Properties

    [Fact]
    public void NonNullAssertion_OnClassProperty()
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnClassMethodResult()
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
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("provided\n", output);
    }

    #endregion

    #region Arithmetic Operations

    [Fact]
    public void NonNullAssertion_InArithmeticExpression()
    {
        var source = """
            let x: number | null = 5;
            let y: number | null = 3;
            let result = x! + y!;
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("8\n", output);
    }

    [Fact]
    public void NonNullAssertion_InComparison()
    {
        var source = """
            let x: number | null = 10;
            let y: number | null = 5;
            console.log(x! > y!);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Combined with Other Operators

    [Fact]
    public void NonNullAssertion_BeforeTernary()
    {
        var source = """
            let x: number | null = 10;
            let result = x! > 5 ? "big" : "small";
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("big\n", output);
    }

    [Fact]
    public void NonNullAssertion_AfterOptionalChaining()
    {
        var source = """
            interface Data { value: number | null; }
            let obj: Data | null = { value: 42 };
            let result = obj?.value!;
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void NonNullAssertion_InStringConcatenation()
    {
        var source = """
            let first: string | null = "Hello";
            let last: string | null = "World";
            console.log(first! + " " + last!);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello World\n", output);
    }

    #endregion

    #region Type Narrowing

    [Fact]
    public void NonNullAssertion_NarrowsUnionType()
    {
        var source = """
            let x: string | null = "hello";
            let y: string = x!;  // Should work - ! narrows to string
            console.log(y.length);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnMultiTypeUnion()
    {
        var source = """
            let x: string | number | null = "test";
            let y = x!;  // Type is string | number (null removed)
            console.log(typeof y);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("string\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnAlreadyNonNull()
    {
        var source = """
            let x: string = "hello";
            let y = x!;  // Redundant but valid
            console.log(y);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Nested Expressions

    [Fact]
    public void NonNullAssertion_InNestedExpression()
    {
        var source = """
            let a: number | null = 10;
            let b: number | null = 20;
            let result = (a! + b!) * 2;
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnGroupedExpression()
    {
        var source = """
            function getValue(): number | null { return 5; }
            let result = (getValue())!;
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    #endregion

    #region Template Literals

    [Fact]
    public void NonNullAssertion_InTemplateLiteral()
    {
        var source = """
            let name: string | null = "Alice";
            let greeting = `Hello, ${name!}!`;
            console.log(greeting);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, Alice!\n", output);
    }

    #endregion

    #region Arrow Functions

    [Fact]
    public void NonNullAssertion_InArrowFunction()
    {
        var source = """
            let transform = (x: string | null) => x!.toUpperCase();
            console.log(transform("test"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("TEST\n", output);
    }

    [Fact]
    public void NonNullAssertion_InArrowFunctionReturn()
    {
        var source = """
            let transform = (x: string | null): string => {
                let result = x!;
                return result.toUpperCase();
            };
            console.log(transform("hello"));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("HELLO\n", output);
    }

    [Fact]
    public void NonNullAssertion_SingleParamArrowWithoutParens()
    {
        // Single-parameter arrow function without parentheses
        var source = """
            let fn = x => x!;
            let result: string | null = "hello";
            console.log(fn(result));
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void NonNullAssertion_SingleParamArrowWithBinaryOp()
    {
        // Single-parameter arrow with non-null assertion followed by comparison
        var source = """
            let arr: (number | null)[] = [1, 2, 3, 4, 5];
            let result = arr.filter(x => x! > 2);
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("[3, 4, 5]\n", output);
    }

    [Fact]
    public void NonNullAssertion_SingleParamArrowWithArithmetic()
    {
        // Single-parameter arrow with non-null assertion followed by arithmetic
        var source = """
            let arr: (number | null)[] = [1, 2, 3];
            let result = arr.map(x => x! * 2);
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("[2, 4, 6]\n", output);
    }

    [Fact]
    public void NonNullAssertion_ChainedSingleParamArrows()
    {
        // Chained array methods with single-parameter arrows
        var source = """
            let arr: (number | null)[] = [1, 2, 3, 4, 5];
            let result = arr.filter(x => x! > 2).map(x => x! * 10);
            console.log(result);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("[30, 40, 50]\n", output);
    }

    #endregion

    #region With Type Assertions

    [Fact]
    public void NonNullAssertion_CombinedWithTypeAssertion()
    {
        var source = """
            let x: unknown = "hello";
            let y = (x as string | null)!;
            console.log(y);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void NonNullAssertion_OnNumberZero()
    {
        // Zero is falsy but not null - assertion should pass
        var source = """
            let x: number | null = 0;
            console.log(x! + 1);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnEmptyString()
    {
        // Empty string is falsy but not null - assertion should pass
        var source = """
            let x: string | null = "";
            console.log(x!.length);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void NonNullAssertion_OnBoolean()
    {
        var source = """
            let x: boolean | null = false;
            console.log(x!);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void NonNullAssertion_Consecutive()
    {
        // Multiple ! in a row (redundant but valid)
        var source = """
            let x: string | null = "test";
            let y = x!!;
            console.log(y);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    #endregion
}
