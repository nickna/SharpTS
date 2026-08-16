using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Inheritance member-compatibility diagnostics for the <c>extends</c> clause, mirroring tsc on the
/// <c>subtypesAndSuperTypes/subtypingWithObjectMembers*</c> conformance tests:
/// <list type="bullet">
/// <item><b>TS2416</b> — a derived class property whose type is not assignable to the base property
/// it overrides ("Property 'X' in type 'D' is not assignable to the same property in base type 'B'").</item>
/// <item><b>TS2415</b> — a derived class that overrides a base member with mismatched accessibility
/// ("Class 'D' incorrectly extends base class 'B'").</item>
/// <item><b>TS2430</b> — a derived interface that makes a base-required member optional.</item>
/// </list>
/// </summary>
public class InheritanceMemberCompatibilityTests
{
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var parseResult = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess);
        return new TypeChecker(maxErrors: 50).CheckWithRecovery(parseResult.Statements).Diagnostics;
    }

    private static void AssertHasCode(IReadOnlyList<Diagnostic> diags, string tsCode) =>
        Assert.Contains(diags, d => d.TsCode == tsCode);

    private static void AssertNoCode(IReadOnlyList<Diagnostic> diags, string tsCode) =>
        Assert.DoesNotContain(diags, d => d.TsCode == tsCode);

    // ---- TS2416: derived property type not assignable to base ----

    [Fact]
    public void DerivedFieldIncompatibleWithBase_ReportsTS2416()
    {
        var diags = Check("""
            class Base { foo: string; }
            class A { bar: Base; }
            class B extends A { bar: string; }
            """);
        AssertHasCode(diags, "TS2416");
    }

    [Fact]
    public void DerivedFieldNarrowsToSubtype_IsAccepted()
    {
        var diags = Check("""
            class Base { foo: string; }
            class Derived extends Base { bar: string; }
            class A { foo: Base; }
            class B extends A { foo: Derived; }
            """);
        AssertNoCode(diags, "TS2416");
        AssertNoCode(diags, "TS2415");
    }

    [Fact]
    public void DerivedFieldSameType_IsAccepted()
    {
        var diags = Check("""
            class A { foo: number; }
            class B extends A { foo: number; }
            """);
        AssertNoCode(diags, "TS2416");
    }

    [Fact]
    public void NonOverridingMember_IsNotChecked()
    {
        // `baz` exists only on the derived class — nothing to relate against.
        var diags = Check("""
            class A { foo: number; }
            class B extends A { baz: string; }
            """);
        AssertNoCode(diags, "TS2416");
        AssertNoCode(diags, "TS2415");
    }

    [Fact]
    public void BaseMemberTypedUndefined_AcceptsAnyOverride()
    {
        // `typeof undefined` widens to `any` in the default (non-strict) configuration, so overriding
        // it with a concrete type must not error (undefinedIsSubtypeOfEverything).
        var diags = Check("""
            class Base { foo: typeof undefined; }
            class D extends Base { foo: string; }
            """);
        AssertNoCode(diags, "TS2416");
    }

    // ---- TS2415: accessibility mismatch across an override ----

    [Fact]
    public void DerivedMakesPublicMemberPrivate_ReportsTS2415()
    {
        var diags = Check("""
            class A { public foo: number; }
            class B extends A { private foo: number; }
            """);
        AssertHasCode(diags, "TS2415");
    }

    [Fact]
    public void DerivedMakesPrivateMemberPublic_ReportsTS2415()
    {
        var diags = Check("""
            class A { private foo: number; }
            class B extends A { foo: number; }
            """);
        AssertHasCode(diags, "TS2415");
    }

    [Fact]
    public void SameAccessibility_IsAccepted()
    {
        var diags = Check("""
            class A { public foo: number; }
            class B extends A { public foo: number; }
            """);
        AssertNoCode(diags, "TS2415");
    }

    // ---- TS2415: construct-signature-valued index signature across a generic base (#896) ----

    [Fact]
    public void GenericIndexConstructSignature_TooManyRequiredParams_ReportsTS2415()
    {
        // The base's index value `new () => T` reaches the override check via StringIndexOf, which
        // substitutes A<T>'s argument. Substitution must keep the construct signature so the derived
        // `new (a: T) => T` (one more required param) is flagged TS2415 under generics.
        var diags = Check("""
            class A<T> { [k: string]: new () => T; }
            class B<T> extends A<T> { [k: string]: new (a: T) => T; }
            """);
        AssertHasCode(diags, "TS2415");
    }

    [Fact]
    public void GenericIndexConstructSignature_SameArity_IsAccepted()
    {
        var diags = Check("""
            class A<T> { [k: string]: new () => T; }
            class B<T> extends A<T> { [k: string]: new () => T; }
            """);
        AssertNoCode(diags, "TS2415");
    }

    // ---- TS2430: derived interface makes a base-required member optional ----

    [Fact]
    public void DerivedInterfaceMakesRequiredMemberOptional_ReportsTS2430()
    {
        var diags = Check("""
            class Base { foo: string; }
            interface T { Foo: Base; }
            interface S extends T { Foo?: Base; }
            """);
        AssertHasCode(diags, "TS2430");
    }

    [Fact]
    public void DerivedInterfaceKeepsMemberRequired_IsAccepted()
    {
        var diags = Check("""
            class Base { foo: string; }
            interface T { Foo: Base; }
            interface S extends T { Foo: Base; }
            """);
        AssertNoCode(diags, "TS2430");
    }

    [Fact]
    public void BaseInterfaceMemberOptional_DerivedOptional_IsAccepted()
    {
        var diags = Check("""
            class Base { foo: string; }
            interface T { Foo?: Base; }
            interface S extends T { Foo?: Base; }
            """);
        AssertNoCode(diags, "TS2430");
    }

    // ---- TS2416 under generics (#898): the gate was previously skipped for generic classes ----
    // Mirrors the `subtypesOfTypeParameterWithConstraints` conformance cases over base `C3<T>{foo:T}`.

    [Fact]
    public void GenericOverride_IncompatibleTypeParameter_ReportsTS2416()
    {
        // D3: extends C3<T>, foo: U. The base property resolves to the derived `T`; `U` is not
        // assignable to `T` (T extends U, not the reverse).
        var diags = Check("""
            class C3<T> { foo: T; }
            class D3<T extends U, U> extends C3<T> {
                foo: U;
            }
            """);
        AssertHasCode(diags, "TS2416");
    }

    [Fact]
    public void GenericOverride_SameTypeParameter_IsAccepted()
    {
        // D1: extends C3<T>, foo: T — exact match.
        var diags = Check("""
            class C3<T> { foo: T; }
            class D1<T extends U, U> extends C3<T> {
                foo: T;
            }
            """);
        AssertNoCode(diags, "TS2416");
    }

    [Fact]
    public void GenericOverride_SubstitutedBaseArg_IsAccepted()
    {
        // D4: extends C3<U>, foo: U. The base property must be substituted to the derived `U`
        // (not C3's own `T`); `U` is assignable to `U`. Guards against the substitution false-positive.
        var diags = Check("""
            class C3<T> { foo: T; }
            class D4<T extends U, U> extends C3<U> {
                foo: U;
            }
            """);
        AssertNoCode(diags, "TS2416");
    }

    [Fact]
    public void GenericOverride_TransitiveConstraintToBaseArg_IsAccepted()
    {
        // D2: extends C3<U>, foo: T where T extends U — T is assignable to the base's U.
        var diags = Check("""
            class C3<T> { foo: T; }
            class D2<T extends U, U> extends C3<U> {
                foo: T;
            }
            """);
        AssertNoCode(diags, "TS2416");
    }

    [Fact]
    public void GenericOverride_ConcreteIncompatibleWithBaseTypeParameter_ReportsTS2416()
    {
        // D27-style: extends C3<T>, foo: Date — a concrete type can't satisfy the open `T`
        // (T could be instantiated with a different subtype of its constraint).
        var diags = Check("""
            class C3<T> { foo: T; }
            class D27<T extends U, U extends V, V extends Date> extends C3<T> {
                foo: Date;
            }
            """);
        AssertHasCode(diags, "TS2416");
    }

    [Fact]
    public void GenericOverride_NarrowingToConstraint_IsAccepted()
    {
        // D14: extends C3<Date>, foo: T where T's apparent type is Date — assignable to Date.
        var diags = Check("""
            class C3<T> { foo: T; }
            class D14<T extends U, U extends V, V extends Date> extends C3<Date> {
                foo: T;
            }
            """);
        AssertNoCode(diags, "TS2416");
    }
}
