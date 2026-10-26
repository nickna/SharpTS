using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DefaultLibraryPromiseHeritageTests
{
    // Preserve the selected declarations rather than filling omitted members from
    // an unconditional native Promise surface. The global value stays fully typed.
    private const string Library = """
        interface PromiseLike<T> { then<TResult1 = T, TResult2 = never>(onfulfilled?: ((value: T) => TResult1 | PromiseLike<TResult1>) | null, onrejected?: ((reason: any) => TResult2 | PromiseLike<TResult2>) | null): PromiseLike<TResult1 | TResult2>; }
        interface Promise<T> {
            then<TResult1 = T, TResult2 = never>(onfulfilled?: ((value: T) => TResult1 | PromiseLike<TResult1>) | null, onrejected?: ((reason: any) => TResult2 | PromiseLike<TResult2>) | null): Promise<TResult1 | TResult2>;
            libraryValue: T;
            optional?: string;
            readonly libraryCode: number;
        }
        interface PromiseConstructor {
            new<T>(executor: (resolve: (value: T | PromiseLike<T>) => void, reject: (reason?: any) => void) => void): Promise<T>;
            readonly prototype: Promise<any>;
            readonly libraryVersion: number;
        }
        declare var Promise: PromiseConstructor;
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["typed-declaration", """
            class P extends Promise<number> {}
            const p = new P(resolve => resolve(1));
            const native: Promise<number> = p;
            const mapped: Promise<string> = p.then(value => { const numberValue: number = value; return 'ok'; });
            async function check() { const value: number = await p; }
            """, "", 0];

        yield return ["wrong-executor-declaration", """
            class P extends Promise<number> {}
            new P(resolve => resolve("bad"));
            """, "TS2345", 2];

        yield return ["wrong-then-declaration", """
            class P extends Promise<number> {}
            const p = new P(resolve => resolve(1));
            p.then(value => { const bad: string = value; });
            """, "TS2322", 3];

        yield return ["typed-expression", """
            const P = class extends Promise<number> {};
            const p = new P(resolve => resolve(1));
            const native: Promise<number> = p;
            const mapped: Promise<string> = p.then(value => { const numberValue: number = value; return 'ok'; });
            async function check() { const value: number = await p; }
            """, "", 0];

        yield return ["wrong-executor-expression", """
            const P = class extends Promise<number> {};
            new P(resolve => resolve("bad"));
            """, "TS2345", 2];

        yield return ["wrong-then-expression", """
            const P = class extends Promise<number> {};
            const p = new P(resolve => resolve(1));
            p.then(value => { const bad: string = value; });
            """, "TS2322", 3];

        yield return ["typed-generic-declaration", """
            class P<T> extends Promise<T> {}
            const p = new P<number>(resolve => resolve(1));
            const native: Promise<number> = p;
            const mapped: Promise<string> = p.then(value => { const numberValue: number = value; return 'ok'; });
            async function check() { const value: number = await p; }
            """, "", 0];

        yield return ["wrong-executor-generic-declaration", """
            class P<T> extends Promise<T> {}
            new P<number>(resolve => resolve("bad"));
            """, "TS2345", 2];

        yield return ["wrong-then-generic-declaration", """
            class P<T> extends Promise<T> {}
            const p = new P<number>(resolve => resolve(1));
            p.then(value => { const bad: string = value; });
            """, "TS2322", 3];

        yield return ["typed-generic-expression", """
            const P = class Named<T> extends Promise<T> {};
            const p = new P<number>(resolve => resolve(1));
            const native: Promise<number> = p;
            const mapped: Promise<string> = p.then(value => { const numberValue: number = value; return 'ok'; });
            async function check() { const value: number = await p; }
            """, "", 0];

        yield return ["wrong-executor-generic-expression", """
            const P = class Named<T> extends Promise<T> {};
            new P<number>(resolve => resolve("bad"));
            """, "TS2345", 2];

        yield return ["wrong-then-generic-expression", """
            const P = class Named<T> extends Promise<T> {};
            const p = new P<number>(resolve => resolve(1));
            p.then(value => { const bad: string = value; });
            """, "TS2322", 3];

        yield return ["heritage-zero-arguments", """
            class P extends Promise {}
            """, "TS2508", 1];

        yield return ["heritage-two-arguments", """
            class P extends Promise<number, string> {}
            """, "TS2508", 1];

        yield return ["value-shadow-nongeneric", """
            function check() { class Promise {} class P extends Promise<number> {} }
            """, "TS2315", 1];

        yield return ["type-only-shadow", """
            function check() { interface Promise<T> { local: string; } class P extends Promise<number> {} const p = new P(resolve => resolve(1)); p.then(value => { const numberValue: number = value; }); }
            """, "", 0];

        yield return ["missing-executor", """
            class P extends Promise<number> {} new P();
            """, "TS2554", 1];

        yield return ["noncallable-executor", """
            class P extends Promise<number> {} new P(1);
            """, "TS2345", 1];

        yield return ["wrong-native-promise-assignment", """
            class P extends Promise<number> {} const value: Promise<string> = new P(resolve => resolve(1));
            """, "TS2322", 1];

        yield return ["wrong-awaited-value", """
            class P<T> extends Promise<T> {} async function check() { const bad: string = await new P<number>(resolve => resolve(1)); }
            """, "TS2322", 1];

        yield return ["inferred-field-write", """
            class P extends Promise<number> { value = 1; } const p = new P(resolve => resolve(1)); p.value = "bad";
            """, "TS2322", 1];

        yield return ["constructor-static-relation", """
            class P extends Promise<number> { static value = 1; } class Q extends Promise<number> { static value = "bad"; } const ctor: typeof P = Q;
            """, "TS2322", 1];

        yield return ["selected-library-surface", """
            class P extends Promise<number> {} const p = new P(resolve => resolve(1)); const value: number = p.libraryValue; const optional: string | undefined = p.optional; const code: number = p.libraryCode; const prototype: Promise<any> = P.prototype; const version: number = P.libraryVersion; const mapped: Promise<string> = p.then(value => { const n: number = value; return "ok"; });
            """, "", 0];

        yield return ["selected-library-generic-expression", """
            const P = class Named<T> extends Promise<T> {}; const text: string = new P<string>(resolve => resolve("x")).libraryValue;
            """, "", 0];

        yield return ["selected-optional-write", """
            class P extends Promise<number> {} const value: string = new P(resolve => resolve(1)).optional;
            """, "TS2322", 1];

        yield return ["selected-readonly-instance-write", """
            class P extends Promise<number> {} new P(resolve => resolve(1)).libraryCode = 2;
            """, "TS2540", 1];

        yield return ["selected-readonly-static-write", """
            class P extends Promise<number> {} P.libraryVersion = 2;
            """, "TS2540", 1];

        yield return ["selected-missing-member", """
            class P extends Promise<number> {} new P(resolve => resolve(1)).catch;
            """, "TS2339", 1];

        yield return ["selected-static-not-instance", """
            class P extends Promise<number> {} new P(resolve => resolve(1)).prototype;
            """, "TS2576", 1];

        yield return ["selected-augmentation-valid", """
            interface Promise<T> { augmented: T; readonly extraCode: string; } class P<T> extends Promise<T> {} const p = new P<number>(resolve => resolve(1)); const value: number = p.augmented; const code: string = p.extraCode;
            """, "", 0];

        yield return ["selected-augmentation-wrong-type", """
            interface Promise<T> { augmented: T; } class P<T> extends Promise<T> {} const bad: string = new P<number>(resolve => resolve(1)).augmented;
            """, "TS2322", 1];

        yield return ["selected-augmentation-readonly", """
            interface Promise<T> { readonly extraCode: string; } class P extends Promise<number> {} new P(resolve => resolve(1)).extraCode = "bad";
            """, "TS2540", 1];

        // Accessor selection must retain the authoritative default-library Promise route.
        yield return ["promise-generic-inherited-private", """
            class Root<T> extends Promise<T> { private get value(): T { return undefined as any; } }
            class Middle<U> extends Root<U> {}
            class Leaf<V> extends Middle<V> {}
            declare const receiver: Leaf<number>;
            const result: number = receiver.value;
            """, "TS2341", 5];

        yield return ["promise-generic-inherited-type", """
            class Root<T> extends Promise<T> { public get value(): T { return undefined as any; } }
            class Middle<U> extends Root<U> {}
            class Leaf<V> extends Middle<V> {}
            declare const receiver: Leaf<number>;
            const result: string = receiver.value;
            """, "TS2322", 5];

        yield return ["generic-public-and-promise-typing", """
            class Root<T> { public get value(): T { return undefined as any; } }
            class Middle<U> extends Root<U> {}
            class Leaf<V> extends Middle<V> {}
            declare const receiver: Leaf<number>;
            const result: number = receiver.value;
            class PromiseRoot<T> extends Promise<T> { public get value(): T { return undefined as any; } }
            class PromiseMiddle<U> extends PromiseRoot<U> {}
            class PromiseLeaf<V> extends PromiseMiddle<V> {}
            declare const promiseReceiver: PromiseLeaf<number>;
            const promiseResult: number = promiseReceiver.value;

            class IdentityBase<T> {
                get identity(): <T>(value: T) => T { return function<T>(value: T): T { return value; }; }
            }
            class IdentityMiddle<U> extends IdentityBase<U> {}
            class IdentityChild extends IdentityMiddle<number> {}
            const ordinary = new IdentityChild();
            const ordinaryText: string = ordinary.identity("text");
            const ordinaryNumber: number = ordinary.identity(7);
            class IdentityPromiseBase<T> extends Promise<T> {
                get identity(): <T>(value: T) => T { return function<T>(value: T): T { return value; }; }
            }
            class IdentityPromiseMiddle<U> extends IdentityPromiseBase<U> {}
            class IdentityPromiseChild extends IdentityPromiseMiddle<number> {}
            const promised = new IdentityPromiseChild(resolve => resolve(7));
            const promisedText: string = promised.identity("text");
            const promisedNumber: number = promised.identity(7);
            """, "", 0];
    }

    [Theory, MemberData(nameof(Cases))]
    public void TrustedPromiseHeritagePreservesTypedConstructorsAndMembers(
        string name, string source, string expectedCode, int expectedLine)
    {
        Assert.NotEmpty(name);
        var diagnostics = Check(source);
        if (expectedCode.Length == 0)
        {
            Assert.Empty(diagnostics);
            return;
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.TsCode);
        Assert.Equal(expectedLine, diagnostic.Line);
    }

    public static IEnumerable<object[]> MetadataCases()
    {
        yield return ["nested-chain-accepted", "selected", """
            class P<T> extends Promise<T> {} class Q<U> extends P<U> {} const q = new Q<number>(resolve => resolve(1)); const native: Promise<number> = q; const mapped: Promise<string> = q.then(value => { const n: number = value; return "ok"; }); async function check() { const value: number = await q; }
            """, "", 0];

        yield return ["nested-chain-wrong-executor", "selected", """
            class P<T> extends Promise<T> {} class Q<U> extends P<U> {} new Q<number>(resolve => resolve("bad"));
            """, "TS2345", 1];

        yield return ["nested-chain-wrong-callback", "selected", """
            class P<T> extends Promise<T> {} class Q<U> extends P<U> {} new Q<number>(resolve => resolve(1)).then(value => { const bad: string = value; });
            """, "TS2322", 1];

        yield return ["generic-readonly-write", "selected", """
            class P<T> extends Promise<T> {} new P<number>(resolve => resolve(1)).libraryCode = 2;
            """, "TS2540", 1];

        yield return ["generic-inherited-write", "selected", """
            class P<T> extends Promise<T> {} new P<number>(resolve => resolve(1)).libraryValue = "bad";
            """, "TS2322", 1];

        yield return ["own-method-T-shielding", "ownT", """
            class P<T> extends Promise<T> {} const text: string = new P<number>(resolve => resolve(1)).identity("x"); const numberValue: number = new P<number>(resolve => resolve(1)).identity(2);
            """, "", 0];

        yield return ["own-method-T-wrong-result", "ownT", """
            class P<T> extends Promise<T> {} const bad: number = new P<number>(resolve => resolve(1)).identity("x");
            """, "TS2322", 1];

        yield return ["constructor-overloads-accepted", "overloaded", """
            class P extends Promise<number> {} const one: Promise<number> = new P(resolve => resolve(1)); const two: Promise<number> = new P(resolve => resolve(2), "tag");
            """, "", 0];

        yield return ["constructor-overload-wrong-tag", "overloaded", """
            class P extends Promise<number> {} new P(resolve => resolve(1), 2);
            """, "TS2345", 1];

        yield return ["constructor-overload-wrong-value", "overloaded", """
            class P extends Promise<number> {} new P(resolve => resolve("bad"), "tag");
            """, "TS2345", 1];

        yield return ["renamed-constructor-overloads-accepted", "renamedOverloads", """
            class P extends Promise<number> {} const one: Promise<number> = new P(resolve => resolve(1)); const two: Promise<number> = new P(resolve => resolve(2), "tag");
            """, "", 0];

        yield return ["renamed-constructor-overload-wrong-value", "renamedOverloads", """
            class P extends Promise<number> {} new P(resolve => resolve("bad"), "tag");
            """, "TS2345", 1];

        yield return ["constructor-constraint-accepted", "constrained", """
            class P extends Promise<number> {} new P(resolve => resolve(1));
            """, "", 0];

        yield return ["constructor-constraint-rejected", "constrained", """
            class P extends Promise<string> {}
            """, "TS2344", 1];

        yield return ["constructor-default-accepted", "defaulted", """
            class P extends Promise {} const native: Promise<number> = new P(resolve => resolve(1));
            """, "", 0];

        yield return ["constructor-default-wrong-value", "defaulted", """
            class P extends Promise {} new P(resolve => resolve("bad"));
            """, "TS2345", 1];

        yield return ["constructor-constraint-default-accepted", "constrainedDefault", """
            class P extends Promise {} const native: Promise<number> = new P(resolve => resolve(1));
            """, "", 0];

        yield return ["constructor-constraint-default-rejected", "constrainedDefault", """
            class P extends Promise<string> {}
            """, "TS2344", 1];

        yield return ["same-arity-executor-overload-second-alternative", "sameArity", """
            class P extends Promise<number> {} new P((resolve, code: number) => resolve(1));
            """, "", 0];

        yield return ["constraint-overload-second-alternative", "firstConstraint", """
            class P extends Promise<number> {} new P(resolve => resolve(1));
            """, "TS2344", 1];

        yield return ["generic-inherited-valid-write-and-readonly-read", "selected", """
            class P<T> extends Promise<T> {} const p = new P<number>(resolve => resolve(1)); p.libraryValue = 2; const value: number = p.libraryValue; const code: number = p.libraryCode;
            """, "", 0];

        yield return ["generic-own-mutable-override", "selected", """
            class P<T> extends Promise<T> { libraryCode = 1; } new P<number>(resolve => resolve(1)).libraryCode = 2;
            """, "", 0];

        yield return ["constructor-overload-missing-arguments", "overloaded", """
            class P extends Promise<number> {} new P();
            """, "TS2554", 1];

        yield return ["constructor-overload-surplus-arguments", "overloaded", """
            class P extends Promise<number> {} new P(resolve => resolve(1), "tag", true);
            """, "TS2554", 1];

        yield return ["derived-generic-expression-accepted", "selected", """
            class P<T> extends Promise<T> {} const Q = class Named<U> extends P<U> {}; const q = new Q<number>(resolve => resolve(1)); const native: Promise<number> = q; const mapped: Promise<string> = q.then(value => { const n: number = value; return "ok"; }); async function check() { const value: number = await q; }
            """, "", 0];

        yield return ["derived-generic-expression-wrong-executor", "selected", """
            class P<T> extends Promise<T> {} const Q = class Named<U> extends P<U> {}; new Q<number>(resolve => resolve("bad"));
            """, "TS2345", 1];

        yield return ["derived-generic-expression-wrong-callback", "selected", """
            class P<T> extends Promise<T> {} const Q = class Named<U> extends P<U> {}; new Q<number>(resolve => resolve(1)).then(value => { const bad: string = value; });
            """, "TS2322", 1];
    }

    [Theory, MemberData(nameof(MetadataCases))]
    public void ProjectionPreservesGenericAncestryAndSignatureMetadata(
        string name, string variant, string source, string expectedCode, int expectedLine)
    {
        Assert.NotEmpty(name);
        var diagnostics = Check(source, librarySource: MetadataLibrary(variant));
        if (expectedCode.Length == 0)
        {
            Assert.Empty(diagnostics);
            return;
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedCode, diagnostic.TsCode);
        Assert.Equal(expectedLine, diagnostic.Line);
    }

    [Fact]
    public void ModuleLocalTypeShadowDoesNotReplaceTheCanonicalConstructorInstance()
    {
        const string source = """
            interface Promise<T> { local: string; }
            class P extends Promise<number> {}
            const p = new P(resolve => resolve(1));
            p.then(value => { const numberValue: number = value; });
            export {};
            """;
        Assert.Empty(Check(source, isScript: false));
    }

    private const string ConstructorSignature =
        "new<T>(executor: (resolve: (value: T | PromiseLike<T>) => void, reject: (reason?: any) => void) => void): Promise<T>;";

    private static string MetadataLibrary(string variant)
    {
        var constructor = variant switch
        {
            "selected" or "ownT" => ConstructorSignature,
            "overloaded" => ConstructorSignature + "\n"
                + "new<T>(executor: (resolve: (value: T | PromiseLike<T>) => void, reject: (reason?: any) => void) => void, tag: string): Promise<T>;",
            "renamedOverloads" =>
                "new<X>(executor: (resolve: (value: X | PromiseLike<X>) => void, reject: (reason?: any) => void) => void): Promise<X>;\n"
                + "new<Y>(executor: (resolve: (value: Y | PromiseLike<Y>) => void, reject: (reason?: any) => void) => void, tag: string): Promise<Y>;",
            "constrained" => ConstructorSignature.Replace("new<T>", "new<T extends number>"),
            "defaulted" => ConstructorSignature.Replace("new<T>", "new<T = number>"),
            "constrainedDefault" => ConstructorSignature.Replace("new<T>", "new<T extends number = number>"),
            "sameArity" =>
                "new<T>(executor: (resolve: (value: T) => void, label: string) => void): Promise<T>; "
                + "new<T>(executor: (resolve: (value: T) => void, code: number) => void): Promise<T>;",
            "firstConstraint" =>
                "new<T extends string>(executor: (resolve: (value: T) => void) => void): Promise<T>; "
                + "new<T extends number>(executor: (resolve: (value: T) => void) => void): Promise<T>;",
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var library = Library.Replace(ConstructorSignature, constructor);
        return variant == "ownT"
            ? library.Replace("libraryValue: T;", "identity<T>(value: T): T;\n    libraryValue: T;")
            : library;
    }

    [Fact]
    public void ReusedCheckerRefreshesSelectedLibraryDeclarationIdentity()
    {
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        Assert.Empty(Check("class P extends Promise<number> {} const code: number = new P(resolve => resolve(1)).libraryCode;", checker));

        var stringLibrary = Library.Replace("readonly libraryCode: number;", "readonly libraryCode: string;");
        Assert.Empty(Check("class P extends Promise<number> {} const code: string = new P(resolve => resolve(1)).libraryCode;",
            checker, stringLibrary));
        var diagnostic = Assert.Single(Check(
            "class P extends Promise<number> {} const code: number = new P(resolve => resolve(1)).libraryCode;",
            checker, stringLibrary));
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public void ReusedCheckerResetsDefaultLibraryProjectionAndShadowBindings()
    {
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        Assert.Empty(Check("class First extends Promise<number> {}", checker));
        Assert.Empty(Check("class Native extends Promise<number> {}", checker, loadLibrary: false));
        var diagnostic = Assert.Single(Check(
            "function check() { class Promise {} class P extends Promise<number> {} }", checker));
        Assert.Equal("TS2315", diagnostic.TsCode);
        Assert.Equal(1, diagnostic.Line);
        Assert.Empty(Check("const Last = class Named<T> extends Promise<T> {};", checker));
    }

    private static IReadOnlyList<Diagnostic> Check(
        string source, TypeChecker? checker = null, string? librarySource = null, bool loadLibrary = true, bool isScript = true)
    {
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-default-promise-heritage"));
        var modules = new List<ParsedModule>();
        if (loadLibrary)
        {
            modules.Add(new ParsedModule(Path.Combine(directory, "lib.test.d.ts"), Parse(librarySource ?? Library))
            {
                IsDefaultLibrary = true,
                IsDeclarationFile = true,
            });
        }
        // These oracle sources are scripts; module-local type shadows are tested explicitly.
        modules.Add(new ParsedModule(Path.Combine(directory, "main.ts"), Parse(source)) { IsScript = isScript });
        checker ??= new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules(modules, new ModuleResolver(directory));
        return checker.GetDiagnostics();
    }

    private static List<Stmt> Parse(string source)
    {
        var result = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(result.IsSuccess);
        return result.Statements;
    }
}
