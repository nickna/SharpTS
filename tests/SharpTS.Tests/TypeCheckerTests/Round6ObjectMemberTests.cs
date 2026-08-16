using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Round-6 fixes (#226 object-member cluster): the optional-vs-required presence rule, tsc's
/// weak-type check (TS2559), and structural cache identity for same-named interfaces.
/// </summary>
public class Round6ObjectMemberTests
{
    [Fact]
    public void SourceOptionalMember_DoesNotSatisfyRequiredTarget()
    {
        var source = """
            class Base { x: string; }
            interface C { opt: Base }
            interface D { opt?: Base }
            var c: C;
            var d: D;
            function use() { c = d; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void SourceOptionalMember_StructuralRecordTarget_AlsoRejected()
    {
        var source = """
            class Base { x: string; }
            interface D { opt?: Base }
            var a: { opt: Base; };
            var d: D;
            function use() { a = d; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void RequiredSource_SatisfiesOptionalTarget()
    {
        var source = """
            class Base { x: string; }
            interface C { opt?: Base }
            interface F { opt: Base }
            var c: C;
            var f: F;
            function use() { c = f; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void WeakType_NoCommonProperties_IsError()
    {
        // tsc TS2559: target is all-optional ("weak"); a source with properties but none in
        // common is rejected despite the vacuous structural pass.
        var source = """
            class Base { x: string; }
            interface C { opt?: Base }
            interface D { other: Base }
            var c: C;
            var d: D;
            function use() { c = d; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void WeakType_OverlappingProperty_IsAccepted()
    {
        var source = """
            class Base { x: string; }
            interface C { opt?: Base; other?: string }
            interface D { other: string }
            var c: C;
            var d: D;
            function use() { c = d; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void SameNamedInterfaces_InDifferentScopes_DoNotShareCacheVerdicts()
    {
        // Module 1's C/D (optional target) accept; module 2's same-named C/D (required target)
        // must reject — a name-only cache key conflated them.
        var source = """
            class Base { x: string; }
            module M1 {
                interface C { opt?: Base }
                interface D { opt?: Base }
                var c: C;
                var d: D;
                c = d;
            }
            module M2 {
                interface C { opt: Base }
                interface D { opt?: Base }
                var c: C;
                var d: D;
                c = d;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
