using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LoadedObjectPrototypeDomainTests
{
    private const string Library = "type PropertyKey=string|number|symbol; interface Object { hasOwnProperty(v:PropertyKey):boolean; }";

    [Fact]
    public void InheritedMemberKeepsTheLoadedParameterNameAndPropertyKeyDomain()
    {
        const string source = "interface Test1 { toString:null|'string'; } type Test2=Test1&{optional?:unknown}; declare const source:Test1; const target:Test2={...source}; const hasOwn=target.hasOwnProperty; export {};";
        var (checker, map, entry) = Check(source);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var declaration = entry.Statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == "hasOwn");
        var member = Assert.IsType<TypeInfo.Function>(map.Get(declaration.Initializer!));
        Assert.Equal("v", Assert.Single(member.ParamNames!));
        var domain = Assert.IsType<TypeInfo.Union>(Assert.Single(member.ParamTypes));
        Assert.Contains(domain.FlattenedTypes, type => type is TypeInfo.String);
        Assert.Contains(domain.FlattenedTypes, type => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER });
        Assert.Contains(domain.FlattenedTypes, type => type is TypeInfo.Symbol);
        Assert.DoesNotContain(domain.FlattenedTypes, type => type is TypeInfo.Any);
    }

    [Theory]
    [InlineData("const value={}; value.hasOwnProperty('x'); value.hasOwnProperty(1); value.hasOwnProperty(Symbol());", false)]
    [InlineData("const value={}; value.hasOwnProperty(true);", true)]
    [InlineData("interface Object { hasOwnProperty(v:boolean):boolean; } const value={}; value.hasOwnProperty(true); export {};", true)]
    [InlineData("interface Shape { hasOwnProperty(v:boolean):string; } declare const shape:Shape; shape.hasOwnProperty(true); export {};", false)]
    public void CanonicalLibraryOwnershipAndOwnOverridesRemainDistinct(string source, bool error)
    {
        var (checker, _, _) = Check(source);
        Assert.Equal(error, checker.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static (TypeChecker Checker, TypeMap Map, ParsedModule Entry) Check(string source)
    {
        string directory = Path.Combine(Path.GetTempPath(), "loaded_object_" + Guid.NewGuid().ToString("N"));
        var library = new ParsedModule(Path.Combine(directory, "lib.test.d.ts"), new Parser(new Lexer(Library).ScanTokens()).ParseOrThrow())
            { IsDefaultLibrary = true, IsDeclarationFile = true };
        var entry = new ParsedModule(Path.Combine(directory, "main.ts"), new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        var checker = new TypeChecker(strictNullChecks: true); var map = checker.CheckModules([library, entry], new ModuleResolver(directory));
        return (checker, map, entry);
    }
}
