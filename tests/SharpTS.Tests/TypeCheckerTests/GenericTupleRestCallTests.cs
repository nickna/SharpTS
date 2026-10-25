using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericTupleRestCallTests
{
    public static IEnumerable<object[]> InvalidCalls()
    {
        foreach (var (source, code) in new[]
        {
            ("declare function same<T>(...args:[T,T]):void; same(1,'bad');", "TS2345"),
            ("declare function same<T>(...args:[T,T]):void; same<number>(1,'bad');", "TS2345"),
            ("declare function same<T>(...args:[T,T?]):void; same(1,'bad');", "TS2345"),
            ("declare function same<T>(...args:[T,...T[]]):void; same(1,'bad');", "TS2345"),
            ("declare function same<T>(...args:[[T,T]]):void; same([1,'bad']);", "TS2322"),
            ("declare function same<T>(...args:[T,T]):void; const pair:[number,string]=[1,'bad']; same(...pair);", "TS2345"),
            ("declare function same<T>(...args:[T,T]):void; same(...[1,'bad']);", "TS2345"),
            ("declare function same<T>(...args:[T,T]):void; same(1);", "TS2554"),
            ("declare function same<T>(...args:[T,T]):void; same(1,2,3);", "TS2554"),
            ("declare function same<T>(...args:[T,T]):void; const values:number[]=[1,2]; same(...values);", "TS2556"),
            ("declare function same<T>(...args:[T,T]):void; const values:string[]=['bad']; same<number>(1,2,...values);", "TS2556"),
            ("declare function same<T>(...args:[T,(x:T)=>void]):void; same<number>(1,(x:string)=>{});", "TS2345"),
            ("declare function same<T=number>(...args:[T?]):T; const value=same(); const wrong:string=value;", "TS2322"),
            ("declare function same<T=number>(...args:[T?]):T; const wrong:number=same(undefined);", "TS2322"),
            ("declare function same<T>(...args:[...T[],number]):void; same<string>('ok',true,1);", "TS2345"),
            ("interface F { <T>(...args:[T,T]):void; } declare const f:F; f(1,'bad');", "TS2345"),
            ("declare const f: { <T>(...args:[T,T]):void; }; f(1,'bad');", "TS2345"),
            ("declare function f<T>(...args:[T,T]):void; declare function f<T>(...args:[T,T,T]):void; f(1,'bad');", "TS2345"),
            ("declare function f<T>(first:T,...rest:[T,T]):T; f(...[1,2,'bad']);", "TS2345"),
            ("declare function f<T>(first:T,...rest:[T,T]):T; const values:[number,number,string]=[1,2,'bad']; f(...values);", "TS2345"),
            ("declare function f<T>(...args:[[T,T]]):T; const values:[number,string]=[1,'bad']; f<number>([...values]);", "TS2345"),
            ("declare function f<T>(...args:[T,T]):void; declare function f(x:boolean,y:boolean):void; f(1,'bad');", "TS2769"),
            ("declare function f<T>(...args:[T,T]):void; declare function f(x:boolean):void; f(1,'bad');", "TS2345"),
            ("declare function f<T>(...args:[T,T]):void; declare function f(callback:(x:number)=>void):void; f(x=>x.notThere());", "TS2339")
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(InvalidCalls))]
    public void TupleRestParametersValidateEverySuppliedPosition(string source, string code, bool modules)
    {
        Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);
    }

    public static IEnumerable<object[]> ValidCalls()
    {
        foreach (string source in new[]
        {
            "declare function same<T>(...args:[T,T]):T; const value:number=same(1,2);",
            "declare function same<T>(...args:[T,T?]):T; const value:number=same(1);",
            "declare function same<T>(...args:[T,T?]):T; const value:number=same(1,undefined);",
            "declare function same<T>(...args:[T,...T[]]):T; const value:number=same(1,2,3);",
            "declare function same<T>(...args:[[T,T]]):T; const value:number=same([1,2]);",
            "declare function same<T=number>(...args:[T?]):T; const value:number=same();",
            "declare function same<T>(...args:[T?,T?]):void; const values:number[]=[1,2]; same(...values);",
            "declare function same<T>(...args:[T,...T[]]):void; const values:number[]=[2,3]; same(1,...values);",
            "declare function same<T>(...args:[...T[],number]):void; const values:string[]=['a','b']; same(...values,1);",
            "declare function same<T extends any[]>(...args:[number,...T,string]):T; const value:[boolean,number]=same(1,true,2,'end');",
            "declare function same<T>(...args:[T,(x:T)=>T]):T; const value:number=same(1,x=>x+1);",
            "declare function same<T extends number>(...args:[T,T]):T; const value:number=same(1,2);",
            "declare function same<T>(...args:[T,T]):void; const values:[number,number]=[1,2]; same(...values);",
            "declare function same<T>(...args:[T,T]):void; same(...[1,2]);",
            "interface F { <T>(...args:[T,T]):T; } declare const f:F; const value:number=f(1,2);",
            "declare const f: { <T>(...args:[T,T]):T; }; const value:number=f(1,2);",
            "declare function f<T>(...args:[T,T]):T; declare function f<T>(...args:[T,T,T]):T; const value:number=f(1,2,3);",
            "declare function f<T>(...args:[T?,T?]):void; const values:number[]=[1,2]; f(...values,2);",
            "declare function f<T>(...args:[T?,string?]):void; const values:number[]=[1,2]; f(...values);",
            "declare function f<T>(...args:[T?,...string[]]):void; const values:number[]=[1,2]; f(...values);",
            "declare function f<T>(first:T,...rest:[T,T]):T; const value:number=f(...[1,2,3]);",
            "declare function f<T>(first:T,...rest:[T,T]):T; const values:[number,number,number]=[1,2,3]; const value:number=f(...values);",
            "declare function f<T>(...args:[[T,T]]):T; const values:[number,number]=[1,2]; const value:number=f<number>([...values]);",
            "declare function f<T>(...args:[[T,T]]):T; const value:number=f<number>(([1,2]));",
            "interface F<A> { <T extends A>(...args:[T,T]):T; } declare const f:F<number>; const value:number=f<number>(1,2);",
            "declare function f<T>(...args:[T,T]):void; declare function f(callback:(x:number)=>void):void; f(x=>x.toFixed());",
            "declare function f<T extends number>(...args:[T,T]):T; declare function f(x:boolean,y:boolean):boolean; const value:boolean=f(true,false);"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidCalls))]
    public void TupleRestInferencePreservesOptionalVariadicAndContextualCalls(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory]
    [InlineData("interface F { <T>(...args:[T,T]):T; } declare const f:F; f<number>(1,2);", 0)]
    [InlineData("declare function f<T>(...args:[T,T]):T; declare function f<T>(...args:[T,T,T]):T; f<number>(1,2,3);", 1)]
    [InlineData("declare function f<T>(...args:[T,T]):void; declare function f(callback:(x:number)=>void):void; f(x=>{ const n:number=x; });", 1)]
    public void TupleCallsRetainTheirOriginalEditorCandidateAndInstantiation(string source, int ordinal)
    {
        var document = new SourceDocument("tuple-call.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata(true);
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var syntax = Assert.Single(document.EditorSyntax!.Invocations);
        var fact = checker.EditorFacts.Freeze().GetInvocation(document, syntax.Owner)!;
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.Equal(ordinal, fact.SelectedOrdinal);
        Assert.True(fact.Candidates[ordinal].Instantiated!.IsAvailable);
        Assert.Equal("number", fact.SelectedSignature!.ReturnType.Text);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "generic-tuple-rest-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
