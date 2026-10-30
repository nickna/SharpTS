using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class HigherOrderIndexedInferenceTests
{
    private const string Factory = """
        function invoker<K extends string | number | symbol, A extends any[]>(key: K, ...args: A) {
            return <T extends Record<K, (...args: A) => any>>(obj: T): ReturnType<T[K]> => obj[key](...args);
        }
        """;

    [Theory, ModeData]
    public void HigherOrderIndexedCallsRunWithStringNumberAndSymbolKeys(ExecutionMode mode)
    {
        const string source = Factory + """
            const partial=invoker('test',true);
            console.log(partial({test:(flag:boolean)=>flag?123:0}));
            console.log(invoker(7,false)({7:(flag:boolean)=>flag?0:456}));
            const key:unique symbol=Symbol();
            console.log(invoker(key)({[key]:()=>789}));
            function invokeAny<K extends string,T extends Record<K,any>>(obj:T,key:K){return obj[key](1);}
            console.log(invokeAny({test:(value:number)=>value+1},'test'));
            """;
        Assert.Equal("123\n456\n789\n2\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void GenericIndexedReadsPartialConstraintsAndFinalConditionalInferenceRemainDistinct()
    {
        const string source = Factory + """
            const partial = invoker('test', true);
            const result = partial({ test: (a: boolean) => 123 });
            const direct = invoker('test', true)({ test: (a: boolean) => 123 });
            """;
        var (checker, document, statements, map) = Check(source);
        Assert.Empty(checker.GetDiagnostics());
        var partial = Assert.IsType<TypeInfo.GenericFunction>(map.Get(Declaration("partial").Initializer!));
        var constraint = Assert.IsType<TypeInfo.MappedType>(Assert.Single(partial.TypeParams).Constraint);
        Assert.Equal("test", Assert.IsType<TypeInfo.StringLiteral>(constraint.Constraint).Value);
        var callable = Assert.IsType<TypeInfo.Function>(constraint.ValueType);
        var arguments = Assert.IsType<TypeInfo.Tuple>(Assert.Single(callable.ParamTypes));
        Assert.Equal(TypeInfo.Primitive.Boolean, Assert.Single(arguments.ElementTypes));
        var deferred = Assert.IsType<TypeInfo.ConditionalType>(partial.ReturnType);
        Assert.DoesNotContain("<ReturnType:result>", deferred.ToString());
        Assert.DoesNotContain("<ReturnType:result>", CheckedTypeDisplay.Render(deferred with { }).Text);
        Assert.DoesNotContain("<ReturnType:result>", EditorTypeRenderer.Render(deferred with { }).Text);
        Assert.Equal(TypeInfo.Primitive.Number, map.Get(Declaration("result").Initializer!));
        Assert.Equal(TypeInfo.Primitive.Number, map.Get(Declaration("direct").Initializer!));
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            source[observation.Span.Start..observation.Span.End] == "obj[key]" && observation.Display.Text == "T[K]");
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            source[observation.Span.Start..observation.Span.End] == "...args" && observation.Display.Text == "A");
        Assert.Equal("<T extends Record<\"test\", (args_0: boolean) => any>>(obj: T) => ReturnType<T[\"test\"]>",
            CheckedTypeDisplay.Render(partial).Text);
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            source[observation.Span.Start..observation.Span.End] == "invoker" && observation.Display.Text ==
            "<K extends string | number | symbol, A extends any[]>(key: K, ...args: A) => <T extends Record<K, (...args: A) => any>>(obj: T) => ReturnType<T[K]>");
        Stmt.Const Declaration(string name) => Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == name);
    }

    [Theory]
    [InlineData("const value=invoker('length')({length:()=> 'text'});", "string")]
    [InlineData("const value=invoker('count')({count:()=>123});", "number")]
    [InlineData("const value=invoker(7,true)({7:(flag:boolean)=>123});", "number")]
    [InlineData("const key:unique symbol=Symbol();const value=invoker(key)({[key]:()=>true});", "boolean")]
    public void RenamedAndNonStringKeysInferTheSelectedMembersReturn(string call, string expected)
    {
        var (checker, document, _, _) = Check(Factory.Replace("invoker", "selectMember").Replace("obj", "receiver") + call.Replace("invoker", "selectMember"));
        Assert.Empty(checker.GetDiagnostics());
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            document.Text[observation.Span.Start..observation.Span.End] == "value" && observation.Display.Text == expected);
    }

    [Theory]
    [InlineData("declare const key:'text'|7;const value=invoker(key)({text:()=>123,7:()=> 'numeric'});", "number", "string")]
    [InlineData("declare const key:1|7;const value=invoker(key)({1:()=>123,7:()=> 'numeric'});", "number", "string")]
    [InlineData("const symbol:unique symbol=Symbol();declare const key:'text'|typeof symbol;const value=invoker(key)({text:()=>123,[symbol]:()=> 'symbolic'});", "number", "string")]
    public void UnionKeyDomainsIncludeEverySelectedMembersReturn(string call, string first, string second)
    {
        var (_, _, statements, map) = Check(Factory + call);
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "value");
        var result = Assert.IsType<TypeInfo.Union>(map.Get(declaration.Initializer!));
        Assert.Equal(new[] { first, second }.Order(StringComparer.Ordinal), result.FlattenedTypes
            .Select(type => CheckedTypeDisplay.Render(type).Text).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("invoker('test', true)({wrong:(value:boolean)=>1});")]
    [InlineData("invoker('test', true)({test:(value:string)=>1});")]
    public void PartialInstantiationStillEnforcesTheSelectedKeyAndArgumentTuple(string call)
    {
        var statements = new Parser(new Lexer(Factory + call).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var result = checker.CheckWithRecovery(statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2345");
    }

    [Fact]
    public void MutableRestInferenceRetainsAssertionsLiteralAnnotationsAndConstParameters()
    {
        const string source = """
            function gather<A extends any[]>(...values:A){return values;}
            function strings<A extends string[]>(...values:A){return values;}
            function fixed<const A extends readonly unknown[]>(...values:A){return values;}
            const fresh=gather(true,1,'x');
            const empty=gather();
            const asserted=gather(true as const,1 as const,'x' as const);
            const declared:true=true;
            const annotated=gather(declared);
            const constrained=strings('x');
            const immutable=fixed(true,1,'x');
            """;
        var (_, _, statements, map) = Check(source);
        foreach (var (name, expected) in new[] {
            ("fresh", "[boolean, number, string]"), ("empty", "[]"), ("asserted", "[true, 1, \"x\"]"),
            ("annotated", "[true]"), ("constrained", "[\"x\"]"), ("immutable", "readonly [true, 1, \"x\"]") })
        {
            var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == name);
            Assert.Equal(expected, CheckedTypeDisplay.Render(map.Get(declaration.Initializer!)!).Text);
        }
    }

    [Fact]
    public void IntrinsicReturnInferenceDoesNotCaptureAnUnrelatedCallerR()
    {
        const string source = """
            type AliasedReturn<F> = ReturnType<F>;
            function invoker<R,K extends string|number|symbol,A extends any[]>(ignored:R,key:K,...args:A) {
                return <T extends Record<K,(...args:A)=>any>>(obj:T):AliasedReturn<T[K]>=>obj[key](...args);
            }
            const value=invoker(0,'test',true)({test:(flag:boolean)=>'text'});
            """;
        var (_, _, statements, map) = Check(source);
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "value");
        Assert.IsType<TypeInfo.String>(map.Get(declaration.Initializer!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedReturnTypeAndSourceInferRKeepIndependentBindings(bool sourceAliasFirst)
    {
        const string sourceAlias = "type UserExtract<T> = T extends (...args:any[])=>infer R ? ReturnType<()=>R> : never;";
        const string intrinsicAlias = "declare function read<R,T extends()=>any>(ignored:R,value:T):ReturnType<()=>ReturnType<T>>;";
        string source = (sourceAliasFirst ? sourceAlias + intrinsicAlias : intrinsicAlias + sourceAlias) + """
            const value=read(0,()=>123);
            const inferred:UserExtract<()=>string>='text';
            """;
        var (_, _, statements, map) = Check(source);
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "value");
        Assert.Equal(TypeInfo.Primitive.Number, map.Get(declaration.Initializer!));
    }

    [Fact]
    public void NestedConditionalCheckTypesEvaluateAfterInstantiation()
    {
        const string source = """
            type Twice<F extends()=>()=>any> = ReturnType<ReturnType<F>>;
            declare function nested<R,F extends()=>()=>any>(ignored:R,value:F):Twice<F>;
            const result=nested('ignored',()=>()=>123);
            """;
        var (_, _, statements, map) = Check(source);
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "result");
        Assert.Equal(TypeInfo.Primitive.Number, map.Get(declaration.Initializer!));
    }

    [Fact]
    public void DeferredIntrinsicPatternHasZeroRequiredRestArgumentsAndOneResultBinding()
    {
        var (_, _, statements, map) = Check(Factory + "const selected=invoker('count');");
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "selected");
        var selected = Assert.IsType<TypeInfo.GenericFunction>(map.Get(declaration.Initializer!));
        var conditional = Assert.IsType<TypeInfo.ConditionalType>(selected.ReturnType);
        var pattern = Assert.IsType<TypeInfo.Function>(conditional.ExtendsType);
        Assert.Equal(0, pattern.MinArity);
        Assert.True(pattern.HasRestParam);
        Assert.IsType<TypeInfo.Any>(Assert.Single(pattern.ParamTypes));
        Assert.IsType<TypeInfo.Any>(conditional.FalseType);
        Assert.Same(pattern.ReturnType, conditional.TrueType);
    }

    [Theory]
    [InlineData("()=>string", "string")]
    [InlineData("(first:string,second:number)=>boolean", "boolean")]
    [InlineData("(...args:number[])=>string", "string")]
    [InlineData("(...args:[string,number])=>boolean", "boolean")]
    [InlineData("(value?:never)=>string", "string")]
    public void AnyRestReturnPatternAcceptsActualSignatureArityAndRestShape(string signature, string expected)
    {
        string source = $$"""
            type SourceReturn<F> = F extends (...args:any[])=>infer R ? R : never;
            declare function intrinsic<F extends (...args:any[])=>any>(value:F):ReturnType<F>;
            declare const callback:{{signature}};
            declare const sourceResult:SourceReturn<{{signature}}>;
            const intrinsicResult=intrinsic(callback);
            """;
        var (checker, document, _, _) = Check(source);
        foreach (var name in new[] { "sourceResult", "intrinsicResult" })
            Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
                document.Text[observation.Span.Start..observation.Span.End] == name && observation.Display.Text == expected);
    }

    [Theory]
    [InlineData("(value:never)=>void")]
    [InlineData("(first:never,second:string)=>number")]
    [InlineData("(...args:never[])=>string")]
    [InlineData("(...args:[never,string])=>boolean")]
    public void AnyRestReturnPatternStillRejectsUnsatisfiableParameterPositions(string signature)
    {
        string source = $$"""
            type SourceReturn<F> = F extends (...args:any[])=>infer R ? R : never;
            declare function intrinsic<F extends (...args:any)=>any>(value:F):ReturnType<F>;
            declare const callback:{{signature}};
            declare const sourceResult:SourceReturn<{{signature}}>;
            const intrinsicResult=intrinsic(callback);
            """;
        var (checker, document, _, _) = Check(source);
        foreach (var (name, expected) in new[] { ("sourceResult", "never"), ("intrinsicResult", "any") })
            Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
                document.Text[observation.Span.Start..observation.Span.End] == name && observation.Display.Text == expected);
    }

    [Fact]
    public void NestedPartialRecordKeysRetainTheirIndexedDomain()
    {
        const string source = """
            function update<T extends Record<K,string>,K extends string>(
                obj:T, fields:K[], mapping:Partial<Record<T[K],T[K]>>):Record<K,string> {
                for(const field of fields){
                    const next:T[K]|undefined=mapping[obj[field]];
                    if(next){obj[field]=next;}
                }
                return obj;
            }
            """;
        Check(source);
    }

    [Fact]
    public void AnAnyMemberConstraintPermitsInvocationWithoutReplacingTheIndexedSourceFact()
    {
        const string source = """
            function invoke<K extends string,T extends Record<K,any>>(obj:T,key:K){return obj[key](1);}
            """;
        var (checker, document, _, _) = Check(source);
        var observations = checker.EditorFacts.GetSourceTypeObservations(document);
        Assert.Contains(observations, observation => document.Text[observation.Span.Start..observation.Span.End] == "obj[key]" && observation.Display.Text == "T[K]");
        Assert.Contains(observations, observation => document.Text[observation.Span.Start..observation.Span.End] == "obj[key](1)" && observation.Display.Text == "any");
    }

    [Fact]
    public void AConcreteNonCallableMemberConstraintIsStillRejected()
    {
        const string source = "function invoke<K extends string,T extends Record<K,number>>(obj:T,key:K){return obj[key](1);}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Contains(result.Errors, error => error.Message.Contains("Can only call functions", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericIndexSignaturesSurviveOrdinarySubstitution()
    {
        const string source = """
            function identity<T>(value:{[key:string]:T}):{[key:string]:T}{return value;}
            const dictionary=identity<number>({answer:42});
            const value=dictionary['other'];
            """;
        var (checker, _, statements, map) = Check(source);
        Assert.Empty(checker.GetDiagnostics());
        var declaration = Assert.Single(statements.OfType<Stmt.Const>(), statement => statement.Name.Lexeme == "dictionary");
        Assert.Equal(TypeInfo.Primitive.Number, Assert.IsType<TypeInfo.Record>(map.Get(declaration.Initializer!)).StringIndexType);
    }

    private static (TypeChecker Checker, SourceDocument Document, List<Stmt> Statements, TypeMap Map) Check(string source)
    {
        var document = new SourceDocument("higher-order-inference.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var map = checker.Check(statements, document);
        Assert.Empty(checker.GetDiagnostics());
        return (checker, document, statements, map);
    }
}
