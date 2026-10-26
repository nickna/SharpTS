using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DefaultLibraryErrorHeritageTests
{
    private const string Library = """
        interface Error { name: string; message: string; stack?: string; readonly libraryCode: number; toString(): string; }
        interface ErrorConstructor {
            new(message?: string): Error;
            (message?: string): Error;
            readonly prototype: Error;
        }
        declare var Error: ErrorConstructor;
        interface TypeError extends Error {}
        interface TypeErrorConstructor {
            new(message?: string): TypeError;
            (message?: string): TypeError;
            readonly prototype: TypeError;
        }
        declare var TypeError: TypeErrorConstructor;
        """;

    [Fact]
    public void TrustedConstructorInterfacesPreserveInheritedInstanceTypes()
    {
        const string source = """
            class E extends Error {}
            class Detailed extends E { constructor(message: string) { super(message); } }
            class NativeSubtype extends TypeError {}
            const Expression = class extends TypeError {};
            const empty: Error = new E();
            const error: Error = new Detailed("message");
            const message: string = error.message;
            const nativeName: string = new NativeSubtype("typed").name;
            const expressionMessage: string = new Expression("expression").message;
            const stack: string | undefined = new E("stack").stack;
            const text: string = new NativeSubtype().toString();
            const libraryCode: number = new NativeSubtype().libraryCode;
            """;
        Assert.Empty(Check(source));
    }

    public static IEnumerable<object[]> TypedFailures()
    {
        yield return ["constructor-argument", "class E extends Error {} new E(1);", "TS2345"];
        yield return ["super-argument", "class E extends Error { constructor() { super(1); } }", "TS2345"];
        yield return ["subtype-constructor-argument", "class E extends TypeError {} new E(1);", "TS2345"];
        yield return ["expression-super-argument", "const E = class extends TypeError { constructor() { super(1); } };", "TS2345"];
        yield return ["instance-member-type", "class E extends Error {} const value: number = new E().message;", "TS2322"];
        yield return ["subtype-inherited-member", "class E extends TypeError {} const value: number = new E().name;", "TS2322"];
        yield return ["optional-instance-member", "class E extends Error {} const value: string = new E().stack;", "TS2322"];
        yield return ["unknown-member", "class E extends Error {} new E().missing;", "TS2339"];
        yield return ["constructor-member-is-not-instance-member", "class E extends Error {} new E().prototype;", "TS2576"];
        yield return ["inherited-readonly-instance-member", "class E extends TypeError {} new E().libraryCode = 1;", "TS2540"];
    }

    [Theory, MemberData(nameof(TypedFailures))]
    public void TrustedProjectionRetainsConstructorAndMemberDiagnostics(string name, string source, string code)
    {
        Assert.NotEmpty(name);
        var diagnostics = Check(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == code);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == "TS2507");
    }

    [Fact]
    public void LocalNonconstructorErrorDoesNotUseTheNativeProjection()
    {
        var diagnostics = Check("function check() { const Error = 1; class E extends Error {} }");
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2507");
    }

    [Fact]
    public void LocalUserErrorClassRetainsItsOwnConstructorAndMembers()
    {
        const string source = """
            function check() {
                class Error { constructor(public code: number) {} }
                class E extends Error {}
                const value: number = new E(7).code;
            }
            """;
        Assert.Empty(Check(source));
    }

    [Fact]
    public void UnrelatedConstructibleInterfaceDoesNotUseTheNativeProjection()
    {
        // General constructible-interface heritage is a separate existing checker boundary.
        const string source = """
            function check() {
                interface ErrorConstructor { new(code: number): { code: number }; }
                const Error: ErrorConstructor = (undefined as any);
                class E extends Error {}
            }
            """;
        Assert.Contains(Check(source), diagnostic => diagnostic.TsCode == "TS2507");
    }

    [Fact]
    public void ReusedCheckerPreservesCurrentProgramShadowBindings()
    {
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        Assert.Empty(Check("class First extends Error {} new First(\"first\");", checker));
        const string source = """
            function sourceClass() {
                class Error { constructor(public code: number) {} }
                class E extends Error {}
                const code: number = new E(7).code;
            }
            function nonconstructor() { const Error = 1; class E extends Error {} }
            """;
        var diagnostic = Assert.Single(Check(source, checker));
        Assert.Equal("TS2507", diagnostic.TsCode);
    }

    [Fact]
    public void TrustedConstructorOverloadsPreserveEveryAlternativeAndRejectInvalidCalls()
    {
        string library = Library.Replace("new(message?: string): Error;",
            "new(message?: string): Error; new(message: string, code: number): Error;");
        const string valid = """
            class E extends Error {}
            const one: Error = new E("one");
            const two: Error = new E("two", 7);
            """;
        Assert.Empty(Check(valid, librarySource: library));
        foreach (var call in new[] { "new E(true);", "new E(\"extra\", 7, true);" })
        {
            var diagnostics = Check("class E extends Error {} " + call, librarySource: library);
            Assert.NotEmpty(diagnostics);
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.TsCode == "TS2507");
        }
    }

    private static IReadOnlyList<Diagnostic> Check(
        string source, TypeChecker? checker = null, string? librarySource = null)
    {
        string directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-default-error-heritage"));
        var library = new ParsedModule(Path.Combine(directory, "lib.test.d.ts"), Parse(librarySource ?? Library))
        {
            IsDefaultLibrary = true,
            IsDeclarationFile = true,
        };
        var entry = new ParsedModule(Path.Combine(directory, "main.ts"), Parse(source));
        checker ??= new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules([library, entry], new ModuleResolver(directory));
        return checker.GetDiagnostics();
    }

    private static List<Stmt> Parse(string source)
    {
        var result = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(result.IsSuccess);
        return result.Statements;
    }
}
