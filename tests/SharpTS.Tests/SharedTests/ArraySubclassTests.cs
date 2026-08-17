using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for guest classes extending the built-in Array (#233).
/// Runs against both interpreter and compiler.
/// </summary>
public class ArraySubclassTests
{
    [Theory, ModeData]
    public void ExtendsArray_ArrayBehaviorAndBrand(ExecutionMode mode)
    {
        var source = """
            class MyArray extends Array {}
            const m: any = new MyArray();
            m.push(1);
            m.push(2);
            console.log(m.length);
            console.log(m[0], m[1]);
            console.log(m instanceof MyArray);
            console.log(m instanceof Array);
            console.log(Array.isArray(m));
            console.log(JSON.stringify(m));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1 2\ntrue\ntrue\ntrue\n[1,2]\n", output);
    }

    [Theory, ModeData]
    public void ExtendsArray_ImplicitConstructorForwardsArguments(ExecutionMode mode)
    {
        var source = """
            class Values extends Array {}
            const values: any = new Values(42, "foo");
            const holes: any = new Values(3);
            console.log(values.length, values[0], values[1]);
            console.log(holes.length, 0 in holes);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2 42 foo\n3 false\n", output);
    }

    [Theory, ModeData]
    public void ClassExpressionExtendsArray_UsesArrayBaseAndForwardsArguments(ExecutionMode mode)
    {
        var source = """
            const Values = class extends Array {};
            const values: any = new Values();
            values.push("x", "y");
            console.log(values.length, values[0], values[1]);
            console.log(values instanceof Values, values instanceof Array, Array.isArray(values));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2 x y\ntrue true true\n", output);
    }

    [Theory, ModeData]
    public void ExtendsArray_MethodsFieldsAndIteration(ExecutionMode mode)
    {
        var source = """
            class SumList extends Array {
                tag: string = "sum";
                total(): number {
                    let t = 0;
                    for (const x of this as any) t += x;
                    return t;
                }
            }
            const s: any = new SumList();
            s.push(3);
            s.push(4);
            console.log(s.total());
            console.log(s.tag);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\nsum\n", output);
    }

    [Theory, ModeData]
    public void ExtendsArray_ConstructorWithSuper(ExecutionMode mode)
    {
        var source = """
            class Stack extends Array {
                constructor() { super(); }
                peek(): any { return this[this.length - 1]; }
            }
            const s: any = new Stack();
            s.push("a");
            s.push("b");
            console.log(s.peek());
            console.log(s.length, s instanceof Stack, s instanceof Array);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("b\n2 true true\n", output);
    }

    [Theory, ModeData]
    public void ExtendsArray_SuperWithLengthArgument(ExecutionMode mode)
    {
        // ECMA-262 §23.1.1.1: a single numeric constructor argument sets the
        // length (holes), other shapes append the arguments as elements.
        var source = """
            class Sized extends Array {
                constructor(n: number) { super(n); }
            }
            class Pair extends Array {
                constructor(a: any, b: any) { super(a, b); }
            }
            const z: any = new Sized(3);
            console.log(z.length);
            const p: any = new Pair("x", "y");
            console.log(p.length, p[0], p[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2 x y\n", output);
    }

    [Theory, ModeData]
    public void ExtendsArray_GetterResolves(ExecutionMode mode)
    {
        var source = """
            class Peekable extends Array {
                get top(): any { return this[this.length - 1]; }
            }
            const p: any = new Peekable();
            p.push(10);
            p.push(20);
            console.log(p.top);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void ExtendsUnbridgedBuiltIn_PreciseRuntimeError(ExecutionMode mode)
    {
        // Built-ins without a subclassing bridge (Error/Array/#233 and
        // Promise/#242 have bridges; Map et al do not yet) keep yielding a
        // precise error rather than the generic "Superclass must be a class".
        var source = """
            class MyMap extends Map {}
            console.log("declared");
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.Run(source, mode));
        Assert.Contains("cannot extend built-in 'Map'", ex.Message);
    }
}
