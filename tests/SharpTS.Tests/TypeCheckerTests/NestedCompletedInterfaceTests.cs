using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NestedCompletedInterfaceTests
{
    private static string[] Check(string source)
    {
        string directory = Path.GetFullPath("nested-interface-completion");
        static List<Stmt> Parse(string text) => new Parser(new Lexer(text).ScanTokens()).Parse().Statements;
        var library = new ParsedModule(Path.Combine(directory, "lib.base.d.ts"), Parse("""
            interface String { length: number; }
            interface StringConstructor { readonly prototype: String; }
            declare var String: StringConstructor;
            """)) { IsDefaultLibrary = true, IsDeclarationFile = true };
        var extension = new ParsedModule(Path.Combine(directory, "lib.extension.d.ts"), Parse("""
            interface String { includes(needle: string, position?: number): boolean; }
            """)) { IsDefaultLibrary = true, IsDeclarationFile = true };
        var entry = new ParsedModule(Path.Combine(directory, "main.ts"), Parse(source));
        var checker = new TypeChecker(strictNullChecks: true, maxErrors: 20);
        checker.CheckModules([library, extension, entry], new ModuleResolver(directory));
        return checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode ?? "").ToArray();
    }

    [Fact]
    public void NestedPrototypeReadsAndWritesUseTheCompletedInterface()
        => Assert.Empty(Check("""
            const original = String.prototype.includes;
            String.prototype.includes = function (needle: string, position?: number): boolean { return false; };
            String.prototype.includes = original;
            """));

    [Theory]
    [InlineData("String.prototype.missing;", "TS2339")]
    [InlineData("String.prototype.includes = 1;", "TS2322")]
    [InlineData("const wrong: number = String.prototype.includes('x');", "TS2322")]
    public void CompletedInterfacesRetainPreciseMemberDiagnostics(string source, string code)
        => Assert.Contains(code, Check(source));
}
