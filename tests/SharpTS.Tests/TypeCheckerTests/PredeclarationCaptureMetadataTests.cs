using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PredeclarationCaptureMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RepeatedBodyChecksPreserveForwardLexicalReadMetadata(bool async, bool modules)
    {
        string source = "let value=100; " + (async ? "async " : "") + """
            function run(){
                {
                    const read=()=>value;
                    const kind=()=>typeof value;
                    let value:number;
                    value=42;
                }
            }
            """;
        var (statements, map) = Check(source, modules);
        var function = Assert.Single(statements.OfType<Stmt.Function>());
        var block = Assert.Single(function.Body!.OfType<Stmt.Block>());
        var read = Assert.Single(block.Statements.OfType<Stmt.Const>().Where(declaration => declaration.Name.Lexeme == "read"));
        var arrow = Assert.IsType<Expr.ArrowFunction>(read.Initializer);
        Assert.IsType<TypeInfo.Any>(Assert.IsType<TypeInfo.Function>(map.Get(arrow)).ReturnType);
        Assert.IsType<TypeInfo.Any>(map.Get(arrow.ExpressionBody!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializedMutableCaptureStillUsesItsDeclaredUnion(bool modules)
    {
        var (statements, map) = Check("""
            let value:string|number='before';
            const read=()=>value;
            value=1;
            """, modules);
        var read = Assert.Single(statements.OfType<Stmt.Const>());
        var arrow = Assert.IsType<Expr.ArrowFunction>(read.Initializer);
        var result = Assert.IsType<TypeInfo.Union>(Assert.IsType<TypeInfo.Function>(map.Get(arrow)).ReturnType);
        Assert.Contains(result.FlattenedTypes, type => type is TypeInfo.String);
        Assert.Contains(result.FlattenedTypes, type => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER });
    }

    private static (List<Stmt> Statements, TypeMap Map) Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
        {
            var statements = TestHarness.ParseOrThrow(source);
            var result = checker.CheckWithRecovery(statements);
            Assert.Empty(result.Diagnostics);
            return (statements, result.TypeMap);
        }
        string path = Path.Combine(Path.GetTempPath(), "predecl-capture-metadata-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        var map = checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
        return (entry.Statements, map);
    }
}
