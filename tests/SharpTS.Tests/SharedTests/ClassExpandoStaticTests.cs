using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for expando statics on class constructors via bracket assignment (#262).
/// Node allows arbitrary string/symbol-keyed statics on class objects.
/// </summary>
public class ClassExpandoStaticTests
{
    [Theory, ModeData]
    public void StringKeyedExpandoStatic_RoundTrips(ExecutionMode mode)
    {
        var source = """
            class C {}
            (C as any)["foo"] = 1;
            console.log((C as any)["foo"] === 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SymbolKeyedExpandoStatic_RoundTrips(ExecutionMode mode)
    {
        var source = """
            class C {}
            (C as any)[Symbol.species] = 2;
            console.log((C as any)[Symbol.species] === 2);
            console.log((C as any)[Symbol.iterator] === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ExpandoStatics_InAsyncContext_RoundTrip(ExecutionMode mode)
    {
        var source = """
            class C {}
            async function go() {
                (C as any)["bar"] = 3;
                (C as any)[Symbol.toPrimitive] = 4;
                console.log((C as any)["bar"] === 3);
                console.log((C as any)[Symbol.toPrimitive] === 4);
            }
            go();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // Both modes walk the constructor parent chain for expando statics (#265);
    // Node inherits them via Object.getPrototypeOf(D) === C.
    [Theory, ModeData]
    public void ExpandoStatics_InheritedThroughSubclassChain(ExecutionMode mode)
    {
        var source = """
            class C {}
            class D extends C {}
            (C as any)["foo"] = 1;
            (C as any)[Symbol.species] = 2;
            console.log((D as any)["foo"] === 1);
            console.log((D as any)[Symbol.species] === 2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // An own expando static on the subclass shadows the inherited one (#265),
    // and setting it on the subclass must not mutate the base's own value.
    [Theory, ModeData]
    public void ExpandoStatics_OwnShadowsInherited(ExecutionMode mode)
    {
        var source = """
            class C {}
            class D extends C {}
            const sp = Symbol("sp");
            (C as any)["foo"] = 1;
            (C as any)[sp] = 1;
            (D as any)["foo"] = 9;
            (D as any)[sp] = 9;
            console.log((D as any)["foo"] === 9);
            console.log((D as any)[sp] === 9);
            console.log((C as any)["foo"] === 1);
            console.log((C as any)[sp] === 1);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    // A two-level chain resolves an expando static set on the grandparent (#265).
    [Theory, ModeData]
    public void ExpandoStatics_InheritedThroughTwoLevels(ExecutionMode mode)
    {
        var source = """
            class A {}
            class B extends A {}
            class C extends B {}
            (A as any)["foo"] = 7;
            (A as any)[Symbol.species] = 8;
            console.log((C as any)["foo"] === 7);
            console.log((C as any)[Symbol.species] === 8);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
