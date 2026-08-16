using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Regression tests for #185: DeepReadonly-style aliases (a generic alias referenced from a
/// mapped type's value with the mapped parameter in its arguments, e.g.
/// <c>{ [P in Keys&lt;T&gt;]: DeepReadonly&lt;T[P]&gt; }</c>) used to expand eagerly with the unbound
/// parameter baked into the instantiation key — Part[P], Part[P][P], … — recursing until the
/// process died with a stack overflow. Such references now defer until the key is substituted,
/// and genuinely divergent instantiations are bounded by the TS2589 depth guard.
/// </summary>
public class RecursiveAliasInstantiationTests
{
    private const string DeepReadonlyPrelude = """
        type NonFunctionPropertyNames<T> = { [K in keyof T]: T[K] extends Function ? never : K }[keyof T];
        type DeepReadonlyObject<T> = { readonly [P in NonFunctionPropertyNames<T>]: DeepReadonly<T[P]> };
        type DeepReadonly<T> = T extends any[] ? T : T extends object ? DeepReadonlyObject<T> : T;
        """;

    [Fact]
    public void DeepReadonlyStyleAlias_OverFlatInterface_Converges()
    {
        var source = DeepReadonlyPrelude + """

            interface Flat { id: number; name: string; }
            function f(part: DeepReadonly<Flat>) {
                let name: string = part.name;
                let id: number = part.id;
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DeepReadonlyStyleAlias_OverSelfRecursiveInterface_Converges()
    {
        // Part references itself (subparts: Part[]) — the instantiation cycle must hit the
        // in-flight same-key guard and terminate instead of growing a fresh key each round.
        var source = DeepReadonlyPrelude + """

            interface Part { id: number; name: string; subparts: Part[]; }
            function f(part: DeepReadonly<Part>) {
                let name: string = part.name;
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void MappedValueWithWrongPropertyType_StillChecks()
    {
        // The deferred alias must still resolve to the real property type per key — assigning
        // the (readonly-mapped) string property to a number must fail.
        var source = DeepReadonlyPrelude + """

            interface Flat { id: number; name: string; }
            function f(part: DeepReadonly<Flat>) {
                let bad: number = part.name;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GrowingAliasInstantiation_ReportsTs2589_InsteadOfOverflowing()
    {
        // Each round derives a fresh instantiation key (Grow<number>, Grow<number[]>, …), so
        // the same-key cycle guard can never fire; the depth guard must stop it with the
        // canonical TS2589 instead of a process-killing stack overflow.
        var source = """
            type Grow<T> = { v: Grow<T[]> };
            let x: Grow<number> = null as any;
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("excessively deep", ex.Message);
    }
}
