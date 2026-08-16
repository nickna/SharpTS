using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Declaration identity (type-AST migration slice 4): same-named declarations in different
/// scopes are distinct types — neither compat-cache verdicts nor name bindings may leak from
/// one to the other. Pins the two cancelling bugs found via assignmentCompatWithObjectMembers*:
/// name-keyed class cache collisions, and namespace type pre-registration leaking into the
/// enclosing scope.
/// </summary>
public class DeclarationIdentityTests
{
    [Fact]
    public void SameNameClasses_InDifferentNamespaces_DoNotShareCompatVerdicts()
    {
        // Module 1's `s = t` is a genuine error (sibling field types); module 2's `s = t` is
        // fine (Derived2 extends Base). With name-keyed cache entries, module 1's verdict
        // answered for module 2.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            module OnlyDerived {
                class Base { foo: string; }
                class Derived extends Base { bar: string; }
                class Derived2 extends Base { baz: string; }
                class S { foo: Derived; }
                class T { foo: Derived2; }
                var s: S;
                var t: T;
                s = t;
            }
            """));
        TestHarness.RunInterpreted("""
            module OnlyDerived {
                class Base { foo: string; }
                class Derived extends Base { bar: string; }
                class Derived2 extends Base { baz: string; }
                class S { foo: Derived; }
                class T { foo: Derived2; }
                var s: S;
                var t: T;
            }
            module WithBase {
                class Base { foo: string; }
                class Derived2 extends Base { baz: string; }
                class S { foo: Base; }
                class T { foo: Derived2; }
                var s: S;
                var t: T;
                s = t;
            }
            """);
    }

    [Fact]
    public void SameNameInterfaces_InDifferentNamespaces_BindLocally()
    {
        // A same-named interface in an earlier namespace must not capture the later
        // namespace's references (including the recursive self-reference in S2's own body).
        TestHarness.RunInterpreted("""
            module SimpleTypes {
                interface S2 { foo: string; }
            }
            module ObjectTypes {
                class S { foo: S; }
                var s: S;
                interface S2 { foo: S2; }
                var s2: S2;
                s = s2;
            }
            """);
    }

    [Fact]
    public void RecursiveInterfaceSelfReference_InsideNamespace_Resolves()
    {
        TestHarness.RunInterpreted("""
            module M {
                interface Tree { value: number; left: Tree; }
                class Node2 { value: number; left: Node2; }
                var t: Tree;
                var n: Node2;
                t = n;
                n = t;
            }
            """);
    }
}
