using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for truthiness narrowing (#486): a <c>if (x)</c> / <c>if (!x)</c> guard removes
/// always-falsy constituents (null, undefined, void, and the false/0/"" literal types) from
/// the truthy branch and always-truthy constituents from the falsy branch — the same way the
/// explicit <c>!== null/undefined</c> and <c>typeof</c> guards already narrow. Covers bare
/// variables, property-access paths, negation, and the &amp;&amp; / ternary / loop forms that
/// share the guard analyzer.
/// </summary>
public class TruthinessNarrowingTests
{
    [Fact]
    public void Truthiness_StringOrUndefined_NarrowsToStringInThen()
    {
        // The exact repro from #486.
        var source = """
            function f(d: string | undefined): number {
                if (d) { return d.length; }
                return 0;
            }
            console.log(f("hello"));
            console.log(f(undefined));
            """;

        Assert.Equal("5\n0\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_StringOrNull_NarrowsToStringInThen()
    {
        var source = """
            function k(x: string | null): number {
                if (x) { return x.length; }
                return 0;
            }
            console.log(k("abc"));
            """;

        Assert.Equal("3\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_ObjectUnion_NarrowsAwayUndefinedInThen()
    {
        var source = """
            function g(x: { n: number } | undefined): number {
                if (x) { return x.n; }
                return 0;
            }
            console.log(g({ n: 5 }));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_Negation_NarrowsAfterEarlyReturn()
    {
        // `if (!d) return` narrows d to the truthy type for the code that follows.
        var source = """
            function f(d: string | undefined): number {
                if (!d) return 0;
                return d.length;
            }
            console.log(f("hiya"));
            """;

        Assert.Equal("4\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_NegationEarlyReturn_TripleUnion()
    {
        var source = """
            function f(x: { n: number } | null | undefined): number {
                if (!x) return -1;
                return x.n;
            }
            console.log(f({ n: 9 }));
            console.log(f(null));
            console.log(f(undefined));
            """;

        Assert.Equal("9\n-1\n-1\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_DoubleNegation_NarrowsLikeTruthy()
    {
        var source = """
            function f(x: string | undefined): number {
                if (!!x) { return x.length; }
                return 0;
            }
            console.log(f("hello"));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_LogicalAnd_NarrowsRightOperand()
    {
        // `x && x.n` must narrow x for the right operand (the std defensive-access idiom).
        var source = """
            function h(x: { n: number } | undefined): number {
                return (x && x.n) || 0;
            }
            console.log(h({ n: 7 }));
            console.log(h(undefined));
            """;

        Assert.Equal("7\n0\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_Ternary_NarrowsThenBranch()
    {
        var source = """
            function f(x: { n: number } | null): number {
                return x ? x.n : 0;
            }
            console.log(f({ n: 8 }));
            console.log(f(null));
            """;

        Assert.Equal("8\n0\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_PropertyPath_NarrowsNestedAccess()
    {
        var source = """
            type Box = { inner: { x: number } | undefined };
            function f(b: Box): number {
                if (b.inner) { return b.inner.x; }
                return 0;
            }
            console.log(f({ inner: { x: 42 } }));
            console.log(f({ inner: undefined }));
            """;

        Assert.Equal("42\n0\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_WhileCondition_NarrowsBody()
    {
        var source = """
            function f(x: string | undefined): number {
                let total = 0;
                let i = 0;
                while (x) { total += x.length; i++; if (i > 2) break; }
                return total;
            }
            console.log(f("ab"));
            """;

        Assert.Equal("6\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_FalsyNumberLiteralUnion_NarrowedAway()
    {
        // 0 is always-falsy: the truthy branch keeps only 1 | 2.
        var source = """
            function f(x: 0 | 1 | 2): number {
                if (x) { return x; }
                return 99;
            }
            console.log(f(0));
            console.log(f(2));
            """;

        Assert.Equal("99\n2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_ElseBranch_KeepsStringSinceEmptyStringIsFalsy()
    {
        // The falsy branch keeps `string` (an empty string is falsy), so `string | undefined`
        // stays `string | undefined` in the else — matching tsc's getTypeWithFacts(Falsy).
        var source = """
            function f(d: string | undefined): string {
                if (d) { return d.toUpperCase(); }
                return d === undefined ? "U" : d.toUpperCase();
            }
            console.log(f("ab"));
            console.log(f(""));
            console.log(f(undefined));
            """;

        Assert.Equal("AB\n\nU\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_Compiled_MatchesInterpreted()
    {
        // Narrowing is a type-check-time concern; confirm the guarded program also compiles
        // and runs identically under the IL backend.
        var source = """
            function f(d: string | undefined): number {
                if (d) { return d.length; }
                return 0;
            }
            console.log(f("compiled"));
            """;

        Assert.Equal("8\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void Truthiness_UnguardedAccess_StillErrors()
    {
        // Sanity: truthiness narrowing must not make unguarded access on a nullable type pass.
        var source = """
            function f(d: string | undefined): number {
                return d.length;
            }
            """;

        Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_DoesNotLeakPastEmptyThenBlock()
    {
        // After `if (d) {}` (no early exit), d is back to `string | undefined`: accessing it
        // unguarded afterwards must still error.
        var source = """
            function f(d: string | undefined): number {
                if (d) { }
                return d.length;
            }
            """;

        Assert.Throws<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ----- #557: a general `boolean` narrows to `true`/`false` under a truthiness guard -----
    // (the only general primitive tsc narrows this way; `string`/`number` stay intact).

    [Fact]
    public void Truthiness_GeneralBoolean_NarrowsToTrueInThen()
    {
        // The exact repro from #557. The `: true` return slot proves `b` narrows to `true`.
        var source = """
            function f(b: boolean): true {
                if (b) { return b; }
                throw new Error("unreachable");
            }
            console.log(f(true));
            """;

        Assert.Equal("true\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_GeneralBoolean_NarrowsToFalseInFalsyBranch()
    {
        // The falsy branch narrows `boolean` to `false`. After a terminating truthy branch the
        // fall-through sees `false`.
        var source = """
            function g(b: boolean): false {
                if (b) { throw new Error("no"); }
                return b;
            }
            console.log(g(false));
            """;

        Assert.Equal("false\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_GeneralBoolean_Negation_NarrowsToTrueAfterEarlyThrow()
    {
        // `if (!b) throw` leaves `b` as `true` for the code that follows.
        var source = """
            function f(b: boolean): true {
                if (!b) { throw new Error("no"); }
                return b;
            }
            console.log(f(true));
            """;

        Assert.Equal("true\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_BooleanOrUndefined_NarrowsBothBranches()
    {
        // With a union, the truthy branch narrows to `true`; the falsy branch keeps
        // `false | undefined` (both are falsy).
        var source = """
            function truthy(b: boolean | undefined): true {
                if (b) { return b; }
                throw new Error("no");
            }
            function falsy(b: boolean | undefined): false | undefined {
                if (b) { throw new Error("no"); }
                return b;
            }
            console.log(truthy(true));
            console.log(falsy(false));
            console.log(falsy(undefined));
            """;

        Assert.Equal("true\nfalse\nundefined\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_GeneralBoolean_WhileBodyNarrowsToTrue()
    {
        // The shared guard analyzer narrows `boolean` to `true` inside a loop body too.
        var source = """
            function f(b: boolean): true[] {
                const out: true[] = [];
                while (b) { out.push(b); break; }
                return out;
            }
            console.log(f(true).length);
            """;

        Assert.Equal("1\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_GeneralBoolean_UnguardedReturnAsTrue_StillErrors()
    {
        // Sanity: the narrowing is guard-gated — an unguarded `boolean` is not assignable to `true`.
        var source = """
            function f(b: boolean): true {
                return b;
            }
            """;

        Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Truthiness_GeneralBoolean_Compiled_MatchesInterpreted()
    {
        var source = """
            function f(b: boolean): true {
                if (b) { return b; }
                throw new Error("no");
            }
            console.log(f(true));
            """;

        Assert.Equal("true\n", TestHarness.RunCompiled(source));
    }
}
