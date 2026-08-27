using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class SymbolSemanticsTests
{
    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        return new TypeChecker(new TypeCheckerOptions
            {
                StrictNullChecks = true,
                StrictPropertyInitialization = true,
                CheckVariableUseBeforeAssignment = true,
                MaxErrors = 50,
            })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;
    }

    private static void AssertCodeAtLine(
        IReadOnlyList<Diagnostic> diagnostics,
        string tsCode,
        int line) =>
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == tsCode && diagnostic.Line == line);

    private static void AssertNoCode(
        IReadOnlyList<Diagnostic> diagnostics,
        string tsCode) =>
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == tsCode);

    [Fact]
    public void SymbolPrimitiveAndWrapperRelateInOnlyThePrimitiveToWrapperDirection()
    {
        var diagnostics = Check("""
            interface Symbol { }
            declare var primitive: symbol;
            var wrapper: Symbol;
            wrapper = primitive;
            primitive = wrapper;
            """);

        AssertCodeAtLine(diagnostics, "TS2322", 5);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == "TS2322" && diagnostic.Line == 4);
        AssertNoCode(diagnostics, "TS2454");
    }

    [Fact]
    public void SymbolComparisonsRejectOnlyDisjointOperands()
    {
        var diagnostics = Check("""
            const value = Symbol();
            value === value;
            value === 1;
            false !== value;
            """);

        AssertCodeAtLine(diagnostics, "TS2367", 3);
        AssertCodeAtLine(diagnostics, "TS2367", 4);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.TsCode == "TS2367"));
    }

    [Fact]
    public void LooseNullComparisonOverlapsAUnionContainingUndefined()
    {
        var diagnostics = Check("""
            declare const eventName: string | symbol | undefined;
            eventName == null;
            eventName === null;
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == "TS2367" && diagnostic.Line == 2);
        AssertCodeAtLine(diagnostics, "TS2367", 3);
    }

    [Fact]
    public void InstanceOfRejectsSymbolOperandsIncludingAUnionWithAnObject()
    {
        var diagnostics = Check("""
            Symbol() instanceof Symbol;
            Symbol instanceof Symbol();
            (Symbol() || {}) instanceof Object;
            """);

        AssertCodeAtLine(diagnostics, "TS2358", 1);
        AssertCodeAtLine(diagnostics, "TS2359", 2);
        AssertCodeAtLine(diagnostics, "TS2358", 3);
    }

    [Fact]
    public void ComputedSymbolAccessorUsesSetterTypeForReadsAndWrites()
    {
        var diagnostics = Check("""
            class C {
                get [Symbol.hasInstance]() { return ""; }
                set [Symbol.hasInstance](value: number) { }
            }
            new C()[Symbol.hasInstance] = 0;
            new C()[Symbol.hasInstance] = "";
            """);

        AssertCodeAtLine(diagnostics, "TS2322", 2);
        AssertCodeAtLine(diagnostics, "TS2322", 6);
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.TsCode == "TS2322"));
    }

    [Fact]
    public void ComputedSymbolMemberParticipatesInImplementsAndExcessPropertyChecks()
    {
        var diagnostics = Check("""
            interface I { [Symbol.toPrimitive]: () => boolean; }
            class C implements I { [Symbol.toPrimitive]() { return ""; } }
            """);

        AssertCodeAtLine(diagnostics, "TS2416", 2);
    }

    [Fact]
    public void GenericComputedSymbolInterfaceRetainsItsKeysForExcessPropertyChecks()
    {
        var diagnostics = Check("""
            interface I<T, U> {
                [Symbol.unscopables]: T;
                [Symbol.isConcatSpreadable]: U;
            }
            declare function use<T, U>(value: I<T, U>): void;
            use({
                [Symbol.isConcatSpreadable]: "",
                [Symbol.toPrimitive]: 0,
                [Symbol.unscopables]: true
            });
            """);

        AssertCodeAtLine(diagnostics, "TS2353", 8);
    }

    [Fact]
    public void ComputedInterfaceKeyRequiresAPropertyKeyType()
    {
        var diagnostics = Check("""
            interface I { [Symbol.keyFor]: string; }
            interface J { [Symbol.iterator]: string; }
            """);

        AssertCodeAtLine(diagnostics, "TS2464", 1);
        AssertNoCode(diagnostics, "TS1169");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Line == 2);
    }

    [Fact]
    public void GlobalSymbolAugmentationIsVisibleToComputedMembers()
    {
        var diagnostics = Check("""
            interface Symbol { wrapper: true; }
            interface SymbolConstructor { readonly custom: symbol; }
            declare var Symbol: SymbolConstructor;
            const custom: typeof Symbol.custom = Symbol.custom;
            class C { [custom]() { return 1; } }
            type T = { [Symbol.custom]: () => number };
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ShadowedSymbolConstructorDoesNotAliasGlobalWellKnownSymbol()
    {
        var diagnostics = Check("""
            interface SymbolConstructor { readonly iterator: symbol; }
            const object = { [Symbol.iterator]: 0 };
            namespace Local {
                var Symbol: SymbolConstructor;
                object[Symbol.iterator];
            }
            """);

        AssertCodeAtLine(diagnostics, "TS2454", 5);
        AssertNoCode(diagnostics, "TS2339");
    }

    [Fact]
    public void SymbolUnionReadBeforeAssignmentIsReportedAcrossTypeofBranches()
    {
        var diagnostics = Check("""
            enum E { }
            var value: symbol | E;
            value;
            if (typeof value === "number") {
                value;
            }
            else {
                value;
            }
            """);

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.TsCode == "TS2454"));
        AssertCodeAtLine(diagnostics, "TS2454", 3);
        AssertCodeAtLine(diagnostics, "TS2454", 4);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == "TS2454" && diagnostic.Line == 5);
        AssertCodeAtLine(diagnostics, "TS2454", 8);
    }

    [Fact]
    public void NamespaceComputedSymbolMembersPreserveTheirDeclaredInterfaceType()
    {
        var diagnostics = Check("""
            namespace M {
                interface I { }
                export class C {
                    [Symbol.iterator]: I;
                    [Symbol.toPrimitive](x: I) { }
                    [Symbol.isConcatSpreadable](): I { return undefined; }
                    get [Symbol.toPrimitive]() { return undefined; }
                    set [Symbol.toPrimitive](x: I) { }
                }
            }
            """);

        AssertCodeAtLine(diagnostics, "TS2564", 4);
        AssertCodeAtLine(diagnostics, "TS2322", 6);
        AssertCodeAtLine(diagnostics, "TS2322", 7);
        AssertCodeAtLine(diagnostics, "TS2300", 7);
        AssertCodeAtLine(diagnostics, "TS2300", 8);
    }
}
