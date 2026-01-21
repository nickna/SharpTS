using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Compiler tests for const type parameters (TypeScript 5.0+ feature).
/// Verifies that const type parameters compile and execute correctly.
/// </summary>
public class ConstTypeParameterCompilerTests
{
    #region Basic Const Type Parameter Functions

    [Fact]
    public void ConstTypeParam_BasicFunction_Compiles()
    {
        var source = """
            function identity<const T>(x: T): T {
                return x;
            }
            console.log(identity(42));
            console.log(identity("hello"));
            console.log(identity(true));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\nhello\ntrue\n", output);
    }

    [Fact]
    public void ConstTypeParam_WithConstraint_Compiles()
    {
        var source = """
            function double<const T extends number>(value: T): number {
                return value * 2;
            }
            console.log(double(21));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ConstTypeParam_MultipleTypeParams_Compiles()
    {
        var source = """
            function pair<const K, V>(key: K, value: V): string {
                return key + "=" + value;
            }
            console.log(pair("name", "Alice"));
            console.log(pair("age", 30));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("name=Alice\nage=30\n", output);
    }

    [Fact]
    public void ConstTypeParam_MixedConstAndNonConst_Compiles()
    {
        var source = """
            function transform<const T, U>(value: T, fn: (x: T) => U): U {
                return fn(value);
            }
            console.log(transform(5, x => x * 2));
            console.log(transform("hello", x => x.length));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n5\n", output);
    }

    #endregion

    #region Const Type Parameter Classes

    [Fact]
    public void ConstTypeParam_InClass_Compiles()
    {
        var source = """
            class Box<const T> {
                value: T;
                constructor(v: T) {
                    this.value = v;
                }
                getValue(): T {
                    return this.value;
                }
            }
            let numBox: Box<number> = new Box<number>(42);
            let strBox: Box<string> = new Box<string>("hello");
            console.log(numBox.getValue());
            console.log(strBox.getValue());
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\nhello\n", output);
    }

    [Fact]
    public void ConstTypeParam_ClassWithConstraint_Compiles()
    {
        var source = """
            class Container<const T extends object> {
                data: T;
                constructor(d: T) {
                    this.data = d;
                }
            }
            let c: Container<object> = new Container<object>({ x: 1, y: 2 });
            console.log((c.data as any).x);
            console.log((c.data as any).y);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Const Type Parameter with Complex Types

    [Fact]
    public void ConstTypeParam_WithObjectLiteral_Compiles()
    {
        var source = """
            function wrap<const T>(obj: T): { value: T } {
                return { value: obj };
            }
            let wrapped = wrap({ name: "Alice", age: 30 });
            console.log(wrapped.value.name);
            console.log(wrapped.value.age);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice\n30\n", output);
    }

    [Fact]
    public void ConstTypeParam_WithArrayLiteral_Compiles()
    {
        var source = """
            function getFirst<const T>(arr: T[]): T {
                return arr[0];
            }
            console.log(getFirst([10, 20, 30]));
            console.log(getFirst(["a", "b", "c"]));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\na\n", output);
    }

    [Fact]
    public void ConstTypeParam_NestedObjects_Compiles()
    {
        var source = """
            function deepWrap<const T>(obj: T): { outer: { inner: T } } {
                return { outer: { inner: obj } };
            }
            let result = deepWrap({ x: 1 });
            console.log(result.outer.inner.x);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    #endregion

    #region Explicit Type Arguments with Const

    [Fact]
    public void ConstTypeParam_ExplicitTypeArg_Compiles()
    {
        var source = """
            function identity<const T>(x: T): T {
                return x;
            }
            let result: number = identity<number>(42);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Default Type Parameters with Const

    [Fact]
    public void ConstTypeParam_WithDefault_Compiles()
    {
        var source = """
            function identity<const T = string>(x: T): T {
                return x;
            }
            console.log(identity("hello"));
            console.log(identity(42));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\n42\n", output);
    }

    #endregion

    #region Generic Method in Class with Const

    [Fact]
    public void ConstTypeParam_GenericMethodInClass_Compiles()
    {
        var source = """
            class Wrapper {
                wrap<const T>(value: T): { wrapped: T } {
                    return { wrapped: value };
                }
            }
            let w = new Wrapper();
            console.log(w.wrap(42).wrapped);
            console.log(w.wrap("hello").wrapped);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\nhello\n", output);
    }

    #endregion
}
