using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class FreshCallCandidateContextTests
{
    private const string Prelude = """
        type Action={type:'size',size:number}|{type:'textSize',value:number};
        declare const condition:boolean;
        declare function generic<T extends Action>(action:T):T;
        """;

    [Theory]
    [InlineData("generic({type:'size',size:1});")]
    [InlineData("generic(({type:'size',size:1}));")]
    [InlineData("generic(condition?{type:'size',size:1}:{type:'textSize',value:2});")]
    [InlineData("generic<Action>({type:'size',size:1});")]
    [InlineData("generic<{type:'size',size:1}>({type:'size',size:1});")]
    [InlineData("declare function nested<T extends {action:Action}>(value:T):T; nested({action:{type:'size',size:1}});")]
    [InlineData("declare function nested<T extends {actions:Action[]}>(value:T):T; nested({actions:[{type:'size',size:1},{type:'textSize',value:2}]});")]
    [InlineData("declare function rest<T extends Action>(...values:T[]):T; rest({type:'size',size:1});")]
    [InlineData("declare function tuple<T extends Action>(...values:[T]):T; tuple({type:'size',size:1});")]
    [InlineData("declare function tuple<T extends Action>(...values:[[T]]):T; tuple([{type:'size',size:1}]);")]
    [InlineData("declare function tuple(...values:[Action]):void; tuple({type:'size',size:1});")]
    [InlineData("declare function dependent<K extends string,T extends {type:K}>(key:K,value:T):T; dependent('size',{type:'size',size:1});")]
    [InlineData("declare function over(value:Action):'action'; declare function over(value:string):'string'; over({type:'size',size:1});")]
    [InlineData("declare function over<T extends Action>(value:T):T; declare function over<T extends Action>(value:T,extra:number):T; over({type:'size',size:1});")]
    [InlineData("declare function over<T extends Action>(...values:[T]):T; declare function over<T extends Action>(...values:[T,number]):T; over({type:'size',size:1});")]
    [InlineData("declare function mixed<T extends Action>(value:T):T; declare function mixed(value:string):string; mixed({type:'size',size:1});")]
    [InlineData("interface Callable {<T extends Action>(value:T):T;} declare const callable:Callable; callable({type:'size',size:1});")]
    [InlineData("interface Callable {(value:Action):'action';(value:string):'string';} declare const callable:Callable; callable({type:'size',size:1});")]
    public void EachSignatureSuppliesItsOwnFreshAggregateContext(string call)
        => Check(Prelude + call);

    [Theory]
    [InlineData("declare function identity<T>(value:T):T; identity({type:'size',size:1});")]
    [InlineData("declare function broad<T extends {type:string,size:number}>(value:T):T; broad({type:'size',size:1});")]
    public void UnconstrainedAndBroadConstraintsKeepMutableMemberWidening(string source)
    {
        var (_, map, call) = Check(source);
        var result = Assert.IsType<TypeInfo.Record>(map.Get(call));
        Assert.IsType<TypeInfo.String>(result.Fields["type"]);
        Assert.Equal(TypeInfo.Primitive.Number, result.Fields["size"]);
    }

    [Theory]
    [InlineData("generic({type:'size',size:1});", false)]
    [InlineData("generic({type:'size',size:1 as const});", true)]
    [InlineData("const value:1=1; const tag:'size'='size'; generic({type:tag,size:value});", true)]
    public void InferredRecordKeepsOnlyContextSupportedOrSourceDeclaredLiterals(string source, bool literalPayload)
    {
        var (_, map, call) = Check(Prelude + source);
        var result = Assert.IsType<TypeInfo.Record>(map.Get(call));
        Assert.Equal("size", Assert.IsType<TypeInfo.StringLiteral>(result.Fields["type"]).Value);
        if (literalPayload) Assert.Equal(1, Assert.IsType<TypeInfo.NumberLiteral>(result.Fields["size"]).Value);
        else Assert.Equal(TypeInfo.Primitive.Number, result.Fields["size"]);
        var argument = Assert.IsType<TypeInfo.Record>(map.Get(call.Arguments[0]));
        Assert.Equal(result.Fields["size"], argument.Fields["size"]);
    }

    [Fact]
    public void ConstInferenceFreezesTheResultWithoutFreezingTheSourceExpression()
    {
        var (_, map, call) = Check(Prelude + "declare function fixed<const T extends Action>(value:T):T; fixed({type:'size',size:1});");
        var result = Assert.IsType<TypeInfo.Record>(map.Get(call));
        Assert.True(result.IsReadonly);
        Assert.Equal(1, Assert.IsType<TypeInfo.NumberLiteral>(result.Fields["size"]).Value);
        var argument = Assert.IsType<TypeInfo.Record>(map.Get(call.Arguments[0]));
        Assert.False(argument.IsReadonly);
        Assert.Equal(1, Assert.IsType<TypeInfo.NumberLiteral>(argument.Fields["size"]).Value);
    }

    [Fact]
    public void NestedArrayContextKeepsDiscriminantsWhileNumericElementsStillWiden()
    {
        var (_, map, call) = Check(Prelude + "declare function nested<T extends {action:Action,values:number[]}>(value:T):T; nested({action:{type:'size',size:1},values:[1,2]});");
        var result = Assert.IsType<TypeInfo.Record>(map.Get(call));
        var values = Assert.IsType<TypeInfo.Array>(result.Fields["values"]);
        Assert.Equal(TypeInfo.Primitive.Number, values.ElementType);
        var argument = Assert.IsType<TypeInfo.Record>(map.Get(call.Arguments[0]));
        Assert.Equal(values, argument.Fields["values"]);
    }

    [Fact]
    public void NestedTupleLiteralCandidateKeepsItsContextualMemberFacts()
    {
        var (_, map, call) = Check("""
            declare function nested(...args:[[{type:'size',size:number}]]):'nested';
            declare function nested(...args:[string]):'other';
            nested([{type:'size',size:1}]);
            """);
        Assert.Equal("nested", Assert.IsType<TypeInfo.StringLiteral>(map.Get(call)).Value);
        var tuple = Assert.IsType<TypeInfo.Tuple>(map.Get(call.Arguments[0]));
        var record = Assert.IsType<TypeInfo.Record>(Assert.Single(tuple.Elements).Type);
        Assert.Equal("size", Assert.IsType<TypeInfo.StringLiteral>(record.Fields["type"]).Value);
        Assert.Equal(TypeInfo.Primitive.Number, record.Fields["size"]);
    }

    [Theory]
    [InlineData(false, "broad", false)]
    [InlineData(true, "literal", false)]
    [InlineData(false, "broad", true)]
    [InlineData(true, "literal", true)]
    public void DeclarationOrderAndSelectedExpressionFactsRemainCandidateSpecific(bool reversed, string expected, bool tuple)
    {
        string broad = tuple ? "declare function over(...value:[{type:string,size:number}]):'broad';" :
            "declare function over(value:{type:string,size:number}):'broad';";
        string literal = tuple ? "declare function over(...value:[{type:'size',size:number}]):'literal';" :
            "declare function over(value:{type:'size',size:number}):'literal';";
        var (_, map, call) = Check((reversed ? literal + broad : broad + literal) + "over({type:'size',size:1});");
        Assert.Equal(expected, Assert.IsType<TypeInfo.StringLiteral>(map.Get(call)).Value);
        var argument = Assert.IsType<TypeInfo.Record>(map.Get(call.Arguments[0]));
        if (reversed) Assert.Equal("size", Assert.IsType<TypeInfo.StringLiteral>(argument.Fields["type"]).Value);
        else Assert.IsType<TypeInfo.String>(argument.Fields["type"]);
        Assert.Equal(TypeInfo.Primitive.Number, argument.Fields["size"]);
    }

    [Fact]
    public void ContextualProjectionPreservesAccessorsAndMethods()
    {
        var (_, map, call) = Check(Prelude + "generic({type:'size',size:1,get value(){return 2;},method(x:number){return x+1;}});");
        var argument = Assert.IsType<TypeInfo.Record>(map.Get(call.Arguments[0]));
        Assert.True(argument.IsGetterOnly("value"));
        Assert.Equal(TypeInfo.Primitive.Number, argument.Fields["value"]);
        Assert.IsType<TypeInfo.Function>(argument.Fields["method"]);
    }

    [Theory]
    [InlineData("generic({type:'wrong',size:1});")]
    [InlineData("generic({type:'size',size:'wrong'});")]
    [InlineData("generic({type:'size'});")]
    [InlineData("const retained={type:'size',size:1}; generic(retained);")]
    [InlineData("generic({type:'size',size:1} as {type:string,size:number});")]
    [InlineData("const later:{type:string}={type:'size'}; generic({type:'size',size:1,...later});")]
    [InlineData("generic<{type:'size',size:1}>({type:'size',size:2});")]
    [InlineData("declare function split(value:{type:'size',size:number}):void; declare function split(value:{type:'textSize',value:number}):void; split(condition?{type:'size',size:1}:{type:'textSize',value:2});")]
    public void ContextDoesNotRepairRetainedDomainsOrCombineDifferentOverloads(string source)
    {
        var statements = new Parser(new Lexer(Prelude + source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode is "TS2345" or "TS2769");
    }

    private static (List<Stmt>, TypeMap, Expr.Call) Check(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var map = checker.Check(statements);
        Assert.Empty(checker.GetDiagnostics());
        var call = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(statements[^1]).Expr);
        return (statements, map, call);
    }
}
