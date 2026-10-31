using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class SpecializedLiteralOverloadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["direct", "declare function pick(x:string):'broad';declare function pick(x:'x'):'literal';const r:'literal'=pick('x');"];
        yield return ["spread", "declare function pick(x:string,y:number):'broad';declare function pick(x:'x',y:1):'literal';const args=['x',1] as const;const r:'literal'=pick(...args);"];
        yield return ["alias", "type X='x';declare function pick(x:string):'broad';declare function pick(x:X):'literal';const r:'broad'=pick('x');"];
        yield return ["parenthesized", "declare function pick(x:string):'broad';declare function pick(x:('x')):'literal';const r:'broad'=pick('x');"];
        yield return ["union", "declare function pick(x:string):'broad';declare function pick(x:'x'|'y'):'literal';const r:'broad'=pick('x');"];
        yield return ["numeric", "declare function pick(x:number):'broad';declare function pick(x:1):'literal';const r:'literal'=pick(1);"];
        yield return ["boolean", "declare function pick(x:boolean):'broad';declare function pick(x:true):'literal';const r:'literal'=pick(true);"];
        yield return ["null", "declare function pick(x:any):'broad';declare function pick(x:null):'literal';const r:'literal'=pick(null);"];
        yield return ["bigint", "declare function pick(x:bigint):'broad';declare function pick(x:1n):'literal';const r:'literal'=pick(1n);"];
        yield return ["template", "declare function pick(x:string):'broad';declare function pick(x:`x`):'literal';const r:'literal'=pick('x');"];
        yield return ["generic", "declare function pick<T>(x:T,tag:string):'broad';declare function pick<T>(x:T,tag:'x'):T;const r:number=pick(1,'x');"];
        yield return ["instantiated", "interface Choose<T>{(x:string):'broad';(x:T):'literal';}declare const pick:Choose<'x'>;const r:'broad'=pick('x');"];
        yield return ["callable", "interface Choose{(x:string):'broad';(x:'x'):'literal';}declare const pick:Choose;const r:'literal'=pick('x');"];
        yield return ["method", "class C{pick(x:string):'broad';pick(x:'x'):'literal';pick(x:string):any{return 'literal';}}const r:'literal'=new C().pick('x');"];
        yield return ["parenthesizedNull", "declare function pick(x:any):'broad';declare function pick(x:(null)):'literal';const r:'broad'=pick(null);"];
        yield return ["parenthesizedTemplate", "declare function pick(x:string):'broad';declare function pick(x:(`x`)):'literal';const r:'broad'=pick('x');"];
        yield return ["templateInterpolation", "declare function pick(x:string):'broad';declare function pick(x:`${'x'}`):'literal';const r:'broad'=pick('x');"];
        yield return ["anySubtype", "declare function pick(x:any):'broad';declare function pick(x:'x'):'literal';declare const value:any;const r:'broad'=pick(value);"];
        yield return ["specializedOrder", "declare function pick(x:'x',y:number):'first';declare function pick(x:'x',y:any):'second';const r:'first'=pick('x',1);"];
        yield return ["fallback", "declare function pick(x:string,y:number):'broad';declare function pick(x:'x',y:string):'literal';const r:'broad'=pick('x',1);"];
        yield return ["recordCallable", "type Choose={(x:string):'broad';(x:'x'):'literal';};declare const pick:Choose;const r:'literal'=pick('x');"];
        yield return ["forwardCall", "const r:'literal'=pick('x');function pick(x:string):'broad';function pick(x:'x'):'literal';function pick(x:string):any{return 'literal';}"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { "script", "module", "editor" }
        .Select(mode => new object[] { row[0], row[1], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OnlyWrittenLiteralParametersReceiveSpecializedPriority(string name, string source, string mode)
    {
        Assert.NotEmpty(name);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (mode == "module")
        {
            string path = Path.Combine(Path.GetTempPath(), "specialized-literal-overloads", "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string,string> { [path] = source });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Empty(checker.GetDiagnostics());
        }
        else if (mode == "editor")
        {
            var document = new SourceDocument("specialized.ts", source);
            var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
            Assert.Empty(checker.WithEditorMetadata().CheckWithRecovery(statements, document).Diagnostics);
        }
        else Assert.Empty(checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }

    [Theory]
    [InlineData("'x'", false)]
    [InlineData("('x')", true)]
    [InlineData("((null))", true)]
    [InlineData("(`x`)", true)]
    public void GroupingMetadataSurvivesNodeCopiesWithoutChangingResolvedTypes(string annotation, bool grouped)
    {
        var statement = Assert.IsType<Stmt.Function>(Assert.Single(TestHarness.ParseOrThrow("declare function f(x:" + annotation + "):void;")));
        var node = statement.Parameters[0].TypeAnnotationNode!;
        Assert.Equal(grouped, node.IsParenthesizedLiteral);
        Assert.Equal(grouped, (node with { Line = node.Line + 1 }).IsParenthesizedLiteral);
        string value = annotation.Contains("null", StringComparison.Ordinal) ? "null" : "'x'";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow("const value:" + annotation + "=" + value + ";")).Diagnostics);
    }

    [Theory]
    [InlineData("'x'")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("`x`")]
    public void ParenthesizedLiteralCopiesKeepTheInnerSourceRange(string literal)
    {
        string source = "type T = ( " + literal + " );";
        var document = new SourceDocument("literal-range.ts", source);
        var parser = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax();
        var alias = Assert.IsType<Stmt.TypeAlias>(Assert.Single(parser.ParseOrThrow()));
        var node = alias.TypeDefinitionNode!;
        Assert.True(node.IsParenthesizedLiteral);
        var span = document.Spans.GetSpan(node)!.Value;
        Assert.Equal(literal, source[span.Start..span.End]);
        Assert.Contains(document.EditorSyntax!.GetRecords(node), record => record.Kind == EditorSyntaxKind.Grouping);
    }

    [Fact]
    public void RetainedReplBindingsKeepTheirOriginalPriority()
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        Assert.Empty(checker.CheckWithRecovery(TestHarness.ParseOrThrow("declare function pick(x:string):'broad';declare function pick(x:'x'):'literal';")).Diagnostics);
        Assert.Empty(checker.CheckWithRecovery(TestHarness.ParseOrThrow("const result:'literal'=pick('x');")).Diagnostics);
    }
}
