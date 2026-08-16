using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for property access on a BARE <c>null</c>/<c>undefined</c> type (issue #742). Such a receiver
/// has no properties, so <c>tsc</c> rejects the access — TS2339 ("does not exist on type 'undefined'")
/// for <c>undefined</c>, TS2531 ("Object is possibly 'null'") for <c>null</c> — just as it does for a
/// union that contains them (see <c>CheckGetOnUnion</c>). SharpTS previously let a bare nullish receiver
/// fall through the property-access dispatch to <c>any</c>, silently accepting the access. Optional
/// chaining (<c>x?.p</c>) short-circuits to <c>undefined</c> and stays legal.
/// </summary>
public class BareNullishPropertyAccessTests
{
    [Fact]
    public void BareUndefined_PropertyAccess_RejectedWithTs2339()
    {
        // The issue's `f()` repro: a property read on a bare `undefined` type does not exist.
        var source = """
            function f(): number {
                let x: undefined = undefined;
                return x.length;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2339", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void BareNull_PropertyAccess_RejectedWithTs2531()
    {
        var source = """
            function f(): void {
                let x: null = null;
                x.foo;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2531", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void BareUndefined_OptionalChaining_ShortCircuitsToUndefined()
    {
        // `x?.length` on a bare `undefined` is legal and evaluates to `undefined`.
        var source = """
            function h(x: undefined): number | undefined {
                return x?.length;
            }
            console.log(h(undefined));
            """;

        Assert.Equal("undefined\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BareNull_OptionalChaining_ShortCircuitsToUndefined()
    {
        var source = """
            function h(x: null): number | undefined {
                return x?.valueOf;
            }
            console.log(h(null));
            """;

        Assert.Equal("undefined\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ReassignToNull_ThenAccess_FlaggedViaBareNullCheck()
    {
        // The issue's `g()` repro: `x = null` now narrows the variable to bare `null` (#742, dropping
        // the former IsPurelyNullish guard), and `x.length` is flagged directly as possibly-null.
        var source = """
            function g(x: string | null): number {
                x = null;
                return x.length;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2531", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ReassignToUndefined_ThenAccess_FlaggedViaBareUndefinedCheck()
    {
        var source = """
            function g(x: string | undefined): number {
                x = undefined;
                return x.length;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2339", ex.Diagnostic.TsCode);
    }
}
