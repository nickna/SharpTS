using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-checking coverage for issue #130:
/// <list type="bullet">
/// <item>object spread (<c>{ ...src }</c>) of an interface/object-typed value is accepted and its
/// members are merged into the result type (previously rejected with TS2698);</item>
/// <item>an optional member contributed by an intersection (<c>A &amp; { y?: T }</c>) stays optional
/// after the intersection is flattened (previously dropped when the part was a Record);</item>
/// <item><c>Object.prototype</c> members (<c>hasOwnProperty</c>, <c>toString</c>, ...) resolve on any
/// object type instead of producing TS2339.</item>
/// </list>
/// </summary>
public class SpreadInterfaceIntersectionTests
{
    [Fact]
    public void SpreadOfInterfaceValue_MergesMembers()
    {
        // `{ ...src }` where src is interface-typed: members flow into the result, so `.foo` is string.
        TestHarness.RunInterpreted(
            "interface I { foo: string; } const src: I = { foo: \"a\" }; const t = { ...src }; let s: string = t.foo;");
    }

    [Fact]
    public void SpreadOfInterfaceValue_NotRejectedAsNonObject()
    {
        // Regression guard for the former TS2698 "Spread requires an object" false positive.
        TestHarness.RunInterpreted(
            "interface I { foo: string; } const src: I = { foo: \"a\" }; const t = { ...src };");
    }

    [Fact]
    public void IntersectionOptionalMember_StaysOptional()
    {
        // `y?` comes from a Record part of the intersection; omitting it must still type-check.
        TestHarness.RunInterpreted(
            "interface A { x: number; } type B = A & { y?: string }; const b: B = { x: 1 };");
    }

    [Fact]
    public void IntersectionOptionalMember_RequiredPartStillEnforced()
    {
        // The required member from the interface part is still required.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted(
                "interface A { x: number; } type B = A & { y?: string }; const b: B = { y: \"q\" };"));
    }

    [Fact]
    public void HasOwnProperty_ResolvesOnInterfaceType()
    {
        TestHarness.RunInterpreted(
            "interface I { foo: string; } const o: I = { foo: \"a\" }; const h: boolean = o.hasOwnProperty(\"foo\");");
    }

    [Fact]
    public void ToString_ResolvesOnRecordType()
    {
        TestHarness.RunInterpreted("const r = { a: 1 }; const s: string = r.toString();");
    }

    [Fact]
    public void MissingProperty_StillErrors()
    {
        // The prototype-member fallback must not mask genuinely-absent members.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("interface I { foo: string; } const o: I = { foo: \"a\" }; let x: string = o.bar;"));
    }
}
