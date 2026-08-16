using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Conditional-type <c>infer</c> matching against a class instance must see the class's full
/// structural surface — public fields, getters, AND methods, merged across the entire superclass
/// chain (#461 own methods, #492 inherited members). Previously the property source
/// (<c>ExtractPropertiesWithTypes</c>) read only a class's OWN fields and getters, so a structural
/// extends clause like <c>{ val: infer R }</c> silently took its false branch when <c>val</c> was a
/// method or was inherited.
///
/// Each test pins the resolved conditional via an annotation: if <c>J&lt;C&gt;</c> resolves to the
/// true branch (the inferred member type) the matching-typed assignment is accepted and the
/// mismatched one throws; if it wrongly took the false branch (<c>"no"</c>) the expectations invert.
///
/// Most tests use regular (non-<c>declare</c>) classes, but <c>declare class</c> inheritance is now
/// covered too: #505 fixed <c>declare class</c> dropping its <c>extends</c> clause, and #506 fixed the
/// member collectors stopping at a generic-instantiation superclass (<c>extends Base&lt;number&gt;</c>).
/// </summary>
public class InheritedInferMatchTests
{
    // ---- inherited FIELD (#492) ----

    [Fact]
    public void InheritedField_ResolvesToInferredType()
    {
        // val is declared on Base; Sub inherits it. J<Sub> must be "correct", so "correct" assigns.
        var source = """
            class Base { val: "correct" = "correct"; }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InheritedField_WrongAssignment_IsTypeError()
    {
        // J<Sub> is "correct" (inherited), so a number violates it.
        var source = """
            class Base { val: "correct" = "correct"; }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- inherited GETTER (#492) ----

    [Fact]
    public void InheritedGetter_ResolvesToInferredType()
    {
        var source = """
            class Base { get val(): "correct" { return "correct"; } }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- inherited METHOD (#492 method variant) ----

    [Fact]
    public void InheritedMethod_ResolvesToInferredReturnType()
    {
        // toJSON is an inherited method; { toJSON(): infer R } must bind R to its return type.
        var source = """
            class Base { toJSON(): "correct" { return "correct"; } }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let x: J<Sub> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InheritedMethod_WrongAssignment_IsTypeError()
    {
        var source = """
            class Base { toJSON(): "correct" { return "correct"; } }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let x: J<Sub> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- own METHOD (#461 — the prerequisite this fix also closes) ----

    [Fact]
    public void OwnMethod_ResolvesToInferredReturnType()
    {
        // A class's OWN method must be matchable too — the property source omitted methods entirely.
        var source = """
            class MyClass { toJSON(): "correct" { return "correct"; } }
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let x: J<MyClass> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- deeper chain ----

    [Fact]
    public void TwoLevelInheritance_ResolvesToInferredType()
    {
        // val lives two levels up; the whole Superclass chain is merged.
        var source = """
            class A { val: "correct" = "correct"; }
            class B extends A {}
            class C extends B { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<C> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DerivedMemberShadowsInherited()
    {
        // The derived declaration wins over the inherited one (own-wins precedence).
        var source = """
            class Base { val: "base" = "base"; }
            class Sub extends Base { val: "derived" = "derived"; }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "derived";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DerivedMemberShadowsInherited_InheritedValueRejected()
    {
        var source = """
            class Base { val: "base" = "base"; }
            class Sub extends Base { val: "derived" = "derived"; }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "base";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- generic check type (the receiver is itself a generic instantiation) ----

    [Fact]
    public void GenericInstantiationCheckType_SubstitutesAndInfers()
    {
        // J<Box<number>> binds R to the substituted field type (number).
        var source = """
            class Box<T> { value: T; constructor(v: T) { this.value = v; } }
            type J<T> = T extends { value: infer R } ? R : "no";
            let x: J<Box<number>> = 123;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void GenericInstantiationCheckType_WrongAssignment_IsTypeError()
    {
        var source = """
            class Box<T> { value: T; constructor(v: T) { this.value = v; } }
            type J<T> = T extends { value: infer R } ? R : "no";
            let x: J<Box<number>> = "nope";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- exclusions / false-branch controls ----

    [Fact]
    public void PrivateInheritedMember_IsExcluded_TakesFalseBranch()
    {
        // A private inherited member is not part of the structural surface, so the match fails and
        // J<Sub> is the false branch "no" — which "no" satisfies.
        var source = """
            class Base { private val: "correct" = "correct"; }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "no";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void PrivateInheritedMember_TrueBranchValueRejected()
    {
        // Confirms the above really took the false branch: "correct" is not assignable to "no".
        var source = """
            class Base { private val: "correct" = "correct"; }
            class Sub extends Base { extra(): void {} }
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "correct";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AbsentMember_TakesFalseBranch()
    {
        // No val anywhere in the hierarchy → false branch.
        var source = """
            class Base { other: number = 1; }
            class Sub extends Base {}
            type J<T> = T extends { val: infer R } ? R : "no";
            let x: J<Sub> = "no";
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- string-resolver (as-cast) path, to confirm the fix is independent of resolution route ----

    [Fact]
    public void AsCast_InheritedField_ResolvesToInferredType()
    {
        var source = """
            class Base { val: "correct" = "correct"; }
            class Sub extends Base {}
            type J<T> = T extends { val: infer R } ? R : "no";
            const s: "correct" = (null as any as J<Sub>);
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- method shapes (params; interface parity) — the broader #461 surface ----

    [Fact]
    public void OwnMethod_ParameterInfer_IsTypeError()
    {
        // infer in PARAMETER position: P binds to the method's parameter type (number).
        var source = """
            class C { m(a: number): void {} }
            type Arg<T> = T extends { m(a: infer P): void } ? P : "no";
            let z: Arg<C> = "str";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void InterfaceMethod_StillMatches()
    {
        // Interface members already carried methods; lock that the shared path keeps working
        // (R binds to "correct", so the false-branch literal "no" is rejected).
        var source = """
            interface I { toJSON(): "correct"; }
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            let z: J<I> = "no";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void DeclareClass_OwnMethod_Matches()
    {
        // An OWN method on a `declare class` matches. This is the exact repro form from #461 / PR #498.
        var source = """
            declare class MyClass { toJSON(): "correct"; }
            type J<T> = T extends { toJSON(): infer R } ? R : "no";
            const z: J<MyClass> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- #506: generic-instantiation superclass (`class Sub extends Base<number>`) ----

    [Fact]
    public void GenericSuperclass_NonGenericSub_InheritedFieldInfers()
    {
        // Sub's superclass is Base<number> (an InstantiatedGeneric, not a bare Class). The member
        // collectors must walk into it and substitute T:=number, so R binds to number. The verbatim
        // repro #2 from #506.
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub extends Base<number> { extra(): void {} }
            type J<T> = T extends { value: infer R } ? R : "no";
            let x: J<Sub> = 123;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void GenericSuperclass_NonGenericSub_WrongAssignment_IsTypeError()
    {
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub extends Base<number> { extra(): void {} }
            type J<T> = T extends { value: infer R } ? R : "no";
            let x: J<Sub> = "nope";
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void GenericSuperclass_GenericSub_ComposesSubstitutionDownTheChain()
    {
        // class Sub<U> extends Base<U>, instantiated as Sub<string>: the substitution composes
        // (U:=string, then Base's T:=U), so value resolves to string.
        var source = """
            class Base<T> { value: T; constructor(v: T) { this.value = v; } }
            class Sub<U> extends Base<U> { constructor(v: U) { super(v); } }
            type J<T> = T extends { value: infer R } ? R : "no";
            let x: J<Sub<string>> = "ok";
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- #505: declare-class inheritance (the `extends` clause is no longer dropped) ----

    [Fact]
    public void DeclareClass_InheritedField_Matches()
    {
        // declare class Sub extends Base {} — val is inherited from Base. Before #505 the superclass
        // was dropped, so val was invisible and J<Sub> wrongly took the false branch.
        var source = """
            declare class Base { val: "correct"; }
            declare class Sub extends Base {}
            type J<T> = T extends { val: infer R } ? R : "no";
            const z: J<Sub> = "correct";
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DeclareClass_InheritedField_WrongAssignment_IsTypeError()
    {
        var source = """
            declare class Base { val: "correct"; }
            declare class Sub extends Base {}
            type J<T> = T extends { val: infer R } ? R : "no";
            const z: J<Sub> = 42;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
