using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for parsing nested generic types where the lexer produces >> or >>> as single tokens.
/// The parser must correctly split these compound tokens in type contexts while preserving
/// their behavior as shift operators in expression contexts.
/// Runs against both interpreter and compiler.
/// </summary>
public class NestedGenericsParsingTests
{
    #region Double Nested Generics (>> token splitting)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_VariableDeclaration(ExecutionMode mode)
    {
        var source = """
            interface Data { value: number; }
            let x: Partial<Readonly<Data>> = { value: 42 };
            console.log(x.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_FunctionParameter(ExecutionMode mode)
    {
        var source = """
            interface Item { name: string; }
            function process(data: Partial<Readonly<Item>>): string {
                return data.name ?? "default";
            }
            console.log(process({ name: "test" }));
            console.log(process({}));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\ndefault\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_FunctionReturnType(ExecutionMode mode)
    {
        var source = """
            interface Config { debug: boolean; }
            function getConfig(): Partial<Readonly<Config>> {
                return { debug: true };
            }
            let cfg = getConfig();
            console.log(cfg.debug);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_GenericClassInstantiation(ExecutionMode mode)
    {
        var source = """
            class Box<T> {
                value: T;
                constructor(v: T) { this.value = v; }
            }
            interface Point { x: number; y: number; }
            let box: Box<Readonly<Point>> = new Box<Readonly<Point>>({ x: 1, y: 2 });
            console.log(box.value.x);
            console.log(box.value.y);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_MultipleTypeArguments(ExecutionMode mode)
    {
        var source = """
            interface Entry<K, V> { key: K; value: V; }
            let e: Partial<Entry<string, number>> = { key: "test" };
            console.log(e.key);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_MultipleVariables(ExecutionMode mode)
    {
        // Tests multiple nested generic declarations in sequence
        var source = """
            interface Person { name: string; }
            interface Config { debug: boolean; }
            let p: Partial<Readonly<Person>> = { name: "Alice" };
            let c: Partial<Readonly<Config>> = { debug: true };
            console.log(p.name);
            console.log(c.debug);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DoubleNested_InFunctionCall(ExecutionMode mode)
    {
        var source = """
            interface Data { x: number; }
            function process(d: Partial<Readonly<Data>>): number {
                return d.x ?? 0;
            }
            let result = process({ x: 42 });
            console.log(result);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Triple Nested Generics (>>> token splitting)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TripleNested_VariableDeclaration(ExecutionMode mode)
    {
        var source = """
            interface Data { value: number; }
            let x: Partial<Readonly<Required<Data>>> = { value: 100 };
            console.log(x.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TripleNested_FunctionParameter(ExecutionMode mode)
    {
        var source = """
            interface Settings { enabled: boolean; }
            function apply(s: Partial<Readonly<Required<Settings>>>): boolean {
                return s.enabled ?? false;
            }
            console.log(apply({ enabled: true }));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TripleNested_MultipleVariables(ExecutionMode mode)
    {
        var source = """
            interface Item { id: number; }
            interface Config { enabled: boolean; }
            let item: Partial<Readonly<Required<Item>>> = { id: 1 };
            let cfg: Partial<Readonly<Required<Config>>> = { enabled: true };
            console.log(item.id);
            console.log(cfg.enabled);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void TripleNested_GenericClass(ExecutionMode mode)
    {
        var source = """
            class Wrapper<T> {
                data: T;
                constructor(d: T) { this.data = d; }
            }
            interface Cfg { mode: string; }
            let w: Wrapper<Partial<Readonly<Cfg>>> = new Wrapper<Partial<Readonly<Cfg>>>({ mode: "test" });
            console.log(w.data.mode);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    #endregion

    #region Quadruple+ Nested Generics (multiple splits)

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void QuadrupleNested_VariableDeclaration(ExecutionMode mode)
    {
        var source = """
            interface Base { val: number; }
            let x: Partial<Readonly<Required<Partial<Base>>>> = { val: 999 };
            console.log(x.val);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("999\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DeeplyNested_FiveLevel(ExecutionMode mode)
    {
        var source = """
            interface Core { n: number; }
            let deep: Partial<Readonly<Required<Partial<Readonly<Core>>>>> = { n: 55 };
            console.log(deep.n);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("55\n", output);
    }

    #endregion

    #region Mixed Contexts

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MixedNesting_DifferentLevels(ExecutionMode mode)
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            let x: Partial<Readonly<A>> = { a: 1 };
            let y: Partial<Readonly<Required<B>>> = { b: "test" };
            console.log(x.a);
            console.log(y.b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntest\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InClassField(ExecutionMode mode)
    {
        var source = """
            interface Item { name: string; }
            class Container {
                item: Partial<Readonly<Item>>;
                constructor() {
                    this.item = { name: "test" };
                }
            }
            let c = new Container();
            console.log(c.item.name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InMethodParameter(ExecutionMode mode)
    {
        var source = """
            interface Data { x: number; }
            class Processor {
                process(d: Partial<Readonly<Data>>): number {
                    return d.x ?? 0;
                }
            }
            let p = new Processor();
            console.log(p.process({ x: 42 }));
            console.log(p.process({}));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InMethodReturnType(ExecutionMode mode)
    {
        var source = """
            interface Result { success: boolean; }
            class Service {
                getResult(): Partial<Readonly<Result>> {
                    return { success: true };
                }
            }
            let s = new Service();
            console.log(s.getResult().success);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InArrowFunction(ExecutionMode mode)
    {
        var source = """
            interface Value { v: number; }
            let fn: (x: Partial<Readonly<Value>>) => number = (x) => x.v ?? -1;
            console.log(fn({ v: 123 }));
            console.log(fn({}));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("123\n-1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InTypeAssertion(ExecutionMode mode)
    {
        var source = """
            interface Obj { prop: string; }
            let data: unknown = { prop: "value" };
            let typed = data as Partial<Readonly<Obj>>;
            console.log(typed.prop);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("value\n", output);
    }

    #endregion

    #region Regression: Right-Shift Operators Still Work

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RightShift_InExpression(ExecutionMode mode)
    {
        var source = """
            let x = 16 >> 2;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UnsignedRightShift_InExpression(ExecutionMode mode)
    {
        var source = """
            let x = 16 >>> 2;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RightShift_NegativeNumber(ExecutionMode mode)
    {
        var source = """
            let x = -16 >> 2;
            console.log(x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("-4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RightShift_WithParentheses(ExecutionMode mode)
    {
        var source = """
            let a = 64;
            let b = (a >> 1) >> 2;
            console.log(b);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RightShift_InComparison(ExecutionMode mode)
    {
        var source = """
            let x = 16 >> 2;
            console.log(x > 3);
            console.log(x < 5);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MixedContext_TypeAndShiftOperator(ExecutionMode mode)
    {
        // Tests that >> works as shift in expressions while also having nested generics
        var source = """
            interface Num { n: number; }
            let typed: Partial<Readonly<Num>> = { n: 32 };
            let n = typed.n ?? 0;
            let shifted = n >> 2;
            console.log(shifted);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_EmptyTypeArgs(ExecutionMode mode)
    {
        // Generic with nested generic that has its own empty case
        var source = """
            interface Empty {}
            let x: Partial<Readonly<Empty>> = {};
            console.log("ok");
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_WithOptionalProperties(ExecutionMode mode)
    {
        var source = """
            interface Config {
                name?: string;
                count?: number;
            }
            let c: Partial<Readonly<Config>> = { name: "test" };
            console.log(c.name);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_ConsecutiveDeclarations(ExecutionMode mode)
    {
        var source = """
            interface A { a: number; }
            interface B { b: string; }
            interface C { c: boolean; }
            let x: Partial<Readonly<A>> = { a: 1 };
            let y: Partial<Readonly<B>> = { b: "two" };
            let z: Partial<Readonly<C>> = { c: true };
            console.log(x.a);
            console.log(y.b);
            console.log(z.c);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntwo\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_InConditionalReturn(ExecutionMode mode)
    {
        var source = """
            interface Data { value: number; }
            function getValue(condition: boolean): Partial<Readonly<Data>> {
                if (condition) {
                    return { value: 42 };
                }
                return { value: 0 };
            }
            let result = getValue(true);
            console.log(result.value);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedGenerics_Record_With_NestedType(ExecutionMode mode)
    {
        var source = """
            interface Inner { x: number; }
            let rec: Record<string, Partial<Readonly<Inner>>> = {
                first: { x: 1 },
                second: { x: 2 }
            };
            console.log(rec.first.x);
            console.log(rec.second.x);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    #endregion
}
