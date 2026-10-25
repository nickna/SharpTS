using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NestedCallbackPropertyDiagnosticTests
{
    public static IEnumerable<object[]> InvalidCalls()
    {
        foreach (var (source, code) in new[]
        {
            ("interface F{<T>(value:T,options:{transform:(value:T)=>T}):T;} declare const f:F; f<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("interface F{(value:number,options:{transform:(value:number)=>number}):number;} declare const f:F; f(1,{transform:(x:string)=>1});", "TS2322"),
            ("declare const f:{<T>(value:T,options:{transform:(value:T)=>T}):T;}; f<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("interface F{<T>(value:T,options:{transform:(value:T)=>T}):T; <T>(a:T,b:T,c:T):T;} declare const f:F; f<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("interface F{<T>(value:T,options:{transform:(value:T)=>T}):T; <T>(value:T,options:{transform:(value:boolean)=>T}):T;} declare const f:F; f<number>(1,{transform:(x:string)=>1});", "TS2769"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T;\nmap<number>(1,{\n transform:(x:string)=>1\n});", "TS2322"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; map(1,{transform:(x:string)=>1});", "TS2345"),
            ("declare function map<T>(...args:[T,{transform:(value:T)=>T}]):T; map<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("declare function map(...args:[number,{transform:(value:number)=>number}]):number; map(1,{transform:(x:string)=>1});", "TS2322"),
            ("declare function map<T>(value:T,options:{inner:{transform:(value:T)=>T}}):T; map<number>(1,{inner:{transform:(x:string)=>1}});", "TS2322"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; map<number>(1,({transform:((x:string)=>1)}));", "TS2322"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; const wrong=(x:string)=>1; map<number>(1,{transform:wrong});", "TS2322"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; const wrong={transform:(x:string)=>1}; map<number>(1,wrong);", "TS2345"),
            ("declare function map<T>(value:T,transform:(value:T)=>T):T; map<number>(1,(x:string)=>1);", "TS2345"),
            ("declare function map<T>(...args:[T,{transform:(value:T)=>T}]):T; declare function map<T>(...args:[T,T,T]):T; map<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("declare function map<T>(...args:[T,{transform:(value:T)=>T}]):T; declare function map<T>(...args:[T,{transform:(value:boolean)=>T}]):T; map<number>(1,{transform:(x:string)=>1});", "TS2769"),
            ("declare function map<T>(value:T,options:{transform:(value:T)=>T}):T; declare function map<T>(a:T,b:T,c:T):T; map<number>(1,{transform:(x:string)=>1});", "TS2322"),
            ("declare function map(value:number,options:{transform:(value:number)=>number}):number; map(1,{transform:(x:string)=>1});", "TS2322"),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(InvalidCalls))]
    public void FreshCallbackPropertiesUseAssignmentDiagnosticsAndKeepArgumentAndOverloadBoundaries(string source, string code, bool modules)
        => Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallbackDiagnosticPointsToTheWrittenProperty(bool modules)
    {
        const string source = """
            declare function map<T>(value:T,options:{transform:(value:T)=>T}):T;
            map<number>(1,{
                transform:(x:string)=>1
            });
            """;
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatibleCallbacksPreserveContextualInferenceAndMethodVariance(bool modules)
        => Assert.Empty(Check("""
            declare function map<T>(value:T,options:{transform:(value:T)=>T}):T;
            const explicit:number=map<number>(1,{transform:(value:number)=>value+1});
            const inferred:number=map(1,{transform:value=>value+1});
            const broad:number=map<number>(1,{transform:(value:number|string)=>1});
            declare function method<T>(value:T,options:{transform(value:T|string):T}):T;
            const bivariant:number=method<number>(1,{transform:(value:number)=>value+1});
            declare function sink<T>(value:T,options:{callback:(value:T)=>void}):void;
            sink<number>(1,{callback:value=>value+1});
            interface F { <T>(value:T,options:{transform:(value:T)=>T}):T; }
            declare const f:F;
            const throughInterface:number=f<number>(1,{transform:(value:number)=>value+1});
            """, modules));

    [Theory, ModeData]
    public void ValidNestedCallbackAnnotationsExecuteWithVerifiedCompilerIl(ExecutionMode mode)
    {
        const string source = """
            function map<T>(value:T,options:{transform:(value:T)=>T}):T { return options.transform(value); }
            console.log(map<number>(2,{transform:(value:number)=>value+1}));
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
        {
            var document = new SourceDocument("nested-callback.ts", source);
            var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
            return checker.CheckWithRecovery(statements, document).Diagnostics;
        }
        string path = Path.Combine(Path.GetTempPath(), "nested-callback-diagnostic-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
