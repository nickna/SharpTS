using SharpTS.Parsing;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NumberPropertyValidationTests
{
    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).Parse().Statements;

    [Theory]
    [InlineData("const value: number = 1; value.length;")]
    [InlineData("const value = 1 as const; value.missing;")]
    [InlineData("function read<T extends number>(value: T) { return value.length; }")]
    [InlineData("function read(value: string | number) { return value.length; }")]
    public void MissingNumberPropertiesAreRejected(string source)
    {
        var statements = Parse(source);
        Assert.Equal("TS2339", Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements)).Diagnostic.TsCode);
        var diagnostic = Assert.Single(new TypeChecker().CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2339", diagnostic.TsCode);
    }

    [Fact]
    public void NumberMethodsRetainTheirReturnTypes()
    {
        new TypeChecker().Check(Parse("""
            const value: number = 1;
            const fixed: string = value.toFixed(2);
            const precision: string = value.toPrecision();
            const exponential: string = value.toExponential(2);
            const text: string = value.toString(16);
            const localized: string = value.toLocaleString('en-US');
            const primitive: number = value.valueOf();
            function read<T extends number>(n: T): string { return n.toFixed(); }
            """));
        Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(Parse(
            "const value: number = 1; const invalid: number = value.toFixed();"))).Diagnostic.TsCode);
    }

    [Fact]
    public void NumberFormattingRejectsTypedNullAndAllowsExplicitDynamicCoercion()
    {
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(Parse("(123).toExponential(null);")).Diagnostics);
        Assert.Equal("TS2345", diagnostic.TsCode);
        new TypeChecker(TypeCheckerOptions.Strict).Check(Parse("(123).toExponential(null as any);"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleCheckingValidatesNumberMembersWithAndWithoutLibraries(bool loadLibraries)
    {
        string path = Path.GetFullPath("number-property-library/main.ts");
        var files = new Dictionary<string, string>
        {
            [path] = "const value: number = 1; const text: string = value.toFixed(2); value.length;"
        };
        var resolver = new ModuleResolver(path, files, new TypeScriptProgramOptions
        {
            LoadDefaultLib = loadLibraries,
            NoLib = !loadLibraries,
            PreferDeclarationFiles = true
        });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        var diagnostic = Assert.Single(checker.GetDiagnostics());
        Assert.Equal("TS2339", diagnostic.TsCode);
        Assert.Contains("length", diagnostic.Message);
    }
}
