using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OrdinaryTupleRestCallTests
{
    public static IEnumerable<object[]> InvalidCalls()
    {
        foreach (var (source, code) in new[]
        {
            ("declare function f(...args:[number,number]):void; f(1,\"bad\");", "TS2345"),
            ("declare function f(...args:[number,string?]):void; f(1,2);", "TS2345"),
            ("declare function f(...args:[number,...string[]]):void; f(1,\"ok\",2);", "TS2345"),
            ("declare function f(...args:[...string[],number]):void; f(\"ok\",true,2);", "TS2345"),
            ("declare function f(...args:[[number,number]]):void; f([1,\"bad\"]);", "TS2322"),
            ("declare function f(...args:[number,number]):void; f(...[1,\"bad\"]);", "TS2345"),
            ("declare function f(...args:[number,number]):void; const xs:number[]=[]; f(...xs);", "TS2556"),
            ("declare function f(...args:[number,number]):void; f(1);", "TS2554"),
            ("declare function f(...args:[number,number]):void; f(1,2,3);", "TS2554"),
            ("declare function f(...args:[number,(x:number)=>void]):void; f(1,(x:string)=>{});", "TS2345"),
            ("declare function f(...args:[number,(x:number)=>void]):void; f(1,x=>x.notThere());", "TS2339"),
            ("interface F{(...args:[number,number]):void;} declare const f:F; f(1,\"bad\");", "TS2345"),
            ("declare const f:{(...args:[number,number]):void;}; f(1,\"bad\");", "TS2345"),
            ("interface F<T>{(...args:[T,T]):void;} declare const f:F<number>; f(1,\"bad\");", "TS2345"),
            ("declare function f(...args:[number,number]):void; declare function f(...args:[string,string]):void; f(1,\"bad\");", "TS2769"),
            ("interface F{(...args:[number,number]):number; (...args:[string,string]):string;} declare const f:F; f(1,\"bad\");", "TS2769"),
            ("declare function f<T>(x:T):T; declare function f(...args:[number,number]):void; f(1,\"bad\");", "TS2345"),
            ("declare const o:{ f(...args:[number,number]):void; }; o.f(1,\"bad\");", "TS2345"),
            ("declare function f(...args:[number,number]):void; declare function f(callback:(x:number)=>void):void; f(x=>x.notThere());", "TS2339"),
            ("interface F{<T>(value:T):T; (...args:[number,number]):void;} declare const f:F; f(1,'bad');", "TS2345"),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(InvalidCalls))]
    public void TupleRestArgumentsValidateTypesAritySpreadsAndContextualCallbacks(string source, string code, bool modules)
        => Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);

    public static IEnumerable<object[]> ValidCalls()
    {
        foreach (string source in new[]
        {
            "interface F{<T>(value:T):T; (...args:[number,number]):void;} declare const f:F; f(1,2); const s:string=f('a');",
            "interface F<A>{<T extends A>(...args:[T,T]):T; (...args:[boolean,boolean]):boolean;} declare const f:F<number>; const n:number=f<number>(1,2); const b:boolean=f(true,false);",
            "interface F<T>{(...args:[T,T]):T;} declare const f:F<number>; const n:number=f(1,2);",
            "declare function f(...args:[number,number]):void; declare function f(callback:(x:number)=>void):void; f(x=>x.toFixed());",
            "declare function f(...args:[number,number]):'number'; declare function f(...args:[any,any]):'any'; declare const value:any; const result:'any'=f(value,value);",
            "declare function f(...args:[number,number]):void; f(1,2);",
            "declare function f(...args:[number,string?]):void; f(1); f(1,undefined); f(1,\"ok\");",
            "declare function f(...args:[number,...string[]]):void; f(1,\"ok\",\"fine\");",
            "declare function f(...args:[...string[],number]):void; const xs:string[]=[]; f(...xs,2);",
            "declare function f(...args:[[number,number]]):void; const pair:[number,number]=[1,2]; f(([...pair]));",
            "declare function f(...args:[number,number]):void; const pair:[number,number]=[1,2]; f(...pair); f(...[1,2]);",
            "declare function f(...args:[number?,number?]):void; const xs:number[]=[]; f(...xs);",
            "declare function f(first:number,...rest:[number,number]):void; f(...[1,2,3]);",
            "declare function f(...args:[number,(x:number)=>number]):number; const n:number=f(1,x=>x+1);",
            "declare function f(...args:[number,number]):number; declare function f(...args:[string,string]):string; const s:string=f(\"a\",\"b\");",
            "interface F{(...args:[number,number]):number; (...args:[string,string]):string;} declare const f:F; const s:string=f(\"a\",\"b\");",
            "declare function f<T>(x:T):T; declare function f(...args:[number,number]):void; f(1,2); const s:string=f(\"a\");",
            "declare function f(...args:[]):void; f();",
            "declare function f(...args:[number,string?]):void; const pair:[number,string?]=[1]; f(...pair);",
            "declare function f(...args:[number,number]):void; const pair=[1,2] as const; f(...pair);",
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidCalls))]
    public void TupleRestCallsPreserveOptionalVariadicNestedAndCallableForms(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory]
    [InlineData("declare function f(...args:[number,number]):number; f(1,2);", 0, "number")]
    [InlineData("declare function f(...args:[number,number]):number; declare function f(...args:[string,string]):string; f('a','b');", 1, "string")]
    [InlineData("interface F{(...args:[number,number]):number; (...args:[string,string]):string;} declare const f:F; f('a','b');", 1, "string")]
    [InlineData("declare function f<T>(x:T):T; declare function f(...args:[number,number]):void; f(1,2);", 1, "void")]
    [InlineData("interface F{<T>(value:T):T; (...args:[number,number]):void;} declare const f:F; f(1,2);", 1, "void")]
    [InlineData("interface F{<T>(value:T):T; (...args:[number,number]):void;} declare const f:F; f<number>(1);", 0, "number")]
    public void TupleRestCallsPreserveEditorCandidateIdentity(string source, int ordinal, string returnType)
    {
        var document = new SourceDocument("ordinary-tuple-call.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata(true);
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var syntax = Assert.Single(document.EditorSyntax!.Invocations);
        var fact = checker.EditorFacts.Freeze().GetInvocation(document, syntax.Owner)!;
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.Equal(ordinal, fact.SelectedOrdinal);
        Assert.Equal(returnType, fact.SelectedSignature!.ReturnType.Text);
    }

    [Theory, ModeData]
    public void TupleRestFunctionsAndCallbacksExecuteWithVerifiedCompilerIl(ExecutionMode mode)
    {
        const string source = """
            function sum(...args:[number,number]):number { return args[0]+args[1]; }
            function apply(...args:[number,(value:number)=>number]):number { return args[1](args[0]); }
            function describe(prefix:string,...args:[number,string?]):string { return prefix+args[0]+(args[1] ?? ''); }
            const pair:[number,number]=[3,4];
            console.log(sum(1,2));
            console.log(sum(...pair));
            console.log(apply(5,value=>value+1));
            console.log(describe('item',2,'!'));
            """;
        Assert.Equal("3\n7\n6\nitem2!\n", TestHarness.Run(source, mode));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "ordinary-tuple-rest-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
