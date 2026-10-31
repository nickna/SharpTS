using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ConditionalCallContextTests
{
    private const string Prelude = """
        type Action={type:'size',size:number}|{type:'textSize',value:number}|{type:'fillTolerance',value:number};
        declare function dispatch(action:Action):void;
        declare function rest(...actions:Action[]):void;
        declare const sink:{dispatch:(action:Action)=>void};
        declare const first:boolean;
        declare const second:boolean;
        """;

    [Theory]
    [InlineData("dispatch(first?{type:'textSize',value:12}:{type:'size',size:3});")]
    [InlineData("dispatch((first?{type:'textSize',value:12}:second?{type:'fillTolerance',value:0.5}:{type:'size',size:3}));")]
    [InlineData("rest(first?{type:'textSize',value:12}:{type:'size',size:3});")]
    [InlineData("sink.dispatch(first?{type:'textSize',value:12}:{type:'size',size:3});")]
    [InlineData("dispatch(first?({type:'textSize',value:12} as const):({type:'size',size:3} as const));")]
    [InlineData("const kind:'size'='size'; dispatch(first?{type:kind,size:3}:{type:'textSize',value:12});")]
    public void FreshConditionalBranchesReceiveTheSelectedParameterContext(string call)
    {
        var (_, _, checker) = Check(Prelude + call);
        Assert.Empty(checker.GetDiagnostics());
    }

    [Fact]
    public void ContextKeepsDiscriminantsAndActualPayloadTypesThroughNestedRecords()
    {
        const string source = """
            type Move={type:'textMove',point:{x:number,y:number}}|{type:'pointerMove',point:{x:number,y:number}};
            declare function move(action:Move):void;
            declare const condition:boolean;
            move(condition?{type:'textMove',point:{x:1,y:2}}:{type:'pointerMove',point:{x:3,y:4}});
            """;
        var (statements, map, checker) = Check(source);
        Assert.Empty(checker.GetDiagnostics());
        var call = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(statements[^1]).Expr);
        var argument = Assert.IsType<Expr.Ternary>(Assert.Single(call.Arguments));
        var union = Assert.IsType<TypeInfo.Union>(map.Get(argument));
        Assert.Equal(2, union.FlattenedTypes.Count);
        foreach (var (branch, expected) in new[] { (argument.ThenBranch, "textMove"), (argument.ElseBranch, "pointerMove") })
        {
            var record = Assert.IsType<TypeInfo.Record>(map.Get(branch));
            Assert.Equal(expected, Assert.IsType<TypeInfo.StringLiteral>(record.Fields["type"]).Value);
            var point = Assert.IsType<TypeInfo.Record>(record.Fields["point"]);
            Assert.Equal(TypeInfo.Primitive.Number, point.Fields["x"]);
            Assert.Equal(TypeInfo.Primitive.Number, point.Fields["y"]);
        }
    }

    [Theory]
    [InlineData("dispatch(first?{type:'missing',size:1}:{type:'textSize',value:2});")]
    [InlineData("dispatch(first?{type:'size',size:'wrong'}:{type:'textSize',value:2});")]
    [InlineData("const retained={type:'size',size:1}; dispatch(first?retained:{type:'textSize',value:2});")]
    [InlineData("const retained=first?{type:'size',size:1}:{type:'textSize',value:2}; dispatch(retained);")]
    [InlineData("dispatch(first?({type:'size',size:1} as {type:string,size:number}):{type:'textSize',value:2});")]
    public void ParameterContextDoesNotRewriteRetainedOrExplicitDomainsOrInvalidPayloads(string call)
    {
        var statements = new Parser(new Lexer(Prelude + call).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2345");
    }

    private static (List<Stmt> Statements, TypeMap Map, TypeChecker Checker) Check(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        return (statements, checker.Check(statements), checker);
    }
}
