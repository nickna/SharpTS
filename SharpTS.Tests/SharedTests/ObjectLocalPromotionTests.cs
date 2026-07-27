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
/// fast paths; the escape cases must NOT be promoted (they fall back to the general Dictionary path) and
/// must still produce correct results — i.e. interpreter/compiled parity must hold even when the object
/// is passed, returned, spread, enumerated, dynamically indexed, compared, captured, or compound-assigned.
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
