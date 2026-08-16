using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-checking coverage for class-body index signatures (issue #123):
/// <c>class A { [k: string]: T }</c>. The index type is modelled on the class and consulted
/// when indexing a class instance, mirroring index signatures on interfaces/object types.
/// </summary>
public class ClassIndexSignatureTests
{
    [Fact]
    public void ClassWithStringIndex_Parses()
    {
        // Distinguished from a computed property name; no parse error.
        TestHarness.RunInterpreted("class A { [k: string]: number } let a: A = new A();");
    }

    [Fact]
    public void StringIndexAccess_ReturnsValueType()
    {
        TestHarness.RunInterpreted("class A { [k: string]: number } let a: A = new A(); let n: number = a[\"x\"];");
    }

    [Fact]
    public void StringIndexAccess_TypeMismatchErrors()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("class A { [k: string]: number } let a: A = new A(); let s: string = a[\"x\"];"));
    }

    [Fact]
    public void NumberIndexAccess_ReturnsValueType()
    {
        TestHarness.RunInterpreted("class A { [k: number]: string } let a: A = new A(); let s: string = a[0];");
    }

    [Fact]
    public void NumericKey_FallsBackToStringIndex()
    {
        // A class with only a string index signature still accepts numeric keys.
        TestHarness.RunInterpreted("class A { [k: string]: number } let a: A = new A(); let n: number = a[0];");
    }

    [Fact]
    public void DeclareClassWithIndexSignature_IsModeled()
    {
        // `declare class` has no runtime body, so assert via a type mismatch that short-circuits
        // before interpretation — confirming the ambient class's index type is modeled too.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("declare class A { [k: string]: number } let a: A; let s: string = a[\"x\"];"));
    }

    [Fact]
    public void ComputedPropertyName_StillParses_NotMistakenForIndexSignature()
    {
        // [expr] (no `ident:` shape) must remain a computed property name, not an index signature.
        TestHarness.RunInterpreted("const k = \"key\"; class A { [k]: number = 1; } let a: A = new A();");
    }

    #region Index-signature assignability (issue: index signatures must be compatible)

    [Fact]
    public void IndexSignature_SubtypeValue_AssignableToClassIndex()
    {
        // `{ [x: string]: Derived }` is assignable to a class with `{ [x: string]: Base }` (Derived <: Base).
        TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            class A { [x: string]: Base; }
            function g(b: { [x: string]: Derived }): A { return b; }
            """);
    }

    [Fact]
    public void IndexSignature_SupertypeValue_NotAssignableToNarrowerIndex()
    {
        // The reverse direction is unsafe: a Base-valued index is not assignable to a Derived-valued one.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            function h(a: { [x: string]: Base }): { [x: string]: Derived } { return a; }
            """));
    }

    [Fact]
    public void IndexSignature_NamedMemberAssignableToIndexType_Ok()
    {
        // A source's named member compatible with the target's string index type is fine.
        TestHarness.RunInterpreted("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            function good(src: { p: Derived }): { [k: string]: Base } { return src; }
            """);
    }

    [Fact]
    public void IndexSignature_NamedMemberNotAssignableToIndexType_Error()
    {
        // A named member incompatible with the target's string index type is rejected.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            function bad(src: { p: number }): { [k: string]: string } { return src; }
            """));
    }

    #endregion
}
