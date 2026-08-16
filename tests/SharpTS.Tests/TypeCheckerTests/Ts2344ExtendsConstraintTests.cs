using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// #895: a type-argument constraint violation (TS2344) in an <c>extends</c>/superclass clause must be
/// RECORDED at the declaration's line and let checking continue — not throw line-less and abort the
/// enclosing <c>module</c>/block. Mirrors the <c>subtypingWith{Numeric,String}Indexer2/3/4</c>
/// conformance cluster. Also covers the interface-extends index-signature TS2430 check that those
/// interface cases additionally depend on. Uses <see cref="TypeChecker.CheckWithRecovery"/> (the
/// pipeline's recovery path) and asserts on <c>(line, TsCode)</c> diagnostics — never on a throw.
/// </summary>
public class Ts2344ExtendsConstraintTests
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

    private static void AssertHasCodeAtLine(IReadOnlyList<Diagnostic> diags, string tsCode, int line) =>
        Assert.Contains(diags, d => d.TsCode == tsCode && d.Line == line);

    // ---- TS2344 in a generic class `extends` clause inside a module ----

    [Fact]
    public void GenericClassExtendsConstraintViolationInModule_RecordsTS2344AtDeclLine_AndChecksSiblings()
    {
        // The TS2344 must land on `class B` (line 5), NOT line 1, and the throw must not abort the
        // module — so `B3`'s incompatible index signature (TS2415) is still reported (line 6).
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            module M {
                class A<T extends Derived> { [x: number]: T; }
                class B extends A<Base> { [x: number]: Derived; }
                class B3<T extends Derived> extends A<T> { [x: number]: Base; }
            }
            """);
        AssertHasCodeAtLine(diags, "TS2344", 5);
        AssertHasCodeAtLine(diags, "TS2415", 6);
    }

    [Fact]
    public void GenericClassExtendsConstraintViolationInModule_DoesNotReportAtLine1()
    {
        // Regression guard for the original bug: the line-less throw fell back to line 1.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            module M {
                class A<T extends Derived> { [x: number]: T; }
                class B extends A<Base> { [x: number]: Derived; }
                class B3<T extends Derived> extends A<T> { [x: number]: Base; }
            }
            """);
        AssertHasCodeAtLine(diags, "TS2344", 5);
        AssertHasCodeAtLine(diags, "TS2415", 6); // sibling B3 still checked
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2344" && d.Line == 1);
    }

    [Fact]
    public void ClassExtendsConstraintViolation_StillRunsOwnIndexSigCheck_BothCodesAtDeclLine()
    {
        // indexer4-style: `B`'s own index signature (string) is also incompatible with the base's.
        // Because the TS2344 no longer aborts B's check, BOTH TS2344 and TS2415 surface, at line 4.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            class A<T extends Derived> { [x: number]: T; }
            class B extends A<Base> { [x: number]: string; }
            """);
        AssertHasCodeAtLine(diags, "TS2344", 4);
        AssertHasCodeAtLine(diags, "TS2415", 4);
    }

    [Fact]
    public void GenericClassExtendsSatisfiedConstraint_NoTS2344()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            class A<T extends Base> { value: T; }
            class B extends A<Derived> { }
            """);
        AssertNoCode(diags, "TS2344");
    }

    // ---- TS2344 in a generic interface `extends` clause inside a module ----

    [Fact]
    public void GenericInterfaceExtendsConstraintViolationInModule_RecordsTS2344_AndChecksSiblings()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            module M {
                interface A<T extends Derived> { [x: number]: T; }
                interface B extends A<Base> { [x: number]: Derived; }
                interface B3<T extends Derived> extends A<T> { [x: number]: Base; }
            }
            """);
        AssertHasCodeAtLine(diags, "TS2344", 5);
        AssertHasCodeAtLine(diags, "TS2430", 6); // sibling B3 index-sig still checked
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2344" && d.Line == 1);
    }

    // ---- TS2430 interface-extends index-signature compatibility (Part B) ----

    [Fact]
    public void InterfaceExtendsIncompatibleIndexSignature_ReportsTS2430()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A { [x: number]: Derived; }
            interface B extends A { [x: number]: Base; }
            """);
        AssertHasCodeAtLine(diags, "TS2430", 4);
    }

    [Fact]
    public void GenericInterfaceExtendsIncompatibleIndexSignature_ReportsTS2430()
    {
        // base index resolves to the open `T`; a concrete `Base` cannot satisfy it.
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A<T extends Base> { [x: number]: T; }
            interface B3<T extends Base> extends A<T> { [x: number]: Base; }
            """);
        AssertHasCode(diags, "TS2430");
        AssertNoCode(diags, "TS2344");
    }

    [Fact]
    public void InterfaceExtendsCompatibleIndexSignature_IsAccepted()
    {
        var diags = Check("""
            interface Base { foo: string; }
            interface Derived extends Base { bar: string; }
            interface A { [x: number]: Base; }
            interface B extends A { [x: number]: Derived; }
            """);
        AssertNoCode(diags, "TS2430");
    }
}
