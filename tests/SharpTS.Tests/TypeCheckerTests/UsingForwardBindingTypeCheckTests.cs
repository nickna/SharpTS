using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class UsingForwardBindingTypeCheckTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return ["exact-forward-arrow", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource=null;console.log(read()===null);"];
        yield return ["comma-forward-binding", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>second;using first=null,second=null;console.log(first===null,read()===null);"];
        yield return ["annotated-null-forward", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource:null=null;const precise:null=resource;console.log(read()===null,precise===null);"];
        yield return ["inferred-object-type-after-declaration", "/// <reference lib=\"esnext.disposable\" />\nfunction run(){const read=()=>resource;using resource={id:7,[Symbol.dispose](){}};const n:number=resource.id;console.log(read()===resource,n)}run();"];
        yield return ["sibling-scope-bindings", "/// <reference lib=\"esnext.disposable\" />\n{const read=()=>resource;using resource=null;console.log(read()===null)}{using resource=null;console.log(resource===null)}"];
        yield return ["await-using-forward", "/// <reference lib=\"esnext.disposable\" />\nasync function run(){const read=()=>resource;await using resource=null;console.log(read()===null)}run();"];
    }

    [Theory, MemberData(nameof(PositiveCases))]
    public void DeferredReadsSeeCurrentListUsingBindings(string name, string source)
        => Check(name, source);

    [Theory, MemberData(nameof(UsingForwardLexicalBindingTests.Cases), MemberType = typeof(UsingForwardLexicalBindingTests))]
    public void SharedUsingProgramsRemainTypeCorrect(string name, string source, string expected)
    {
        Assert.NotEmpty(expected);
        Check(name, source);
    }

    private static void Check(string name, string source)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        new TypeChecker().Check(statements, document);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["initializer-annotation", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource:null={ [Symbol.dispose](){} };", "not assignable"];
        yield return ["inferred-later-property", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource={id:7,[Symbol.dispose](){}};const wrong:string=resource.id;", "not assignable"];
        yield return ["scope-leak", "/// <reference lib=\"esnext.disposable\" />\n{using resource=null;}const read=()=>resource;", "Undefined variable"];
        yield return ["annotated-primitive-resource", "/// <reference lib=\"esnext.disposable\" />\nconst read=()=>resource;using resource:number=1;", "cannot be used"];
        yield return ["await-using-nonasync", "/// <reference lib=\"esnext.disposable\" />\nfunction run(){const read=()=>resource;await using resource=null;}", "only allowed inside an async function"];
        yield return ["eager-direct-read", "/// <reference lib=\"esnext.disposable\" />\nconsole.log(resource);using resource=null;", "before being assigned"];
        yield return ["eager-local-copy", "/// <reference lib=\"esnext.disposable\" />\nconst seen=resource;using resource=null;", "before being assigned"];
        yield return ["self-initializer", "/// <reference lib=\"esnext.disposable\" />\nusing resource=resource;", "before being assigned"];
        yield return ["eager-shadow-read", "/// <reference lib=\"esnext.disposable\" />\nconst resource=100;{console.log(resource);using resource=null;}", "before being assigned"];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void ForwardRegistrationRetainsDeclarationChecks(string name, string source, string diagnostic)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);
        Assert.Contains(result.Diagnostics, item => item.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase));
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void ModulePreparationRetainsEagerAndDeclarationChecks(string name, string source, string diagnostic)
    {
        string path = Path.Combine(Path.GetTempPath(), "using-forward-checker", name + ".ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker();
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Contains(checker.GetDiagnostics(), item => item.Message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ForwardReadBindsToTheActualResourceToken()
    {
        const string source = "const read=()=>resource;using resource=null;";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var declaration = Assert.IsType<Stmt.Using>(statements[1]).Bindings.Single().Name!;
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Const>(statements[0]).Initializer);
        var use = Assert.IsType<Expr.Variable>(arrow.ExpressionBody).Name;
        var checker = new TypeChecker();
        checker.Check(statements);
        var symbol = checker.Bindings.GetSymbol(declaration);
        Assert.NotNull(symbol);
        Assert.Same(symbol, checker.Bindings.GetSymbol(use));
    }
}
