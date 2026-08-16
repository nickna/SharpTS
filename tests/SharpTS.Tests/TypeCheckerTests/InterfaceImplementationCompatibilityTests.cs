using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Member-compatibility diagnostics for the class <c>implements</c> clause, mirroring tsc on the
/// <c>subtypesAndSuperTypes/subtypingWith{NumericIndexer,ObjectMembers}5</c> conformance tests (#897):
/// <list type="bullet">
/// <item><b>TS2420</b> — index-signature variant: a class index signature whose value type is not
/// assignable to the implemented interface's (incl. the string-index-covers-numeric-keys rule and
/// generic classes where the interface index resolves to an open type parameter).</item>
/// <item><b>TS2559</b> — a class that shares no properties with an all-optional ("weak") interface.</item>
/// <item>Throw-and-abort recovery: a failing <c>implements</c> records at the class-name line and
/// lets sibling declarations inside a <c>module</c> still be checked (no line-1 mis-attribution).</item>
/// </list>
/// </summary>
public class InterfaceImplementationCompatibilityTests
{
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var parseResult = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess);
        return new TypeChecker(maxErrors: 50).CheckWithRecovery(parseResult.Statements).Diagnostics;
    }

    private static void AssertNoCode(IReadOnlyList<Diagnostic> diags, string tsCode) =>
        Assert.DoesNotContain(diags, d => d.TsCode == tsCode);

    private static void AssertCodeAtLine(IReadOnlyList<Diagnostic> diags, string tsCode, int line) =>
        Assert.Contains(diags, d => d.TsCode == tsCode && d.Line == line);

    private static void AssertNoCodeAtLine(IReadOnlyList<Diagnostic> diags, string tsCode, int line) =>
        Assert.DoesNotContain(diags, d => d.TsCode == tsCode && d.Line == line);

    // ---- TS2420: index-signature compatibility on the implements path ----

    [Fact]
    public void StringIndexCoveringNumericYieldsSupertype_ReportsTS2420()
    {
        // [x:string]:Base covers numeric keys, so it supplies `Base` for the interface's
        // [x:number]:Derived — and Base is not assignable to Derived.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A { [x: number]: Derived; }
            class B implements A { [x: string]: Base; }
            """);
        AssertCodeAtLine(diags, "TS2420", 4);
    }

    [Fact]
    public void StringIndexCoveringNumericYieldsSubtype_IsAccepted()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A { [x: number]: Base; }
            class B implements A { [x: string]: Derived; }
            """);
        AssertNoCode(diags, "TS2420");
    }

    [Fact]
    public void NonGenericClassImplementsGenericInterface_CompatibleIndex_IsAccepted()
    {
        // A<Base> ⇒ [x:number]:Base; class [x:string]:Derived supplies Derived ⇒ assignable to Base.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: number]: T; }
            class B implements A<Base> { [x: string]: Derived; }
            """);
        AssertNoCode(diags, "TS2420");
    }

    [Fact]
    public void GenericClassIndexAgainstOpenTypeParameter_ReportsTS2420()
    {
        // The interface index resolves to the class's open `T`; a concrete `Base` is not assignable
        // to a bare `T` (T could be instantiated with a different subtype of its constraint).
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: number]: T; }
            class B<T extends Derived> implements A<T> {
                [x: string]: Base;
            }
            """);
        AssertCodeAtLine(diags, "TS2420", 4);
    }

    [Fact]
    public void GenericClassConcreteIndexEqualToConstraint_StillReportsTS2420()
    {
        // Even `Derived` (the constraint) is not assignable to the bare `T` — matches tsc.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: number]: T; }
            class B<T extends Derived> implements A<T> {
                [x: string]: Derived;
            }
            """);
        AssertCodeAtLine(diags, "TS2420", 4);
    }

    // ---- TS2559: weak-type (all-optional interface, no properties in common) ----

    [Fact]
    public void ClassSharesNoMembersWithAllOptionalInterface_ReportsTS2559()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface A { foo?: Base; }
            class B implements A { fooo: string; }
            """);
        AssertCodeAtLine(diags, "TS2559", 3);
        AssertNoCode(diags, "TS2420");
    }

    [Fact]
    public void ClassSharesAMemberWithAllOptionalInterface_IsAccepted()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface A { foo?: Base; }
            class B implements A { foo: Base; }
            """);
        AssertNoCode(diags, "TS2559");
    }

    [Fact]
    public void RequiredMemberMissing_ReportsTS2420NotTS2559()
    {
        // A non-weak interface (required member) yields the missing-member TS2420, not the weak-type code.
        var diags = Check("""
            interface Base { foo: string; }
            interface A { foo: Base; }
            class B implements A { fooo: string; }
            """);
        AssertCodeAtLine(diags, "TS2420", 3);
        AssertNoCode(diags, "TS2559");
    }

    // ---- Throw-and-abort recovery: sibling declarations inside a module still get checked ----

    [Fact]
    public void FailingImplementsInModule_DoesNotAbortSiblings_AndUsesClassNameLine()
    {
        var diags = Check("""
            module M {
                interface A { foo: string; }
                class B implements A { x: number; }
                interface A2 { bar: string; }
                class B2 implements A2 { y: number; }
            }
            """);
        // Both failures surface, each at its own class-name line...
        AssertCodeAtLine(diags, "TS2420", 3);
        AssertCodeAtLine(diags, "TS2420", 5);
        // ...and nothing is mis-attributed to the module's line.
        AssertNoCodeAtLine(diags, "TS2420", 1);
    }
}
