using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for <c>Record&lt;E, V&gt;</c> where <c>E</c> is an enum (#183): the mapped type expands to
/// one required property per enum member (keyed by the member's value), so an object missing a
/// member key is a missing-property error — matching tsc's behavior on
/// <c>assignmentCompatWithEnumIndexer</c>. Index signatures are kept alongside so <c>x[E.A]</c>
/// still reads/writes as <c>V</c>.
/// </summary>
public class RecordEnumKeyTests
{
    [Fact]
    public void EmptyObject_AssignedToRecordOverEnum_IsMissingPropertyError()
    {
        var source = """
            enum E { A }
            let foo: Record<E, any> = {};
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ObjectMissingOneEnumMemberKey_IsError()
    {
        var source = """
            enum E { A, B }
            let foo: Record<E, string> = { 0: "a" };
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ObjectWithAllNumericEnumMemberKeys_IsValid()
    {
        var source = """
            enum E { A, B }
            let foo: Record<E, string> = { 0: "a", 1: "b" };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StringEnum_RequiresMemberValueKeys()
    {
        var source = """
            enum S { X = "x", Y = "y" }
            let foo: Record<S, number> = { x: 1 };
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void StringEnum_AllMemberValueKeys_IsValid()
    {
        var source = """
            enum S { X = "x", Y = "y" }
            let foo: Record<S, number> = { x: 1, y: 2 };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void IndexingRecordOverEnum_ByEnumMember_StillWorks()
    {
        var source = """
            enum E { A, B }
            let foo: Record<E, string> = { 0: "a", 1: "b" };
            let v: string = foo[E.A];
            foo[E.B] = "c";
            enum S { X = "x" }
            let bar: Record<S, number> = { x: 1 };
            let n: number = bar[S.X];
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void RecordOverEnum_RendersMemberKeys_NotBareBraces()
    {
        // Distinct Record<enum, V> expansions must not collide in the compatibility cache
        // (which keys on ToString) — see the analogous IndexSignatureCacheTests.
        var source = """
            enum E { A }
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            function f(b: Record<E, Base>, d: Record<E, Derived>) {
                var a: Record<E, Derived>;
                a = b;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}
