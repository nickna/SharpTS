using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for destructuring assignment (arrays and objects). Runs against both interpreter and compiler.
/// </summary>
public class DestructuringTests
{
    #region Array Destructuring

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_BasicAssignment(ExecutionMode mode)
    {
        var source = """
            const arr: number[] = [1, 2, 3];
            const [a, b] = arr;
            console.log(a);
            console.log(b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_WithRest(ExecutionMode mode)
    {
        var source = """
            const [head, ...tail] = [1, 2, 3, 4];
            console.log(head);
            console.log(tail.length);
            console.log(tail[0]);
            console.log(tail[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_WithHoles(ExecutionMode mode)
    {
        var source = """
            const [first, , third] = [1, 2, 3];
            console.log(first);
            console.log(third);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_TrailingComma(ExecutionMode mode)
    {
        var source = """
            const [t1, t2,] = [100, 200];
            console.log(t1);
            console.log(t2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n", output);
    }

    #endregion

    #region Object Destructuring

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ObjectDestructuring_BasicAssignment(ExecutionMode mode)
    {
        var source = """
            const obj = { name: "Alice", age: 30 };
            const { name, age } = obj;
            console.log(name);
            console.log(age);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ObjectDestructuring_WithRename(ExecutionMode mode)
    {
        var source = """
            const obj = { name: "Bob" };
            const { name: userName } = obj;
            console.log(userName);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Bob\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ObjectDestructuring_WithDefault(ExecutionMode mode)
    {
        var source = """
            const obj: any = {};
            const { missing: value = "default" } = obj;
            console.log(value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("default\n", output);
    }

    #endregion

    #region Iterable (non-indexable) Sources (#685)

    // Array destructuring follows the iterator protocol, so it must work over any
    // iterable — not just index-addressable arrays/strings. Before #685 the parser
    // desugared `[a, b] = src` to positional index access, which mis-evaluated (and
    // the type checker rejected) generators, Set and Map.

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverGenerator(ExecutionMode mode)
    {
        var source = """
            function* g() { yield 10; yield 20; }
            const [a, b] = g();
            console.log(a);
            console.log(b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverGeneratorWithRest(ExecutionMode mode)
    {
        var source = """
            function* g() { yield 1; yield 2; yield 3; yield 4; }
            const [first, ...rest] = g();
            console.log(first);
            console.log(rest.length);
            console.log(rest[0]);
            console.log(rest[1]);
            console.log(rest[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n2\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverSet(ExecutionMode mode)
    {
        var source = """
            const s = new Set<number>([1, 2, 3]);
            const [x, y] = s;
            console.log(x);
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverSetWithHoleAndDefault(ExecutionMode mode)
    {
        // Set has three values [10, 20, 30]; index 3 is missing → default applies.
        var source = """
            const s = new Set<number>([10, 20, 30]);
            const [, second, , fourth = 99] = s;
            console.log(second);
            console.log(fourth);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverMapEntry(ExecutionMode mode)
    {
        // Iterating a Map yields [key, value] entries.
        var source = """
            const m = new Map<string, number>([["k", 1]]);
            const [first] = m;
            console.log(first[0]);
            console.log(first[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("k\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_NestedOverMapEntries(ExecutionMode mode)
    {
        // Nested pattern destructures each [key, value] entry positionally.
        var source = """
            const m = new Map<string, number>([["a", 1], ["b", 2]]);
            const [[k1, v1], [k2, v2]] = m;
            console.log(k1);
            console.log(v1);
            console.log(k2);
            console.log(v2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\n1\nb\n2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_OverCustomIterable(ExecutionMode mode)
    {
        var source = """
            const iterable = {
                [Symbol.iterator]() {
                    let i = 0;
                    return {
                        next() {
                            return i < 3
                                ? { value: i++ * 10, done: false }
                                : { value: undefined, done: true };
                        }
                    };
                }
            };
            const [a, b, c] = iterable;
            console.log(a);
            console.log(b);
            console.log(c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_GeneratorElementTypeIsUsable(ExecutionMode mode)
    {
        // The destructured bindings carry the iterable's element type (number), so
        // arithmetic type-checks and runs — exercising the type-checker side of #685.
        var source = """
            function* g() { yield 5; yield 7; }
            const [a, b] = g();
            console.log(a + b);
            console.log(a * b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("12\n35\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_FastPathArrayUnchanged(ExecutionMode mode)
    {
        // Regression guard: the iterator-protocol normalization must not disturb the
        // existing array/tuple fast path (arrays pass straight through).
        var source = """
            const arr: number[] = [100, 200, 300];
            const [a, ...rest] = arr;
            console.log(a);
            console.log(rest.length);
            const tup: [string, number] = ["x", 7];
            const [s, n] = tup;
            console.log(s);
            console.log(n + 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n2\nx\n8\n", output);
    }

    #endregion

    #region Nested Destructuring

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_Arrays(ExecutionMode mode)
    {
        var source = """
            const nested: number[][] = [[1, 2], [3, 4]];
            const [[a, b], [c, d]] = nested;
            console.log(a);
            console.log(b);
            console.log(c);
            console.log(d);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_Objects(ExecutionMode mode)
    {
        var source = """
            const user = { profile: { email: "test@test.com" } };
            const { profile: { email } } = user;
            console.log(email);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test@test.com\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void MixedDestructuring_ObjectWithArray(ExecutionMode mode)
    {
        var source = """
            const data = { items: [1, 2, 3] };
            const { items: [m1, m2] } = data;
            console.log(m1);
            console.log(m2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Nested over a mixed (non-tuple) array literal (#783)

    // `[[7], 8]` infers as `(number[] | number)[]`; without contextual typing the desugared `src[0]`
    // is a non-indexable union and the nested `[m]` pattern fails to type-check. The binding pattern's
    // shape is threaded as a tuple context so the literal infers as `[number[], number]`.

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_MixedArrayLiteral_Declaration(ExecutionMode mode)
    {
        var source = """
            const [[m], n] = [[7], 8];
            console.log(m);
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_MixedArrayLiteral_Assignment(ExecutionMode mode)
    {
        var source = """
            let m = 0;
            let n = 0;
            [[m], n] = [[7], 8];
            console.log(m);
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_MixedArrayLiteral_ExplicitTupleAnnotation(ExecutionMode mode)
    {
        var source = """
            const [[m], n]: [number[], number] = [[7], 8];
            console.log(m);
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n8\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_MixedArrayLiteral_DeepNesting(ExecutionMode mode)
    {
        var source = """
            const [[[x]], y] = [[[1]], 2];
            console.log(x);
            console.log(y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    // Bonus: a flat mixed literal source now infers element types positionally (matching tsc), so the
    // element methods (`toFixed`/`toUpperCase`) type-check instead of reporting a spurious error.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Destructuring_FlatMixedArrayLiteral_ElementTypesUsable(ExecutionMode mode)
    {
        var source = """
            const [a, b] = [1, "two"];
            console.log(a.toFixed(1));
            console.log(b.toUpperCase());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1.0\nTWO\n", output);
    }

    // Regression guard: a UNIFORM nested literal must stay an array (`number[]`), NOT be over-tupled —
    // `a.push(...)` would fail to type-check on a fixed-length tuple.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NestedDestructuring_UniformArrayLiteral_StaysArray(ExecutionMode mode)
    {
        var source = """
            const [a, b] = [[1, 2], [3, 4]];
            a.push(9);
            console.log(a.length);
            console.log(b[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n3\n", output);
    }

    #endregion

    #region String Rest Element (#753)

    // #753: a rest element over a STRING source must collect a fresh ARRAY of characters, not bind
    // the trailing substring. Non-rest character bindings are identical either way.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_StringRest_BindsCharArray(ExecutionMode mode)
    {
        var source = """
            const [a, ...rest] = "hello";
            console.log(a);
            console.log(Array.isArray(rest));
            console.log(rest.length);
            console.log(rest.join("-"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h\ntrue\n4\ne-l-l-o\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_StringNonRest_Unchanged(ExecutionMode mode)
    {
        var source = """
            const [a, b, c = "Z"] = "hi";
            console.log(a, b, c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h i Z\n", output);
    }

    #endregion

    #region Assignment Destructuring (#754)

    // #754: destructuring assignment to EXISTING l-values (no const/let), array and object patterns.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_ArrayBasicAndSwap(ExecutionMode mode)
    {
        var source = """
            let a = 0, b = 0;
            [a, b] = [1, 2];
            console.log(a, b);
            [a, b] = [b, a];
            console.log(a, b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2\n2 1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_DefaultsHolesAndRest(ExecutionMode mode)
    {
        var source = """
            let c = 0, d = 0;
            [c, d = 9] = [5];
            console.log(c, d);
            let x, y;
            [, x, , y] = [1, 2, 3, 4];
            console.log(x, y);
            let head: number, tail: number[];
            [head, ...tail] = [1, 2, 3, 4];
            console.log(head, Array.isArray(tail), tail.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 9\n2 4\n1 true 2,3,4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_MemberTargets(ExecutionMode mode)
    {
        var source = """
            const o: any = {};
            const arr: number[] = [0, 0];
            [o.p, arr[1]] = [10, 20];
            console.log(o.p, arr[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10 20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_Object_RenameRestAndDefault(ExecutionMode mode)
    {
        var source = """
            let pa: number, pb: number, rr: any;
            ({ a: pa, b: pb, ...rr } = { a: 1, b: 2, z: 9 });
            console.log(pa, pb, JSON.stringify(rr));
            const src: any = { p: 3 };
            let ox, oy;
            ({ p: ox, q: oy = 4 } = src);
            console.log(ox, oy);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2 {\"z\":9}\n3 4\n", output);
    }

    // The right-hand side is normalized through the #685 iterator protocol, so a non-indexable
    // iterable (Set) destructures by assignment just like a declaration.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_IteratorSource(ExecutionMode mode)
    {
        var source = """
            let s1, s2;
            [s1, s2] = new Set<number>([11, 22]);
            console.log(s1, s2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11 22\n", output);
    }

    // An assignment expression evaluates to its right-hand side (the ORIGINAL rhs, not the
    // normalized array), so it composes in expression position and chains.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_ExpressionPositionAndChained(ExecutionMode mode)
    {
        var source = """
            let e1, e2;
            const ret = ([e1, e2] = [100, 200]);
            console.log(JSON.stringify(ret), e1, e2);
            let a, b, c, d;
            [a, b] = [c, d] = [1, 2];
            console.log(a, b, c, d);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[100,200] 100 200\n1 2 1 2\n", output);
    }

    #endregion

    #region Undefined-only Defaults (#784)

    // #784: a destructuring default applies ONLY when the matched value is `undefined`, never `null`
    // (ECMA-262), unlike `??`. Declaration form, array and object patterns.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaults_AppliedForUndefinedNotNull_Declaration(ExecutionMode mode)
    {
        var source = """
            const [a = 9] = [null];
            const [b = 9] = [undefined];
            const o: { x?: number | null } = { x: null };
            const { x = 9 } = o;
            console.log(String(a), String(b), String(x));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null 9 null\n", output);
    }

    // #784: same undefined-only rule for assignment destructuring (#754 form), AND a value-type default
    // over an ABSENT element must yield the default — not NaN. The lowered spill temp lives inside an
    // expression, so an unboxed numeric slot would coerce the runtime `undefined` sentinel (compiled).
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaults_AssignmentDestructuring_UndefinedOnlyAndValueType(ExecutionMode mode)
    {
        var source = """
            let a; [a = 5] = [null];
            let b; [b = 5] = [];
            const arr: number[] = [];
            let c; [c = 5] = arr;
            const o: { z?: number } = {};
            let d; ({ z: d = 5 } = o);
            console.log(String(a), String(b), String(c), String(d));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null 5 5 5\n", output);
    }

    // #784: the default expression is evaluated lazily (only when undefined) and the matched value is
    // read exactly once (a getter is not invoked twice).
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaults_LazyAndSingleEvaluation(ExecutionMode mode)
    {
        var source = """
            let ran = 0;
            const side = () => { ran++; return 99; };
            const [p = side()] = [7];
            console.log(p, ran);
            const [q = side()] = [undefined];
            console.log(q, ran);
            let reads = 0;
            const src = { get k() { reads++; return undefined; } };
            const { k = 42 } = src;
            console.log(k, reads);
            """;

        var output = TestHarness.Run(source, mode);
        // p: default not evaluated (value present); q: default evaluated exactly once; k: getter read once.
        Assert.Equal("7 0\n99 1\n42 1\n", output);
    }

    #endregion

    #region Typed-array and Buffer Sources (#781, #1288)

    // #781/#1288: typed arrays and Buffers are normalized to a fresh Array before destructuring.
    // This keeps ordinary element bindings working and makes a rest binding a real Array rather
    // than a typed-array/Buffer slice.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ArrayDestructuring_TypedArrayAndBufferSources(ExecutionMode mode)
    {
        var source = """
            const u8 = new Uint8Array([1, 2, 3]);
            const [a, b] = u8;
            const [, ...typedRest] = u8;
            console.log(a, b, Array.isArray(typedRest), JSON.stringify(typedRest));

            const buffer = Buffer.from([4, 5, 6]);
            const [c, ...bufferRest] = buffer;
            console.log(c, Array.isArray(bufferRest), JSON.stringify(bufferRest));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2 true [2,3]\n4 true [5,6]\n", output);
    }

    #endregion

    #region Object Shorthand-with-default Assignment Destructuring (#780)

    // #780: `({ a = 5 } = src)` — the object-pattern cover-grammar form is now accepted by the parser and
    // routes through the #754 assignment-destructuring lowering (undefined-only default, #784).
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_ObjectShorthandDefault(ExecutionMode mode)
    {
        var source = """
            const present: { a?: number } = { a: 10 };
            let a; ({ a = 5 } = present);
            const missing: { b?: number } = {};
            let b; ({ b = 5 } = missing);
            console.log(String(a), String(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10 5\n", output);
    }

    // #780: the cover-grammar `{ a = 5 }` is only valid as a destructuring pattern; using it as a plain
    // object-literal EXPRESSION remains a semantic error, as in tsc.
    [Fact]
    public void ObjectLiteral_ShorthandDefault_AsExpression_IsTypeError()
    {
        var source = """
            let a = 1;
            const o = { a = 5 };
            console.log(o);
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    #endregion

    #region Nested Pattern with Default in Assignment Destructuring (#779)

    // #779: a nested array/object pattern that itself carries a default in an ASSIGNMENT destructuring.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_NestedArrayPatternWithDefault(ExecutionMode mode)
    {
        var source = """
            let a; [[a] = [9]] = [[1]];
            let b; [[b] = [9]] = [];
            console.log(String(a), String(b));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 9\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AssignmentDestructuring_NestedObjectPatternWithDefault(ExecutionMode mode)
    {
        var source = """
            const src: { p?: { x: number } } = { p: { x: 1 } };
            let x; ({ p: { x: x } = { x: 7 } } = src);
            const empty: { p?: { x: number } } = {};
            let y; ({ p: { x: y } = { x: 7 } } = empty);
            console.log(String(x), String(y));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 7\n", output);
    }

    #endregion

    #region Defaulted Read Over a Source Lacking the Property (#796)

    // #796: a destructuring property read carrying a DEFAULT, over a source whose static type does
    // not declare that property, must NOT report a spurious "Property does not exist" (TS2339) — the
    // default makes the access safe (tsc accepts these). Covers the declaration form and the shipped
    // #780/#754 assignment forms, with empty/closed object sources.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaulted_OverEmptySource_Declaration(ExecutionMode mode)
    {
        var source = """
            const { a = 5 } = {};
            console.log(String(a));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaulted_OverEmptySource_AssignmentShorthandAndRename(ExecutionMode mode)
    {
        var source = """
            let b; ({ b = 5 } = {});
            let x; ({ a: x = 5 } = {});
            console.log(String(b), String(x));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaulted_NestedPatternDefault_OverEmptyAndPresentSource(ExecutionMode mode)
    {
        // The inner default `{}` lacks `x`; the read must stay tolerant. With a present source the
        // real value flows through; with an empty source the inner pattern reads undefined.
        var source = """
            let r; ({ p: { x: r } = {} } = { p: { x: 1 } });
            let s; ({ p: { x: s } = {} } = {});
            console.log(String(r), String(s));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 undefined\n", output);
    }

    // #796: a source that DECLARES the property (even optional) still narrows the defaulted binding
    // to the property type — the tolerance must not regress the well-typed case.
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Defaulted_OverDeclaredOptionalSource_StillWorks(ExecutionMode mode)
    {
        var source = """
            const o: { a?: number } = {};
            const { a = 5 } = o;
            console.log(String(a));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    // #796 regression guard: a NON-defaulted read over a closed source must remain a TS2339 error,
    // matching tsc (`const { a } = {}` is an error). The leniency applies only when a default exists.
    [Fact]
    public void NonDefaulted_OverEmptySource_IsTypeError()
    {
        var source = """
            const { a } = {};
            console.log(a);
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    #endregion
}
