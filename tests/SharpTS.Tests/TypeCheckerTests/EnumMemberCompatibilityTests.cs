using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EnumMemberCompatibilityTests
{
    [Fact]
    public void SameNamedEnumsCompareTheirMemberValuesAndDeclaration()
    {
        var statements = TestHarness.ParseOrThrow("""
            namespace First { export enum E { A = 1 } }
            namespace Same { export enum E { A = 1 } }
            namespace Second { export enum E { A = 2 } }
            namespace Extra { export enum E { A = 1, B = 2 } }
            namespace ConstFirst { export const enum E { A = 1 } }
            namespace ConstSecond { export const enum E { A = 1 } }
            declare function same(value: Same.E): void;
            declare function second(value: Second.E): void;
            declare function first(value: First.E): void;
            declare function extra(value: Extra.E): void;
            declare function sameConst(value: ConstFirst.E): void;
            declare function firstObject(value: typeof First.E): void;
            same(First.E.A);
            second(First.E.A);
            first(First.E.A);
            extra(First.E.A);
            first(Extra.E.A);
            sameConst(ConstFirst.E.A);
            sameConst(ConstSecond.E.A);
            firstObject(Same.E);
            firstObject(Second.E);
            """);
        var diagnostics = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics;

        Assert.Equal(new[] { 14, 17, 19, 21 }, diagnostics.Select(diagnostic => diagnostic.Line));
        Assert.All(diagnostics, diagnostic => Assert.Equal("TS2345", diagnostic.TsCode));
    }

    [Fact]
    public void ConstantNumericInitializersParticipateInCompatibility()
    {
        var statements = TestHarness.ParseOrThrow("""
            namespace Computed { export enum E { Base = 1, A = Base << 1 } }
            namespace Same { export enum E { Base = 1, A = 2 } }
            namespace Different { export enum E { Base = 1, A = 3 } }
            declare function same(value: Same.E): void;
            declare function different(value: Different.E): void;
            same(Computed.E.A);
            different(Computed.E.A);
            """);
        var diagnostic = Assert.Single(
            new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);

        Assert.Equal(7, diagnostic.Line);
        Assert.Equal("TS2345", diagnostic.TsCode);
    }

    [Fact]
    public void OpaqueAmbientMembersCanMatchKnownMembersInEnumUnions()
    {
        var statements = TestHarness.ParseOrThrow("""
            declare namespace Ambient { enum E { A, B = 19 } }
            namespace Known { export enum E { A = 17, B = 19 } }
            declare let ambient: Ambient.E;
            declare let known: Known.E;
            ambient = known;
            known = ambient;
            """);

        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }

    [Fact]
    public void MergedConstEnumDeclarationsKeepTheSameBinding()
    {
        var statements = TestHarness.ParseOrThrow("""
            const enum E { A = 1 }
            const captured: E = E.A;
            const enum E { B = 2 }
            const result: E = captured;
            """);

        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
    }

    [Fact]
    public void RecordCompatibilityCacheDistinguishesSameNamedEnumFields()
    {
        var statements = TestHarness.ParseOrThrow("""
            namespace First { export enum E { A = 1 } }
            namespace Same { export enum E { A = 1 } }
            namespace Different { export enum E { A = 2 } }
            declare function take(x: { value: First.E }): void;
            declare const same: { value: Same.E };
            declare const different: { value: Different.E };
            take(same);
            take(different);
            """);

        var diagnostic = Assert.Single(
            new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal(8, diagnostic.Line);
        Assert.Equal("TS2345", diagnostic.TsCode);
    }

    [Fact]
    public void RecordsWithEquivalentInterfaceFieldsCollapseInUnions()
    {
        var statements = TestHarness.ParseOrThrow("""
            interface Value { member: number }
            type Left = { field: Partial<Value> }
            type Right = { field: Partial<Value> }
            declare const combined: Left | Right;
            const wrong: number = combined;
            """);

        var diagnostic = Assert.Single(
            new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.DoesNotContain(" | ", diagnostic.Message);
    }

    [Fact]
    public void HoistedFunctionSignatureRetainsKnownEnumValues()
    {
        var statements = TestHarness.ParseOrThrow("""
            enum E { A = 1 }
            namespace Same { export enum E { A = 1 } }
            namespace Different { export enum E { A = 2 } }
            function take(value: E): E { return value; }
            take(Same.E.A);
            take(Different.E.A);
            """);

        var diagnostic = Assert.Single(
            new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal(6, diagnostic.Line);
        Assert.Equal("TS2345", diagnostic.TsCode);
    }

    [Fact]
    public void NamespacesInAmbientBlocksKeepEnumMembersAmbient()
    {
        var statements = TestHarness.ParseOrThrow("""
            declare module "ambient" { export namespace A { enum E { Unknown } } }
            declare global { namespace B { enum E { Unknown } } }
            """);

        var module = Assert.IsType<Stmt.DeclareModule>(statements[0]);
        var exported = Assert.IsType<Stmt.Export>(Assert.Single(module.Members));
        var moduleNamespace = Assert.IsType<Stmt.Namespace>(exported.Declaration);
        var global = Assert.IsType<Stmt.DeclareGlobal>(statements[1]);
        var globalNamespace = Assert.IsType<Stmt.Namespace>(Assert.Single(global.Members));
        Assert.True(Assert.IsType<Stmt.Enum>(Assert.Single(moduleNamespace.Members)).IsAmbient);
        Assert.True(Assert.IsType<Stmt.Enum>(Assert.Single(globalNamespace.Members)).IsAmbient);
    }
}
