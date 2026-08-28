using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Shared call/construct signature relationship rules (#1536).  These cases deliberately exercise
/// the relation through declarations and assignments rather than the TypeScript conformance paths,
/// so a failure identifies the semantic rule instead of a baseline-mapping symptom.
/// </summary>
public class SignatureRelationshipTests
{
    private static IReadOnlyList<Diagnostic> Diagnose(string source)
    {
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        return new TypeChecker(TypeCheckerOptions.Strict with { MaxErrors = 50 })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;
    }

    public static TheoryData<string, string, bool> AssignmentPairs => new()
    {
        { "(x?: number) => number", "(x: number) => number", false },
        { "(x: number) => number", "(x?: number) => number", true },
        { "(x: number, y?: string, ...z: number[]) => number", "(x: number, y: string) => number", false },
        { "(x: number, y?: string, ...z: number[]) => number", "(x: number, ...z: number[]) => number", false },
        { "(x: number) => number[]", "<T>(x: T) => T[]", true },
        { "<T>(x: T) => T[]", "(x: number) => number[]", false },
        { "new (x?: number) => number", "new (x: number) => number", false },
        { "new (x: number) => number", "new (x?: number) => number", true },
        { "(x: number) => number", "new (x: number) => number", false },
        { "new (x: number) => number", "(x: number) => number", false },
        { "{ new (x: new (a: number) => number): number[]; new (x: new (a: string) => string): string[] }", "new <T>(x: new (a: T) => T) => T[]", true },
        { "new <T>(x: new (a: T) => T) => T[]", "{ new (x: new (a: number) => number): number[]; new (x: new (a: string) => string): string[] }", true },
        { "{ new (x: { new (a: number): number; new (a: string): string }): any[]; new (x: { new (a: boolean): boolean; new (a: Date): Date }): any[] }", "new <T>(x: new (a: T) => T) => T[]", true },
        { "new <T>(x: new (a: T) => T) => T[]", "{ new (x: { new (a: number): number; new (a: string): string }): any[]; new (x: { new (a: boolean): boolean; new (a: Date): Date }): any[] }", true },
        { "<S extends { p: string }[]>(x: S) => void", "<T extends { p: string }>(x: T[]) => void", true },
        { "<T extends { p: string }>(x: T[]) => void", "<S extends { p: string }[]>(x: S) => void", true },
    };

    [Theory]
    [MemberData(nameof(AssignmentPairs))]
    public void Assignment_RelatesSignaturePair(
        string targetType,
        string sourceType,
        bool expectedCompatible)
    {
        var diagnostics = Diagnose($$"""
            declare let source: {{sourceType}};
            let target: {{targetType}};
            target = source;
            """);

        Assert.Equal(expectedCompatible, diagnostics.All(d => d.TsCode != "TS2322"));
    }

    [Fact]
    public void OverloadedSource_MustCoverEveryTargetSignature()
    {
        var diagnostics = Diagnose("""
            declare let source: { (x: number): number; (x: boolean): number };
            let target: { (x: number): number; (x: string): number };
            target = source;
            """);

        Assert.Contains(diagnostics, d => d.TsCode == "TS2322");
    }

    public static TheoryData<string, string, bool> MemberPairs => new()
    {
        { "(x: number) => number[]", "<T>(x: T) => T[]", true },
        { "(x: number) => string[]", "<T>(x: T) => string[]", true },
        { "(x: number) => void", "<T>(x: T) => T", true },
        { "(x: string, y: number) => string", "<T, U>(x: T, y: U) => T", true },
        { "(x: (arg: string) => number) => string", "<T, U>(x: (arg: T) => U) => T", true },
        { "(x: (arg: Base) => Derived) => Base", "<T extends Base, U extends Derived>(x: (arg: T) => U) => T", true },
        { "(x: (arg: Base) => Derived) => (r: Base) => Derived", "<T extends Base, U extends Derived>(x: (arg: T) => U) => (r: T) => U", true },
        { "(x: (arg: Base) => Derived, y: (arg: Base) => Derived) => (r: Base) => Derived", "<T extends Base, U extends Derived>(x: (arg: T) => U, y: (arg: T) => U) => (r: T) => U", true },
        { "(x: (arg: Base) => Derived, y: (arg: Base) => Derived) => (r: Base) => Derived", "<T extends Base, U extends Derived>(x: (arg: T) => U, y: (arg: { foo: string; bing: number }) => U) => (r: T) => U", true },
        { "(...x: Derived[]) => Derived", "<T extends Derived>(...x: T[]) => T", true },
        { "(x: { foo: string }, y: { foo: string; bar: string }) => Base", "<T extends Base>(x: T, y: T) => T", true },
        { "(x: Array<Base>, y: Array<Derived2>) => Array<Derived>", "<T extends Array<Base>>(x: Array<Base>, y: T) => Array<Derived>", true },
        { "(x: Array<Base>, y: Array<Derived>) => Array<Derived>", "<T extends Array<Derived>>(x: Array<Base>, y: T) => T", true },
        { "(x: { a: string; b: number }) => Object", "<T, U>(x: { a: T; b: U }) => T", true },
        { "<T>(x: T) => T[]", "<U, V>(x: { a: U; b: V }) => U[]", false },
        { "(x: number) => string[]", "<T, U>(x: T) => U[]", true },
        { "<T extends Base>(x: { a: T; b: T }) => T[]", "<T>(x: { a: T; b: T }) => T[]", true },
        { "<T>(x: T) => T[]", "<T>(x: T) => string[]", false },
        { "{ (x: number): number[]; (x: string): string[] }", "<T>(x: T) => T[]", true },
        { "{ <T extends Derived>(x: T): number[]; <U extends Base>(x: U): number[] }", "<T extends Base>(x: T) => number[]", true },
        { "{ (x: (a: number) => number): number[]; (x: (a: string) => string): string[] }", "<T>(x: (a: T) => T) => T[]", true },
        { "{ (x: { (a: number): number; (a: string): string }): any[]; (x: { (a: boolean): boolean; (a: Date): Date }): any[] }", "<T>(x: (a: T) => T) => T[]", true },
    };

    [Theory]
    [MemberData(nameof(MemberPairs))]
    public void InterfaceMemberInheritance_RelatesSignaturePair(
        string baseType,
        string derivedType,
        bool expectedCompatible)
    {
        var diagnostics = Diagnose($$"""
            class Base { foo: string = ""; }
            class Derived extends Base { bar: string = ""; }
            class Derived2 extends Derived { baz: string = ""; }
            interface Target { member: {{baseType}} }
            interface Source extends Target { member: {{derivedType}} }
            """);

        bool actualCompatible = diagnostics.All(d => d.TsCode != "TS2430");
        Assert.True(
            actualCompatible == expectedCompatible,
            string.Join(Environment.NewLine, diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void NamespaceConstructSignatureVariable_IsCheckedForDefiniteAssignment()
    {
        var diagnostics = Diagnose("""
            namespace N {
                declare function use1(value: new (x: number) => void): typeof value;
                declare function use1(value: any): any;
                var ctor1: new (x: number) => number;
                use1(ctor1);
                var ctor2: new <T>(x: T) => string;
                use1(ctor2);

                declare function use2(value: new (x: number, y: number) => void): typeof value;
                declare function use2(value: any): any;
                var ctor3: new (x: number, y: number) => number;
                use2(ctor3);
                var ctor4: new <T>(x: T) => string;
                use2(ctor4);
            }
            """);

        Assert.Equal(4, diagnostics.Count(d => d.TsCode == "TS2454"));
    }

    [Fact]
    public void AssertionToUnconstrainedTypeParameter_HasSufficientOverlap()
    {
        var diagnostics = Diagnose("""
            function unconstrained<T>() { return <T>null; }
            function constrained<T extends { p: string }>() { return <T>null; }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.TsCode == "TS2352");
        Assert.Equal(2, diagnostic.Line);
    }

    [Theory]
    [InlineData("(x: number): void", "(x: number): number")]
    [InlineData("new (x: number): void", "new (x: number): number")]
    public void DirectSignatureInheritance_VoidTargetAcceptsReturnedValue(
        string baseSignature,
        string derivedSignature)
    {
        var diagnostics = Diagnose($$"""
            interface Base { {{baseSignature}} }
            interface Derived extends Base { {{derivedSignature}} }
            """);

        Assert.DoesNotContain(diagnostics, d => d.TsCode == "TS2430");
    }

    [Theory]
    [InlineData("(x: number): number", "(x: number): string")]
    [InlineData("new (x: number): number", "new (x: number): string")]
    public void DirectSignatureInheritance_AddsAnOverload(
        string baseSignature,
        string derivedSignature)
    {
        var diagnostics = Diagnose($$"""
            interface Base { {{baseSignature}} }
            interface Derived extends Base { {{derivedSignature}} }
            """);

        Assert.DoesNotContain(diagnostics, d => d.TsCode == "TS2430");
    }
}
