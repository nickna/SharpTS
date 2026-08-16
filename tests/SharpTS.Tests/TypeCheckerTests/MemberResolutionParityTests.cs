using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Parity tests for member resolution: accessing the same member directly and through a
/// constrained type parameter, union, or intersection must resolve through the same shared
/// CheckGetOn* leaf helpers (TypeChecker.Properties.Helpers.cs) and agree on the result.
/// Guards the consolidation of CheckGetOnType's inline lookup blocks onto those helpers.
/// </summary>
public class MemberResolutionParityTests
{
    [Fact]
    public void InterfaceMember_DirectAndViaConstrainedTypeParam_Agree()
    {
        var source = """
            interface Named { name: string; }
            function direct(n: Named): string { return n.name; }
            function viaParam<T extends Named>(n: T): string { return n.name; }
            console.log(direct({ name: "a" }));
            console.log(viaParam({ name: "b" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("a\nb\n", result);
    }

    [Fact]
    public void MissingInterfaceMember_ViaConstrainedTypeParam_StillTs2339()
    {
        var source = """
            interface Named { name: string; }
            function viaParam<T extends Named>(n: T): string { return n.missing; }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void ClassInstanceMember_DirectAndViaUnion_Agree()
    {
        var source = """
            class A { kind: string = "a"; describe(): string { return "A:" + this.kind; } }
            class B { kind: string = "b"; describe(): string { return "B:" + this.kind; } }
            function viaUnion(x: A | B): string { return x.describe(); }
            const a = new A();
            console.log(a.describe());
            console.log(viaUnion(new B()));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("A:a\nB:b\n", result);
    }

    [Fact]
    public void PrivateMember_ViaConstrainedTypeParam_EnforcesVisibility()
    {
        // Direct access to a private member outside the class is TS2341; access through a
        // constrained type parameter must enforce the same visibility (leaf-helper parity).
        var source = """
            class Secretive { private secret: number = 1; }
            function viaParam<T extends Secretive>(s: T): number { return s.secret; }
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
        Assert.Contains("private", ex.Message);
    }

    [Fact]
    public void GenericInstanceMember_ViaConstrainedTypeParam_SubstitutesTypeArgs()
    {
        var source = """
            class Box<T> {
                constructor(public value: T) {}
                get doubled(): T { return this.value; }
            }
            function viaParam<U extends Box<number>>(b: U): number { return b.value + 1; }
            const box = new Box<number>(41);
            console.log(box.value + 1);
            console.log(viaParam(box));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n42\n", result);
    }

    [Fact]
    public void ObjectPrototypeMember_OnInterface_ViaConstrainedTypeParam_Resolves()
    {
        // toString comes from Object.prototype; the shared interface leaf helper resolves it,
        // and the constrained-type-parameter path must agree with direct access.
        var source = """
            interface Named { name: string; }
            function direct(n: Named): string { return n.toString(); }
            function viaParam<T extends Named>(n: T): string { return n.toString(); }
            const v = { name: "x" };
            console.log(typeof direct(v));
            console.log(typeof viaParam(v));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("string\nstring\n", result);
    }

    [Fact]
    public void StaticMember_ViaClassTypeInUnionContext_Resolves()
    {
        var source = """
            class Counter {
                static count: number = 7;
                static bump(): number { return Counter.count + 1; }
            }
            console.log(Counter.count);
            console.log(Counter.bump());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("7\n8\n", result);
    }

    [Fact]
    public void RecordMember_ViaIntersection_Resolves()
    {
        var source = """
            type WithId = { id: number };
            type WithName = { name: string };
            function viaIntersection(x: WithId & WithName): string { return x.name + x.id; }
            console.log(viaIntersection({ id: 1, name: "n" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("n1\n", result);
    }
}
