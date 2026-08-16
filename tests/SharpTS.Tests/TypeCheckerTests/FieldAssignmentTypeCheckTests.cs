using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Type-checking of instance field initializers and instance field assignments. Both were
/// previously unchecked: a non-setter field assignment (<c>this.x = v</c>) and an instance field
/// initializer (<c>x = v</c> / <c>r = () =&gt; {…}</c>) now validate against the field's declared type
/// and type-check their bodies.
/// </summary>
public class FieldAssignmentTypeCheckTests
{
    [Fact]
    public void InstanceFieldInitializer_TypeMismatch_Errors()
    {
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("class C { x: number = \"bad\"; }"));
    }

    [Fact]
    public void InstanceFieldInitializer_Compatible_Ok()
    {
        TestHarness.RunInterpreted("class C { x: number = 5; } let c: C = new C(); console.log(c.x);");
    }

    [Fact]
    public void InstanceFieldAssignment_TypeMismatch_Errors()
    {
        // `this.x = v` is now checked against x's declared type.
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("class C { x: number = 0; m() { this.x = \"bad\"; } }"));
    }

    [Fact]
    public void InstanceFieldAssignment_Compatible_Ok()
    {
        TestHarness.RunInterpreted("""
            class C { x: number = 0; m(): void { this.x = 7; } }
            let c: C = new C();
            c.m();
            console.log(c.x);
            """);
    }

    [Fact]
    public void FieldArrowInitializer_BodyIsTypeChecked()
    {
        // The arrow body assigned to a field is type-checked: `this.t = this.u` with unrelated
        // constrained type parameters is an error (it previously went unchecked).
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("""
                class Foo { foo: string; }
                class C<T extends Foo, U extends Foo> {
                    t: T;
                    u: U;
                    r = () => { this.t = this.u; };
                }
                """));
    }

    [Fact]
    public void UntypedField_AssignmentNotConstrained()
    {
        // A field with no annotation is `any`; assignments to it are unconstrained.
        TestHarness.RunInterpreted("""
            class C { x = 0; m(): void { this.x = 1; } }
            let c: C = new C();
            c.m();
            console.log("ok");
            """);
    }
}
