using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class MutableLiteralInferenceTests
{
    [Fact]
    public void MutableLiteralFieldsAndReturnsWidenButExplicitLiteralsAndConstAssertionsStayFixed()
    {
        const string source = """
            const fresh=1; const fixed:1=1;
            const object={a:1,aliasFresh:fresh,aliasFixed:fixed,fn:(a:boolean)=>123};
            function f1(s:string){return {a:1,b:s};}
            function single(){return 1;}
            function choices(x:boolean){return x?1:2;}
            const preserved={a:1} as const;
            const asserted=()=>123 as const;
            const typed:{a:1}={a:1};
            const literalReturn:()=>1=()=>1;
            const nestedContext:()=>{a:1}=()=>({a:1});
            const numericContext:()=>number=()=>123;
            const booleanContext:{a:boolean}={a:true};
            let mutable={a:1 as 1};
            object.a=2;
            """;
        var (checker, document, statements, map) = Check(source);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var observations = checker.EditorFacts.GetSourceTypeObservations(document);
        void Fact(string text, string expected) => Assert.Contains(observations, observation =>
            source[observation.Span.Start..observation.Span.End] == text && observation.Display.Text == expected);
        Fact("object", "{ a: number; aliasFixed: 1; aliasFresh: number; fn: (a: boolean) => number; }");
        Fact("{a:1,aliasFresh:fresh,aliasFixed:fixed,fn:(a:boolean)=>123}", "{ a: number; aliasFixed: 1; aliasFresh: number; fn: (a: boolean) => number; }");
        Fact("f1", "(s: string) => { a: number; b: string; }"); Fact("single", "() => number");
        Fact("choices", "(x: boolean) => 1 | 2");
        Fact("preserved", "{ readonly a: 1; }"); Fact("asserted", "() => 123");
        Fact("typed", "{ a: 1; }"); Fact("literalReturn", "() => 1");
        Fact("nestedContext", "() => { a: 1; }"); Fact("numericContext", "() => number");
        Fact("mutable", "{ a: 1; }");
        var objectDeclaration = statements.OfType<Stmt.Const>().Single(statement => statement.Name.Lexeme == "object");
        var record = Assert.IsType<TypeInfo.Record>(map.Get(objectDeclaration.Initializer!));
        Assert.IsType<TypeInfo.Primitive>(record.Fields["a"]);
        Assert.IsType<TypeInfo.NumberLiteral>(record.Fields["aliasFixed"]);
    }

    [Theory]
    [InlineData("const value={a:1} as const; value.a=2;", "TS2540")]
    [InlineData("const value:{a:1}={a:1}; value.a=2;", "TS2322")]
    public void WideningDoesNotAdmitWritesToReadonlyOrExplicitLiteralSlots(string source, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => Check(source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void FreshArgumentsAndAssignmentsReceiveDiscriminatedContexts()
    {
        const string source = """
            type Command={kind:'line',x:number}|{kind:'stop'};
            function accept(command:Command):void{}
            function many(commands:Command[]):void{}
            accept({kind:'line',x:1}); many([{kind:'line',x:1}]);
            let command:Command={kind:'line',x:1}; command={kind:'stop'};
            const boolContext:()=>boolean=()=>true;
            type Item={x?:'ok'};
            const items:Item|Item[]=[{x:'ok'}]; items.push({x:'ok'});
            function select(props:{multi:true,changed:(value:string[])=>void}|{multi:false,changed:(value:string)=>void}):void{}
            select({multi:false,changed:value=>{value.toUpperCase();}});
            """;
        var (checker, document, _, _) = Check(source);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            source[observation.Span.Start..observation.Span.End] == "()=>true" && observation.Display.Text == "() => true");
    }

    [Fact]
    public void ContextualVoidMethodsCanDiscardTheirActualReturnValue()
    {
        const string source = """
            interface Target { f(x:number):void; }
            declare let target:Target;
            target={f:()=>1}; target={f:<U>(x:U)=>1};
            target={f:function(){return 1;}}; target={f(x:number){return '';}};
            declare let callable:{f(x:number):void}; callable={f:()=>1};
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.Empty(new TypeChecker().CheckWithRecovery(statements).Diagnostics);
    }

    private static (TypeChecker Checker, SourceDocument Document, List<Stmt> Statements, TypeMap Map) Check(string source)
    {
        var document = new SourceDocument("mutable-inference.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(strictNullChecks: true).WithEditorMetadata();
        return (checker, document, statements, checker.Check(statements, document));
    }
}
