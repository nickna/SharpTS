using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the object-literal "shape struct" promotion optimization (#862): a provably non-escaping
/// <c>const</c>/<c>let</c> object literal with a fixed, statically-known primitive shape is compiled to a
/// generated value-type struct with typed fields (number→double, boolean→bool, string→string), so
/// <c>o.x</c> reads/writes lower to direct <c>ldfld</c>/<c>stfld</c> instead of a
/// <c>Dictionary&lt;string, object&gt;</c> lookup with boxing.
///
/// These run against BOTH the interpreter and the compiler. The positive cases exercise the promoted
/// fast paths, including stable spread chains and numeric consumers. Escaping values retain ordinary
/// object semantics while independent sources may stay promoted — i.e. interpreter/compiled parity
/// must hold even when the object is passed, returned, dynamically spread, enumerated, indexed,
/// compared, captured, or compound-assigned.
/// A wrong escape rule, or a miscompiled struct fast path, surfaces here as a compiled-mode mismatch.
/// </summary>
public class ObjectLocalPromotionTests
{
    // ── Positive cases: promotable shapes ──────────────────────────────────

    [Theory, ModeData]
    public void Promoted_NumberRecord_ReadFieldsInLoop(ExecutionMode mode)
    {
        // The benchmark shape (objectWork): a fresh per-iteration record read by field.
        // sum += o.x + o.y = i + (i+1) = 2i+1, summed over [0,n) → n^2.
        var source = """
            function objectWork(n: number): number {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const o = { x: i, y: i + 1 };
                    sum = sum + o.x + o.y;
                }
                return sum;
            }
            console.log(objectWork(100));
            """;

        Assert.Equal("10000\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_FieldWrite_MutatesThenReads(ExecutionMode mode)
    {
        // `const o` binds the slot but its fields stay mutable.
        var source = """
            function f(): number {
                const o = { x: 1, y: 2 };
                o.x = 10;
                return o.x + o.y;
            }
            console.log(f());
            """;

        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_FieldWrite_ReturnsAssignedValue(ExecutionMode mode)
    {
        // `o.x = v` is an expression whose value is the assigned RHS.
        var source = """
            function f(): number {
                const o = { x: 0, y: 0 };
                const v: number = (o.x = 42);
                return v + o.x;
            }
            console.log(f());
            """;

        Assert.Equal("84\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_BooleanAndNumberFields(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const o = { ok: true, n: 5 };
                if (o.ok) { return o.n; }
                return 0;
            }
            console.log(f());
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_StringFields_Concat(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const o = { first: "a", last: "b" };
                return o.first + o.last;
            }
            console.log(f());
            """;

        Assert.Equal("ab\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_MixedPrimitiveFields(ExecutionMode mode)
    {
        // number + string + boolean fields in one shape, with a string-typed result mixing kinds.
        var source = """
            function f(): string {
                const o = { id: "x", count: 3, active: true };
                let s: string = o.id + o.count;
                if (o.active) { s = s + "!"; }
                return s;
            }
            console.log(f());
            """;

        Assert.Equal("x3!\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_MultipleObjects_Independent(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const a = { x: 1, y: 2 };
                const b = { x: 10, y: 20 };
                a.x = 100;
                return a.x + a.y + b.x + b.y;
            }
            console.log(f());
            """;

        // 100 + 2 + 10 + 20 = 132
        Assert.Equal("132\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_FieldWrittenFromComputedNumber(ExecutionMode mode)
    {
        var source = """
            function f(n: number): number {
                const o = { a: 0, b: 0 };
                o.a = n * 2;
                o.b = o.a + 1;
                return o.a + o.b;
            }
            console.log(f(5));
            """;

        // a = 10, b = 11 → 21
        Assert.Equal("21\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_StableSpreadCopiesPrimitiveShape(ExecutionMode mode)
    {
        var source = """
            function f(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const source = { x: i, y: i + 1 };
                    const result = { ...source, z: i + 2 };
                    total = total + result.x + result.y + result.z;
                }
                return total;
            }
            console.log(f(10));
            """;

        Assert.Equal("165\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_MultipleSpreadsSnapshotAtEvaluationPosition(ExecutionMode mode)
    {
        // The first spread must observe x=1, while the second spread observes the intervening write.
        // `changed` and the overwritten y initializer must still be evaluated in source order.
        var source = """
            function f(): number {
                const source = { x: 1, y: 2 };
                const other = { y: 4, z: 5 };
                const result = {
                    ...source,
                    changed: (source.x = 9),
                    ...other,
                    ...source,
                    y: 7
                };
                return result.x * 1000 + result.y * 100 + result.z * 10 + result.changed;
            }
            console.log(f());
            """;

        Assert.Equal("9759\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_ObjectKeysIsFreshMutableAndKeepsClosedShape(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const record = { first: 1, second: 2 };
                record.first = 3;
                const firstKeys: string[] = Object.keys(record);
                firstKeys[0] = "changed";
                firstKeys.push("extra");
                const secondKeys: string[] = Object.keys(record);
                return firstKeys.join(",") + "|" + secondKeys.join(",") + "|" + record.first;
            }
            console.log(f());
            """;

        Assert.Equal("changed,second,extra|first,second|3\n", TestHarness.Run(source, mode));
    }

    // ── Escape cases: must fall back, must stay correct ────────────────────

    [Theory, ModeData]
    public void Escape_PassedToFunction(ExecutionMode mode)
    {
        var source = """
            function sumXY(p: { x: number; y: number }): number { return p.x + p.y; }
            function f(): number {
                const o = { x: 3, y: 4 };
                return sumXY(o);
            }
            console.log(f());
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_Returned(ExecutionMode mode)
    {
        var source = """
            function make(): { x: number; y: number } {
                const o = { x: 7, y: 8 };
                return o;
            }
            const r = make();
            console.log(r.x + r.y);
            """;

        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_Spread(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const o = { x: 1, y: 2 };
                const p = { ...o, z: 3 };
                return p.x + p.y + p.z;
            }
            console.log(f());
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpreadResult_NumericConsumerMutatesResultIndependentlyOfSource(ExecutionMode mode)
    {
        var source = """
            function consume(value: any): number {
                value.x = value.x + 10;
                return value.x + value.y + value.z;
            }
            function f(): number {
                const source = { x: 1, y: 2 };
                const result = { ...source, z: 3 };
                result.y = 20;
                return consume(result) + source.x;
            }
            console.log(f());
            """;

        Assert.Equal("35\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_DynamicSpreadPreservesKeysSymbolsGettersAndNullishSources(ExecutionMode mode)
    {
        var source = """
            let calls: number = 0;
            const sym: symbol = Symbol("s");
            const dynamic: any = {
                2: "two",
                first: "a",
                get value(): number { calls = calls + 1; return calls; },
                [sym]: "symbol"
            };
            const result: any = {
                before: "b",
                ...(null as any),
                ...dynamic,
                ...(undefined as any),
                first: "overwritten",
                after: "z"
            };
            console.log(Object.keys(result).join(","));
            console.log(result.first);
            console.log(result.value);
            console.log(result[sym]);
            console.log(calls);
            """;

        Assert.Equal(
            "2,before,first,value,after\noverwritten\n1\nsymbol\n1\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_ProxySpreadUsesOneKeySnapshot(ExecutionMode mode)
    {
        var source = """
            const target: any = { a: 1, b: 2 };
            let ownKeysCount: number = 0;
            let descriptorCount: number = 0;
            let getCount: number = 0;
            const proxy: any = new Proxy(target, {
                ownKeys(inner: any): any[] {
                    ownKeysCount = ownKeysCount + 1;
                    return Reflect.ownKeys(inner);
                },
                getOwnPropertyDescriptor(inner: any, key: any): any {
                    descriptorCount = descriptorCount + 1;
                    return Reflect.getOwnPropertyDescriptor(inner, key);
                },
                get(inner: any, key: any): any {
                    getCount = getCount + 1;
                    return inner[key];
                }
            });
            const result: any = { ...proxy };
            console.log(result.a);
            console.log(result.b);
            console.log(ownKeysCount);
            console.log(descriptorCount);
            console.log(getCount);
            """;

        Assert.Equal("1\n2\n1\n2\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_ObjectKeysNumericSymbolsAndShapeMutationUseGenericOrdering(ExecutionMode mode)
    {
        var source = """
            const sym: symbol = Symbol("hidden");
            const record: any = {
                10: "ten",
                first: "a",
                2: "two",
                1: "one",
                [sym]: "symbol"
            };
            record.last = "z";
            console.log(Object.keys(record).join(","));
            """;

        Assert.Equal("1,2,10,first,last\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_ObjectKeysAccessorAndProxyRemainObservable(ExecutionMode mode)
    {
        var source = """
            let getterCalls: number = 0;
            const accessor: any = {
                a: 1,
                get b(): number { getterCalls = getterCalls + 1; return 2; }
            };
            console.log(Object.keys(accessor).join(","));
            console.log(getterCalls);

            let ownKeysCalls: number = 0;
            const proxy: any = new Proxy({ x: 1, y: 2 }, {
                ownKeys(target: any): any[] {
                    ownKeysCalls = ownKeysCalls + 1;
                    return Reflect.ownKeys(target);
                }
            });
            console.log(Object.keys(proxy).join(","));
            console.log(ownKeysCalls);
            """;

        Assert.Equal("a,b\n0\nx,y\n1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_ForInEnumeration(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const o = { x: 1, y: 2, z: 3 };
                let count: number = 0;
                for (const k in o) { count = count + 1; }
                return count;
            }
            console.log(f());
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_StrictEquality(ExecutionMode mode)
    {
        // Object identity comparison — a promoted struct local has no stable reference, so this must
        // fall back. Two distinct literals are never reference-equal.
        var source = """
            function f(): number {
                const a = { x: 1, y: 2 };
                const b = { x: 1, y: 2 };
                return (a === b) ? 1 : 0;
            }
            console.log(f());
            """;

        Assert.Equal("0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_CapturedByClosure(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const o = { x: 5, y: 6 };
                const get = (): number => o.x;
                return get() + o.y;
            }
            console.log(f());
            """;

        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_CompoundFieldAssign(ExecutionMode mode)
    {
        // `o.x += v` is intentionally not promoted in the first cut → falls back, must stay correct.
        var source = """
            function f(): number {
                const o = { x: 1, y: 2 };
                o.x += 10;
                return o.x + o.y;
            }
            console.log(f());
            """;

        Assert.Equal("13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_NestedObjectField(ExecutionMode mode)
    {
        // A non-primitive (nested object) field disqualifies the shape → falls back.
        var source = """
            function f(): number {
                const o = { a: { b: 1 }, c: 2 };
                return o.a.b + o.c;
            }
            console.log(f());
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_DynamicBracketRead(ExecutionMode mode)
    {
        // Bracket access (even with a literal key) is a dynamic index → disqualifies → falls back.
        var source = """
            function f(): number {
                const o = { x: 4, y: 5 };
                return o["x"] + o["y"];
            }
            console.log(f());
            """;

        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }
}
