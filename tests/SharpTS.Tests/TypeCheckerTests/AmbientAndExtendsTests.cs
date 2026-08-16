using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Three checker gaps surfaced by the conformance Fail survey:
/// ambient function declarations (single and overloaded) must define the name immediately;
/// interfaces may extend generic interface instantiations (<c>extends A&lt;Base&gt;</c>);
/// interfaces may extend classes (inheriting their member types).
/// Ambient functions have no runtime binding, so test code never CALLS them at the top level —
/// only the type checker's view is asserted.
/// </summary>
public class AmbientAndExtendsTests
{
    [Fact]
    public void DeclareFunction_DefinesName_ForTypeChecking()
    {
        var source = """
            declare function pick(x: number): string;
            function use() {
                let s: string = pick(1);
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void DeclareFunction_WrongArgumentType_IsError()
    {
        var source = """
            declare function pick(x: number): string;
            function use() {
                let s: string = pick("nope");
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void DeclareFunction_Overloads_ResolvePerSignature()
    {
        var source = """
            declare function conv(x: number): string;
            declare function conv(x: string): number;
            function use() {
                let s: string = conv(1);
                let n: number = conv("a");
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InterfaceExtendsGenericInstantiation_InheritsSubstitutedMembers()
    {
        var source = """
            class Base { foo: string; }
            interface A<T> {
                value: T;
            }
            interface B extends A<Base> { }
            function use() {
                var b: B;
                var s: string = b.value.foo;
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void InterfaceExtendsGenericInstantiation_MemberTypeEnforced()
    {
        var source = """
            interface A<T> {
                value: T;
            }
            interface B extends A<number> { }
            function use() {
                var b: B;
                var s: string = b.value;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void InterfaceExtendsClass_InheritsMemberTypes()
    {
        var source = """
            class Base { public foo: string; }
            interface I extends Base { bar: number; }
            function use() {
                var i: I;
                var s: string = i.foo;
                var n: number = i.bar;
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void OverloadResolution_AnyArgument_PicksAnyOverload()
    {
        // tsc's two-pass overload resolution: an `any` argument is assignable to every parameter
        // but is a SUBTYPE only of any/unknown, so the (x: any) overload wins over (x: number).
        var source = """
            declare function f(x: number): number;
            declare function f(x: any): any;
            declare function g(x: string): string;
            declare function g(x: any): any;
            function use(a: any) {
                var r = f(a);
                var r = g(a);
            }
            """;
        TestHarness.RunInterpreted(source); // both calls yield any — redeclarations agree
    }

    [Fact]
    public void InterfaceExtendsClass_NonObjectBase_StillRejected()
    {
        var source = """
            type NotAnInterface = string;
            interface I extends NotAnInterface { }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
