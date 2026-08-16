using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Inherited members must be visible to structural assignability and to member-access type
/// resolution, across two superclass shapes the shared member collectors
/// (<c>CollectPublicInstanceMembers</c> / <c>CollectGenericClassMembers</c>) previously stopped at:
/// <list type="bullet">
///   <item>a <c>declare class</c>'s <c>extends</c> clause, which <c>CheckDeclareClass</c> dropped
///   entirely (#505); and</item>
///   <item>a generic-instantiation superclass <c>extends Base&lt;number&gt;</c> — an
///   <c>InstantiatedGeneric</c>, not a bare <c>TypeInfo.Class</c>, so the collectors' <c>is
///   TypeInfo.Class</c> walk exited at it without substituting the base's type arguments (#506).</item>
/// </list>
/// The conditional <c>infer</c>-matching side of both lives in <see cref="InheritedInferMatchTests"/>;
/// this file covers the structural-assignability and member-access consumers of the same collectors.
/// </summary>
public class InheritedMemberResolutionTests
{
    // ---- #505: member access through a declare class's (previously dropped) extends clause ----

    [Fact]
    public void DeclareClass_InheritedMemberAccess_ResolvesDeclaredType()
    {
        // s.val is inherited from Base and typed "correct"; returning it as a number is TS2322. The
        // verbatim repro from #505. Before the fix the superclass was dropped, val was not found, and
        // the function type-checked with no error.
        var source = """
            declare class Base { val: "correct"; }
            declare class Sub extends Base {}
            function f(s: Sub): number { return s.val; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void DeclareClass_InheritedMemberAccess_AcceptsDeclaredType()
    {
        var source = """
            declare class Base { val: "correct"; }
            declare class Sub extends Base {}
            function f(s: Sub): "correct" { return s.val; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DeclareClass_NonClassSuperclass_StillRejected()
    {
        // The shared superclass resolver also runs for declare classes, so `extends` of a non-class
        // remains an error (it is not silently dropped).
        var source = """
            type NotAClass = { x: number };
            declare class Sub extends NotAClass {}
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- #506: structural assignability through a generic-instantiation superclass ----

    [Fact]
    public void GenericSuperclass_InstanceAssignableToMatchingInterface()
    {
        // Sub inherits `value: number` from Base<number>; tsc accepts the assignment to HasNum. The
        // verbatim repro #1 from #506 — fails before the fix (Sub's collected member set is empty).
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub extends Base<number> {}
            interface HasNum { value: number; }
            const h: HasNum = new Sub(5);
            console.log(h.value);
            """;
        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GenericSuperclass_InheritedMemberSubstituted_WrongTargetRejected()
    {
        // Sub inherits `value: number`; the target wants string. This must stay rejected — guarding
        // against a fix that folds the base's members in WITHOUT substituting (leaving `value: any`,
        // which would wrongly satisfy `{ value: string }`).
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub extends Base<number> {}
            interface HasStr { value: string; }
            const h: HasStr = new Sub(5);
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GenericSuperclass_GenericSub_ComposesSubstitution()
    {
        // class Sub<U> extends Base<U> instantiated as Sub<string>: the value member must resolve to
        // string (U:=string composed into Base's T), so a string shape is assignable.
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub<U> extends Base<U> { constructor(v: U) { super(v); } }
            interface HasStr { value: string; }
            const h: HasStr = new Sub<string>("x");
            console.log(h.value);
            """;
        Assert.Equal("x\n", TestHarness.RunInterpreted(source));
    }

    // ---- #639: the BRANDED-target collector (CollectAllInstanceMembers) also walks a
    //            generic-instantiation superclass. A target class branded by a private/protected
    //            member is matched over ALL its members; the inherited generic-base members must be
    //            in that required set (with the base's type arguments substituted), so a same-origin
    //            source whose inherited members have incompatible instantiated types is rejected. ----

    [Fact]
    public void BrandedTarget_GenericBase_SiblingInstantiationSource_Rejected()
    {
        // Source and Target share the protected `brand` from Base (same declaration → they pass the
        // nominal accessibility gate), and have identical OWN members (`ownField: number`). They differ
        // only in the base instantiation: Target extends Base<number>, Source extends Base<string>.
        // The inherited `brand`/`baseField` resolve to number on Target but string on Source, so Source
        // is not assignable to Target — but only once the inherited generic-base members are part of
        // Target's required set. Before #639 that set omitted them and the assignment was accepted.
        var source = """
            class Base<T> {
                protected brand: T;
                baseField: T;
                constructor(v: T) { this.brand = v; this.baseField = v; }
            }
            class Target extends Base<number> { ownField: number = 0; }
            class Source extends Base<string> { ownField: number = 0; constructor() { super("x"); } }
            const t: Target = new Source();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void BrandedTarget_GenericBase_MatchingInstantiationSource_Accepted()
    {
        // Non-vacuity companion: a source extending the SAME instantiation (Base<number>) inherits
        // number-typed brand/baseField, matching the target, so the assignment is accepted. This
        // differs from the rejected case ONLY in the base's type argument, proving the inherited
        // generic-base members are what the check now consults.
        var source = """
            class Base<T> {
                protected brand: T;
                baseField: T;
                constructor(v: T) { this.brand = v; this.baseField = v; }
            }
            class Target extends Base<number> { ownField: number = 0; }
            class Match extends Base<number> { ownField: number = 0; constructor() { super(1); } }
            const t: Target = new Match();
            console.log("ok");
            """;
        Assert.Equal("ok\n", TestHarness.RunInterpreted(source));
    }
}
